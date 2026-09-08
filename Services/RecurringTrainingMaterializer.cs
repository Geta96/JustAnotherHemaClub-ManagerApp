namespace JustAnotherHemaClub.Services;

using JustAnotherHemaClub.Models;

public class RecurringTrainingMaterializer
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;

    public RecurringTrainingMaterializer(IGoogleSheetsService sheets, ICacheControl cache)
    { _sheets = sheets; _cache = cache; }

    /// <summary>Deterministic id for a rule's occurrence on a given date.</summary>
    public static string OccurrenceId(RecurringTrainingRule rule, DateTime date) =>
        $"rec_{rule.Id}_{date:yyyyMMdd}";

    /// <summary>
    /// For every active rule, creates the upcoming occurrence(s) from today up to
    /// <paramref name="lookAheadDays"/> (default 7 = "the whole week ahead", so the
    /// Home "Next lesson" card and the Trainings list always have the upcoming
    /// sessions ready).
    ///
    /// Forward-only by design: it never fabricates brand-new past rows (that would
    /// spam phantom sessions nobody attended whenever a rule's StartDate predates
    /// the first materialization). Idempotent: re-running never duplicates rows,
    /// thanks to deterministic ids, and a look-alike check on (date, start time,
    /// topic) prevents duplicating sessions that were created manually for the
    /// same slot.
    /// </summary>
    public async Task MaterializeDueAsync(int lookAheadDays = 7)
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

        var today   = DateTime.Today;
        var horizon = today.AddDays(lookAheadDays);
        var created = false;

        foreach (var rule in rules)
        {
            // Forward-only: never earlier than today, never before the rule starts.
            var from = rule.StartDate.Date;
            if (from < today) from = today;

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

    /// <summary>
    /// Ensures the occurrence of <paramref name="rule"/> on <paramref name="date"/>
    /// exists as a materialized <see cref="TrainingSession"/> and returns it
    /// (either the pre-existing row or a freshly-created one).
    ///
    /// Duplicate-safe: re-reads the current trainings and skips creation if a row
    /// already covers this slot — either by our deterministic id
    /// (<see cref="OccurrenceId"/>) OR by a look-alike (date, start-time, topic)
    /// match (e.g. a manually-created first session). This is the same guard the
    /// batch <see cref="MaterializeDueAsync"/> uses, so calling this from the Home
    /// screen can never produce a duplicate.
    /// </summary>
    public async Task<TrainingSession> EnsureOccurrenceAsync(RecurringTrainingRule rule, DateTime date)
    {
        var day = date.Date;
        var id = OccurrenceId(rule, day);
        var topicKey = (rule.Topic ?? "").Trim().ToLowerInvariant();

        var existing = await _sheets.GetTrainingsAsync();

        // Already materialized under our deterministic id?
        var byId = existing.FirstOrDefault(t => t.Id == id);
        if (byId is not null) return byId;

        // Already covered by a look-alike row on the same (date, start time, topic)?
        var bySlot = existing.FirstOrDefault(t =>
            t.Date.Date == day &&
            t.Date.TimeOfDay == rule.TimeOfDay &&
            (t.Topic ?? "").Trim().ToLowerInvariant() == topicKey);
        if (bySlot is not null) return bySlot;

        // Nothing covers this slot yet — create it.
        var session = new TrainingSession
        {
            Id      = id,
            Date    = day + rule.TimeOfDay,
            EndDate = day + rule.EndTimeOfDay,
            Topic   = rule.Topic,
        };
        await _sheets.UpsertTrainingAsync(session);
        _cache.InvalidateTrainings();
        return session;
    }
}