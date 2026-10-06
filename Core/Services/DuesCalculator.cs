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
///      max(0, cost ? alreadyPaid) and Overpayment = max(0, alreadyPaid ?
///      cost). It's the caller's job to roll Overpayment into next month's
///      alreadyPaid if it wants credit to carry across months.
///
/// SessionCount mapping:
///   0   ? unlimited pass (always applicable when attendance ? 1).
///             MonthCount = 1 ? standard monthly pass (cost = price).
///             MonthCount = 2 ? two-month pass (cost = price ÷ 2 per month).
///   1   ? single-session ticket (cost = price × attendance).
///   N>1 ? N-session pack (applicable iff attendance ? N; cost = flat price).
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
            .Where(r => !r.IsCustomPeriod)          // custom-period passes are billed once per window, not per month
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

    /// <summary>The price a given fencer pays for a rule (student vs full).</summary>
    public static decimal PriceFor(PriceRule rule, bool isStudent) =>
        isStudent ? rule.StudentPrice : rule.FullPrice;

    /// <summary>
    /// Builds a <see cref="DuesQuote"/> for a month whose cost is fixed by an
    /// external policy (e.g. a custom-period pass charged once for a whole
    /// window). <paramref name="totalDue"/> is the gross cost placed on this
    /// month; outstanding / overpayment are derived from <paramref name="alreadyPaid"/>.
    /// </summary>
    public static DuesQuote FixedQuote(
        int sessionsAttended, decimal totalDue, decimal alreadyPaid, string tierLabel)
    {
        var outstanding = Math.Max(0m, totalDue    - alreadyPaid);
        var overpayment = Math.Max(0m, alreadyPaid - totalDue);
        return new DuesQuote(
            SessionsAttended: sessionsAttended,
            TotalDue:         totalDue,
            EffectivePaid:    alreadyPaid,
            Outstanding:      outstanding,
            Overpayment:      overpayment,
            TierLabel:        tierLabel,
            IsCovered:        outstanding == 0m,
            IsOverpaid:       overpayment > 0m);
    }

    /// <summary>
    /// Turns a fencer's payment position into a human-friendly description of what
    /// they have effectively pre-paid for, rather than the misleading "Overpayed
    /// by X" wording.
    ///
    /// <paramref name="effectivePaidThisMonth"/> is the total funds committed
    /// toward the current month (cash paid this month + credit carried in), and
    /// <paramref name="forwardCredit"/> is the surplus that will carry into future
    /// months. The distinction matters: a fencer who pays the full monthly fee up
    /// front but has only attended one session so far is billed the cheaper
    /// single-session tier, leaving a large "credit" � yet they have simply
    /// "Payed for the month", not overpaid.
    ///
    /// The logic mirrors how a fencer actually buys ahead:
    ///   � If the money put toward this month covers (at least) the biggest
    ///     whole-period pass (unlimited monthly / multi-month / custom period),
    ///     they have "Payed for the month". Any surplus above that pass is a
    ///     genuine overpay ("� with X Ft overpay").
    ///   � Otherwise the forward credit is expressed in prepaid sessions at the
    ///     cheapest per-session rate (so buying a 4-session pass reads as "Payed
    ///     for 4 more sessions"). Any leftover below one more session is overpay.
    ///   � When nothing can interpret the position we fall back to the plain
    ///     "Overpayed by X Ft" wording.
    /// </summary>
    public static string DescribeOverpayment(
        decimal effectivePaidThisMonth,
        decimal forwardCredit,
        bool isStudent,
        IReadOnlyList<PriceRule>? rules = null)
    {
        var (primary, overpay) = DescribeOverpaymentParts(
            effectivePaidThisMonth, forwardCredit, isStudent, rules);
        return string.IsNullOrEmpty(overpay) ? primary : $"{primary} {overpay}";
    }

    /// <summary>
    /// Same interpretation as <see cref="DescribeOverpayment"/>, but returns the
    /// wording split into two parts so the UI can show them on separate lines:
    ///   � <c>Primary</c> � "Payed for the month" / "Payed for X more sessions"
    ///     (or the "Overpayed by X Ft" fallback).
    ///   � <c>Overpay</c> � the optional "with X Ft overpay" suffix, or an empty
    ///     string when there is no leftover overpay.
    /// Keeping the two-line split here means the web and MAUI UIs share a single
    /// source of truth for the green payment wording.
    /// </summary>
    public static (string Primary, string Overpay) DescribeOverpaymentParts(
        decimal effectivePaidThisMonth,
        decimal forwardCredit,
        bool isStudent,
        IReadOnlyList<PriceRule>? rules = null)
    {
        if (effectivePaidThisMonth <= 0m && forwardCredit <= 0m) return ("", "");

        var effective = (rules is null || rules.Count == 0) ? DefaultRules : rules;

        // Biggest whole-period pass (unlimited monthly / multi-month / custom period).
        decimal monthPrice = 0m;
        // Cheapest per-session rate across all session-based tiers (single + packs).
        decimal perSession = 0m;

        foreach (var r in effective)
        {
            var price = PriceFor(r, isStudent);
            if (price <= 0m) continue;

            if (r.SessionCount == 0)
            {
                if (price > monthPrice) monthPrice = price;
            }
            else
            {
                var per = price / r.SessionCount;
                if (perSession == 0m || per < perSession) perSession = per;
            }
        }

        // Paid enough THIS month to cover a whole month/period pass � they are
        // simply covered for the month, even if the cheaper per-session tier was
        // billed because only a few sessions have happened so far.
        if (monthPrice > 0m && effectivePaidThisMonth >= monthPrice)
        {
            var overpay = effectivePaidThisMonth - monthPrice;
            return ("Payed for the month",
                    overpay > 0m ? $"with {overpay:N0} Ft overpay" : "");
        }

        // Otherwise express the carried credit as prepaid future sessions.
        if (perSession > 0m && forwardCredit > 0m)
        {
            var sessions = (int)Math.Floor(forwardCredit / perSession);
            if (sessions >= 1)
            {
                var overpay = forwardCredit - sessions * perSession;
                var s = $"Payed for {sessions} more session{(sessions == 1 ? "" : "s")}";
                return (s, overpay > 0m ? $"with {overpay:N0} Ft overpay" : "");
            }
        }

        // Nothing could interpret the position � plain overpay wording.
        return ($"Overpayed by {forwardCredit:N0} Ft", "");
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
