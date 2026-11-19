using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Moq;

namespace JustAnotherHemaClub.Tests.Notifications;

public class NotificationSyncServiceTests
{
    private readonly Mock<IGoogleSheetsService> _sheets = new();
    private readonly AuthService _auth;
    private readonly Mock<INotificationSettings> _settings = new();
    private readonly Mock<INotificationService> _notifications = new();

    private NotificationSyncService CreateSut() =>
        new(_sheets.Object, _auth, _settings.Object, _notifications.Object);

    public NotificationSyncServiceTests()
    {
        Preferences.Default.Clear();

        var sp = new Mock<IServiceProvider>();
        var store = new Mock<ICredentialStore>();
        _auth = new AuthService(sp.Object, store.Object);

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer>());
        _sheets.Setup(s => s.GetPaymentRequestsAsync()).ReturnsAsync(new List<PaymentRequest>());
        _sheets.Setup(s => s.GetPaymentsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<Payment>());
        _sheets.Setup(s => s.GetNewsAsync()).ReturnsAsync(new List<NewsItem>());
        _sheets.Setup(s => s.GetTrainingsAsync()).ReturnsAsync(new List<TrainingSession>());
        _sheets.Setup(s => s.GetPriceRulesAsync()).ReturnsAsync(new List<PriceRule>());
    }

    [Fact]
    public async Task CheckAndNotifyAsync_GuestOrNullUser_DoesNothing()
    {
        await CreateSut().CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        _auth.LoginAsGuest();
        await CreateSut().CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_AllNotificationsDisabled_DoesNotFetchOrShow()
    {
        _auth.RestoreSession(new Fencer { Id = "f-disabled", Name = "User", IsInstructor = true });
        _settings.Setup(s => s.IsEnabled("f-disabled")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("f-disabled")).Returns(false);

        await CreateSut().CheckAndNotifyAsync();

        _sheets.Verify(s => s.GetNewsAsync(), Times.Never);
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_NewNewsItem_ShowsNotificationOnce()
    {
        _auth.RestoreSession(new Fencer { Id = "f-news", Name = "User" });
        _settings.Setup(s => s.IsEnabled("f-news")).Returns(true);

        var news = new List<NewsItem>
        {
            new() { Id = "news-100", Title = "Club Picnic", Body = "Bring swords and snacks!", CreatedAtUtc = DateTime.UtcNow }
        };
        _sheets.Setup(s => s.GetNewsAsync()).ReturnsAsync(news);

        var sut = CreateSut();
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Club Picnic",
            "Bring swords and snacks!"), Times.Once);

        // Second call should not re-notify for already seen news
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Club Picnic",
            "Bring swords and snacks!"), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_InstructorNotifications_PendingPaymentRequestAndPasswordReset()
    {
        _auth.RestoreSession(new Fencer { Id = "inst-1", Name = "Coach", IsInstructor = true });
        _settings.Setup(s => s.IsEnabled("inst-1")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("inst-1")).Returns(true);

        var student = new Fencer
        {
            Id = "f-stud",
            Name = "Anna Varga",
            PendingPasswordHash = "HASH123",
            PasswordResetRequestedAtUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { student });

        var pendingReq = new PaymentRequest
        {
            Id = "req-99",
            FencerId = "f-stud",
            Year = 2026,
            Month = 4,
            Amount = 7000m,
            Status = PaymentRequestStatus.Pending
        };
        _sheets.Setup(s => s.GetPaymentRequestsAsync()).ReturnsAsync(new List<PaymentRequest> { pendingReq });

        var sut = CreateSut();
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New payment request",
            It.Is<string>(msg => msg.Contains("Anna Varga") && msg.Contains("2026-04"))), Times.Once);

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Password reset requested",
            It.Is<string>(msg => msg.Contains("Anna Varga"))), Times.Once);

        // Calling again should not duplicate notifications
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CheckAndNotifyAsync_AcceptedPaymentRequest_NotifiesFencerAndSuppressesDuplicatePaymentNotification()
    {
        var today = DateTime.Today;
        _auth.RestoreSession(new Fencer { Id = "f-me", Name = "Member" });
        _settings.Setup(s => s.IsEnabled("f-me")).Returns(true);

        var acceptedReq = new PaymentRequest
        {
            Id = "req-acc-1",
            FencerId = "f-me",
            Year = today.Year,
            Month = today.Month,
            Amount = 12000m,
            Status = PaymentRequestStatus.Accepted,
            ReviewedAtUtc = DateTime.UtcNow
        };
        var matchingPayment = new Payment
        {
            FencerId = "f-me",
            Year = today.Year,
            Month = today.Month,
            Amount = 12000m,
            PaidOn = DateTime.Now
        };

        _sheets.Setup(s => s.GetPaymentRequestsAsync()).ReturnsAsync(new List<PaymentRequest> { acceptedReq });
        _sheets.Setup(s => s.GetPaymentsAsync(today.Year, today.Month)).ReturnsAsync(new List<Payment> { matchingPayment });

        await CreateSut().CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Payment request accepted",
            It.Is<string>(msg => msg.Contains($"{today.Year}-{today.Month:00}"))), Times.Once);

        // Duplicate "Payment recorded" notification should be suppressed when request was just accepted
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Payment recorded",
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_DirectPaymentRecorded_NotifiesFencer()
    {
        var today = DateTime.Today;
        _auth.RestoreSession(new Fencer { Id = "f-direct", Name = "Member" });
        _settings.Setup(s => s.IsEnabled("f-direct")).Returns(true);

        var payment = new Payment
        {
            FencerId = "f-direct",
            Year = today.Year,
            Month = today.Month,
            Amount = 9000m,
            PaidOn = DateTime.Now
        };
        _sheets.Setup(s => s.GetPaymentsAsync(today.Year, today.Month)).ReturnsAsync(new List<Payment> { payment });

        await CreateSut().CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Payment recorded",
            It.Is<string>(msg => msg.Contains($"{today.Year}-{today.Month:00}"))), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_RejectedPaymentRequest_NotifiesRequesterOnce()
    {
        _auth.RestoreSession(new Fencer { Id = "f-me", Name = "Member" });
        _settings.Setup(s => s.IsEnabled("f-me")).Returns(true);

        var rejectedReq = new PaymentRequest
        {
            Id = "req-rej-1",
            FencerId = "f-me",
            RequestedByFencerId = "f-me",
            Year = 2026,
            Month = 4,
            Amount = 7000m,
            Status = PaymentRequestStatus.Rejected,
            ReviewedAtUtc = DateTime.UtcNow
        };
        _sheets.Setup(s => s.GetPaymentRequestsAsync()).ReturnsAsync(new List<PaymentRequest> { rejectedReq });

        var sut = CreateSut();
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Payment request rejected",
            It.Is<string>(msg => msg.Contains("2026-04"))), Times.Once);

        // Calling again should not duplicate notification
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "Payment request rejected",
            It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("Approved", "Password reset approved")]
    [InlineData("Rejected", "Password reset rejected")]
    public async Task CheckAndNotifyAsync_PasswordResetReviewed_NotifiesRequesterOnce(string status, string expectedTitle)
    {
        var reviewedAt = new DateTime(2026, 4, 2, 15, 30, 0, DateTimeKind.Utc);
        var fencer = new Fencer
        {
            Id = "f-pw",
            Name = "Member",
            LastPasswordResetStatus = status,
            LastPasswordResetReviewedAtUtc = reviewedAt
        };
        _auth.RestoreSession(fencer);
        _settings.Setup(s => s.IsEnabled("f-pw")).Returns(true);
        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { fencer });

        var sut = CreateSut();
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            expectedTitle,
            It.IsAny<string>()), Times.Once);

        // Calling again should not duplicate notification
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            expectedTitle,
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProfileViewModel_LoadsAndSavesNotificationPreferences()
    {
        var fencer = new Fencer
        {
            Id = "inst-prof",
            Name = "Coach",
            Email = "coach@club.hu",
            Active = true,
            IsInstructor = true
        };
        _auth.RestoreSession(fencer);

        _settings.Setup(s => s.IsEnabled("inst-prof")).Returns(true);
        _settings.Setup(s => s.IsInstructorEnabled("inst-prof")).Returns(false);
        _settings.Setup(s => s.EnsurePermissionAsync()).ReturnsAsync(true);

        var vm = new ProfileViewModel(_auth, _sheets.Object, _settings.Object);
        await vm.LoadAsync();

        vm.NotificationsEnabled.Should().BeTrue();
        vm.InstructorNotificationsEnabled.Should().BeFalse();

        vm.NotificationsEnabled = false;
        vm.InstructorNotificationsEnabled = true;
        await vm.SaveAsync();

        _settings.Verify(s => s.EnsurePermissionAsync(), Times.Once);
        _settings.Verify(s => s.SetEnabled("inst-prof", false), Times.Once);
        _settings.Verify(s => s.SetInstructorEnabled("inst-prof", true), Times.Once);
        vm.StatusMessage.Should().Be("Profile saved.");
    }
}
