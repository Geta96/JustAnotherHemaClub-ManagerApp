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

    // Suppress silent re-loads for 30 seconds after the last successful fetch.
    private static readonly TimeSpan SilentReloadThrottle = TimeSpan.FromSeconds(30);
    private DateTime _lastLoadedUtc = DateTime.MinValue;

    public ObservableCollection<Fencer> Fencers { get; } = new();

    private Dictionary<string, (int Sessions, decimal Amount, bool Paid)> _statusByFencer = new();

    // Inputs cached for the details view's stat calculations.
    private List<TrainingSession> _allTrainings = new();
    private List<IndividualLesson> _allLessons = new();

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

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        // Skip silent refreshes within the throttle window (rapid back-navigation).
        if (!showSpinner && DateTime.UtcNow - _lastLoadedUtc < SilentReloadThrottle)
            return;

        if (showSpinner) IsLoading = true;
        BackendRequestSucceeded = false;
        BackendError = null;
        BackendStatus = "Loading fencers from Google Sheets...";

        try
        {
            var today = DateTime.Today;

            var fencersTask    = _sheets.GetFencersAsync();
            var trainingsTask  = _sheets.GetTrainingsAsync();
            var paymentsTask   = _sheets.GetPaymentsAsync(today.Year, today.Month);
            var lessonsTask    = _sheets.GetIndividualLessonsAsync();
            var priceRulesTask = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(fencersTask, trainingsTask, paymentsTask, lessonsTask, priceRulesTask);

            var all = fencersTask.Result.OrderBy(f => f.Name).ToList();
            var allTrainings = trainingsTask.Result;
            var payments = paymentsTask.Result;
            var allRules = priceRulesTask.Result;
            var allLessons = lessonsTask.Result;

            // The credit-carry pre-pass needs prior-month payments; fetch them
            // (cache makes repeat calls cheap) before handing everything to the
            // background computation.
            var priorMonths = allTrainings
                .Where(t => t.Date.Year < today.Year ||
                            (t.Date.Year == today.Year && t.Date.Month < today.Month))
                .Select(t => (Y: t.Date.Year, M: t.Date.Month))
                .Distinct()
                .OrderBy(t => t.Y).ThenBy(t => t.M)
                .ToList();

            var priorPaymentTasks = priorMonths
                .ToDictionary(ym => ym, ym => _sheets.GetPaymentsAsync(ym.Y, ym.M));
            if (priorPaymentTasks.Count > 0)
                await Task.WhenAll(priorPaymentTasks.Values);

            var priorPayments = priorMonths.ToDictionary(
                ym => ym, ym => priorPaymentTasks[ym].Result);

            // ===== Heavy CPU aggregation OFF the UI thread =====
            // The credit-carry pre-pass loops every fencer × every prior month;
            // running it on the dispatcher is what made the first load lag.
            var statusByFencer = await Task.Run(() => ComputeStatuses(
                all, allTrainings, payments, priorMonths, priorPayments, allRules, today));

            // ----- UI-thread publish -----
            Fencers.Clear();
            foreach (var f in all) Fencers.Add(f);

            _statusByFencer = statusByFencer;
            _allTrainings = allTrainings;
            _allLessons = allLessons;

            SelectedFencer ??= Fencers.FirstOrDefault();
            RecomputeSelectedDetails();

            BackendRequestSucceeded = true;
            BackendStatus = $"Backend request successful. Loaded {Fencers.Count} fencer(s).";
            _lastLoadedUtc = DateTime.UtcNow;
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
    /// Pure CPU computation of each fencer's current-month status (sessions,
    /// outstanding amount, paid flag) including the credit-carry pre-pass. Safe
    /// to run on a background thread — touches no UI-bound state.
    /// </summary>
    private static Dictionary<string, (int Sessions, decimal Amount, bool Paid)> ComputeStatuses(
        List<Fencer> all,
        List<TrainingSession> allTrainings,
        List<Payment> payments,
        List<(int Y, int M)> priorMonths,
        Dictionary<(int Y, int M), List<Payment>> priorPayments,
        List<PriceRule> allRules,
        DateTime today)
    {
        List<PriceRule> RulesForMonth(int y, int m)
        {
            var from = new DateTime(y, m, 1);
            var to   = from.AddMonths(1).AddDays(-1);
            return allRules.Where(r => r.StartDate.Date <= to &&
                                        (r.EndDate is null || r.EndDate.Value.Date >= from))
                           .ToList();
        }

        var monthRules = RulesForMonth(today.Year, today.Month);

        var monthTrainings = allTrainings
            .Where(t => t.Date.Year == today.Year && t.Date.Month == today.Month)
            .ToList();

        var attendanceByMonth = priorMonths.ToDictionary(
            ym => ym,
            ym => allTrainings.Where(t => t.Date.Year == ym.Y && t.Date.Month == ym.M)
                              .SelectMany(t => t.AttendeeFencerIds)
                              .GroupBy(id => id)
                              .ToDictionary(g => g.Key, g => g.Count()));

        var paidByMonthByFencer = priorMonths.ToDictionary(
            ym => ym,
            ym => priorPayments[ym]
                    .GroupBy(p => p.FencerId)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount)));

        var creditIntoCurrentMonth = new Dictionary<string, decimal>(all.Count);
        foreach (var f in all)
        {
            decimal credit = 0m;
            foreach (var ym in priorMonths)
            {
                attendanceByMonth[ym].TryGetValue(f.Id, out var att);
                paidByMonthByFencer[ym].TryGetValue(f.Id, out var paid);
                if (att == 0 && paid == 0m && credit == 0m) continue;

                var rules = RulesForMonth(ym.Y, ym.M);
                var quote = DuesCalculator.Calculate(att, f.IsStudent, rules, paid + credit);
                credit = quote.Overpayment;
            }
            creditIntoCurrentMonth[f.Id] = credit;
        }

        var paidByFencer = payments
            .GroupBy(p => p.FencerId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var attendance = monthTrainings
            .SelectMany(t => t.AttendeeFencerIds)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return all.ToDictionary(
            f => f.Id,
            f =>
            {
                attendance.TryGetValue(f.Id, out var count);
                paidByFencer.TryGetValue(f.Id, out var cash);
                creditIntoCurrentMonth.TryGetValue(f.Id, out var credit);

                var quote = DuesCalculator.Calculate(count, f.IsStudent, monthRules, cash + credit);
                return (Sessions: count, Amount: quote.Outstanding, Paid: quote.IsCovered);
            });
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
            : (Sessions: 0, Amount: 0m, Paid: true);

        SelectedDetails = BuildDetails(SelectedFencer, monthStatus.Sessions, monthStatus.Amount, monthStatus.Paid);

        OnPropertyChanged(nameof(HasSelection));
    }

    private FencerDetailsVm BuildDetails(Fencer fencer, int sessionsThisMonth, decimal amountDue, bool isPaid)
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
            ? "—"
            : string.Join(", ",
                perMonth
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                    .Select(x => new DateTime(x.Year, x.Month, 1)
                        .ToString("MMM yyyy", CultureInfo.InvariantCulture)));

        string averageAttendanceText;
        string mostAttendanceText;
        if (perMonth.Count == 0)
        {
            averageAttendanceText = "—";
            mostAttendanceText = "—";
        }
        else
        {
            // Compact stat-sheet format: "1.5 avg · 2 mo"
            var avg = perMonth.Average(x => x.Count);
            averageAttendanceText = $"{avg:0.0} avg · {perMonth.Count} mo";

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

        return new FencerDetailsVm(
            fencer,
            sessionsThisMonth,
            amountDue,
            isPaid,
            recentSessions: recent,
            activeMonthsText: activeMonthsText,
            averageAttendanceText: averageAttendanceText,
            mostAttendanceText: mostAttendanceText,
            oneOnOneReceived: received,
            oneOnOneGiven: given);
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