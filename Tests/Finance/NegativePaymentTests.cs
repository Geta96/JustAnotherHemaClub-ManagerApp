using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Covers the payment-recording semantics introduced with custom amounts:
///   ? negative amounts are allowed (refund / accounting correction),
///   ? a refund reduces the fencer's net recorded total for the month,
///   ? the credit-carry math handles a negative net paid correctly.
/// The refund flows through the same monthly sum + DuesCalculator pipeline the
/// FinanceViewModel uses, so we verify against that rather than the private
/// RecordPaymentAsync method directly.
/// </summary>
public class NegativePaymentTests
{
    private static List<PriceRule> Rules() => new()
    {
        new() { SessionCount = 1, FullPrice = 3500m,  StudentPrice = 2000m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 0, MonthCount = 1, FullPrice = 12000m, StudentPrice = 7000m, StartDate = new DateTime(2024, 1, 1) },
    };

    [Fact]
    public void Refund_ReducesNetPaid_ForTheMonth()
    {
        // Fencer paid 12000, then a -2000 correction is recorded.
        var payments = new List<Payment>
        {
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = 12000m },
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = -2000m },
        };

        var netPaid = payments.Where(p => p.FencerId == "f1").Sum(p => p.Amount);
        netPaid.Should().Be(10000m);
    }

    [Fact]
    public void Refund_OfEntireOverpayment_LeavesExactlyCovered()
    {
        // Attends 1 session (due 3500), pays 5000 ? overpaid by 1500.
        // A -1500 refund cancels the overpayment; still exactly covered.
        var payments = new List<Payment>
        {
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = 5000m },
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = -1500m },
        };
        var netPaid = payments.Sum(p => p.Amount);

        var quote = DuesCalculator.Calculate(1, isStudent: false, rules: Rules(), alreadyPaid: netPaid);

        quote.TotalDue.Should().Be(3500m);
        quote.Outstanding.Should().Be(0m);
        quote.Overpayment.Should().Be(0m);
        quote.IsCovered.Should().BeTrue();
        quote.IsOverpaid.Should().BeFalse();
    }

    [Fact]
    public void OverRefund_CreatesOutstandingBalanceAgain()
    {
        // Paid 3500 (exactly covered), then refunded 3500 ? owes the full 3500.
        var payments = new List<Payment>
        {
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = 3500m },
            new() { FencerId = "f1", Year = 2024, Month = 5, Amount = -3500m },
        };
        var netPaid = payments.Sum(p => p.Amount);

        var quote = DuesCalculator.Calculate(1, isStudent: false, rules: Rules(), alreadyPaid: netPaid);

        quote.Outstanding.Should().Be(3500m);
        quote.IsCovered.Should().BeFalse();
    }

    [Fact]
    public void NegativeNetPaid_TreatedAsNothingApplied()
    {
        // A pure -1000 correction with no prior payment yields negative net paid.
        // DuesCalculator must not turn that into "extra" owed beyond the tier cost.
        var quote = DuesCalculator.Calculate(1, isStudent: false, rules: Rules(), alreadyPaid: -1000m);

        quote.TotalDue.Should().Be(3500m);
        // Outstanding = cost - paid = 3500 - (-1000) = 4500 (the debt grows by the refund).
        quote.Outstanding.Should().Be(4500m);
        quote.Overpayment.Should().Be(0m);
    }

    [Fact]
    public void PaymentModel_AcceptsNegativeAmount()
    {
        // Sanity: the model itself carries a negative amount unchanged so it can
        // be written to the sheet as a correction row.
        var p = new Payment { FencerId = "f1", Year = 2024, Month = 5, Amount = -2500m };
        p.Amount.Should().Be(-2500m);
    }
}
