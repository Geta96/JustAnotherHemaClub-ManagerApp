using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FinanceViewModel : ObservableObject
{
    public const string TabMonthly = "Monthly";
    public const string TabYearly  = "Yearly";
    public const string TabAllTime = "All Time";

    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<MonthFinanceVm> Months { get; } = new();

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;
    public bool ShowPersonalSummary  => !_auth.IsLoggedInInstructor && _auth.CurrentFencer is not null;

    /// <summary>
    /// Carousel items wrap (this) so each tab page starts with the right
    /// BindingContext without needing a RelativeSource walk to the ContentPage.
    /// </summary>
    public IReadOnlyList<FinanceTab> Tabs { get; }

    [ObservableProperty] private decimal personalTotalDue;
    [ObservableProperty] private bool personalAllPaid;
    [ObservableProperty] private string personalSummary = "";
    [ObservableProperty] private bool isLoading;

    [ObservableProperty] private int selectedTabIndex;
    public bool IsMonthlyTab => SelectedTabIndex == 0;
    public bool IsYearlyTab  => SelectedTabIndex == 1;
    public bool IsAllTimeTab => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMonthlyTab));
        OnPropertyChanged(nameof(IsYearlyTab));
        OnPropertyChanged(nameof(IsAllTimeTab));
    }

    [RelayCommand] private void ShowMonthlyTab() => SelectedTabIndex = 0;
    [RelayCommand] private void ShowYearlyTab()  => SelectedTabIndex = 1;
    [RelayCommand] private void ShowAllTimeTab() => SelectedTabIndex = 2;

    // All-time aggregates
    [ObservableProperty] private decimal allTimeIncome;
    [ObservableProperty] private decimal allTimeExpenses;
    [ObservableProperty] private decimal allTimeBalance;
    [ObservableProperty] private int     allTimeSessions;
    [ObservableProperty] private int     activeFencers;
    [ObservableProperty] private double  allTimeAvgAttendance;

    // Year aggregates
    [ObservableProperty] private int     year = DateTime.Today.Year;
    [ObservableProperty] private decimal yearIncome;
    [ObservableProperty] private decimal yearExpenses;
    [ObservableProperty] private decimal yearBalance;
    [ObservableProperty] private int     yearSessions;
    [ObservableProperty] private double  yearAvgAttendance;

    public FinanceViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;

        Tabs = _auth.IsLoggedInInstructor
            ? new[]
              {
                  new FinanceTab(TabMonthly, this),
                  new FinanceTab(TabYearly,  this),
                  new FinanceTab(TabAllTime, this),
              }
            : new[] { new FinanceTab(TabMonthly, this) };
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            // Use a wide but real window. DateTime.MinValue/MaxValue arithmetic
            // overflows inside GoogleSheets serial-date formatting and the whole
            // load silently fails -> empty Finance page.
            var rangeFrom = new DateTime(2000, 1, 1);
            var rangeTo   = new DateTime(DateTime.Today.Year + 5, 12, 31);

            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var expensesTask  = _sheets.GetExpensesAsync(rangeFrom, rangeTo);
            await Task.WhenAll(fencersTask, trainingsTask, expensesTask);

            var fencers     = fencersTask.Result;
            var trainings   = trainingsTask.Result;
            var expensesAll = expensesTask.Result;

            var today           = DateTime.Today;
            var isInstructor    = _auth.IsLoggedInInstructor;
            var currentFencerId = _auth.CurrentFencer?.Id;

            // Always include the current month so the page is never empty,
            // plus every month that has trainings, expenses, or (for the
            // logged-in fencer) a recorded payment we still need to look up.
            var monthsSet = new HashSet<(int Y, int M)> { (today.Year, today.Month) };
            foreach (var s in trainings)    monthsSet.Add((s.Date.Year, s.Date.Month));
            foreach (var e in expensesAll)  monthsSet.Add((e.Date.Year, e.Date.Month));

            var ordered = monthsSet
                .OrderByDescending(t => t.Y).ThenByDescending(t => t.M)
                .ToList();

            var paymentTasks = ordered
                .Select(t => _sheets.GetPaymentsAsync(t.Y, t.M))
                .ToArray();
            await Task.WhenAll(paymentTasks);

            decimal totalIncome = 0, totalExpenses = 0;
            int totalSessions = 0;
            double weightedAttSum = 0;
            int weightedAttCount = 0;

            decimal yIncome = 0, yExpenses = 0;
            int ySessions = 0;
            double yWeightedAttSum = 0;
            int yWeightedAttCount = 0;

            var built = new List<MonthFinanceVm>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var (y, m) = ordered[i];
                var monthVm = new MonthFinanceVm(y, m);

                var monthSessions = trainings
                    .Where(s => s.Date.Year == y && s.Date.Month == m)
                    .ToList();
                var attendance = monthSessions
                    .SelectMany(s => s.AttendeeFencerIds)
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count());

                var payments    = paymentTasks[i].Result;
                var monthIncome = payments.Sum(p => p.Amount);

                var from         = new DateTime(y, m, 1);
                var to           = from.AddMonths(1).AddDays(-1);
                var monthExpenses = expensesAll
                    .Where(e => e.Date >= from && e.Date <= to)
                    .Sum(e => e.Amount);

                var avg = monthSessions.Count == 0
                    ? 0
                    : monthSessions.Average(s => s.AttendeeFencerIds.Count);

                totalIncome   += monthIncome;
                totalExpenses += monthExpenses;
                totalSessions += monthSessions.Count;
                if (monthSessions.Count > 0)
                {
                    weightedAttSum   += avg * monthSessions.Count;
                    weightedAttCount += monthSessions.Count;
                }
                if (y == Year)
                {
                    yIncome   += monthIncome;
                    yExpenses += monthExpenses;
                    ySessions += monthSessions.Count;
                    if (monthSessions.Count > 0)
                    {
                        yWeightedAttSum   += avg * monthSessions.Count;
                        yWeightedAttCount += monthSessions.Count;
                    }
                }

                // Build per-fencer dues rows.
                var fencersForMonth = isInstructor
                    ? fencers.Where(f => f.Active)
                    : fencers.Where(f => f.Active && f.Id == currentFencerId);

                foreach (var f in fencersForMonth)
                {
                    attendance.TryGetValue(f.Id, out var count);
                    var amount = DuesCalculator.Calculate(count, f.IsStudent);
                    var paid   = payments.Any(p => p.FencerId == f.Id);

                    // For the logged-in fencer we always want a row for the
                    // current month so they see their status, even with 0
                    // sessions. For everyone else (instructor view) skip the
                    // "no sessions and nothing paid" rows.
                    var isMineThisMonth = !isInstructor
                                          && f.Id == currentFencerId
                                          && y == today.Year && m == today.Month;

                    if (count == 0 && !paid && !isMineThisMonth) continue;

                    monthVm.Dues.Add(new FencerDueRow(f, count, amount, paid));
                }

                if (isInstructor)
                {
                    foreach (var e in expensesAll.Where(e => e.Date >= from && e.Date <= to))
                        monthVm.Expenses.Add(e);
                }

                monthVm.RaiseTotals();
                built.Add(monthVm);
            }

            // Auto-expand the most recent month so the page never reads as blank.
            if (built.Count > 0) built[0].IsExpanded = true;

            Months.Clear();
            foreach (var mv in built) Months.Add(mv);

            AllTimeIncome        = totalIncome;
            AllTimeExpenses      = totalExpenses;
            AllTimeBalance       = totalIncome - totalExpenses;
            AllTimeSessions      = totalSessions;
            ActiveFencers        = fencers.Count(f => f.Active);
            AllTimeAvgAttendance = weightedAttCount == 0 ? 0 : weightedAttSum / weightedAttCount;

            YearIncome        = yIncome;
            YearExpenses      = yExpenses;
            YearBalance       = yIncome - yExpenses;
            YearSessions      = ySessions;
            YearAvgAttendance = yWeightedAttCount == 0 ? 0 : yWeightedAttSum / yWeightedAttCount;

            RecomputePersonalSummary();
            OnPropertyChanged(nameof(ShowPersonalSummary));
            OnPropertyChanged(nameof(IsLoggedInInstructor));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FinanceViewModel.LoadAsync] {ex}");

            // Surface the failure instead of leaving the user with a blank page.
            var page = Application.Current?.MainPage;
            if (page is not null)
                await page.DisplayAlert("Couldn't load Finance",
                                        ex.Message,
                                        "OK");
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    private void RecomputePersonalSummary()
    {
        if (_auth.IsLoggedInInstructor || _auth.CurrentFencer is null)
        {
            PersonalTotalDue = 0;
            PersonalAllPaid = true;
            PersonalSummary = "";
            return;
        }

        var myId = _auth.CurrentFencer.Id;
        var total = Months
            .SelectMany(mv => mv.Dues)
            .Where(d => d.Fencer.Id == myId && !d.IsPaid)
            .Sum(d => d.AmountDue);

        PersonalTotalDue = total;
        PersonalAllPaid = total <= 0;
        PersonalSummary = PersonalAllPaid
            ? "All payed up. Thank you!"
            : $"{total:N0} Ft is due.";
    }

    [RelayCommand]
    public async Task MarkPaidAsync(FencerDueRow row)
    {
        if (row is null || row.IsPaid || row.AmountDue <= 0) return;

        var month = Months.First(mvm => mvm.Dues.Contains(row));

        var p = new Payment
        {
            FencerId = row.Fencer.Id,
            Year = month.Year,
            Month = month.Month,
            Amount = row.AmountDue,
            PaidOn = DateTime.Now
        };
        await _sheets.MarkPaidAsync(p);
        row.IsPaid = true;
        month.RaiseTotals();
        RecomputePersonalSummary();
    }

    [RelayCommand]
    public async Task AddExpenseAsync(MonthFinanceVm month)
    {
        if (month is null) return;
        if (string.IsNullOrWhiteSpace(month.NewExpenseDescription) && month.NewExpenseAmount <= 0)
            return;

        var date = new DateTime(month.Year, month.Month,
            Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(month.Year, month.Month)));

        var e = new Expense
        {
            Date = date,
            Category = month.NewExpenseCategory,
            Description = month.NewExpenseDescription,
            Amount = month.NewExpenseAmount
        };
        await _sheets.AddExpenseAsync(e);
        month.Expenses.Add(e);

        month.NewExpenseCategory = "";
        month.NewExpenseDescription = "";
        month.NewExpenseAmount = 0;
        month.IsAddingExpense = false;
        month.RaiseTotals();
    }
}