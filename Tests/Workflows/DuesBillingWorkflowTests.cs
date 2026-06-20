using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests the DuesCalculator in real-world monthly billing scenarios
/// (multi-month credit rollover, rule changes mid-year, etc.).
/// </summary>
public class DuesBillingWorkflowTests
{
    [Fact]
    public void MonthlyBilling_OverpaymentRollsForward()
    {
        // Month 1: attend 1 session, pay 5000. Due = 3500. Overpayment = 1500.
        var m1 = DuesCalculator.Calculate(1, isStudent: false, alreadyPaid: 5000m);
        m1.TotalDue.Should().Be(3500m);
        m1.Overpayment.Should().Be(1500m);
        m1.IsCovered.Should().BeTrue();

        // Month 2: attend 1 session, credit carried from month 1 (1500).
        var m2 = DuesCalculator.Calculate(1, isStudent: false, alreadyPaid: m1.Overpayment);
        m2.TotalDue.Should().Be(3500m);
        m2.Outstanding.Should().Be(2000m); // 3500 - 1500
        m2.IsCovered.Should().BeFalse();
    }

    [Fact]
    public void MonthlyBilling_ZeroAttendance_CreditPreserved()
    {
        // Month 1: prepay 12000 with zero attendance.
        var m1 = DuesCalculator.Calculate(0, isStudent: false, alreadyPaid: 12000m);
        m1.TotalDue.Should().Be(0m);
        m1.Overpayment.Should().Be(12000m);
        m1.IsOverpaid.Should().BeTrue();
        m1.IsCovered.Should().BeTrue();

        // Month 2: attend 5 sessions, use all the credit.
        var m2 = DuesCalculator.Calculate(5, isStudent: false, alreadyPaid: m1.Overpayment);
        m2.TotalDue.Should().Be(12000m); // unlimited pass
        m2.Outstanding.Should().Be(0m);
        m2.Overpayment.Should().Be(0m);
        m2.IsCovered.Should().BeTrue();
    }

    [Fact]
    public void MonthlyBilling_StudentDiscount_ThroughoutSeason()
    {
        // Student attends 4 sessions/month for 3 months.
        var sessions = 4;
        decimal totalPaidOverSeason = 0m;

        for (int month = 0; month < 3; month++)
        {
            var result = DuesCalculator.Calculate(sessions, isStudent: true);
            totalPaidOverSeason += result.TotalDue;
        }

        // Student should pay less than a non-student over the same period.
        var nonStudentTotal = DuesCalculator.Calculate(4, isStudent: false).TotalDue * 3;
        totalPaidOverSeason.Should().BeLessThan(nonStudentTotal);
    }

    [Fact]
    public void MonthlyBilling_RuleChange_NewerRuleTakesPrecedence()
    {
        var oldRules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2023, 1, 1) },
            new() { SessionCount = 1, FullPrice = 3000m, StudentPrice = 2000m,
                    StartDate = new DateTime(2023, 1, 1) }
        };

        // Before price change: 5 sessions ? unlimited = 10000
        var before = DuesCalculator.Calculate(5, isStudent: false, rules: oldRules);
        before.TotalDue.Should().Be(10000m);

        // After price change: same tier, newer date
        var newRules = new List<PriceRule>
        {
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2023, 1, 1) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2024, 1, 1) }, // newer!
            new() { SessionCount = 1, FullPrice = 3000m, StudentPrice = 2000m,
                    StartDate = new DateTime(2023, 1, 1) }
        };

        var after = DuesCalculator.Calculate(5, isStudent: false, rules: newRules);
        after.TotalDue.Should().Be(14000m); // newer unlimited rule wins
    }

    [Fact]
    public void MonthlyBilling_FlexibleAttendance_PicksCheapest()
    {
        // 3 sessions: single = 3×3500 = 10500, 4-pass = 9000 (applicable since 3 ? 4).
        var result = DuesCalculator.Calculate(3, isStudent: false);
        result.TotalDue.Should().Be(9000m);
        result.TierLabel.Should().Be("4-session pass");
    }

    [Fact]
    public void MonthlyBilling_ExactlyAtPackBoundary()
    {
        // 4 sessions: single = 4×3500 = 14000, 4-pass = 9000 (exact fit), unlimited = 12000.
        var result = DuesCalculator.Calculate(4, isStudent: false);
        result.TotalDue.Should().Be(9000m);
        result.TierLabel.Should().Be("4-session pass");
    }

    [Fact]
    public void MonthlyBilling_GraduateFromStudentMidSeason()
    {
        // Same attendance, different pricing.
        var asStudent = DuesCalculator.Calculate(5, isStudent: true);
        var asNonStudent = DuesCalculator.Calculate(5, isStudent: false);

        asStudent.TotalDue.Should().BeLessThan(asNonStudent.TotalDue);
    }
}
