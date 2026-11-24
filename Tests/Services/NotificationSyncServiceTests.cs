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
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson>());
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

        // Notification toggles now save immediately on change (no shared Save button).
        vm.NotificationsEnabled = false;
        vm.InstructorNotificationsEnabled = true;

        // The save is fired-and-forgotten from the property setter; give it a tick to complete.
        await Task.Delay(10);

        _settings.Verify(s => s.EnsurePermissionAsync(), Times.AtLeastOnce);
        _settings.Verify(s => s.SetEnabled("inst-prof", false), Times.AtLeastOnce);
        _settings.Verify(s => s.SetInstructorEnabled("inst-prof", true), Times.AtLeastOnce);
        vm.StatusMessage.Should().Be("Saved.");
    }

    [Fact]
    public async Task CheckAndNotifyAsync_RequestedLesson_NotifiesOnlyTargetedInstructors()
    {
        var inst1 = new Fencer { Id = "inst-1", Name = "Coach A", IsInstructor = true };
        var inst2 = new Fencer { Id = "inst-2", Name = "Coach B", IsInstructor = true };
        var inst3 = new Fencer { Id = "inst-3", Name = "Coach C", IsInstructor = true };
        var student = new Fencer { Id = "stu-1", Name = "Alice", IsInstructor = false };

        var lessonReq = new IndividualLesson
        {
            Id = "lesson-req-1",
            StudentId = "stu-1",
            Date = new DateTime(2026, 5, 10, 18, 0, 0),
            Topic = "Longsword cuts",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst-1", "inst-2" }
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { inst1, inst2, inst3, student });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { lessonReq });

        var sut = CreateSut();

        // 1. Targeted instructor 1 receives notification once
        _auth.RestoreSession(inst1);
        _settings.Setup(s => s.IsEnabled("inst-1")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("inst-1")).Returns(true);
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.Is<string>(msg => msg.Contains("Alice") && msg.Contains("Longsword cuts"))), Times.Once);

        // Second check for inst-1 does not duplicate
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.IsAny<string>()), Times.Once);

        // 2. Targeted instructor 2 also receives notification
        _notifications.Invocations.Clear();
        _auth.RestoreSession(inst2);
        _settings.Setup(s => s.IsEnabled("inst-2")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("inst-2")).Returns(true);
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.Is<string>(msg => msg.Contains("Alice") && msg.Contains("Longsword cuts"))), Times.Once);

        // 3. Non-targeted instructor 3 does NOT receive notification
        _notifications.Invocations.Clear();
        _auth.RestoreSession(inst3);
        _settings.Setup(s => s.IsEnabled("inst-3")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("inst-3")).Returns(true);
        await sut.CheckAndNotifyAsync();

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_AcceptedLesson_NotifiesOnlyTargetedStudent()
    {
        var inst = new Fencer { Id = "inst-1", Name = "Coach A", IsInstructor = true };
        var student1 = new Fencer { Id = "stu-1", Name = "Alice" };
        var student2 = new Fencer { Id = "stu-2", Name = "Bob" };

        var acceptedLesson = new IndividualLesson
        {
            Id = "lesson-acc-1",
            StudentId = "stu-1",
            InstructorId = "inst-1",
            Date = new DateTime(2026, 5, 12, 17, 30, 0),
            Topic = "Messer",
            Status = IndividualLessonStatus.Accepted
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { inst, student1, student2 });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { acceptedLesson });

        var sut = CreateSut();

        // Other student (Bob) does NOT get notified
        _auth.RestoreSession(student2);
        _settings.Setup(s => s.IsEnabled("stu-2")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson scheduled",
            It.IsAny<string>()), Times.Never);

        // Targeted student (Alice) gets notified once
        _auth.RestoreSession(student1);
        _settings.Setup(s => s.IsEnabled("stu-1")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson scheduled",
            It.Is<string>(msg => msg.Contains("Coach A") && msg.Contains("Messer"))), Times.Once);

        // Second check does not duplicate
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson scheduled",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_RejectedLesson_NotifiesOnlyTargetedStudent()
    {
        var student1 = new Fencer { Id = "stu-1", Name = "Alice" };
        var student2 = new Fencer { Id = "stu-2", Name = "Bob" };

        var rejectedLesson = new IndividualLesson
        {
            Id = "lesson-rej-1",
            StudentId = "stu-1",
            Date = new DateTime(2026, 5, 15, 19, 0, 0),
            Topic = "Sabre",
            Status = IndividualLessonStatus.Rejected
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { student1, student2 });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { rejectedLesson });

        var sut = CreateSut();

        // Other student (Bob) does NOT get notified
        _auth.RestoreSession(student2);
        _settings.Setup(s => s.IsEnabled("stu-2")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson rejected",
            It.IsAny<string>()), Times.Never);

        // Targeted student (Alice) gets notified once
        _auth.RestoreSession(student1);
        _settings.Setup(s => s.IsEnabled("stu-1")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson rejected",
            It.Is<string>(msg => msg.Contains("Sabre") && msg.Contains("2026-05-15 19:00"))), Times.Once);

        // Second check does not duplicate
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson rejected",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_PartialReject_StillNotifiesRemainingTargetedInstructor_AndDoesNotNotifyStudentUntilAllReject()
    {
        var inst1 = new Fencer { Id = "inst-1", Name = "Coach A", IsInstructor = true };
        var inst2 = new Fencer { Id = "inst-2", Name = "Coach B", IsInstructor = true };
        var student = new Fencer { Id = "stu-1", Name = "Alice" };

        // Coach A already rejected, so only Coach B remains in RequestedInstructorIds; status is still Requested
        var lesson = new IndividualLesson
        {
            Id = "lesson-partial-1",
            StudentId = "stu-1",
            Date = new DateTime(2026, 5, 20, 18, 0, 0),
            Topic = "Dagger",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst-2" }
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { inst1, inst2, student });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { lesson });

        var sut = CreateSut();

        // Coach A is no longer in RequestedInstructorIds -> no notification
        _auth.RestoreSession(inst1);
        _settings.Setup(s => s.IsEnabled("inst-1")).Returns(true);
        _settings.Setup(s => s.IsInstructorEnabled("inst-1")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // Student does not receive a rejection notification while Coach B can still accept
        _auth.RestoreSession(student);
        _settings.Setup(s => s.IsEnabled("stu-1")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // Coach B is still targeted -> receives the request notification
        _auth.RestoreSession(inst2);
        _settings.Setup(s => s.IsEnabled("inst-2")).Returns(false);
        _settings.Setup(s => s.IsInstructorEnabled("inst-2")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.Is<string>(msg => msg.Contains("Alice") && msg.Contains("Dagger"))), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_InstructorRequestsFromOtherInstructors_NotifiesAskedInstructorsAndRequesterOnAccept()
    {
        var instRequester = new Fencer { Id = "inst-req", Name = "Coach Requester", IsInstructor = true };
        var instTarget1 = new Fencer { Id = "inst-t1", Name = "Coach Target 1", IsInstructor = true };
        var instTarget2 = new Fencer { Id = "inst-t2", Name = "Coach Target 2", IsInstructor = true };

        var lesson = new IndividualLesson
        {
            Id = "lesson-peer-1",
            StudentId = "inst-req",
            Date = new DateTime(2026, 6, 1, 16, 0, 0),
            Topic = "Ringen",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst-t1", "inst-t2" }
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer> { instRequester, instTarget1, instTarget2 });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { lesson });

        var sut = CreateSut();

        // Requesting instructor is not in RequestedInstructorIds, so does not get "New 1 on 1 lesson request"
        _auth.RestoreSession(instRequester);
        _settings.Setup(s => s.IsEnabled("inst-req")).Returns(true);
        _settings.Setup(s => s.IsInstructorEnabled("inst-req")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // Both target instructors receive the request notification
        _auth.RestoreSession(instTarget1);
        _settings.Setup(s => s.IsInstructorEnabled("inst-t1")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.Is<string>(msg => msg.Contains("Coach Requester") && msg.Contains("Ringen"))), Times.Once);

        _notifications.Invocations.Clear();
        _auth.RestoreSession(instTarget2);
        _settings.Setup(s => s.IsInstructorEnabled("inst-t2")).Returns(true);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "New 1 on 1 lesson request",
            It.Is<string>(msg => msg.Contains("Coach Requester") && msg.Contains("Ringen"))), Times.Once);

        // Now Coach Target 2 accepts the lesson -> Coach Requester (as student) gets notified
        _notifications.Invocations.Clear();
        lesson.Status = IndividualLessonStatus.Accepted;
        lesson.InstructorId = "inst-t2";
        lesson.RequestedInstructorIds.Clear();

        _auth.RestoreSession(instRequester);
        await sut.CheckAndNotifyAsync();
        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson scheduled",
            It.Is<string>(msg => msg.Contains("Coach Target 2") && msg.Contains("Ringen"))), Times.Once);
    }

    [Fact]
    public async Task CheckAndNotifyAsync_WhenAppInBackgroundWithColdAuth_UsesPersistedFencerAndInvalidatesCache()
    {
        var cache = new Mock<ICacheControl>();
        var sut = new NotificationSyncService(_sheets.Object, _auth, _settings.Object, _notifications.Object, cache.Object);

        // First run while logged in persists the fencer identity for background jobs
        var student = new Fencer { Id = "stu-bg", Name = "Background Alice" };
        _auth.RestoreSession(student);
        _settings.Setup(s => s.IsEnabled("stu-bg")).Returns(true);
        await sut.CheckAndNotifyAsync();

        // Simulate cold start in Android JobService where AuthService.CurrentFencer is null
        var sp = new Mock<IServiceProvider>();
        var store = new Mock<ICredentialStore>();
        var coldAuth = new AuthService(sp.Object, store.Object);
        var backgroundSut = new NotificationSyncService(_sheets.Object, coldAuth, _settings.Object, _notifications.Object, cache.Object);

        var acceptedLesson = new IndividualLesson
        {
            Id = "lesson-bg-1",
            StudentId = "stu-bg",
            InstructorId = "inst-1",
            Date = new DateTime(2026, 6, 10, 18, 0, 0),
            Topic = "Sabre",
            Status = IndividualLessonStatus.Accepted
        };
        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer>
        {
            student,
            new() { Id = "inst-1", Name = "Coach A", IsInstructor = true }
        });
        _sheets.Setup(s => s.GetIndividualLessonsAsync()).ReturnsAsync(new List<IndividualLesson> { acceptedLesson });

        await backgroundSut.CheckAndNotifyAsync();

        cache.Verify(c => c.InvalidateFencers(), Times.AtLeastOnce);
        cache.Verify(c => c.InvalidateIndividualLessons(), Times.AtLeastOnce);
        cache.Verify(c => c.InvalidatePaymentRequests(), Times.AtLeastOnce);
        cache.Verify(c => c.InvalidateNews(), Times.AtLeastOnce);

        _notifications.Verify(n => n.Show(
            It.IsAny<int>(),
            "1 on 1 lesson scheduled",
            It.Is<string>(msg => msg.Contains("Coach A") && msg.Contains("Sabre"))), Times.Once);
    }
}
