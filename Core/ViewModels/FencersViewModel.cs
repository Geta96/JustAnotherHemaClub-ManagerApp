using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencersViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    // Cancels any in-flight LoadAsync when the user navigates away mid-refresh.
    private CancellationTokenSource? _loadCts;

    // Suppress silent re-loads for 30 seconds after the last successful fetch.
    private static readonly TimeSpan SilentReloadThrottle = TimeSpan.FromSeconds(30);
    private DateTime _lastLoadedUtc = DateTime.MinValue;

    public ObservableCollection<Fencer> Fencers { get; } = new();

    private Dictionary<string, (int Sessions, FencerDuesLedger.DuesSummary Summary)> _statusByFencer = new();

    // Inputs cached for the details view's stat calculations.
    private List<TrainingSession> _allTrainings = new();
    private List<IndividualLesson> _allLessons = new();
    private List<Payment> _allPayments = new();

    // Price rules active for the current month, used to phrase a fencer's
    // forward credit as "Payed for the month" / "Payed for X more sessions".
    private List<PriceRule> _currentMonthRules = new();

    [ObservableProperty] private Fencer? selectedFencer;
    [ObservableProperty] private FencerDetailsVm? selectedDetails;

    // Visible diagnostics on the page
    [ObservableProperty] private bool backendRequestSucceeded;
    [ObservableProperty] private string backendStatus = "Not loaded yet.";
    [ObservableProperty] private string? backendError;
    [ObservableProperty] private bool isLoading;

    public bool HasSelection => SelectedDetails is not null;
    public bool CanPromoteSelected =>
        _auth.IsLoggedInInstructor &&
        SelectedFencer is not null &&
        !SelectedFencer.IsInstructor;

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;

    public FencersViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    /// <summary>
    /// Abandons any in-flight <see cref="LoadAsync"/>. Called when the page is
    /// disappearing so a manual refresh doesn't mutate the UI-bound collections
    /// after the CollectionView has been detached (crashes on Android when the
    /// RecyclerView has already been torn down).
    /// </summary>
    public void CancelLoad() => _loadCts?.Cancel();

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        // Skip silent refreshes within the throttle window (rapid back-navigation).
        if (!showSpinner && DateTime.UtcNow - _lastLoadedUtc < SilentReloadThrottle)
            return;

        // Cancel a previous in-flight load and start a fresh token for this one.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (showSpinner) IsLoading = true;
        BackendRequestSucceeded = false;
        BackendError = null;
        BackendStatus = "Loading fencers from Google Sheets...";

        try
        {
            var today = DateTime.Today;

            var fencersTask    = _sheets.GetFencersAsync();
            var trainingsTask  = _sheets.GetTrainingsAsync();
            var lessonsTask    = _sheets.GetIndividualLessonsAsync();
            var priceRulesTask = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(fencersTask, trainingsTask, lessonsTask, priceRulesTask);

            ct.ThrowIfCancellationRequested();

            var all = fencersTask.Result.OrderBy(f => f.Name).ToList();
            var allTrainings = trainingsTask.Result;
            var allRules = priceRulesTask.Result;
            var allLessons = lessonsTask.Result;

            var currentMonthRules = FencerDuesLedger.RulesForMonth(allRules, today.Year, today.Month);

            // Month span for the dues ledger. This MUST match the Finance page:
            // include the current month, every training month (INCLUDING future
            // materialized sessions), and any payment-only months. Capping at the
            // current month or omitting payment-only months makes overpayment
            // credit that lives in a later month vanish here, so a fencer who is
            // "Overpaid" on Finance would wrongly show "All payed up" on Fencers.
            var monthsSet = new HashSet<(int Y, int M)> { (today.Year, today.Month) };
            foreach (var t in allTrainings) monthsSet.Add((t.Date.Year, t.Date.Month));

            var orderedYm = monthsSet.OrderBy(x => x.Y).ThenBy(x => x.M).ToList();
            var paymentTasks = orderedYm.ToDictionary(
                ym => ym, ym => _sheets.GetPaymentsAsync(ym.Y, ym.M));
            if (paymentTasks.Count > 0)
                await Task.WhenAll(paymentTasks.Values);

            ct.ThrowIfCancellationRequested();

            // Fold in payment-only months (pre-payments / refunds without a
            // training), then re-fetch so their payments enter the credit chain.
            var extraMonths = paymentTasks.Values
                .SelectMany(t => t.Result)
                .Select(p => (Y: p.Year, M: p.Month))
                .Where(ym => !monthsSet.Contains(ym))
                .Distinct()
                .ToList();
            foreach (var ym in extraMonths)
            {
                monthsSet.Add(ym);
                paymentTasks[ym] = _sheets.GetPaymentsAsync(ym.Y, ym.M);
            }
            if (extraMonths.Count > 0)
                await Task.WhenAll(paymentTasks.Values);

            ct.ThrowIfCancellationRequested();

            var monthSpan = monthsSet.OrderBy(x => x.Y).ThenBy(x => x.M).ToList();
            var paymentsByMonth = monthSpan.ToDictionary(
                ym => ym, ym => paymentTasks[ym].Result);

            // ===== Heavy CPU aggregation OFF the UI thread =====
            var statusByFencer = await Task.Run(() => ComputeStatuses(
                all, allTrainings, monthSpan, paymentsByMonth, allRules, today), ct);

            // The page may have been navigated away from while we computed.
            ct.ThrowIfCancellationRequested();

            // ----- UI-thread publish -----
            Fencers.Clear();
            foreach (var f in all) Fencers.Add(f);

            _statusByFencer = statusByFencer;
            _allTrainings = allTrainings;
            _allLessons = allLessons;
            _currentMonthRules = currentMonthRules;

            if (SelectedFencer is null)
            {
                var currentId = _auth.CurrentFencer?.Id;
                SelectedFencer = (!string.IsNullOrWhiteSpace(currentId)
                        ? Fencers.FirstOrDefault(f => f.Id == currentId)
                        : null)
                    ?? Fencers.FirstOrDefault();
            }
            RecomputeSelectedDetails();

            BackendRequestSucceeded = true;
            BackendStatus = $"Backend request successful. Loaded {Fencers.Count} fencer(s).";
            _lastLoadedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Expected when the user navigates away mid-refresh — abandon quietly.
        }
        catch (Exception ex)
        {
            BackendRequestSucceeded = false;
            BackendError = ex.ToString();
            BackendStatus = "Backend request failed.";
            SelectedDetails = null;
            OnPropertyChanged(nameof(HasSelection));
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    /// <summary>
    /// Pure CPU computation of each fencer's CUMULATIVE dues summary (status,
    /// outstanding split, and the months that still owe) via the shared ledger,
    /// plus their current-month session count. Safe to run on a background thread.
    /// </summary>
    private static Dictionary<string, (int Sessions, FencerDuesLedger.DuesSummary Summary)> ComputeStatuses(
        List<Fencer> all,
        List<TrainingSession> allTrainings,
        List<(int Y, int M)> monthSpan,
        Dictionary<(int Y, int M), List<Payment>> paymentsByMonth,
        List<PriceRule> allRules,
        DateTime today)
    {
        var attendanceByMonth = monthSpan.ToDictionary(
            ym => ym,
            ym => allTrainings.Where(t => t.Date.Year == ym.Y && t.Date.Month == ym.M)
                              .SelectMany(t => t.AttendeeFencerIds)
                              .GroupBy(id => id)
                              .ToDictionary(g => g.Key, g => g.Count()));

        var paidByMonth = monthSpan.ToDictionary(
            ym => ym,
            ym => paymentsByMonth[ym]
                    .GroupBy(p => p.FencerId)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount)));

        var currentYm = (today.Year, today.Month);

        var result = new Dictionary<string, (int, FencerDuesLedger.DuesSummary)>(all.Count);
        foreach (var f in all)
        {
            var inputs = new List<FencerDuesLedger.MonthInput>(monthSpan.Count);
            foreach (var ym in monthSpan)
            {
                attendanceByMonth[ym].TryGetValue(f.Id, out var att);
                paidByMonth[ym].TryGetValue(f.Id, out var paid);
                inputs.Add(new FencerDuesLedger.MonthInput(ym.Y, ym.M, att, paid));
            }

            var ledger = FencerDuesLedger.Compute(f.IsStudent, inputs, allRules);
            var summary = FencerDuesLedger.Summarize(ledger, today.Year, today.Month);

            attendanceByMonth[currentYm].TryGetValue(f.Id, out var sessionsThisMonth);
            result[f.Id] = (sessionsThisMonth, summary);
        }

        return result;
    }

    partial void OnSelectedFencerChanged(Fencer? value)
    {
        RecomputeSelectedDetails();
        OnPropertyChanged(nameof(CanPromoteSelected));
    }

    private void RecomputeSelectedDetails()
    {
        if (SelectedFencer is null)
        {
            SelectedDetails = null;
            OnPropertyChanged(nameof(HasSelection));
            return;
        }

        var monthStatus = _statusByFencer.TryGetValue(SelectedFencer.Id, out var s)
            ? s
            : (Sessions: 0, Summary: FencerDuesLedger.AllPaidSummary);

        SelectedDetails = BuildDetails(SelectedFencer, monthStatus.Sessions, monthStatus.Summary);

        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Maps the cumulative summary to display text + colour flags (green/grey/red).</summary>
    private static (string Text, bool Green, bool Grey, bool Red) DescribePayment(
        FencerDuesLedger.DuesSummary s, bool isStudent, IReadOnlyList<PriceRule> currentRules)
        => s.Status switch
        {
            FencerDuesLedger.DuesStatus.Overpaid =>
                (DuesCalculator.DescribeOverpayment(s.ThisMonthEffectivePaid, s.FinalCredit, isStudent, currentRules), true, false, false),
            FencerDuesLedger.DuesStatus.DueThisMonth =>
                ($"Due {s.ThisMonthOutstanding:N0} Ft by the end of this month", false, true, false),
            FencerDuesLedger.DuesStatus.DueWithArrears =>
                ($"Due {s.TotalOutstanding:N0} Ft", false, false, true),
            _ => ("All payed up", true, false, false),
        };

    private FencerDetailsVm BuildDetails(Fencer fencer, int sessionsThisMonth, FencerDuesLedger.DuesSummary summary)
    {
        // Trainings the selected fencer attended, newest first.
        var attended = _allTrainings
            .Where(t => t.AttendeeFencerIds.Contains(fencer.Id))
            .OrderByDescending(t => t.Date)
            .ToList();

        var recent = attended
            .Take(4)
            .Select(t => new FencerSessionRow(t.Topic, t.Date))
            .ToList();

        // Per-month grouping for averages / most attended / active months.
        var perMonth = attended
            .GroupBy(t => (t.Date.Year, t.Date.Month))
            .Select(g => new
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ToList();

        string activeMonthsText = perMonth.Count == 0
            ? "�"
            : string.Join(", ",
                perMonth
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                    .Select(x => new DateTime(x.Year, x.Month, 1)
                        .ToString("MMM yyyy", CultureInfo.InvariantCulture)));

        string averageAttendanceText;
        string mostAttendanceText;
        if (perMonth.Count == 0)
        {
            averageAttendanceText = "�";
            mostAttendanceText = "�";
        }
        else
        {
            // Compact stat-sheet format: "1.5 avg � 2 mo"
            var avg = perMonth.Average(x => x.Count);
            averageAttendanceText = $"{avg:0.0} avg � {perMonth.Count} mo";

            // Compact "most" format: "2 in Jun 2026"
            var top = perMonth.OrderByDescending(x => x.Count).First();
            var topLabel = new DateTime(top.Year, top.Month, 1)
                .ToString("MMM yyyy", CultureInfo.InvariantCulture);
            mostAttendanceText = $"{top.Count} in {topLabel}";
        }

        // 1 on 1 lessons (only counts accepted lessons; pending/rejected don't count).
        int received = _allLessons.Count(l =>
            l.StudentId == fencer.Id &&
            l.Status == IndividualLessonStatus.Accepted);

        int given = _allLessons.Count(l =>
            l.InstructorId == fencer.Id &&
            l.Status == IndividualLessonStatus.Accepted);

        var (statusText, green, grey, red) = DescribePayment(summary, fencer.IsStudent, _currentMonthRules);

        var unpaidRows = (summary.UnpaidMonths ?? Array.Empty<FencerDuesLedger.UnpaidMonth>())
            .OrderBy(u => u.Year).ThenBy(u => u.Month)
            .Select(u => new FencerUnpaidMonthRow(u.Year, u.Month, u.Amount))
            .ToList();

        // Payment History: regular fencers only see their own; instructors see all.
        bool showPaymentHistory =
            _auth.IsLoggedInInstructor || fencer.Id == _auth.CurrentFencer?.Id;

        // Months that still owe something (after cash + carried credit). A month
        // group gets the green "paid up" tick when it is NOT in this set.
        var unpaidMonthSet = (summary.UnpaidMonths ?? Array.Empty<FencerDuesLedger.UnpaidMonth>())
            .Select(u => (u.Year, u.Month))
            .ToHashSet();

        var paymentHistory = showPaymentHistory
            ? _allPayments
                .Where(p => p.FencerId == fencer.Id)
                .GroupBy(p => (p.Year, p.Month))
                .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
                .Select(g => new FencerPaymentMonthGroup(
                    g.Key.Year,
                    g.Key.Month,
                    _allTrainings.Count(t => t.Date.Year == g.Key.Year &&
                                             t.Date.Month == g.Key.Month &&
                                             t.AttendeeFencerIds.Contains(fencer.Id)),
                    g.OrderBy(p => p.PaidOn)
                     .Select(p => new FencerPaymentRow(p.PaidOn, p.Amount)),
                    isPaidUp: !unpaidMonthSet.Contains((g.Key.Year, g.Key.Month))))
                .ToList()
            : new List<FencerPaymentMonthGroup>();

        return new FencerDetailsVm(
            fencer,
            sessionsThisMonth,
            amountDue: summary.TotalOutstanding,
            isPaid: green,
            recentSessions: recent,
            activeMonthsText: activeMonthsText,
            averageAttendanceText: averageAttendanceText,
            mostAttendanceText: mostAttendanceText,
            oneOnOneReceived: received,
            oneOnOneGiven: given,
            paymentStatusText: statusText,
            paymentAllPaid: green,
            paymentDueThisMonth: grey,
            paymentArrears: red,
            unpaidMonths: unpaidRows,
            showPaymentHistory: showPaymentHistory,
            paymentHistory: paymentHistory);
    }

    public async Task<string?> PromoteSelectedAsync(string username, string password)
    {
        if (!_auth.IsLoggedInInstructor) return "Only logged-in instructors can promote.";
        if (SelectedFencer is null) return "Pick a fencer first.";
        if (SelectedFencer.IsInstructor) return "This fencer is already an instructor.";

        try
        {
            SelectedFencer.IsInstructor = true;
            await _sheets.UpsertFencerAsync(SelectedFencer);

            OnPropertyChanged(nameof(CanPromoteSelected));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}