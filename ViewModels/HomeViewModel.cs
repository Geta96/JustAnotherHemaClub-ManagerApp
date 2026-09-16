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
            var rulesTask = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(trainingsTask, rulesTask);

            var trainings = trainingsTask.Result;
            var allRules = rulesTask.Result;

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
                PaymentStatusText = "All payed up";
                PaymentStatusColor = PaymentGreen;
                HasPaymentStatus = true;
                return;
            }

            // Walk a CONTIGUOUS range of months from the fencer's first attendance
            // through the current month — not just the months they attended. A
            // payment can land in a month with no attendance (a pre-payment, or a
            // negative refund/correction); those payment-only months MUST stay in
            // the credit-carry chain or their funds silently vanish and Home would
            // disagree with Finance (which seeds payment-only months for exactly
            // this reason). Querying the whole span also captures corrections made
            // in gap months between attended ones.
            var firstYm = attendedMonths[0];
            var allMonths = new List<(int Year, int Month)>();
            for (var d = new DateTime(firstYm.Year, firstYm.Month, 1);
                 d <= new DateTime(today.Year, today.Month, 1);
                 d = d.AddMonths(1))
            {
                allMonths.Add((d.Year, d.Month));
            }

            // Fetch payments for every month in the span in parallel.
            var paymentTasks = allMonths
                .Select(ym => _sheets.GetPaymentsAsync(ym.Year, ym.Month))
                .ToArray();
            await Task.WhenAll(paymentTasks);

            // Build the raw per-month facts and hand them to the SHARED ledger,
            // so Home, Finance and the profile all agree on what a fencer owes
            // (custom-period passes and credit carry are handled inside it).
            var monthInputs = new List<FencerDuesLedger.MonthInput>(allMonths.Count);
            for (int i = 0; i < allMonths.Count; i++)
            {
                var ym = allMonths[i];

                var attendedCount = trainings
                    .Count(t => t.Date.Year == ym.Year && t.Date.Month == ym.Month &&
                                t.AttendeeFencerIds.Contains(me.Id));

                var cashPaid = paymentTasks[i].Result
                    .Where(p => p.FencerId == me.Id)
                    .Sum(p => p.Amount);

                monthInputs.Add(new FencerDuesLedger.MonthInput(
                    ym.Year, ym.Month, attendedCount, cashPaid));
            }

            // Run the shared ledger and reduce it to the cumulative summary.
            var results = FencerDuesLedger.Compute(me.IsStudent, monthInputs, allRules);
            var summary = FencerDuesLedger.Summarize(results, today.Year, today.Month);

            switch (summary.Status)
            {
                case FencerDuesLedger.DuesStatus.Overpaid:
                    PaymentStatusText = $"Overpayed by {summary.FinalCredit:0} Ft";
                    PaymentStatusColor = PaymentGreen;
                    break;

                case FencerDuesLedger.DuesStatus.DueThisMonth:
                    PaymentStatusText = $"Due {summary.ThisMonthOutstanding:0} by the end of this month";
                    PaymentStatusColor = PaymentGrey;
                    break;

                case FencerDuesLedger.DuesStatus.DueWithArrears:
                    PaymentStatusText = $"Due {summary.TotalOutstanding:0}";
                    PaymentStatusColor = PaymentRed;
                    break;

                case FencerDuesLedger.DuesStatus.AllPaid:
                default:
                    PaymentStatusText = "All payed up";
                    PaymentStatusColor = PaymentGreen;
                    break;
            }

            HasPaymentStatus = true;
        }
        catch
        {
            HasPaymentStatus = false;
        }
    }

    /// <summary>
    /// Returns the first date on or after <paramref name="from"/> on which the
    /// recurring <paramref name="rule"/> fires, or null when the rule has already
    /// ended before that date. Respects the rule's StartDate/EndDate window and
    /// its weekly <see cref="RecurringTrainingRule.DayOfWeek"/>.
    /// </summary>
    private static DateTime? NextOccurrenceOnOrAfter(RecurringTrainingRule rule, DateTime from)
    {
        var start = from.Date;

        // Never project before the rule actually begins.
        if (rule.StartDate.Date > start)
            start = rule.StartDate.Date;

        // Advance to the rule's weekday.
        int delta = ((int)rule.DayOfWeek - (int)start.DayOfWeek + 7) % 7;
        var candidate = start.AddDays(delta);

        // Past the rule's end window? No upcoming occurrence.
        if (rule.EndDate.HasValue && candidate > rule.EndDate.Value.Date)
            return null;

        return candidate;
    }

    /// <summary>
    /// Formats a date for the "Next lesson" card: "Today", "Tomorrow", or the
    /// weekday name (e.g. "Monday") for dates within the coming week, otherwise a
    /// short date like "23 Sep".
    /// </summary>
    private static string FormatFriendlyDay(DateTime when)
    {
        var day = when.Date;
        var today = DateTime.Today;

        if (day == today) return "Today";
        if (day == today.AddDays(1)) return "Tomorrow";
        if (day < today.AddDays(7)) return day.ToString("dddd");
        return day.ToString("d MMM");
    }
}