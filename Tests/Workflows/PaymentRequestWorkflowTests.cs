using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Moq;

namespace JustAnotherHemaClub.Tests.Workflows;

public class PaymentRequestWorkflowTests
{
    private static AuthService CreateAuth(Fencer fencer)
    {
        var sp = new Mock<IServiceProvider>();
        var store = new Mock<ICredentialStore>();
        var auth = new AuthService(sp.Object, store.Object);
        auth.RestoreSession(fencer);
        return auth;
    }

    [Fact]
    public void PaymentRequestRow_FormatsPropertiesCorrectly()
    {
        var req = new PaymentRequest
        {
            Id = "pr-1",
            FencerId = "f-1",
            Year = 2026,
            Month = 3,
            Amount = 12000m,
            Note = "Full month",
            Status = PaymentRequestStatus.Pending
        };

        var row = new PaymentRequestRow(req, "Anna Varga");

        row.FencerId.Should().Be("f-1");
        row.FencerName.Should().Be("Anna Varga");
        row.PeriodText.Should().Be("2026-03");
        row.AmountText.Should().EndWith("Ft");
        row.HasNote.Should().BeTrue();
        row.StatusText.Should().Be("Pending");
    }

    [Fact]
    public void PaymentRequestRow_EmptyNote_HasNoteIsFalse()
    {
        var req = new PaymentRequest { Note = "   " };
        var row = new PaymentRequestRow(req, "Anna");

        row.HasNote.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitPaymentRequest_CreatesPendingRequestInService()
    {
        var data = new TestDataService();
        var auth = CreateAuth(new Fencer { Id = "demo-f1", Name = "Anna Varga", Active = true, IsStudent = true });
        var dialogs = new Mock<IDialogService>();
        var pricesVm = new PricesViewModel(data, auth, dialogs.Object);
        var vm = new FinanceViewModel(data, auth, pricesVm, dialogs.Object, data);

        await vm.LoadAsync();

        vm.CanSendPaymentRequest.Should().BeTrue();
        vm.BeginSendPaymentRequestCommand.Execute(null);
        vm.IsSendingPaymentRequest.Should().BeTrue();

        vm.RequestYear = 2026;
        vm.RequestMonth = 5;
        vm.RequestAmountText = "7000";
        vm.RequestNote = "Student monthly pass";

        await vm.SubmitPaymentRequestCommand.ExecuteAsync(null);

        vm.IsSendingPaymentRequest.Should().BeFalse();
        var allReqs = await data.GetPaymentRequestsAsync();
        allReqs.Should().ContainSingle(r =>
            r.FencerId == "demo-f1" &&
            r.Year == 2026 &&
            r.Month == 5 &&
            r.Amount == 7000m &&
            r.Note == "Student monthly pass" &&
            r.Status == PaymentRequestStatus.Pending);
    }

    [Fact]
    public async Task SubmitPaymentRequest_InvalidAmount_ShowsDialogAndDoesNotSave()
    {
        var data = new TestDataService();
        var auth = CreateAuth(new Fencer { Id = "demo-f1", Name = "Anna Varga", Active = true });
        var dialogs = new Mock<IDialogService>();
        var pricesVm = new PricesViewModel(data, auth, dialogs.Object);
        var vm = new FinanceViewModel(data, auth, pricesVm, dialogs.Object, data);

        vm.RequestAmountText = "-500";
        await vm.SubmitPaymentRequestCommand.ExecuteAsync(null);

        dialogs.Verify(d => d.ShowAsync("Invalid amount", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        (await data.GetPaymentRequestsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task InstructorAcceptsPaymentRequest_MarksPaidAndUpdatesStatusToAccepted()
    {
        var data = new TestDataService();
        var req = new PaymentRequest
        {
            Id = "req-accept-1",
            FencerId = "demo-f1",
            Year = 2026,
            Month = 4,
            Amount = 7000m,
            Note = "Pay Full month",
            Status = PaymentRequestStatus.Pending,
            RequestedByFencerId = "demo-f1"
        };
        await data.UpsertPaymentRequestAsync(req);

        var auth = CreateAuth(new Fencer { Id = "test-001", Name = "Coach", Active = true, IsInstructor = true });
        var dialogs = new Mock<IDialogService>();
        var pricesVm = new PricesViewModel(data, auth, dialogs.Object);
        var vm = new FinanceViewModel(data, auth, pricesVm, dialogs.Object, data);

        await vm.LoadAsync();
        vm.PendingPaymentRequests.Should().ContainSingle(r => r.Request.Id == "req-accept-1");

        var row = vm.PendingPaymentRequests.First(r => r.Request.Id == "req-accept-1");
        await vm.AcceptPaymentRequestCommand.ExecuteAsync(row);

        var updatedReq = (await data.GetPaymentRequestsAsync()).Single(r => r.Id == "req-accept-1");
        updatedReq.Status.Should().Be(PaymentRequestStatus.Accepted);
        updatedReq.ReviewedByInstructorId.Should().Be("test-001");
        updatedReq.ReviewedAtUtc.Should().NotBeNull();

        var payments = await data.GetPaymentsAsync(2026, 4);
        payments.Should().ContainSingle(p => p.FencerId == "demo-f1" && p.Amount == 7000m);
    }

    [Fact]
    public async Task InstructorRejectsPaymentRequest_DoesNotRecordPaymentAndMarksRejected()
    {
        var data = new TestDataService();
        var req = new PaymentRequest
        {
            Id = "req-reject-1",
            FencerId = "demo-f2",
            Year = 2026,
            Month = 4,
            Amount = 12000m,
            Status = PaymentRequestStatus.Pending,
            RequestedByFencerId = "demo-f2"
        };
        await data.UpsertPaymentRequestAsync(req);

        var auth = CreateAuth(new Fencer { Id = "test-001", Name = "Coach", Active = true, IsInstructor = true });
        var dialogs = new Mock<IDialogService>();
        var pricesVm = new PricesViewModel(data, auth, dialogs.Object);
        var vm = new FinanceViewModel(data, auth, pricesVm, dialogs.Object, data);

        await vm.LoadAsync();
        var row = vm.PendingPaymentRequests.First(r => r.Request.Id == "req-reject-1");
        await vm.RejectPaymentRequestCommand.ExecuteAsync(row);

        var updatedReq = (await data.GetPaymentRequestsAsync()).Single(r => r.Id == "req-reject-1");
        updatedReq.Status.Should().Be(PaymentRequestStatus.Rejected);
        updatedReq.ReviewedByInstructorId.Should().Be("test-001");

        var payments = await data.GetPaymentsAsync(2026, 4);
        payments.Should().NotContain(p => p.FencerId == "demo-f2" && p.Amount == 12000m);
    }
}
