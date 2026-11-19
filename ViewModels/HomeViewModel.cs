using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using System.Globalization;

namespace JustAnotherHemaClub.ViewModels;

public class HomeWeeklyRow
{
    public string DayShort { get; init; } = "";
    public string TimeRange { get; init; } = "";
    public string Topic { get; init; } = "";
    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
}

public class HomePaymentMonthRow : ObservableObject
{
    public int Year { get; }
    public int Month { get; }
    public string MonthTitle => new DateTime(Year, Month, 1).ToString("yyyy MMMM", CultureInfo.InvariantCulture);
    public FencerDueRow Due { get; }
    public ObservableCollection<PaymentOptionVm> PaymentOptions { get; } = new();

    private bool _isOptionsExpanded;
    public bool IsOptionsExpanded
    {
        get => _isOptionsExpanded;
        set
        {
            if (SetProperty(ref _isOptionsExpanded, value))
                OnPropertyChanged(nameof(OptionsExpandGlyph));
        }
    }

    public string OptionsExpandGlyph => IsOptionsExpanded ? "\u25BE" : "\u25B8";

    private IRelayCommand? _toggleOptionsCommand;
    public IRelayCommand ToggleOptionsCommand =>
        _toggleOptionsCommand ??= new RelayCommand(() => IsOptionsExpanded = !IsOptionsExpanded);

    public HomePaymentMonthRow(int year, int month, FencerDueRow due)
    {
        Year = year;
        Month = month;
        Due = due;
    }
}

public partial class HomeViewModel : ObservableObject
{
    public const string InstagramUrl =
        "https://www.instagram.com/just.another.hema.club?igsh=MXZudjI3MDJ4eWw1aA==";

    public const string FacebookUrl =
        "https://www.facebook.com/share/18VtVUQPW5/";

    public const string DiscordUrl =
        "https://discord.gg/tEcynS94d";

    private static readonly string[] CandidateHeroImages = BuildCandidateHeroImages();

    private static string[] BuildCandidateHeroImages()
    {
        var list = new List<string> { "home_bg_3.jpg" };
        for (int i = 4; i <= 50; i++)
            list.Add($"home_bg_{i}.jpg");
        return list.ToArray();
    }

    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;
    private readonly AuthService _auth;
    private readonly RecurringTrainingMaterializer _materializer;
    private readonly NotificationSyncService? _notificationSync;
    private readonly IDialogService? _dialogs;

    public ObservableCollection<string> HeroImages { get; } = new();
    public ObservableCollection<HomeWeeklyRow> WeeklyTrainings { get; } = new();
    public ObservableCollection<NewsRow> News { get; } = new();

    [ObservableProperty] private int currentHeroImageIndex;
    [ObservableProperty] private bool isLoadingWeekly;
    [ObservableProperty] private bool hasWeeklyTrainings;
    public bool HasNoWeeklyTrainings => !HasWeeklyTrainings && !IsLoadingWeekly;
    public bool HasNews => News.Count > 0;
    public bool HasNoNews => News.Count == 0;

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

    public ObservableCollection<HomePaymentMonthRow> PaymentMonths { get; } = new();

    private bool _isPaymentStatusExpanded;
    public bool IsPaymentStatusExpanded
    {
        get => _isPaymentStatusExpanded;
        set
        {
            if (SetProperty(ref _isPaymentStatusExpanded, value))
                OnPropertyChanged(nameof(PaymentStatusExpandGlyph));
        }
    }

    public string PaymentStatusExpandGlyph => IsPaymentStatusExpanded ? "\u25BE" : "\u25B8";

    private IRelayCommand? _togglePaymentStatusCommand;
    public IRelayCommand TogglePaymentStatusCommand =>
        _togglePaymentStatusCommand ??= new RelayCommand(() => IsPaymentStatusExpanded = !IsPaymentStatusExpanded);

    // --- Pending password resets (instructors only) ---
    [ObservableProperty] private bool hasPendingResets;
    [ObservableProperty] private string pendingResetsText = "";

    // --- Pending payment requests (instructors only) ---
    public ObservableCollection<PaymentRequestRow> PendingPaymentRequests { get; } = new();
    public bool HasPendingPaymentRequests => _auth.IsLoggedInInstructor && PendingPaymentRequests.Count > 0;

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

    public HomeViewModel(IGoogleSheetsService sheets, ICacheControl cache, AuthService auth, RecurringTrainingMaterializer materializer, NotificationSyncService? notificationSync = null, IDialogService? dialogs = null)
    {
        _sheets = sheets;
        _cache = cache;
        _auth = auth;
        _materializer = materializer;
        _notificationSync = notificationSync;
        _dialogs = dialogs;
        WeeklyTrainings.CollectionChanged += (_, __) =>
        {
            HasWeeklyTrainings = WeeklyTrainings.Count > 0;
            OnPropertyChanged(nameof(HasNoWeeklyTrainings));
        };
        News.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasNews));
            OnPropertyChanged(nameof(HasNoNews));
        };

        ShuffleHeroImages();
    }

    private void ShuffleHeroImages()
    {
        var available = CandidateHeroImages
            .Where(ImageResourceExists)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        if (available.Count == 0)
            available.Add("home_bg_3.jpg");

        HeroImages.Clear();
        foreach (var img in available)
            HeroImages.Add(img);

        CurrentHeroImageIndex = 0;
    }

    private static bool ImageResourceExists(string fileName)
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var resName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var id = context.Resources?.GetIdentifier(resName, "drawable", context.PackageName) ?? 0;
        return id != 0;
#else
        return true;
#endif
    }

    partial void OnIsLoadingWeeklyChanged(bool value)
        => OnPropertyChanged(nameof(HasNoWeeklyTrainings));

    [RelayCommand]
    public async Task LoadAsync()
    {
        ShuffleHeroImages();
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

        // Load club news for the Home news card.
        await LoadNewsAsync();

        // Load the "Next lesson" card (best-effort; a failure just hides it).
        await LoadNextLessonAsync();

        // Payment status card for the logged-in fencer.
        await LoadPaymentStatusAsync();

        // Instructors: surface pending password-reset requests and payment requests.
        await LoadPendingResetsAsync();
        await LoadPendingPaymentRequestsAsync();

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

        if (_notificationSync is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await _notificationSync.CheckAndNotifyAsync(); }
                catch { /* best-effort notification check */ }
            });
        }
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
                // exists as soon as the card shows it. EnsureOccurrenceAsync is
                // duplicate-safe. If it fails we KEEP _nextLessonRule so the
                // Attend command can retry the materialization on demand.
                try
                {
                    var session = await _materializer.EnsureOccurrenceAsync(projectedRule, projectedDate);
                    _nextLesson     = session;
                    _nextLessonRule = null; // now backed by a real row
                    whenStart = session.Date;
                    topic     = session.Topic ?? "";
                }
                catch { /* keep _nextLessonRule so Attend can materialize later */ }
            }
            else
            {
                _nextLesson = nextMaterialized;
                whenStart = matStart!.Value;
                topic = nextMaterialized!.Topic ?? "";
            }

            NextLessonWhen  = $"{FormatFriendlyDay(whenStart)} at {whenStart:HH\\:mm}";
            NextLessonTopic = topic;

            // Only offer Attend when we actually have something to attend against:
            // a materialized session OR a recurring rule we can still materialize.
            CanAttendNextLesson = me is not null && !_auth.IsGuest &&
                                  (_nextLesson is not null || _nextLessonRule is not null);
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
            PaymentMonths.Clear();
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
            var currentMonthRules = FencerDuesLedger.RulesForMonth(allRules, today.Year, today.Month);

            // Months (up to the current one) in which this fencer attended
            // anything, in chronological order.
            var attendedMonths = trainings
                .Where(t => t.Date.Date <= today &&
                            t.AttendeeFencerIds.Contains(me.Id))
                .Select(t => (Year: t.Date.Year, Month: t.Date.Month))
                .Distinct()
                .OrderBy(t => t.Year).ThenBy(t => t.Month)
                .ToList();

            // Always include the current month in the span so the user can see/request
            // payments for this month even if they haven't attended yet.
            var firstYm = attendedMonths.Count > 0
                ? attendedMonths[0]
                : (Year: today.Year, Month: today.Month);

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

            // Build per-month rows (newest first): only show the current month + months behind on payment.
            for (int i = results.Count - 1; i >= 0; i--)
            {
                var r = results[i];
                var contribution = r.Contribution;
                var quote = contribution.Quote;
                var isCurrent = r.Year == today.Year && r.Month == today.Month;

                if (!isCurrent && quote.Outstanding <= 0m)
                    continue;

                var monthRules = FencerDuesLedger.RulesForMonth(allRules, r.Year, r.Month);
                var dueRow = new FencerDueRow(me, quote, contribution.CashPaid, contribution.CreditIn)
                {
                    ActiveRules = monthRules,
                    CanMarkPaid = true
                };

                var monthRow = new HomePaymentMonthRow(r.Year, r.Month, dueRow);
                PopulateMonthPaymentOptions(monthRow);
                PaymentMonths.Add(monthRow);
            }

            switch (summary.Status)
            {
                case FencerDuesLedger.DuesStatus.Overpaid:
                    PaymentStatusText = DuesCalculator.DescribeOverpayment(
                        summary.ThisMonthEffectivePaid, summary.FinalCredit, me.IsStudent,
                        currentMonthRules);
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

    private void PopulateMonthPaymentOptions(HomePaymentMonthRow monthRow)
    {
        monthRow.PaymentOptions.Clear();
        var d = monthRow.Due;
        var year = monthRow.Year;
        var month = monthRow.Month;

        foreach (var opt in PaymentOptionBuilder.Build(
            d.Fencer.IsStudent,
            d.AlreadyPaid,
            d.CreditIn,
            d.SessionsAttended,
            d.TotalCost,
            d.AmountDue,
            d.ActiveRules))
        {
            var amount = opt.Amount;
            var note = opt.MenuText;
            monthRow.PaymentOptions.Add(new PaymentOptionVm(
                opt.MenuText,
                () => SubmitHomePaymentRequestAsync(monthRow, year, month, amount, note),
                opt.Kind));
        }

        monthRow.PaymentOptions.Add(new PaymentOptionVm(
            "Ask to accept custom amount…",
            () => PromptHomeCustomPaymentRequestAsync(monthRow, year, month),
            "primary"));
    }

    private async Task PromptHomeCustomPaymentRequestAsync(HomePaymentMonthRow monthRow, int year, int month)
    {
        if (_dialogs is null) return;
        var monthTitle = new DateTime(year, month, 1).ToString("yyyy MMMM", CultureInfo.InvariantCulture);
        var text = await _dialogs.PromptAsync("Ask to accept payment",
            $"Enter the amount paid for {monthTitle} (Ft):",
            "Send", "Cancel");

        if ((!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) &&
             !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)) ||
            amount <= 0m)
            return;

        await SubmitHomePaymentRequestAsync(monthRow, year, month, amount, "Custom amount");
    }

    private async Task SubmitHomePaymentRequestAsync(HomePaymentMonthRow monthRow, int year, int month, decimal amount, string note)
    {
        var me = _auth.CurrentFencer;
        if (me is null || _auth.IsGuest || amount <= 0m) return;

        try
        {
            await _sheets.UpsertPaymentRequestAsync(new PaymentRequest
            {
                FencerId = me.Id,
                Year = year,
                Month = month,
                Amount = amount,
                Note = note,
                Status = PaymentRequestStatus.Pending,
                RequestedByFencerId = me.Id,
                RequestedAtUtc = DateTime.UtcNow
            });
            _cache.InvalidatePaymentRequests();
            monthRow.IsOptionsExpanded = false;
            await LoadPendingPaymentRequestsAsync();
            if (_dialogs is not null)
                await _dialogs.ShowAsync("Request sent", $"Payment request for {amount:N0} Ft has been sent.");
        }
        catch (Exception ex)
        {
            if (_dialogs is not null)
                await _dialogs.ShowAsync("Request failed", ex.Message);
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
        if (day < today.AddDays(7)) return day.ToString("dddd", CultureInfo.InvariantCulture);
        return day.ToString("d MMM", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task ToggleAttendNextLessonAsync()
    {
        var me = _auth.CurrentFencer;
        if (me is null || _auth.IsGuest) return;

        // If the up-front materialization failed, recover the real row now.
        if (_nextLesson is null && _nextLessonRule is not null)
        {
            try
            {
                _nextLesson = await _materializer.EnsureOccurrenceAsync(_nextLessonRule, _nextLessonDate);
                _nextLessonRule = null;
            }
            catch
            {
                // Still couldn't reach Sheets - leave state untouched so the
                // user can retry rather than seeing a phantom "attended".
                return;
            }
        }

        if (_nextLesson is null) return;

        var wasAttending = _nextLesson.AttendeeFencerIds.Contains(me.Id);
        if (wasAttending)
            _nextLesson.AttendeeFencerIds.Remove(me.Id);
        else
            _nextLesson.AttendeeFencerIds.Add(me.Id);

        // Optimistically reflect the change in the UI.
        IsAttendingNextLesson = !wasAttending;

        try
        {
            // Persist the updated attendee list for this session.
            await _sheets.UpsertTrainingAsync(_nextLesson);
        }
        catch
        {
            // Roll back the optimistic change so the UI reflects reality.
            if (wasAttending) _nextLesson.AttendeeFencerIds.Add(me.Id);
            else _nextLesson.AttendeeFencerIds.Remove(me.Id);
            throw;
        }
    }

    [RelayCommand]
    private static Task OpenInstagram() =>
        Launcher.Default.OpenAsync(InstagramUrl);

    [RelayCommand]
    private static Task OpenFacebook() =>
        Launcher.Default.OpenAsync(FacebookUrl);

    [RelayCommand]
    private static Task OpenDiscord() =>
        Launcher.Default.OpenAsync(DiscordUrl);

    private IAsyncRelayCommand? _openAllNewsCommand;
    public IAsyncRelayCommand OpenAllNewsCommand => _openAllNewsCommand ??= new AsyncRelayCommand(OpenAllNews);

    private static Task OpenAllNews() => Shell.Current.GoToAsync("//news");

    private async Task LoadNewsAsync()
    {
        try
        {
            var newsTask = _sheets.GetNewsAsync();
            var fencersTask = _sheets.GetFencersAsync();
            await Task.WhenAll(newsTask, fencersTask);

            var names = fencersTask.Result.ToDictionary(f => f.Id, f => f.DisplayName);

            News.Clear();
            foreach (var n in newsTask.Result.OrderByDescending(x => x.CreatedAtUtc))
            {
                News.Add(new NewsRow(n, names.GetValueOrDefault(n.CreatedByFencerId, "Instructor")));
            }
        }
        catch
        {
            News.Clear();
        }
    }

    private async Task LoadPendingResetsAsync()
    {
        try
        {
            if (!_auth.IsLoggedInInstructor)
            {
                HasPendingResets = false;
                return;
            }

            _cache.InvalidateFencers();
            var fencers = await _sheets.GetFencersAsync();
            var pending = fencers
                .Where(f => f.HasPendingPasswordReset)
                .OrderBy(f => f.PasswordResetRequestedAtUtc ?? DateTime.MaxValue)
                .ToList();

            if (pending.Count == 0)
            {
                HasPendingResets = false;
                return;
            }

            var header = pending.Count == 1
                ? "1 fencer is waiting for a password reset approval:"
                : $"{pending.Count} fencers are waiting for a password reset approval:";
            PendingResetsText = header + "\n" + string.Join("\n", pending.Select(f =>
                string.IsNullOrWhiteSpace(f.Username) ? $"• {f.DisplayName}" : $"• {f.DisplayName} (@{f.Username})"));
            HasPendingResets = true;
        }
        catch
        {
            HasPendingResets = false;
        }
    }

    [RelayCommand]
    private static Task OpenPendingResets() => Shell.Current.GoToAsync("//fencers");

    private async Task LoadPendingPaymentRequestsAsync()
    {
        PendingPaymentRequests.Clear();
        if (!_auth.IsLoggedInInstructor)
        {
            OnPropertyChanged(nameof(HasPendingPaymentRequests));
            return;
        }

        try
        {
            _cache.InvalidatePaymentRequests();
            var fencersTask = _sheets.GetFencersAsync();
            var requestsTask = _sheets.GetPaymentRequestsAsync();
            await Task.WhenAll(fencersTask, requestsTask);

            var nameMap = fencersTask.Result.ToDictionary(f => f.Id, f => f.DisplayName);
            foreach (var r in requestsTask.Result
                .Where(r => r.Status == PaymentRequestStatus.Pending)
                .OrderByDescending(r => r.RequestedAtUtc))
            {
                PendingPaymentRequests.Add(new PaymentRequestRow(
                    r, nameMap.GetValueOrDefault(r.FencerId, "Unknown")));
            }
        }
        catch
        {
            PendingPaymentRequests.Clear();
        }

        OnPropertyChanged(nameof(HasPendingPaymentRequests));
    }

    [RelayCommand]
    private Task AcceptPaymentRequestAsync(PaymentRequestRow row) => ReviewPaymentRequestAsync(row, accept: true);

    [RelayCommand]
    private Task RejectPaymentRequestAsync(PaymentRequestRow row) => ReviewPaymentRequestAsync(row, accept: false);

    private async Task ReviewPaymentRequestAsync(PaymentRequestRow row, bool accept)
    {
        if (row is null || !_auth.IsLoggedInInstructor) return;
        var meId = _auth.CurrentFencer?.Id ?? "";

        try
        {
            _cache.InvalidatePaymentRequests();
            var fresh = (await _sheets.GetPaymentRequestsAsync())
                .FirstOrDefault(r => r.Id == row.Request.Id);

            if (fresh is null || fresh.Status != PaymentRequestStatus.Pending)
            {
                if (_dialogs is not null)
                    await _dialogs.ShowAsync("Already handled", "This payment request was already reviewed.");
                await LoadPendingPaymentRequestsAsync();
                return;
            }

            if (accept)
            {
                await _sheets.MarkPaidAsync(new Payment
                {
                    FencerId = fresh.FencerId,
                    Year = fresh.Year,
                    Month = fresh.Month,
                    Amount = fresh.Amount,
                    PaidOn = DateTime.Now
                });
                fresh.Status = PaymentRequestStatus.Accepted;
            }
            else
            {
                fresh.Status = PaymentRequestStatus.Rejected;
            }

            fresh.ReviewedByInstructorId = meId;
            fresh.ReviewedAtUtc = DateTime.UtcNow;
            await _sheets.UpsertPaymentRequestAsync(fresh);
            _cache.InvalidatePaymentRequests();

            await LoadPendingPaymentRequestsAsync();
            await LoadPaymentStatusAsync();
        }
        catch (Exception ex)
        {
            if (_dialogs is not null)
                await _dialogs.ShowAsync("Review failed", ex.Message);
        }
    }
}