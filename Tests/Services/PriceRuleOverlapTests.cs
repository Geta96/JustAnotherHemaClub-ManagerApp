using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Tests price-rule overlap detection and how overlapping / changing rules and
/// dates resolve at billing time. The overlap policy mirrors
/// PricesViewModel.RulesOverlap (two rules conflict only when they share the
/// same tier — SessionCount, MonthCount and IsCustomPeriod — and their
/// [StartDate, EndDate] intervals intersect, with a null EndDate meaning
/// open-ended). The billing side is exercised through the public DuesCalculator,
/// which resolves same-tier overlaps by preferring the newest StartDate.
/// </summary>
public class PriceRuleOverlapTests
{
    // ---- Mirrors PricesViewModel.RulesOverlap so the policy can be asserted. ----
    private static bool RulesOverlap(PriceRule a, PriceRule b)
    {
        if (a.IsCustomPeriod != b.IsCustomPeriod) return false;
        if (a.SessionCount   != b.SessionCount)   return false;
        if (a.MonthCount     != b.MonthCount)     return false;

        var aStart = a.StartDate.Date;
        var aEnd   = a.EndDate?.Date ?? DateTime.MaxValue.Date;
        var bStart = b.StartDate.Date;
        var bEnd   = b.EndDate?.Date ?? DateTime.MaxValue.Date;

        return aStart <= bEnd && bStart <= aEnd;
    }

    // ======================== Overlap detection ========================

    [Fact]
    public void Overlap_SameTier_IntersectingDates_Conflicts()
    {
        var a = new PriceRule { SessionCount = 0, FullPrice = 10000m,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) };
        var b = new PriceRule { SessionCount = 0, FullPrice = 12000m,
            StartDate = new DateTime(2026, 5, 1), EndDate = new DateTime(2026, 12, 31) };

        RulesOverlap(a, b).Should().BeTrue();
    }

    [Fact]
    public void Overlap_SameTier_DisjointDates_NoConflict()
    {
        var a = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) };
        var b = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 12, 31) };

        RulesOverlap(a, b).Should().BeFalse();
    }

    [Fact]
    public void Overlap_DifferentTier_NeverConflicts_EvenWhenDatesIntersect()
    {
        var unlimited = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
        var single = new PriceRule { SessionCount = 1,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };

        RulesOverlap(unlimited, single).Should().BeFalse();
    }

    [Fact]
    public void Overlap_CustomPeriod_IsDistinctTier_FromStandardUnlimited()
    {
        // Both are SessionCount 0 / MonthCount 1, but the custom-period flag
        // makes them different tiers, so they do NOT conflict.
        var standard = new PriceRule { SessionCount = 0, MonthCount = 1, IsCustomPeriod = false,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31) };
        var period = new PriceRule { SessionCount = 0, MonthCount = 1, IsCustomPeriod = true,
            StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 8, 31) };

        RulesOverlap(standard, period).Should().BeFalse();
    }

    [Fact]
    public void Overlap_OpenEnded_NullEndDate_ExtendsToInfinity()
    {
        var openEnded = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 1, 1), EndDate = null };
        var future = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2030, 5, 1), EndDate = new DateTime(2030, 6, 30) };

        RulesOverlap(openEnded, future).Should().BeTrue();
    }

    [Fact]
    public void Overlap_TouchingBoundaries_AreInclusive_AndConflict()
    {
        // a ends exactly when b starts — inclusive intervals treat this as overlap.
        var a = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) };
        var b = new PriceRule { SessionCount = 0,
            StartDate = new DateTime(2026, 6, 30), EndDate = new DateTime(2026, 12, 31) };

        RulesOverlap(a, b).Should().BeTrue();
    }

    [Fact]
    public void Overlap_TwoCustomPeriods_SameWindow_Conflict()
    {
        var a = new PriceRule { SessionCount = 0, IsCustomPeriod = true, FullPrice = 10000m,
            StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 8, 31) };
        var b = new PriceRule { SessionCount = 0, IsCustomPeriod = true, FullPrice = 12000m,
            StartDate = new DateTime(2026, 8, 1), EndDate = new DateTime(2026, 9, 30) };

        RulesOverlap(a, b).Should().BeTrue();
    }

    // ======================== Billing under overlap / change ========================

    [Fact]
    public void OverlappingSameTier_NewerStartDate_WinsAtBilling()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 1, 1) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2026, 6, 1) }, // newer
        };

        var quote = DuesCalculator.Calculate(5, isStudent: false, rules: rules);

        quote.TotalDue.Should().Be(14000m); // newest StartDate wins
    }

    [Fact]
    public void OverlappingSameTier_SameStartDate_HigherPriceWins_Deterministically()
    {
        // Tie-break on identical StartDate is highest FullPrice (deterministic).
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 1, 1) },
            new() { SessionCount = 0, FullPrice = 13000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2026, 1, 1) },
        };

        var quote = DuesCalculator.Calculate(5, isStudent: false, rules: rules);

        quote.TotalDue.Should().Be(13000m);
    }

    [Fact]
    public void PriceChange_ViaDateWindows_BillsWindowSpecificPrice()
    {
        // Old price valid Jan–Jun, new price from Jul. Each month uses only the
        // rules whose window covers it (as FinanceViewModel filters per month).
        var allRules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 6, 30) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2026, 7, 1) },
        };

        List<PriceRule> RulesForMonth(int y, int m)
        {
            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1).AddDays(-1);
            return allRules.Where(r => r.StartDate.Date <= to &&
                                       (r.EndDate is null || r.EndDate.Value.Date >= from))
                           .ToList();
        }

        DuesCalculator.Calculate(5, false, RulesForMonth(2026, 3)).TotalDue.Should().Be(10000m);
        DuesCalculator.Calculate(5, false, RulesForMonth(2026, 9)).TotalDue.Should().Be(14000m);
    }

    [Fact]
    public void PriceChange_OverlapMonth_BothWindowsActive_NewerWins()
    {
        // In an overlap month (both rules cover it), the newer StartDate wins.
        var allRules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 7, 31) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2026, 7, 1) },
        };

        // July: both windows include it ? newer (14000) wins.
        var july = allRules.Where(r =>
            r.StartDate.Date <= new DateTime(2026, 7, 31) &&
            (r.EndDate is null || r.EndDate.Value.Date >= new DateTime(2026, 7, 1))).ToList();

        july.Should().HaveCount(2);
        DuesCalculator.Calculate(5, false, july).TotalDue.Should().Be(14000m);
    }

    [Fact]
    public void DateWindow_RuleNotYetStarted_IsNotApplicable()
    {
        // A future-dated rule shouldn't affect a month before its start.
        var allRules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2026, 7, 1) },
        };

        var mayRules = allRules.Where(r =>
            r.StartDate.Date <= new DateTime(2026, 5, 31) &&
            (r.EndDate is null || r.EndDate.Value.Date >= new DateTime(2026, 5, 1))).ToList();

        mayRules.Should().BeEmpty();

        // With no applicable configured rule, the calculator falls back to defaults.
        var quote = DuesCalculator.Calculate(5, false, mayRules);
        quote.TotalDue.Should().Be(DuesCalculator.FullPassPrice); // default unlimited
    }
}
