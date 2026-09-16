using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Covers the FencerDueRow presentation state and the PaymentOptionVm "Kind"
/// discriminator that drives the distinct button styling on the Finance page.
/// </summary>
public class FencerDueRowTests
{
    private static FencerDueRow Row(int sessions, decimal cost, decimal paid, decimal creditIn = 0m)
    {
        var quote = DuesCalculator.FixedQuote(sessions, cost, paid + creditIn, "test tier");
        return new FencerDueRow(new Fencer { Id = "f1", Name = "Test" }, quote, paid, creditIn);
    }

    [Fact]
    public void FreshBill_IsNotPaid_AndReportsAmountDue()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 0m);

        row.IsPaid.Should().BeFalse();
        row.IsNotPaid.Should().BeTrue();
        row.AmountDue.Should().Be(3500m);
    }

    [Fact]
    public void ExactlyPaid_FlagsExactlyPaid_NotOverpaid()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 3500m);

        row.IsPaid.Should().BeTrue();
        row.IsExactlyPaid.Should().BeTrue();
        row.IsOverpaid.Should().BeFalse();
    }

    [Fact]
    public void Overpaid_FlagsOverpaid_WithCorrectCredit()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 5000m);

        row.IsPaid.Should().BeTrue();
        row.IsOverpaid.Should().BeTrue();
        row.IsExactlyPaid.Should().BeFalse();
        row.Overpayment.Should().Be(1500m);
    }

    [Fact]
    public void ApplyTopUp_ClearsBalance_AndMarksPaid()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 0m);

        row.ApplyTopUp(3500m);

        row.AmountDue.Should().Be(0m);
        row.IsPaid.Should().BeTrue();
        row.AlreadyPaid.Should().Be(3500m);
    }

    [Fact]
    public void ApplyTopUp_Partial_LeavesUpgradeState()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 0m);

        row.ApplyTopUp(1500m);

        row.AmountDue.Should().Be(2000m);
        row.IsPaid.Should().BeFalse();
        row.IsUpgrade.Should().BeTrue();
    }

    [Fact]
    public void ApplyTopUp_IgnoresNonPositive()
    {
        var row = Row(sessions: 1, cost: 3500m, paid: 0m);

        row.ApplyTopUp(0m);
        row.ApplyTopUp(-500m);

        row.AmountDue.Should().Be(3500m);
        row.AlreadyPaid.Should().Be(0m);
    }

    [Theory]
    [InlineData("remaining", false, false, true)]
    [InlineData("primary", true, false, false)]
    [InlineData("pass", false, true, false)]
    public void PaymentOptionVm_KindFlags_AreExclusive(
        string kind, bool isPrimary, bool isPass, bool isRemaining)
    {
        var vm = new PaymentOptionVm("x", () => Task.CompletedTask, kind);

        vm.IsPrimary.Should().Be(isPrimary);
        vm.IsPass.Should().Be(isPass);
        vm.IsRemaining.Should().Be(isRemaining);
    }
}
