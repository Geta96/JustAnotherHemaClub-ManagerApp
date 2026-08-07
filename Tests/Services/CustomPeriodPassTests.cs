using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Tests the "custom period" unlimited pass: a fencer who attends at least once
/// inside a rule's [StartDate, EndDate] window owes the full unlimited price a
/// single time for the whole window. These tests cover the calculator-level
/// building blocks (FixedQuote + the fact that custom-period rules are excluded
/// from normal per-month billing) as well as end-to-end month-spanning scenarios
/// that mirror FinanceViewModel.BuildPeriodPasses.
/// </summary>
public class CustomPeriodPassTests
{
    [Fact]
    public void CustomPeriodRule_IsExcludedFromNormalPerMonthBilling()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, IsCustomPeriod = true,
                    FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 7, 1),
                    EndDate   = new DateTime(2026, 8, 31) },
        };

        // With only a custom-period rule available, normal monthly Calculate
        // finds no applicable per-month tier and falls back to "nothing owed".
        var quote = DuesCalculator.Calculate(2, isStudent: false, rules: rules);

        quote.TotalDue.Should().Be(0m);
    }

    [Fact]
    public void FixedQuote_ChargesFullPeriodPrice_Once()
    {
        // First attended month gets the full price, nothing paid yet.
        var quote = DuesCalculator.FixedQuote(
            sessionsAttended: 2, totalDue: 10000m, alreadyPaid: 0m, tierLabel: "period pass");

        quote.TotalDue.Should().Be(10000m);
        quote.Outstanding.Should().Be(10000m);
        quote.IsCovered.Should().BeFalse();
        quote.TierLabel.Should().Be("period pass");
    }

    [Fact]
    public void FixedQuote_CoveredMonths_OweNothing()
    {
        // Subsequent months in the same window are billed at 0.
        var quote = DuesCalculator.FixedQuote(
            sessionsAttended: 1, totalDue: 0m, alreadyPaid: 0m, tierLabel: "covered by period pass");

        quote.TotalDue.Should().Be(0m);
        quote.Outstanding.Should().Be(0m);
        quote.IsCovered.Should().BeTrue();
    }

    [Fact]
    public void FixedQuote_Overpayment_RollsForward()
    {
        var quote = DuesCalculator.FixedQuote(
            sessionsAttended: 2, totalDue: 10000m, alreadyPaid: 12000m, tierLabel: "period pass");

        quote.Overpayment.Should().Be(2000m);
        quote.IsOverpaid.Should().BeTrue();
        quote.IsCovered.Should().BeTrue();
    }

    [Fact]
    public void PriceFor_UsesStudentPrice_ForStudents()
    {
        var rule = new PriceRule { FullPrice = 10000m, StudentPrice = 6000m };

        DuesCalculator.PriceFor(rule, isStudent: false).Should().Be(10000m);
        DuesCalculator.PriceFor(rule, isStudent: true).Should().Be(6000m);
    }

    // ======================================================================
    // End-to-end month-spanning scenarios (mirror BuildPeriodPasses).
    // ======================================================================

    /// <summary>
    /// The exact scenario from the feature request: a summer period pass covering
    /// July+August at 10,000 Ft. Whoever attends at least once anywhere in the
    /// window owes 10,000 once — regardless of how many times or which month.
    /// </summary>
    [Fact]
    public void SummerPeriodPass_EveryoneWhoAttends_Pays10kOnce()
    {
        var rule = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 8, 31)
        };
        var allRules = new List<PriceRule> { rule };

        // fencer1: 5 sessions across Jul+Aug, fencer2: 1 in Jul, fencer3: 2 in Aug.
        var f1 = new Dictionary<(int, int), int> { [(2026, 7)] = 3, [(2026, 8)] = 2 };
        var f2 = new Dictionary<(int, int), int> { [(2026, 7)] = 1 };
        var f3 = new Dictionary<(int, int), int> { [(2026, 8)] = 2 };

        var months = new[] { (2026, 7), (2026, 8) };

        BillPeriod(allRules, months, f1, isStudent: false).Should().Be(10000m);
        BillPeriod(allRules, months, f2, isStudent: false).Should().Be(10000m);
        BillPeriod(allRules, months, f3, isStudent: false).Should().Be(10000m);
    }

    [Fact]
    public void PeriodPass_ChargeLandsOnFirstAttendedMonth_OthersCovered()
    {
        var rule = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 8, 31)
        };
        var months = new[] { (2026, 7), (2026, 8) };
        var attendance = new Dictionary<(int, int), int> { [(2026, 7)] = 1, [(2026, 8)] = 4 };

        var perMonth = AssignPeriod(new List<PriceRule> { rule }, months, attendance, isStudent: false);

        // Full charge on July (first attended), August covered at 0.
        perMonth[(2026, 7)].TotalDue.Should().Be(10000m);
        perMonth[(2026, 8)].TotalDue.Should().Be(0m);
    }

    [Fact]
    public void PeriodPass_StudentPays_DiscountedPeriodPrice()
    {
        var rule = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 8, 31)
        };
        var months = new[] { (2026, 7), (2026, 8) };
        var attendance = new Dictionary<(int, int), int> { [(2026, 7)] = 2 };

        BillPeriod(new List<PriceRule> { rule }, months, attendance, isStudent: true)
            .Should().Be(6000m);
    }

    [Fact]
    public void PeriodPass_NotApplied_WhenNormalBillingIsCheaper()
    {
        // Period price 10,000 but a cheap single-ticket rule also exists. A fencer
        // who only shows up once should pay the cheaper single ticket, not 10k.
        var period = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 8, 31)
        };
        var single = new PriceRule
        {
            SessionCount = 1, FullPrice = 3500m, StudentPrice = 2000m,
            StartDate = new DateTime(2026, 1, 1)
        };
        var allRules = new List<PriceRule> { period, single };
        var months = new[] { (2026, 7), (2026, 8) };
        var attendance = new Dictionary<(int, int), int> { [(2026, 7)] = 1 };

        // 1 single ticket (3,500) is cheaper than the 10,000 period, so the
        // period pass is skipped and normal billing applies.
        BillPeriod(allRules, months, attendance, isStudent: false).Should().Be(3500m);
    }

    [Fact]
    public void PeriodPass_NotCharged_ToFencerWhoNeverAttended()
    {
        var rule = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 8, 31)
        };
        var months = new[] { (2026, 7), (2026, 8) };
        var attendance = new Dictionary<(int, int), int>(); // never attended

        BillPeriod(new List<PriceRule> { rule }, months, attendance, isStudent: false)
            .Should().Be(0m);
    }

    [Fact]
    public void PeriodPass_DoesNotRepeat_AfterWindowEnds()
    {
        // Window is July only; attendance in September must not be billed the
        // period price (the period does not repeat).
        var rule = new PriceRule
        {
            SessionCount = 0, IsCustomPeriod = true,
            FullPrice = 10000m, StudentPrice = 6000m,
            StartDate = new DateTime(2026, 7, 1),
            EndDate   = new DateTime(2026, 7, 31)
        };
        var months = new[] { (2026, 7), (2026, 9) };
        var attendance = new Dictionary<(int, int), int> { [(2026, 7)] = 1, [(2026, 9)] = 1 };

        var perMonth = AssignPeriod(new List<PriceRule> { rule }, months, attendance, isStudent: false);

        perMonth[(2026, 7)].TotalDue.Should().Be(10000m);  // in-window: charged
        perMonth.ContainsKey((2026, 9)).Should().BeFalse(); // out-of-window: not touched by the period
    }

    // ======================== HELPERS ========================
    // These mirror FinanceViewModel.BuildPeriodPasses so the billing policy can
    // be verified without spinning up the whole view model / sheets stack.

    /// <summary>Returns the forced (TotalDue, Label) override per month for a single fencer.</summary>
    private static Dictionary<(int Y, int M), (decimal TotalDue, string Label)> AssignPeriod(
        IReadOnlyList<PriceRule> allRules,
        IReadOnlyList<(int Y, int M)> monthsAscending,
        Dictionary<(int, int), int> attendance,
        bool isStudent)
    {
        var result = new Dictionary<(int Y, int M), (decimal, string)>();

        var periodRules = allRules
            .Where(r => r.IsCustomPeriod && r.SessionCount == 0 && r.EndDate is not null)
            .ToList();

        foreach (var rule in periodRules)
        {
            var from = rule.StartDate.Date;
            var to   = rule.EndDate!.Value.Date;

            var attendedMonths = monthsAscending
                .Where(ym =>
                {
                    var monthStart = new DateTime(ym.Y, ym.M, 1);
                    var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd < from || monthStart > to) return false;
                    return attendance.TryGetValue(ym, out var att) && att > 0;
                })
                .ToList();

            if (attendedMonths.Count == 0) continue;

            var periodPrice = DuesCalculator.PriceFor(rule, isStudent);

            decimal normalSum = 0m;
            foreach (var ym in attendedMonths)
            {
                attendance.TryGetValue(ym, out var att);
                var monthRules = allRules
                    .Where(r => r.StartDate.Date <= new DateTime(ym.Y, ym.M, 1).AddMonths(1).AddDays(-1) &&
                                (r.EndDate is null || r.EndDate.Value.Date >= new DateTime(ym.Y, ym.M, 1)))
                    .ToList();
                normalSum += DuesCalculator.Calculate(att, isStudent, monthRules).TotalDue;
            }

            if (normalSum > 0m && normalSum <= periodPrice) continue;

            for (int i = 0; i < attendedMonths.Count; i++)
            {
                var ym = attendedMonths[i];
                result[ym] = i == 0
                    ? (periodPrice, "period pass")
                    : (0m, "covered by period pass");
            }
        }

        return result;
    }

    /// <summary>Total a fencer pays across the window: period override where present, else normal billing.</summary>
    private static decimal BillPeriod(
        IReadOnlyList<PriceRule> allRules,
        IReadOnlyList<(int Y, int M)> monthsAscending,
        Dictionary<(int, int), int> attendance,
        bool isStudent)
    {
        var overrides = AssignPeriod(allRules, monthsAscending, attendance, isStudent);
        decimal total = 0m;

        foreach (var ym in monthsAscending)
        {
            attendance.TryGetValue(ym, out var att);

            if (overrides.TryGetValue(ym, out var ov))
            {
                total += ov.TotalDue;
                continue;
            }

            if (att == 0) continue;

            var monthRules = allRules
                .Where(r => r.StartDate.Date <= new DateTime(ym.Y, ym.M, 1).AddMonths(1).AddDays(-1) &&
                            (r.EndDate is null || r.EndDate.Value.Date >= new DateTime(ym.Y, ym.M, 1)))
                .ToList();
            total += DuesCalculator.Calculate(att, isStudent, monthRules).TotalDue;
        }

        return total;
    }
}
