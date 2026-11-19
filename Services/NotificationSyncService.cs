using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public sealed class NotificationSyncService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;
    private readonly INotificationSettings _settings;
    private readonly INotificationService _notifications;

    public NotificationSyncService(
        IGoogleSheetsService sheets,
        AuthService auth,
        INotificationSettings settings,
        INotificationService notifications)
    {
        _sheets = sheets;
        _auth = auth;
        _settings = settings;
        _notifications = notifications;
    }

    public async Task CheckAndNotifyAsync()
    {
        var me = _auth.CurrentFencer;
        if (me is null || _auth.IsGuest)
            return;

        var normalEnabled = _settings.IsEnabled(me.Id);
        var instructorEnabled = me.IsInstructor && _settings.IsInstructorEnabled(me.Id);

        if (!normalEnabled && !instructorEnabled)
            return;

        var today = DateTime.Today;
        var fencersTask = _sheets.GetFencersAsync();
        var requestsTask = _sheets.GetPaymentRequestsAsync();
        var paymentsTask = _sheets.GetPaymentsAsync(today.Year, today.Month);
        var newsTask = _sheets.GetNewsAsync();
        await Task.WhenAll(fencersTask, requestsTask, paymentsTask, newsTask);

        var fencers = fencersTask.Result;
        var requests = requestsTask.Result;
        var paymentsThisMonth = paymentsTask.Result;
        var newsList = newsTask.Result;
        var names = fencers.ToDictionary(f => f.Id, f => f.DisplayName);

        // 1. Instructor notifications: pending payment requests + password reset requests
        if (instructorEnabled)
        {
            var seenPayReqs = LoadSet($"seen_pay_reqs_{me.Id}");
            foreach (var r in requests.Where(r => r.Status == PaymentRequestStatus.Pending))
            {
                if (!seenPayReqs.Add(r.Id)) continue;
                var who = names.GetValueOrDefault(r.FencerId, "A fencer");
                _notifications.Show(
                    r.Id.GetHashCode(),
                    "New payment request",
                    $"{who} sent a payment request for {r.Amount:N0} Ft ({r.Year}-{r.Month:00}).");
            }
            SaveSet($"seen_pay_reqs_{me.Id}", seenPayReqs);

            var seenResets = LoadSet($"seen_pw_resets_{me.Id}");
            foreach (var f in fencers.Where(f => f.HasPendingPasswordReset))
            {
                var key = $"{f.Id}_{f.PasswordResetRequestedAtUtc?.Ticks ?? 0}";
                if (!seenResets.Add(key)) continue;
                _notifications.Show(
                    key.GetHashCode(),
                    "Password reset requested",
                    $"{f.DisplayName} requested a password reset.");
            }
            SaveSet($"seen_pw_resets_{me.Id}", seenResets);
        }

        if (!normalEnabled)
            return;

        // 2. News notifications (normal notifications for all users)
        var seenNews = LoadSet($"seen_news_{me.Id}");
        foreach (var n in newsList.OrderBy(n => n.CreatedAtUtc))
        {
            if (!seenNews.Add(n.Id)) continue;
            _notifications.Show(
                n.Id.GetHashCode(),
                string.IsNullOrWhiteSpace(n.Title) ? "New club news" : n.Title,
                n.Body);
        }
        SaveSet($"seen_news_{me.Id}", seenNews);

        // 3. Fencer notifications: payment request accepted OR money accounted to profile
        var seenAccepted = LoadSet($"seen_acc_reqs_{me.Id}");
        var acceptedForMe = requests
            .Where(r => r.FencerId == me.Id && r.Status == PaymentRequestStatus.Accepted)
            .ToList();

        foreach (var r in acceptedForMe)
        {
            if (!seenAccepted.Add(r.Id)) continue;
            _notifications.Show(
                r.Id.GetHashCode(),
                "Payment request accepted",
                $"Your payment of {r.Amount:N0} Ft for {r.Year}-{r.Month:00} was accepted.");
        }
        SaveSet($"seen_acc_reqs_{me.Id}", seenAccepted);

        var seenPayments = LoadSet($"seen_payments_{me.Id}");
        foreach (var p in paymentsThisMonth.Where(p => p.FencerId == me.Id && p.Amount > 0))
        {
            var key = $"{p.Year}_{p.Month}_{p.Amount}_{p.PaidOn.Ticks}";
            if (!seenPayments.Add(key)) continue;

            // Avoid duplicate notification if an accepted request just triggered for the same amount
            var matchesJustAccepted = acceptedForMe.Any(r =>
                r.Year == p.Year && r.Month == p.Month && r.Amount == p.Amount &&
                r.ReviewedAtUtc.HasValue && (DateTime.UtcNow - r.ReviewedAtUtc.Value).TotalMinutes < 5);
            if (!matchesJustAccepted)
            {
                _notifications.Show(
                    key.GetHashCode(),
                    "Payment recorded",
                    $"{p.Amount:N0} Ft was accounted to your profile for {p.Year}-{p.Month:00}.");
            }
        }
        SaveSet($"seen_payments_{me.Id}", seenPayments);

        // 4. End-of-month unpaid fees reminder (all users, last 3 days of the month, once per month)
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        if (today.Day >= daysInMonth - 2)
        {
            var reminderKey = $"eom_reminded_{me.Id}_{today.Year}_{today.Month}";
            if (!Preferences.Default.Get(reminderKey, false))
            {
                var trainings = await _sheets.GetTrainingsAsync();
                var rules = await _sheets.GetPriceRulesAsync();

                var attendedCount = trainings.Count(t =>
                    t.Date.Year == today.Year && t.Date.Month == today.Month &&
                    t.AttendeeFencerIds.Contains(me.Id));
                var paidThisMonth = paymentsThisMonth.Where(p => p.FencerId == me.Id).Sum(p => p.Amount);
                var monthRules = FencerDuesLedger.RulesForMonth(rules, today.Year, today.Month);
                var quote = DuesCalculator.Calculate(attendedCount, me.IsStudent, monthRules, paidThisMonth);

                if (quote.Outstanding > 0)
                {
                    _notifications.Show(
                        reminderKey.GetHashCode(),
                        "Monthly dues reminder",
                        $"You have {quote.Outstanding:N0} Ft in unpaid fees for {today:MMMM}.");
                }
                Preferences.Default.Set(reminderKey, true);
            }
        }
    }

    private static HashSet<string> LoadSet(string key)
    {
        var raw = Preferences.Default.Get(key, "");
        return string.IsNullOrEmpty(raw)
            ? new HashSet<string>(StringComparer.Ordinal)
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static void SaveSet(string key, HashSet<string> set) =>
        Preferences.Default.Set(key, string.Join('|', set));
}