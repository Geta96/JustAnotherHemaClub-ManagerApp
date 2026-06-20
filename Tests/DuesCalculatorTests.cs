using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests;

public class DuesCalculatorTests
{
    [Fact]
    public void Calculate_ZeroAttendance_NothingOwed()
    {
        var result = DuesCalculator.Calculate(0, isStudent: false);

        result.TotalDue.Should().Be(0m);
        result.Outstanding.Should().Be(0m);
        result.IsCovered.Should().BeTrue();
        result.TierLabel.Should().Be("—");
    }

    [Fact]
    public void Calculate_ZeroAttendance_WithCredit_ReturnsOverpayment()
    {
        var result = DuesCalculator.Calculate(0, isStudent: false, alreadyPaid: 5000m);

        result.TotalDue.Should().Be(0m);
        result.Overpayment.Should().Be(5000m);
        result.IsOverpaid.Should().BeTrue();
    }

    [Fact]
    public void Calculate_DefaultRules_SingleSession()
    {
        var result = DuesCalculator.Calculate(1, isStudent: false);

        // Single ticket = 3500 × 1 = 3500
        result.TotalDue.Should().Be(3500m);
        result.TierLabel.Should().Be("single ticket");
    }

    [Fact]
    public void Calculate_DefaultRules_FourSessions_PicksHalfPass()
    {
        // 4 sessions: single = 4×3500 = 14000, half-pass = 9000 (cheaper)
        var result = DuesCalculator.Calculate(4, isStudent: false);

        result.TotalDue.Should().Be(9000m);
        result.TierLabel.Should().Be("4-session pass");
    }

    [Fact]
    public void Calculate_DefaultRules_FiveSessions_PicksFullPass()
    {
        // 5 sessions: single = 5×3500 = 17500, full pass = 12000 (cheaper)
        // 4-pack not applicable (attendance > 4)
        var result = DuesCalculator.Calculate(5, isStudent: false);

        result.TotalDue.Should().Be(12000m);
        result.TierLabel.Should().Be("unlimited monthly pass");
    }

    [Fact]
    public void Calculate_Student_GetsDiscountedPrice()
    {
        // Student single = 2000 (60% of 3500, rounded to 500)
        var result = DuesCalculator.Calculate(1, isStudent: true);

        result.TotalDue.Should().BeLessThan(3500m);
    }

    [Fact]
    public void Calculate_PartiallyPaid_ShowsOutstanding()
    {
        var result = DuesCalculator.Calculate(1, isStudent: false, alreadyPaid: 2000m);

        result.TotalDue.Should().Be(3500m);
        result.Outstanding.Should().Be(1500m);
        result.IsCovered.Should().BeFalse();
    }

    [Fact]
    public void Calculate_Overpaid_ShowsOverpayment()
    {
        var result = DuesCalculator.Calculate(1, isStudent: false, alreadyPaid: 5000m);

        result.TotalDue.Should().Be(3500m);
        result.Outstanding.Should().Be(0m);
        result.Overpayment.Should().Be(1500m);
        result.IsOverpaid.Should().BeTrue();
        result.IsCovered.Should().BeTrue();
    }

    [Fact]
    public void Calculate_CustomRules_PicksCheapestApplicable()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 15000m, StudentPrice = 9000m }, // unlimited
            new() { SessionCount = 6, FullPrice = 10000m, StudentPrice = 6000m }, // 6-pack
            new() { SessionCount = 1, FullPrice = 4000m, StudentPrice = 2500m },  // single
        };

        // 5 sessions: single = 5×4000 = 20000, 6-pack = 10000, unlimited = 15000
        var result = DuesCalculator.Calculate(5, isStudent: false, rules: rules);

        result.TotalDue.Should().Be(10000m);
        result.TierLabel.Should().Be("6-session pass");
    }

    [Fact]
    public void Calculate_CustomRules_PackNotApplicableWhenAttendanceExceedsIt()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 4, FullPrice = 8000m, StudentPrice = 5000m }, // 4-pack
            new() { SessionCount = 1, FullPrice = 3000m, StudentPrice = 2000m }, // single
        };

        // 5 sessions: 4-pack NOT applicable, single = 5×3000 = 15000
        var result = DuesCalculator.Calculate(5, isStudent: false, rules: rules);

        result.TotalDue.Should().Be(15000m);
        result.TierLabel.Should().Be("single ticket");
    }

    [Fact]
    public void Calculate_MultipleRulesForSameTier_PicksNewest()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 12000m, StudentPrice = 7000m, StartDate = new DateTime(2023, 1, 1) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m, StartDate = new DateTime(2024, 1, 1) }, // newer
        };

        var result = DuesCalculator.Calculate(8, isStudent: false, rules: rules);

        result.TotalDue.Should().Be(14000m); // newest rule wins
    }

    [Fact]
    public void Calculate_EmptyRules_UsesDefaults()
    {
        var result = DuesCalculator.Calculate(1, isStudent: false, rules: new List<PriceRule>());

        result.TotalDue.Should().Be(3500m); // fallback single ticket price
    }

    [Fact]
    public void SuggestStudentPrice_RoundsTo500()
    {
        // 60% of 12000 = 7200, rounded to nearest 500 = 7000
        DuesCalculator.SuggestStudentPrice(12000m).Should().Be(7000m);

        // 60% of 3500 = 2100, rounded to nearest 500 = 2000
        DuesCalculator.SuggestStudentPrice(3500m).Should().Be(2000m);

        // 60% of 9000 = 5400, rounded to nearest 500 = 5500
        DuesCalculator.SuggestStudentPrice(9000m).Should().Be(5500m);
    }

    [Fact]
    public void SuggestStudentPrice_ZeroReturnsZero()
    {
        DuesCalculator.SuggestStudentPrice(0m).Should().Be(0m);
    }
}
