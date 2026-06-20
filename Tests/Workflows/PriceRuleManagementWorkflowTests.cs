using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests the PriceRule management workflow — adding, overriding, and
/// date-based activation of pricing rules.
/// </summary>
public class PriceRuleManagementWorkflowTests
{
    [Fact]
    public void AddNewRule_AppliesImmediately()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 1, FullPrice = 4000m, StudentPrice = 2500m,
                    StartDate = DateTime.Today }
        };

        var result = DuesCalculator.Calculate(3, isStudent: false, rules: rules);

        result.TotalDue.Should().Be(12000m); // 3 × 4000
        result.TierLabel.Should().Be("single ticket");
    }

    [Fact]
    public void AddPackRule_PickedWhenCheaper()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 1, FullPrice = 4000m, StudentPrice = 2500m,
                    StartDate = DateTime.Today },
            new() { SessionCount = 5, FullPrice = 15000m, StudentPrice = 9000m,
                    StartDate = DateTime.Today }
        };

        // 4 sessions: single = 16000, 5-pack = 15000 (applicable since 4 ? 5)
        var result = DuesCalculator.Calculate(4, isStudent: false, rules: rules);
        result.TotalDue.Should().Be(15000m);
        result.TierLabel.Should().Be("5-session pass");
    }

    [Fact]
    public void OverrideRule_NewerDateWins()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2023, 1, 1) },
            new() { SessionCount = 0, FullPrice = 12000m, StudentPrice = 7000m,
                    StartDate = new DateTime(2024, 6, 1) }, // price increase
            new() { SessionCount = 1, FullPrice = 3500m, StudentPrice = 2000m,
                    StartDate = new DateTime(2023, 1, 1) }
        };

        // 5 sessions: single = 5×3500 = 17500, unlimited (new) = 12000
        var result = DuesCalculator.Calculate(5, isStudent: false, rules: rules);
        result.TotalDue.Should().Be(12000m);
    }

    [Fact]
    public void StudentPrice_SuggestsRoundedValue()
    {
        // 60% of 15000 = 9000, rounded to 500 = 9000
        DuesCalculator.SuggestStudentPrice(15000m).Should().Be(9000m);

        // 60% of 13000 = 7800, rounded to 500 = 8000
        DuesCalculator.SuggestStudentPrice(13000m).Should().Be(8000m);

        // 60% of 4500 = 2700, rounded to 500 = 2500
        DuesCalculator.SuggestStudentPrice(4500m).Should().Be(2500m);
    }

    [Fact]
    public void NoRules_FallsBackToDefaults()
    {
        var result = DuesCalculator.Calculate(3, isStudent: false, rules: new List<PriceRule>());

        // Default: single = 3500, 4-pack = 9000 (applicable since 3 ? 4), unlimited = 12000
        // Cheapest for 3: 4-pack at 9000 vs single 3×3500 = 10500
        result.TotalDue.Should().Be(9000m);
    }

    [Fact]
    public void IsActiveOn_FiltersDateBoundedRules()
    {
        var expiredRule = new PriceRule
        {
            SessionCount = 0, FullPrice = 8000m, StudentPrice = 5000m,
            StartDate = new DateTime(2023, 1, 1),
            EndDate = new DateTime(2023, 12, 31)
        };

        var currentRule = new PriceRule
        {
            SessionCount = 0, FullPrice = 12000m, StudentPrice = 7000m,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = null
        };

        // The expired rule isn't active today
        expiredRule.IsActiveOn(DateTime.Today).Should().BeFalse();
        currentRule.IsActiveOn(DateTime.Today).Should().BeTrue();
    }

    [Fact]
    public void MultiTierPricing_ComplexScenario()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 1,  FullPrice = 3500m, StudentPrice = 2000m,
                    StartDate = new DateTime(2024, 1, 1) },
            new() { SessionCount = 4,  FullPrice = 9000m, StudentPrice = 5500m,
                    StartDate = new DateTime(2024, 1, 1) },
            new() { SessionCount = 8,  FullPrice = 16000m, StudentPrice = 10000m,
                    StartDate = new DateTime(2024, 1, 1) },
            new() { SessionCount = 0,  FullPrice = 20000m, StudentPrice = 12000m,
                    StartDate = new DateTime(2024, 1, 1) },
        };

        // 2 sessions: single = 7000, 4-pack = 9000, 8-pack = 16000, unlimited = 20000
        DuesCalculator.Calculate(2, false, rules).TotalDue.Should().Be(7000m);

        // 5 sessions: single = 17500, 8-pack = 16000, unlimited = 20000
        // (4-pack not applicable since 5 > 4)
        DuesCalculator.Calculate(5, false, rules).TotalDue.Should().Be(16000m);

        // 9 sessions: single = 31500, unlimited = 20000
        // (4-pack and 8-pack not applicable)
        DuesCalculator.Calculate(9, false, rules).TotalDue.Should().Be(20000m);
    }
}
