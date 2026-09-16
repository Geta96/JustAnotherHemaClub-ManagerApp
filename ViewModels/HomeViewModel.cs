using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public class HomeWeeklyRow
{
    public string DayShort { get; init; } = "";
    public string TimeRange { get; init; } = "";
    public string Topic { get; init; } = "";
    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
}

public partial class HomeViewModel : ObservableObject
{
    public const string InstagramUrl =
        "https://www.instagram.com/just.another.hema.club?igsh=MXZudjI3MDJ4eWw1aA==";

    public const string FacebookUrl =
        "https://www.facebook.com/share/18VtVUQPW5/";

    public const string TelegramUrl =
        "https://t.me/+6EUfQu6kXPY4NWM8";

    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;
    private readonly AuthService _auth;
    private readonly RecurringTrainingMaterializer _materializer;

    public ObservableCollection<HomeWeeklyRow> WeeklyTrainings { get; } = new();

    [ObservableProperty] private bool isLoadingWeekly;
    [ObservableProperty] private bool hasWeeklyTrainings;
    public bool HasNoWeeklyTrainings => !HasWeeklyTrainings && !IsLoadingWeekly;

    // --- Payment status card (logged-in fencer only) ---
    private static readonly Color PaymentGreen = Color.FromArgb("#1F8A2E");
    private static readonly Color PaymentGrey  = Color.FromArgb("#6B6B6B");
    private static readonly Color PaymentRed   = Color.FromArgb("#B23A3A"); // matches DangerRed

    private bool _hasPaymentStatus;
    public bool HasPaymentStatus
    {
        get => _hasPaymentStatus;
        set => SetProperty(ref _hasPaymentStatus, value);
    }

    private string _paymentStatusText = "";
    public string PaymentStatusText
    {
        get => _paymentStatusText;
        set => SetProperty(ref _paymentStatusText, value);
    }

    private Color _paymentStatusColor = PaymentGreen;
    public Color PaymentStatusColor
    {
        get => _paymentStatusColor;
        set => SetProperty(ref _paymentStatusColor, value);
    }

    // --- Next lesson card ---
    private TrainingSession? _nextLesson;

    // When the next lesson is a projected (not-yet-materialized) recurring
    // occurrence, these hold the rule + date so we can materialize it on demand.
    private RecurringTrainingRule? _nextLessonRule;
    private DateTime _nextLessonDate;

    [ObservableProperty] private bool hasNextLesson;
    [ObservableProperty] private string nextLessonWhen = "";
    [ObservableProperty] private string nextLessonTopic = "";
    [ObservableProperty] private bool isAttendingNextLesson;
    [ObservableProperty] private bool canAttendNextLesson;

    public bool NextLessonHasTopic => !string.IsNullOrWhiteSpace(NextLessonTopic);

    /// <summary>Show the blue Attend button only when the user can attend and hasn't yet.</summary>
    public bool ShowAttendButton => CanAttendNextLesson && !IsAttendingNextLesson;

    /// <summary>Show the Wine "Undo" text only when the user can attend and already is.</summary>
    public bool ShowUndoAttend => CanAttendNextLesson && IsAttendingNextLesson;

    // --- Attended visual state (green glow border on the card) ---
    private static readonly Color AttendedGreen = Color.FromArgb("#7FBF3F");
    private static readonly Color DefaultStroke = Color.FromArgb("#B9A9A0"); // matches the Muted card stroke

    public Color NextLessonStroke => IsAttendingNextLesson ? AttendedGreen : DefaultStroke;
    public double NextLessonStrokeThickness => IsAttendingNextLesson ? 3 : 1;
    public double NextLessonGlowOpacity => IsAttendingNextLesson ? 1 : 0;

    partial void OnNextLessonTopicChanged(string value)
        => OnPropertyChanged(nameof(NextLessonHasTopic));

    partial void OnIsAttendingNextLessonChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAttendButton));
        OnPropertyChanged(nameof(ShowUndoAttend));
        OnPropertyChanged(nameof(NextLessonStroke));
        OnPropertyChanged(nameof(NextLessonStrokeThickness));
        OnPropertyChanged(nameof(NextLessonGlowOpacity));
    }

    partial void OnCanAttendNextLessonChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAttendButton));
        OnPropertyChanged(nameof(ShowUndoAttend));
    }

    public HomeViewModel(IGoogleSheetsService sheets, ICacheControl cache, AuthService auth, RecurringTrainingMaterializer materializer)
    {
        _sheets = sheets;
        _cache = cache;
        _auth = auth;
        _materializer = materializer;
        WeeklyTrainings.CollectionChanged += (_, __) =>
        {
            HasWeeklyTrainings = WeeklyTrainings.Count > 0;
            OnPropertyChanged(nameof(HasNoWeeklyTrainings));
        };
    }

    partial void OnIsLoadingWeeklyChanged(bool value)
        => OnPropertyChanged(nameof(HasNoWeeklyTrainings));

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoadingWeekly = true;
        try
        {
            var rules = await _sheets.GetRecurringTrainingsAsync();

            // Active rules only, Monday-first ordering.
            var ordered = rules
                .Where(r => !r.EndDate.HasValue || r.EndDate.Value.Date >= DateTime.Today)
                .OrderBy(r => ((int)r.DayOfWeek + 6) % 7)
                .ThenBy(r => r.TimeOfDay);

            WeeklyTrainings.Clear();
            foreach (var r in ordered)
            {
                var end = r.EndTimeOfDay == default
                    ? r.TimeOfDay.Add(TimeSpan.FromMinutes(90))
                    : r.EndTimeOfDay;

                WeeklyTrainings.Add(new HomeWeeklyRow
                {
                    DayShort  = r.DayOfWeek.ToString().Substring(0, 3),
                    TimeRange = $"{r.TimeOfDay:hh\\:mm} – {end:hh\\:mm}",
                    Topic     = r.Topic ?? ""
                });
            }
        }
        catch
        {
            WeeklyTrainings.Clear();
        }
        finally { IsLoadingWeekly = false; }

        // Load the "Next lesson" card (best-effort; a failure just hides it).
        await LoadNextLessonAsync();

        // Load the personal payment status card (best-effort; hidden on failure).
        await LoadPaymentStatusAsync();

        // Use the user's idle time on the home page to warm the datasets the
        // other tabs need (tournaments, individual lessons, recurring trainings,
        // …). Fire-and-forget: failures are swallowed inside PrefetchAsync's
        // callers and the pages fall back to their own network fetch. This makes
        // the first navigation to Tournaments / Trainings / Finance feel instant.
        _ = Task.Run(async () =>
        {
            try { await _cache.PrefetchAsync(); }
            catch { /* best-effort background warm-up */ }
        });
    }

    /// <summary>
    /// Finds the soonest upcoming training and populates the "Next lesson" card.
    /// A session stays "upcoming" until MIDNIGHT of its own day, so a fencer who
    /// opens the app after the session has ended can still attend it for the rest
    /// of that day. Only logged-in (non-guest) fencers see the Attend button.
    /// </summary>
    private async Task LoadNextLessonAsync()
    {
        try
        {
            var todayStart = DateTime.Today;

            // Read both the already-materialized sessions and the recurring rules
            // in parallel. We consider BOTH sources so the card is correct even
            // when the soonest recurring session hasn't been materialized yet.
            var trainingsTask = _sheets.GetTrainingsAsync();
            var rulesTask     = _sheets.GetRecurringTrainingsAsync();
            await Task.WhenAll(trainingsTask, rulesTask);

            var trainings = trainingsTask.Result;
            var rules     = rulesTask.Result;

            // Soonest already-materialized upcoming session (visible until midnight
            // of its own day).
            var nextMaterialized = trainings
                .Where(t => t.Date.Date >= todayStart)
                .OrderBy(t => t.Date)
                .FirstOrDefault();

            // Soonest PROJECTED recurring occurrence, computed straight from the
            // rules (independent of materialization). For each active rule, find
            // the next date on/after today that the rule fires.
            RecurringTrainingRule? projectedRule = null;
            DateTime projectedDate = default;
            foreach (var rule in rules)
            {
                var occ = NextOccurrenceOnOrAfter(rule, todayStart);
                if (occ is null) continue;
                var start = occ.Value.Date + rule.TimeOfDay;
                if (projectedRule is null || start < (projectedDate.Date + projectedRule.TimeOfDay))
                {
                    projectedRule = rule;
                    projectedDate = occ.Value;
                }
            }

            // Decide which is sooner: the materialization or the projection.
            DateTime? matStart = nextMaterialized?.Date;
            DateTime? projStart = projectedRule is null
                ? null
                : projectedDate.Date + projectedRule.TimeOfDay;

            _nextLesson     = null;
            _nextLessonRule = null;

            if (matStart is null && projStart is null)
            {
                HasNextLesson = false;
                CanAttendNextLesson = false;
                IsAttendingNextLesson = false;
                return;
            }

            // Prefer whichever starts first. When the projected occurrence is the
            // same slot as the materialized one, the materialized row wins (equal
            // or earlier), so we never double up.
            bool useProjected = projStart is not null &&
                                (matStart is null || projStart < matStart);

            DateTime whenStart;
            string topic;
            var me = _auth.CurrentFencer;

            if (useProjected)
            {
                _nextLessonRule = projectedRule;
                _nextLessonDate = projectedDate;
                whenStart = projStart!.Value;
                topic = projectedRule!.Topic ?? "";

                // Proactively materialize the projected occurrence NOW so the row
                // exists as soon as the card shows it (no need to wait for Attend).
                // EnsureOccurrenceAsync is duplicate-safe. Best-effort: if it fails
                // (offline), the card still shows the projected data and Attend will
                // retry the materialization.
                try
                {
                    var session = await _materializer.EnsureOccurrenceAsync(projectedRule, projectedDate);
                    _nextLesson     = session;
                    _nextLessonRule = null; // now backed by a real row
                    whenStart = session.Date;
                    topic     = session.Topic ?? "";
                }
                catch { /* keep projected display; Attend will materialize later */ }
            }
            else
            {
                _nextLesson = nextMaterialized;
                whenStart = matStart!.Value;
                topic = nextMaterialized!.Topic ?? "";
            }

            NextLessonWhen  = $"{FormatFriendlyDay(whenStart)} at {whenStart:HH\\:mm}";
            NextLessonTopic = topic;

            CanAttendNextLesson = me is not null && !_auth.IsGuest;
            IsAttendingNextLesson = me is not null && _nextLesson is not null &&
                                    _nextLesson.AttendeeFencerIds.Contains(me.Id);
            HasNextLesson = true;
        }
        catch
        {
            _nextLesson = null;
            _nextLessonRule = null;
            HasNextLesson = false;
            CanAttendNextLesson = false;
        }
    }

    /// <summary>
    /// Computes the logged-in fencer's payment status across all months up to and
    /// including the current one, then populates the payment-status card:
    ///   ? Overpaid  ? "Overpayed by X Ft"            (green)
    ///   ? All paid  ? "All payed up"                 (green)
    ///   ? Only this month's dues outstanding, and everything before is settled
    ///                ? "Due X by the end of this month" (grey)
    ///   ? Also owes for earlier months
    ///                ? "Due X"                       (red)
    /// Guests / instructors / not-logged-in users don't see the card.
    /// </summary>
    private async Task LoadPaymentStatusAsync()
    {
        try
        {
            var me = _auth.CurrentFencer;
            if (me is null || _auth.IsGuest)
            {
                HasPaymentStatus = false;
                return;
            }

            var today = DateTime.Today;

            var trainingsTask = _sheets.GetTrainingsAsync();
            var rulesTask     = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(trainingsTask, rulesTask);

            var trainings = trainingsTask.Result;
            var allRules  = rulesTask.Result;

            // Months (up to the current one) in which this fencer attended
            // anything, in chronological order.
            var attendedMonths = trainings
                .Where(t => t.Date.Date <= today &&
                            t.AttendeeFencerIds.Contains(me.Id))
                .Select(t => (Year: t.Date.Year, Month: t.Date.Month))
                .Distinct()
                .OrderBy(t => t.Year).ThenBy(t => t.Month)
                .ToList();

            if (attendedMonths.Count == 0)
            {
                // Nothing was ever owed ? treat as fully paid.
                PaymentStatusText  = "All payed up";
                PaymentStatusColor = PaymentGreen;
                HasPaymentStatus   = true;
                return;
            }

            // Fetch payments for each attended month in parallel.
            var paymentTasks = attendedMonths
                .Select(ym => _sheets.GetPaymentsAsync(ym.Year, ym.Month))
                .ToArray();
            await Task.WhenAll(paymentTasks);

            // Build the raw per-month facts and hand them to the SHARED ledger,
            // so Home, Finance and the profile all agree on what a fencer owes
            // (custom-period passes and credit carry are handled inside it).
            var monthInputs = new List<FencerDuesLedger.MonthInput>(attendedMonths.Count);
            for (int i = 0; i < attendedMonths.Count; i++)
            {
                var ym = attendedMonths[i];

                var attendedCount = trainings
                    .Count(t => t.Date.Year == ym.Year && t.Date.Month == ym.Month &&
                                t.AttendeeFencerIds.Contains(me.Id));

                var cashPaid = paymentTasks[i].Result
                    .Where(p => p.FencerId == me.Id)
                    .Sum(p => p.Amount);

                monthInputs.Add(new FencerDuesLedger.MonthInput(
                    ym.Year, ym.Month, attendedCount, cashPaid));
            }

            var ledger = FencerDuesLedger.Compute(me.IsStudent, monthInputs, allRules);

            var thisYm = (today.Year, today.Month);
            decimal priorOutstanding = 0m;      // unpaid dues from months BEFORE this one
            decimal thisMonthOutstanding = 0m;
            decimal finalCredit = 0m;

            foreach (var r in ledger)
            {
                if ((r.Year, r.Month) == thisYm)
                    thisMonthOutstanding += r.Contribution.Quote.Outstanding;
                else
                    priorOutstanding += r.Contribution.Quote.Outstanding;

                finalCredit = r.Contribution.Quote.Overpayment;
            }

            if (priorOutstanding > 0m)
            {
                var totalDue = priorOutstanding + thisMonthOutstanding;
                PaymentStatusText  = $"Due {totalDue:N0} Ft";
                PaymentStatusColor = PaymentRed;
            }
            else if (thisMonthOutstanding > 0m)
            {
                PaymentStatusText  = $"Due {thisMonthOutstanding:N0} Ft by the end of this month";
                PaymentStatusColor = PaymentGrey;
            }
            else if (finalCredit > 0m)
            {
                PaymentStatusText  = $"Overpayed by {finalCredit:N0} Ft";
                PaymentStatusColor = PaymentGreen;
            }
            else
            {
                PaymentStatusText  = "All payed up";
                PaymentStatusColor = PaymentGreen;
            }

            HasPaymentStatus = true;
        }
        catch
        {
            HasPaymentStatus = false;
        }
    }

    /// <summary>
    /// The next date on/after <paramref name="from"/> (inclusive) on which the
    /// rule is active, or null if the rule has ended before then. Scans at most
    /// 7 days since the rule fires weekly.
    /// </summary>
    private static DateTime? NextOccurrenceOnOrAfter(RecurringTrainingRule rule, DateTime from)
    {
        for (int i = 0; i < 7; i++)
        {
            var d = from.Date.AddDays(i);
            if (rule.EndDate is { } end && d > end.Date) return null;
            if (rule.IsActiveOn(d)) return d;
        }
        return null;
    }

    /// <summary>
    /// Human-friendly day label: "Today", "Tomorrow", "This Friday", "Next Tuesday",
    /// or an absolute "Oct 11th" for dates beyond next week.
    /// </summary>
    private static string FormatFriendlyDay(DateTime when)
    {
        var today = DateTime.Today;
        var date = when.Date;
        var days = (date - today).Days;

        if (days == 0) return "Today";
        if (days == 1) return "Tomorrow";

        // Week-based buckets (weeks start on Monday). "This <weekday>" means the
        // date falls in the current calendar week; "Next <weekday>" means it falls
        // in the following calendar week — regardless of raw day distance. This is
        // why e.g. next Monday reads "Next Monday" even though it's only 6 days off.
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var endOfThisWeek = today.AddDays(6 - daysSinceMonday); // Sunday of this week
        var endOfNextWeek = endOfThisWeek.AddDays(7);

        if (date <= endOfThisWeek)
            return $"This {date.DayOfWeek}";

        if (date <= endOfNextWeek)
            return $"Next {date.DayOfWeek}";

        // Further out: absolute "MMM d" with an ordinal suffix, e.g. "Oct 11th".
        return $"{date:MMM} {date.Day}{OrdinalSuffix(date.Day)}";
    }

    private static string OrdinalSuffix(int day)
    {
        if (day is >= 11 and <= 13) return "th";
        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
    }

    /// <summary>
    /// Toggles the current fencer's attendance on the next lesson. Works both ways:
    /// attend if not yet attending, or undo (cancel) if already attending.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAttendNextLessonAsync()
    {
        var me = _auth.CurrentFencer;
        if (me is null || _auth.IsGuest) return;

        // If the card is showing a projected occurrence whose row wasn't
        // materialized yet (proactive materialization failed, e.g. offline),
        // materialize it now — duplicate-safe — before attending.
        if (_nextLesson is null && _nextLessonRule is not null)
        {
            try
            {
                _nextLesson = await _materializer.EnsureOccurrenceAsync(_nextLessonRule, _nextLessonDate);
                _nextLessonRule = null;
            }
            catch { return; } // can't attend a session we couldn't create
        }

        var lesson = _nextLesson;
        if (lesson is null) return;

        var wasAttending = lesson.AttendeeFencerIds.Contains(me.Id);

        // Optimistic local update.
        if (wasAttending) lesson.AttendeeFencerIds.Remove(me.Id);
        else if (!lesson.AttendeeFencerIds.Contains(me.Id)) lesson.AttendeeFencerIds.Add(me.Id);

        try
        {
            await _sheets.UpsertTrainingAsync(lesson);
            IsAttendingNextLesson = !wasAttending;
        }
        catch
        {
            // Roll back on failure so the UI stays consistent with the backend.
            if (wasAttending) lesson.AttendeeFencerIds.Add(me.Id);
            else lesson.AttendeeFencerIds.Remove(me.Id);
            IsAttendingNextLesson = wasAttending;
        }
    }

    [RelayCommand]
    private Task OpenInstagramAsync() => Launcher.Default.OpenAsync(InstagramUrl);

    [RelayCommand]
    private Task OpenFacebookAsync() => Launcher.Default.OpenAsync(FacebookUrl);

    [RelayCommand]
    private Task OpenTelegramAsync() => Launcher.Default.OpenAsync(TelegramUrl);
}