namespace JustAnotherHemaClub.Services;

using JustAnotherHemaClub.Models;

public class RecurringTrainingMaterializer
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;

    // Never backfill further into the past than this, even if a rule has been
    // dormant for months. Keeps the first run after a long offline period cheap.
    private const int MaxBackfillDays = 14;

    public RecurringTrainingMaterializer(IGoogleSheetsService sheets, ICacheControl cache)
    { _sheets = sheets; _cache = cache; }

    /// <summary>
    /// For every active rule:
    ///   - backfills any past occurrences that were missed, capped at the last
    ///     <see cref="MaxBackfillDays"/> days, and
    ///   - creates the next upcoming occurrence(s) up to <paramref name="lookAheadDays"/>
    ///     (default 1 = "the day before the session").
    /// Idempotent: re-running never duplicates rows, thanks to deterministic ids,
    /// and a look-alike check on (date, start time, topic) prevents duplicating
    /// sessions that were created manually for the same slot.
    /// </summary>
    public async Task MaterializeDueAsync(int lookAheadDays = 1)
    {
        var rules    = await _sheets.GetRecurringTrainingsAsync();
        var existing = await _sheets.GetTrainingsAsync();

        // Deterministic id => idempotent. Re-running never creates duplicates.
        static string IdFor(RecurringTrainingRule r, DateTime d) =>
            $"rec_{r.Id}_{d:yyyyMMdd}";

        static string TopicKey(string? s) => (s ?? "").Trim().ToLowerInvariant();

        var existingIds = existing.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        // Look-alike index: any session already on (date, start time, topic) is
        // treated as covering that slot, even if its id isn't our rec_* one
        // (e.g. a manually-created "first" training).
        var existingSlots = existing
            .Select(t => (Date: t.Date.Date, Time: t.Date.TimeOfDay, Topic: TopicKey(t.Topic)))
            .ToHashSet();

        // Precompute, per rule, the date of the latest session we already created for it.
        // Lets us start the backfill loop right after that date instead of from StartDate.
        var lastByRule = existing
            .Where(t => t.Id.StartsWith("rec_", StringComparison.Ordinal))
            .GroupBy(t =>
            {
                // id format: rec_{ruleId}_{yyyyMMdd}
                var parts = t.Id.Split('_');
                return parts.Length >= 3 ? parts[1] : "";
            })
            .ToDictionary(g => g.Key, g => g.Max(t => t.Date.Date));

        var today        = DateTime.Today;
        var horizon      = today.AddDays(lookAheadDays);
        var earliestBack = today.AddDays(-MaxBackfillDays);
        var created      = false;

        foreach (var rule in rules)
        {
            // Walk from the earliest date that still needs consideration...
            var from = rule.StartDate.Date;
            if (lastByRule.TryGetValue(rule.Id, out var last) && last.AddDays(1) > from)
                from = last.AddDays(1);

            // ...but never further back than the 2-week hard ceiling.
            if (from < earliestBack) from = earliestBack;

            // ...up to today + look-ahead (but never past the rule's EndDate).
            var to = horizon;
            if (rule.EndDate is { } end && end.Date < to) to = end.Date;

            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!rule.IsActiveOn(d)) continue;

                var id = IdFor(rule, d);
                if (existingIds.Contains(id)) continue;

                // A session already exists for this date+start-time+topic even
                // though its id isn't ours. Don't pile a duplicate on top of it.
                var slot = (Date: d.Date, Time: rule.TimeOfDay, Topic: TopicKey(rule.Topic));
                if (existingSlots.Contains(slot)) continue;

                await _sheets.UpsertTrainingAsync(new TrainingSession
                {
                    Id      = id,
                    Date    = d.Date + rule.TimeOfDay,
                    EndDate = d.Date + rule.EndTimeOfDay,
                    Topic   = rule.Topic,
                });
                existingIds.Add(id);     // guard against the same run creating duplicates
                existingSlots.Add(slot);
                created = true;
            }
        }

        if (created) _cache.InvalidateTrainings();
    }
}