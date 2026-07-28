using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Snapshot of a single fencer's monthly dues.
///
/// <see cref="TotalDue"/> is the cost of the cheapest applicable membership
/// for the given attendance; <see cref="EffectivePaid"/> is whatever funds
/// the caller said were available for that month — i.e. the sum of cash
/// payments recorded for this fencer/month plus any credit carried in from
/// prior overpayments. <see cref="Outstanding"/> is what's still owed,
/// and <see cref="Overpayment"/> is the amount paid above the tier cost
/// (rolled forward as credit by the caller).
/// </summary>
public readonly record struct DuesQuote(
    int SessionsAttended,
    decimal TotalDue,
    decimal EffectivePaid,
    decimal Outstanding,
    decimal Overpayment,
    string TierLabel,
    bool IsCovered,
    bool IsOverpaid);

/// <summary>
/// Computes monthly dues from a set of <see cref="PriceRule"/>s.
///
/// Selection policy:
///   1. Drop rules that can't cover this much attendance (a 4-pack cannot
///      bill 5 sessions).
///   2. When two or more rules share the same (SessionCount, MonthCount) tier,
///      keep only the newest one — latest StartDate, then highest FullPrice
///      as a deterministic secondary tie-break.
///   3. Across tiers the cheapest per-month cost wins.
///   4. The caller passes <c>alreadyPaid</c> as the total funds
///      available for this month (cash this month + credit carried forward
///      from prior overpayments). The calculator returns Outstanding =
///      max(0, cost − alreadyPaid) and Overpayment = max(0, alreadyPaid −
///      cost). It's the caller's job to roll Overpayment into next month's
///      alreadyPaid if it wants credit to carry across months.
///
/// SessionCount mapping:
///   0   → unlimited pass (always applicable when attendance ≥ 1).
///             MonthCount = 1 → standard monthly pass (cost = price).
///             MonthCount = 2 → two-month pass (cost = price ÷ 2 per month).
///   1   → single-session ticket (cost = price × attendance).
///   N>1 → N-session pack (applicable iff attendance ≤ N; cost = flat price).
///
/// If the Prices sheet is empty we fall back to the historical defaults so
/// the Finance page still produces sensible numbers on a fresh install.
/// </summary>
public static class DuesCalculator
{
    // Fallback defaults — only used when no PriceRules have been configured yet.
    public const decimal SinglePrice    = 3500m;
    public const decimal HalfPassPrice  = 9000m;
    public const decimal FullPassPrice  = 12000m;
    public const decimal StudentMultiplier = 0.60m;

    private static readonly PriceRule[] DefaultRules =
    {
        new() { SessionCount = 1, FullPrice = SinglePrice,
                StudentPrice = SuggestStudentPrice(SinglePrice) },
        new() { SessionCount = 4, FullPrice = HalfPassPrice,
                StudentPrice = SuggestStudentPrice(HalfPassPrice) },
        new() { SessionCount = 0, MonthCount = 1, FullPrice = FullPassPrice,
                StudentPrice = SuggestStudentPrice(FullPassPrice) },
    };

    public static DuesQuote Calculate(
        int sessionsAttended,
        bool isStudent,
        IReadOnlyList<PriceRule>? rules = null,
        decimal alreadyPaid = 0m)
    {
        // No attendance: nothing owed regardless of funds. A positive
        // alreadyPaid here is pure credit going forward.
        if (sessionsAttended <= 0)
            return new DuesQuote(
                SessionsAttended: 0,
                TotalDue:         0m,
                EffectivePaid:    alreadyPaid,
                Outstanding:      0m,
                Overpayment:      Math.Max(0m, alreadyPaid),
                TierLabel:        "—",
                IsCovered:        true,
                IsOverpaid:       alreadyPaid > 0m);

        var effective = (rules is null || rules.Count == 0) ? DefaultRules : rules;

        // Group by (SessionCount, MonthCount) and pick the newest within each tier.
        var perTier = effective
            .Where(r => IsApplicable(r, sessionsAttended))
            .GroupBy(r => (r.SessionCount, r.MonthCount))
            .Select(g => g
                .OrderByDescending(r => r.StartDate.Date)
                .ThenByDescending(r => r.FullPrice)
                .First())
            .ToList();

        decimal bestCost  = decimal.MaxValue;
        string  bestLabel = "—";

        foreach (var r in perTier)
        {
            var price = isStudent ? r.StudentPrice : r.FullPrice;
            decimal cost;
            string  label;

            if (r.SessionCount == 0)
            {
                // Amortize multi-month pass cost per calendar month.
                var months = Math.Max(1, r.MonthCount);
                cost  = price / months;
                label = months == 1 ? "unlimited monthly pass"
                                    : $"unlimited {months}-month pass";
            }
            else if (r.SessionCount == 1)
            {
                cost  = price * sessionsAttended;
                label = "single ticket";
            }
            else
            {
                cost  = price;
                label = $"{r.SessionCount}-session pass";
            }

            if (cost < bestCost) { bestCost = cost; bestLabel = label; }
        }

        if (bestCost == decimal.MaxValue)
            return new DuesQuote(
                SessionsAttended: sessionsAttended,
                TotalDue:         0m,
                EffectivePaid:    alreadyPaid,
                Outstanding:      0m,
                Overpayment:      Math.Max(0m, alreadyPaid),
                TierLabel:        "no applicable rule",
                IsCovered:        true,
                IsOverpaid:       alreadyPaid > 0m);

        var outstanding = Math.Max(0m, bestCost    - alreadyPaid);
        var overpayment = Math.Max(0m, alreadyPaid - bestCost);

        return new DuesQuote(
            SessionsAttended: sessionsAttended,
            TotalDue:         bestCost,
            EffectivePaid:    alreadyPaid,
            Outstanding:      outstanding,
            Overpayment:      overpayment,
            TierLabel:        bestLabel,
            IsCovered:        outstanding == 0m,
            IsOverpaid:       overpayment > 0m);
    }

    private static bool IsApplicable(PriceRule r, int sessionsAttended) =>
        r.SessionCount switch
        {
            0 => true,                                  // unlimited covers everything
            1 => true,                                  // single ticket scales by attendance
            _ => sessionsAttended <= r.SessionCount,    // pack must cover attendance
        };

    /// <summary>Suggested starting point for a student price — roughly 60% of the
    /// full price, rounded to the nearest 500 Ft. Instructors can override it with
    /// any custom amount when creating or editing a price rule.</summary>
    public static decimal SuggestStudentPrice(decimal fullPrice)
    {
        if (fullPrice <= 0) return 0m;
        var raw = fullPrice * StudentMultiplier;
        return Math.Round(raw / 500m, MidpointRounding.AwayFromZero) * 500m;
    }
}