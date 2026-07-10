using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Tests.ViewModels;

/// <summary>
/// Tests FencerDetailsVm — the computed view model shown when a fencer is
/// selected on the Fencers page. Verifies that given data produces the correct
/// UI text and flags.
/// </summary>
public class FencerDetailsVmTests
{
    [Fact]
    public void PaymentSummary_WhenPaid()
    {
        var vm = CreateVm(sessions: 3, amountDue: 0m, isPaid: true);

        vm.PaymentSummary.Should().Contain("All fees paid");
        vm.OwesMoney.Should().BeFalse();
        vm.IsPaid.Should().BeTrue();
    }

    [Fact]
    public void PaymentSummary_WhenOwes()
    {
        var vm = CreateVm(sessions: 4, amountDue: 5000m, isPaid: false);

        vm.PaymentSummary.Should().Contain("Owes");
        vm.PaymentSummary.Should().Contain("5");
        vm.PaymentSummary.Should().Contain("4 session");
        vm.OwesMoney.Should().BeTrue();
    }

    [Fact]
    public void PaymentSummary_ZeroSessions()
    {
        var vm = CreateVm(sessions: 0, amountDue: 0m, isPaid: true);

        // IsPaid takes priority — "All fees paid" even when 0 sessions
        vm.PaymentSummary.Should().Contain("All fees paid");
        vm.OwesMoney.Should().BeFalse();
    }

    [Fact]
    public void Initials_SingleName()
    {
        var fencer = new Fencer { Name = "Alice" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        vm.Initials.Should().Be("A");
    }

    [Fact]
    public void Initials_TwoNames()
    {
        var fencer = new Fencer { Name = "Alice Smith" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        vm.Initials.Should().Be("AS");
    }

    [Fact]
    public void Initials_ThreeNames()
    {
        var fencer = new Fencer { Name = "Alice van Smith" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        // First + Last
        vm.Initials.Should().Be("AS");
    }

    [Fact]
    public void Initials_EmptyName()
    {
        var fencer = new Fencer { Name = "" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        vm.Initials.Should().Be("?");
    }

    [Fact]
    public void HasUsername_True_WhenSet()
    {
        var fencer = new Fencer { Name = "Alice", Username = "alice42" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        vm.HasUsername.Should().BeTrue();
    }

    [Fact]
    public void HasUsername_False_WhenEmpty()
    {
        var fencer = new Fencer { Name = "Alice", Username = "" };
        var vm = new FencerDetailsVm(fencer, 0, 0, true);

        vm.HasUsername.Should().BeFalse();
    }

    [Fact]
    public void RecentSessions_Populated()
    {
        var fencer = new Fencer { Name = "Bob" };
        var sessions = new[]
        {
            new FencerSessionRow("Longsword", new DateTime(2024, 6, 1)),
            new FencerSessionRow("Messer", new DateTime(2024, 6, 8)),
        };

        var vm = new FencerDetailsVm(fencer, 2, 0, true, sessions,
            activeMonthsText: "Jun 2024",
            averageAttendanceText: "2.0 avg · 1 mo",
            mostAttendanceText: "2 in Jun 2024",
            oneOnOneReceived: 3,
            oneOnOneGiven: 0);

        vm.RecentSessions.Should().HaveCount(2);
        vm.HasRecentSessions.Should().BeTrue();
        vm.ActiveMonthsText.Should().Contain("Jun");
        vm.HasActiveMonths.Should().BeTrue();
        vm.AverageAttendanceText.Should().Contain("2.0");
        vm.MostAttendanceText.Should().Contain("Jun");
        vm.OneOnOneReceived.Should().Be(3);
        vm.OneOnOneGiven.Should().Be(0);
    }

    [Fact]
    public void ShowOneOnOneGiven_OnlyForInstructors()
    {
        var instructor = new Fencer { Name = "Coach", IsInstructor = true };
        var vm = new FencerDetailsVm(instructor, 0, 0, true);

        vm.ShowOneOnOneGiven.Should().BeTrue();

        var student = new Fencer { Name = "Student", IsInstructor = false };
        var vm2 = new FencerDetailsVm(student, 0, 0, true);
        vm2.ShowOneOnOneGiven.Should().BeFalse();
    }

    [Fact]
    public void FencerSessionRow_DateText_FormatCorrect()
    {
        var row = new FencerSessionRow("Topic", new DateTime(2024, 6, 15));

        row.DateText.Should().Contain("2024-06-15");
        row.Topic.Should().Be("Topic");
    }

    [Fact]
    public void FencerSessionRow_EmptyTopic_ShowsPlaceholder()
    {
        var row = new FencerSessionRow("", new DateTime(2024, 6, 1));

        row.Topic.Should().Be("(no topic)");
    }

    private static FencerDetailsVm CreateVm(int sessions, decimal amountDue, bool isPaid) =>
        new(new Fencer { Name = "Test Fencer" }, sessions, amountDue, isPaid);
}
