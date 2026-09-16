using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// THE single source of truth for a fencer's dues across time.
///
/// Every page that needs to know what a fencer owes (Finance, Home payment
/// status, profile "owes X") funnels through here so the number can never
/// disagree between screens. The ledger walks a fencer's months in
/// chronological order and, for each month, produces a <see cref="DuesQuote"/>
/// that already accounts for:
///
///   • the cheapest applicable per-month tier (via <see cref="DuesCalculator"/>),
///   • custom-period passes — a period is billed exactly like any other month:
///     the full period price lands on the first attended month in the window,
///     later attended months in the same window are covered (0 due), and the
///     cheaper-wins rule falls back to normal billing when that is cheaper,
///   • overpayment credit carried forward from earlier months.
///
/// Callers only supply raw facts per month (sessions attended + cash paid);
/// they never re-implement any of the billing or credit-carry logic.
/// </summary>
public static class FencerDuesLedger
{
    /// <summary>Raw per-month facts for one fencer (chronological order).</summary>
    public readonly record struct MonthInput(int Year, int Month, int Sessions, decimal CashPaid);

    /// <summary>
    /// The computed contribution of a single month: the finished quote plus the
    /// cash paid that month and the credit carried into it (both needed to build
    /// a <see cref="ViewModels.FencerDueRow"/> without recomputation).
    /// </summary>
    public readonly record struct MonthContribution(DuesQuote Quote, decimal CashPaid, decimal CreditIn);

    /// <summary>A month's computed contribution, tagged with its year/month.</summary>
    public readonly record struct MonthResult(int Year, int Month, MonthContribution Contribution);

    /// <summary>
    /// Rules whose active window overlaps the given calendar month.
    /// </summary>
    public static List<PriceRule> RulesForMonth(IReadOnlyList<PriceRule> allRules, int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        return allRules
            .Where(r => r.StartDate.Date <= to &&
                        (r.EndDate is null || r.EndDate.Value.Date >= from))
            .ToList();
    }

    /// <summary>
    /// Computes the full dues ledger for ONE fencer over the supplied months
    /// (which MUST be in ascending chronological order). Custom-period passes and
    /// credit carry are applied inline, so every returned quote is final.
    /// </summary>
    public static IReadOnlyList<MonthResult> Compute(
        bool isStudent,
        IReadOnlyList<MonthInput> monthsAscending,
        IReadOnlyList<PriceRule> allRules)
    {
        var periodOverride = BuildPeriodOverride(isStudent, monthsAscending, allRules);

        var results = new List<MonthResult>(monthsAscending.Count);
        decimal credit = 0m;

        foreach (var m in monthsAscending)
        {
            var applied = m.CashPaid + credit;

            // A custom period is just another month whose price is fixed by the
            // period rule instead of the per-month tier table — same credit-carry
            // maths either way.
            var quote = periodOverride.TryGetValue((m.Year, m.Month), out var ov)
                ? DuesCalculator.FixedQuote(m.Sessions, ov.TotalDue, applied, ov.Label)
                : DuesCalculator.Calculate(m.Sessions, isStudent, RulesForMonth(allRules, m.Year, m.Month), applied);

            results.Add(new MonthResult(m.Year, m.Month, new MonthContribution(quote, m.CashPaid, credit)));
            credit = quote.Overpayment;
        }

        return results;
    }

    /// <summary>
    /// Bulk variant: computes the ledger for every active fencer across a shared
    /// month set, returning a map keyed by <c>(fencerId, year, month)</c>. Used by
    /// the Finance page, which needs every fencer's numbers at once. Fencer chains
    /// are independent, so the work fans out across cores (leaving one free for the
    /// UI thread, as elsewhere in the finance pipeline).
    /// </summary>
    public static Dictionary<(string Fid, int Y, int M), MonthContribution> ComputeAll(
        IEnumerable<Fencer> activeFencers,
        IReadOnlyList<(int Y, int M)> monthsAscending,
        Dictionary<(int Y, int M), Dictionary<string, int>> attendanceByMonth,
        Dictionary<(int Y, int M), Dictionary<string, decimal>> paidByMonth,
        IReadOnlyList<PriceRule> allRules)
    {
        var fencerList = activeFencers as IList<Fencer> ?? activeFencers.ToList();
        var partials = new Dictionary<(string, int, int), MonthContribution>[fencerList.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.For(0, fencerList.Count, parallelOptions, idx =>
        {
            var f = fencerList[idx];

            var inputs = new List<MonthInput>(monthsAscending.Count);
            foreach (var ym in monthsAscending)
            {
                attendanceByMonth[ym].TryGetValue(f.Id, out var att);
                paidByMonth[ym].TryGetValue(f.Id, out var paid);
                inputs.Add(new MonthInput(ym.Y, ym.M, att, paid));
            }

            var res = Compute(f.IsStudent, inputs, allRules);

            var local = new Dictionary<(string, int, int), MonthContribution>(res.Count);
            foreach (var r in res)
                local[(f.Id, r.Year, r.Month)] = r.Contribution;

            partials[idx] = local;
        });

        var result = new Dictionary<(string Fid, int Y, int M), MonthContribution>(
            fencerList.Count * Math.Max(1, monthsAscending.Count));
        foreach (var local in partials)
            foreach (var kv in local)
                result[kv.Key] = kv.Value;

        return result;
    }

    /// <summary>
    /// Decides, for one fencer, which months are billed by a custom-period pass.
    /// A fencer who attends at least once inside a rule's [StartDate, EndDate]
    /// window owes the full period price a single time (charged on their first
    /// attended month; later attended months in the window are covered at 0).
    /// Cheaper-wins: if summing normal per-month dues across the attended months
    /// would cost less, the period pass is skipped and normal billing applies.
    /// </summary>
    private static Dictionary<(int Y, int M), (decimal TotalDue, string Label)> BuildPeriodOverride(
        bool isStudent,
        IReadOnlyList<MonthInput> monthsAscending,
        IReadOnlyList<PriceRule> allRules)
    {
        var result = new Dictionary<(int Y, int M), (decimal, string)>();

        var periodRules = allRules
            .Where(r => r.IsCustomPeriod && r.SessionCount == 0 && r.EndDate is not null)
            .ToList();
        if (periodRules.Count == 0) return result;

        foreach (var rule in periodRules)
        {
            var from = rule.StartDate.Date;
            var to = rule.EndDate!.Value.Date;

            var attendedMonths = monthsAscending
                .Where(m =>
                {
                    var monthStart = new DateTime(m.Year, m.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd < from || monthStart > to) return false;
                    return m.Sessions > 0;
                })
                .ToList();

            if (attendedMonths.Count == 0) continue;

            var periodPrice = DuesCalculator.PriceFor(rule, isStudent);

            decimal normalSum = 0m;
            foreach (var m in attendedMonths)
                normalSum += DuesCalculator
                    .Calculate(m.Sessions, isStudent, RulesForMonth(allRules, m.Year, m.Month))
                    .TotalDue;

            // normalSum == 0 means there is no applicable per-month tier at all
            // (the period pass is the only rule), so it must NOT count as cheaper.
            if (normalSum > 0m && normalSum <= periodPrice) continue;

            for (int i = 0; i < attendedMonths.Count; i++)
            {
                var m = attendedMonths[i];
                result[(m.Year, m.Month)] = i == 0
                    ? (periodPrice, "period pass")
                    : (0m, "covered by period pass");
            }
        }

        return result;
    }
}
