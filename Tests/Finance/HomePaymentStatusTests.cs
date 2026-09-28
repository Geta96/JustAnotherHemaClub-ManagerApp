using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Covers the Home page "Payment status" card classification:
///   ? Overpaid                                   ? "Overpayed by X Ft"  (green)
///   ? Everything settled                         ? "All payed up"        (green)
///   ? Only THIS month's dues outstanding         ? "Due X by the end of this month" (grey)
///   ? Also owes for a PRIOR month                ? "Due X"               (red)
/// These mirror HomeViewModel.LoadPaymentStatusAsync, which walks attended months
/// chronologically, carrying overpayment credit forward, and splits the residual
/// into current-month vs prior-month buckets.
/// </summary>
public class HomePaymentStatusTests
{
    private enum Status { AllPaid, Overpaid, DueThisMonth, DueWithArrears }

    private static List<PriceRule> Rules() => new()
    {
        new() { SessionCount = 1, FullPrice = 3500m,  StudentPrice = 2000m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 0, MonthCount = 1, FullPrice = 12000m, StudentPrice = 7000m, StartDate = new DateTime(2024, 1, 1) },
    };

    /// <summary>
    /// Pure re-implementation of the HomeViewModel classification. Each entry is
    /// (year, month, sessionsAttended, cashPaidThatMonth). The last month in the
    /// list is treated as "this month".
    /// </summary>
    private static (Status Status, decimal Amount) Classify(
        List<(int Y, int M, int Sessions, decimal Paid)> months, bool isStudent = false)
    {
        var rules = Rules();
        var thisYm = (months[^1].Y, months[^1].M);

        decimal creditIn = 0m, prior = 0m, thisMonth = 0m, finalCredit = 0m;

        foreach (var (y, m, sessions, paid) in months)
        {
            var monthRules = rules
                .Where(r => r.StartDate.Date <= new DateTime(y, m, 1).AddMonths(1).AddDays(-1) &&
                            (r.EndDate is null || r.EndDate.Value.Date >= new DateTime(y, m, 1)))
                .ToList();

            var quote = DuesCalculator.Calculate(sessions, isStudent, monthRules, paid + creditIn);

            if ((y, m) == thisYm) thisMonth += quote.Outstanding;
            else prior += quote.Outstanding;

            creditIn = quote.Overpayment;
            finalCredit = quote.Overpayment;
        }

        if (prior > 0m) return (Status.DueWithArrears, prior + thisMonth);
        if (thisMonth > 0m) return (Status.DueThisMonth, thisMonth);
        if (finalCredit > 0m) return (Status.Overpaid, finalCredit);
        return (Status.AllPaid, 0m);
    }

    [Fact]
    public void AllPaid_WhenEveryMonthExactlyCovered()
    {
        var result = Classify(new()
        {
            (2024, 4, 1, 3500m),   // exactly covered
            (2024, 5, 1, 3500m),   // exactly covered (this month)
        });

        result.Status.Should().Be(Status.AllPaid);
        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void Overpaid_WhenCreditRemainsAfterThisMonth()
    {
        var result = Classify(new()
        {
            (2024, 5, 1, 5000m),   // due 3500, overpaid by 1500 (this month)
        });

        result.Status.Should().Be(Status.Overpaid);
        result.Amount.Should().Be(1500m);
    }

    [Fact]
    public void DueThisMonth_WhenOnlyCurrentMonthOutstanding_PriorSettled()
    {
        var result = Classify(new()
        {
            (2024, 4, 1, 3500m),   // prior month fully paid
            (2024, 5, 1, 0m),      // this month unpaid ? 3500 due
        });

        result.Status.Should().Be(Status.DueThisMonth);
        result.Amount.Should().Be(3500m);
    }

    [Fact]
    public void DueWithArrears_WhenPriorMonthAlsoOutstanding()
    {
        var result = Classify(new()
        {
            (2024, 4, 1, 0m),      // prior month unpaid ? 3500 arrears
            (2024, 5, 1, 0m),      // this month unpaid  ? 3500
        });

        result.Status.Should().Be(Status.DueWithArrears);
        result.Amount.Should().Be(7000m); // arrears + this month
    }

    [Fact]
    public void PriorOverpayment_CoversThisMonth_ShowsAllPaid()
    {
        var result = Classify(new()
        {
            (2024, 4, 5, 12000m),  // unlimited pass, exactly covered, no credit
            (2024, 5, 1, 3500m),   // exactly covered this month
        });

        result.Status.Should().Be(Status.AllPaid);
    }

    [Fact]
    public void PriorCredit_DrawnDown_ToCoverCurrentMonth()
    {
        var result = Classify(new()
        {
            (2024, 4, 1, 7000m),   // due 3500, overpaid 3500 ? credit forward
            (2024, 5, 1, 0m),      // due 3500 but 3500 credit covers it ? all paid
        });

        result.Status.Should().Be(Status.AllPaid);
    }

    [Fact]
    public void PriorCredit_PartiallyCovers_LeavesThisMonthDue()
    {
        var result = Classify(new()
        {
            (2024, 4, 1, 5000m),   // due 3500, overpaid 1500 ? credit forward
            (2024, 5, 1, 0m),      // due 3500 ? 1500 credit = 2000 this month
        });

        result.Status.Should().Be(Status.DueThisMonth);
        result.Amount.Should().Be(2000m);
    }
}
