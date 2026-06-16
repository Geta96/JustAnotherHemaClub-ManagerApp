using System.Collections.ObjectModel;
using System.Globalization;
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
    public const string TabPrices  = "Prices";

    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<MonthFinanceVm> Months { get; } = new();

    public PricesViewModel PricesVm { get; }

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;
    public bool ShowPersonalSummary  => !_auth.IsLoggedInInstructor && _auth.CurrentFencer is not null;

    public IReadOnlyList<FinanceTab> Tabs { get; }

    [ObservableProperty] private decimal personalTotalDue;
    [ObservableProperty] private bool personalAllPaid;
    [ObservableProperty] private string personalSummary = "";
    [ObservableProperty] private bool isLoading;

    [ObservableProperty] private string pricingSummary = "";

    [ObservableProperty] private string pricingWarning = "";

    public bool HasPricingWarning => !string.IsNullOrWhiteSpace(PricingWarning);

    partial void OnPricingWarningChanged(string value)
        => OnPropertyChanged(nameof(HasPricingWarning));

    [ObservableProperty] private int selectedTabIndex;
    public bool IsMonthlyTab => SelectedTabIndex == 0;
    public bool IsYearlyTab  => SelectedTabIndex == 1;
    public bool IsAllTimeTab => SelectedTabIndex == 2;
    public bool IsPricesTab  => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMonthlyTab));
        OnPropertyChanged(nameof(IsYearlyTab));
        OnPropertyChanged(nameof(IsAllTimeTab));
        OnPropertyChanged(nameof(IsPricesTab));
    }

    [RelayCommand] private void ShowMonthlyTab() => SelectedTabIndex = 0;
    [RelayCommand] private void ShowYearlyTab()  => SelectedTabIndex = 1;
    [RelayCommand] private void ShowAllTimeTab() => SelectedTabIndex = 2;
    [RelayCommand] private void ShowPricesTab()  => SelectedTabIndex = 3;

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

    public FinanceViewModel(IGoogleSheetsService sheets, AuthService auth, PricesViewModel pricesVm)
    {
        _sheets = sheets;
        _auth = auth;
        PricesVm = pricesVm;

        Tabs = _auth.IsLoggedInInstructor
            ? new[]
              {
                  new FinanceTab(TabMonthly, this),
                  new FinanceTab(TabYearly,  this),
                  new FinanceTab(TabAllTime, this),
                  new FinanceTab(TabPrices,  this),
              }
            : new[] { new FinanceTab(TabMonthly, this) };
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            var rangeFrom = new DateTime(2000, 1, 1);
            var rangeTo   = new DateTime(DateTime.Today.Year + 5, 12, 31);

            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var expensesTask  = _sheets.GetExpensesAsync(rangeFrom, rangeTo);
            var rulesTask     = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(fencersTask, trainingsTask, expensesTask, rulesTask);

            var fencers     = fencersTask.Result;
            var trainings   = trainingsTask.Result;
            var expensesAll = expensesTask.Result;
            var allRules    = rulesTask.Result;

            PricingSummary = BuildPricingSummary(allRules);
            PricingWarning = BuildPricingWarning(allRules, DateTime.Today);

            var today           = DateTime.Today;
            var isInstructor    = _auth.IsLoggedInInstructor;
            var currentFencerId = _auth.CurrentFencer?.Id;

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

            // ===== Per-month inputs (computed once, reused by both the credit
            // pre-pass and the row-building loop). =====
            var rulesByMonth      = new Dictionary<(int Y, int M), List<PriceRule>>(ordered.Count);
            var attendanceByMonth = new Dictionary<(int Y, int M), Dictionary<string, int>>(ordered.Count);
            var paidByMonth       = new Dictionary<(int Y, int M), Dictionary<string, decimal>>(ordered.Count);

            for (int i = 0; i < ordered.Count; i++)
            {
                var ym = ordered[i];
                var from = new DateTime(ym.Y, ym.M, 1);
                var to   = from.AddMonths(1).AddDays(-1);

                rulesByMonth[ym] = allRules
                    .Where(r => r.StartDate.Date <= to &&
                                (r.EndDate is null || r.EndDate.Value.Date >= from))
                    .ToList();

                attendanceByMonth[ym] = trainings
                    .Where(s => s.Date.Year == ym.Y && s.Date.Month == ym.M)
                    .SelectMany(s => s.AttendeeFencerIds)
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count());

                paidByMonth[ym] = paymentTasks[i].Result
                    .GroupBy(p => p.FencerId)
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            }

            // ===== Credit-carry pre-pass: per fencer, walk months ascending and
            // accumulate the overpayment that should be applied to each month. =====
            var ascending = ordered.OrderBy(t => t.Y).ThenBy(t => t.M).ToList();
            var creditByFencerMonth = BuildCreditCarry(
                fencers.Where(f => f.Active),
                ascending,
                attendanceByMonth,
                paidByMonth,
                rulesByMonth);

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
                var ym = ordered[i];
                var (y, m) = ym;
                var monthVm = new MonthFinanceVm(y, m);

                var monthSessionsList = trainings
                    .Where(s => s.Date.Year == y && s.Date.Month == m)
                    .ToList();
                var attendance   = attendanceByMonth[ym];
                var payments     = paymentTasks[i].Result;
                var paidByFencer = paidByMonth[ym];
                var monthRules   = rulesByMonth[ym];

                var monthIncome = payments.Sum(p => p.Amount);

                var from         = new DateTime(y, m, 1);
                var to           = from.AddMonths(1).AddDays(-1);
                var monthExpenses = expensesAll
                    .Where(e => e.Date >= from && e.Date <= to)
                    .Sum(e => e.Amount);

                var avg = monthSessionsList.Count == 0
                    ? 0
                    : monthSessionsList.Average(s => s.AttendeeFencerIds.Count);

                totalIncome   += monthIncome;
                totalExpenses += monthExpenses;
                totalSessions += monthSessionsList.Count;
                if (monthSessionsList.Count > 0)
                {
                    weightedAttSum   += avg * monthSessionsList.Count;
                    weightedAttCount += monthSessionsList.Count;
                }
                if (y == Year)
                {
                    yIncome   += monthIncome;
                    yExpenses += monthExpenses;
                    ySessions += monthSessionsList.Count;
                    if (monthSessionsList.Count > 0)
                    {
                        yWeightedAttSum   += avg * monthSessionsList.Count;
                        yWeightedAttCount += monthSessionsList.Count;
                    }
                }

                var fencersForMonth = isInstructor
                    ? fencers.Where(f => f.Active)
                    : fencers.Where(f => f.Active && f.Id == currentFencerId);

                foreach (var f in fencersForMonth)
                {
                    attendance.TryGetValue(f.Id, out var count);
                    paidByFencer.TryGetValue(f.Id, out var cashPaid);
                    creditByFencerMonth.TryGetValue((f.Id, y, m), out var creditIn);

                    // Pass cash + credit as alreadyPaid so the calculator can
                    // surface Outstanding and Overpayment correctly.
                    var quote = DuesCalculator.Calculate(
                        count, f.IsStudent, monthRules, cashPaid + creditIn);

                    var isMineThisMonth = !isInstructor
                                          && f.Id == currentFencerId
                                          && y == today.Year && m == today.Month;

                    // Hide rows that have nothing to say: no sessions, no cash,
                    // no carry. Always include the logged-in fencer's current
                    // month so they see their status.
                    if (count == 0 && cashPaid == 0m && creditIn == 0m && !isMineThisMonth)
                        continue;

                    monthVm.Dues.Add(new FencerDueRow(f, quote, cashPaid));
                }

                if (isInstructor)
                {
                    foreach (var e in expensesAll.Where(e => e.Date >= from && e.Date <= to))
                        monthVm.Expenses.Add(e);
                }

                monthVm.RaiseTotals();
                built.Add(monthVm);
            }

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

            if (isInstructor)
                await PricesVm.LoadAsync(showSpinner: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FinanceViewModel.LoadAsync] {ex}");

            var page = Application.Current?.MainPage;
            if (page is not null)
                await page.DisplayAlert("Couldn't load Finance",
                                        ex.Message,
                                        "OK");
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    /// <summary>
    /// For each active fencer, walks the supplied months in chronological order
    /// and accumulates a running overpayment credit. Returns a dictionary
    /// mapping <c>(fencerId, year, month)</c> to the credit that fencer brings
    /// into that month. The credit for the *first* applicable month is always
    /// zero; subsequent months see the previous month's Overpayment, etc.
    /// </summary>
    private static Dictionary<(string Fid, int Y, int M), decimal> BuildCreditCarry(
        IEnumerable<Fencer> activeFencers,
        IReadOnlyList<(int Y, int M)> monthsAscending,
        Dictionary<(int Y, int M), Dictionary<string, int>> attendanceByMonth,
        Dictionary<(int Y, int M), Dictionary<string, decimal>> paidByMonth,
        Dictionary<(int Y, int M), List<PriceRule>> rulesByMonth)
    {
        var result = new Dictionary<(string Fid, int Y, int M), decimal>();
        foreach (var f in activeFencers)
        {
            decimal credit = 0m;
            foreach (var ym in monthsAscending)
            {
                result[(f.Id, ym.Y, ym.M)] = credit;

                attendanceByMonth[ym].TryGetValue(f.Id, out var att);
                paidByMonth[ym].TryGetValue(f.Id, out var paid);

                // No activity AND no carried credit → nothing changes.
                if (att == 0 && paid == 0m && credit == 0m) continue;

                var quote = DuesCalculator.Calculate(att, f.IsStudent, rulesByMonth[ym], paid + credit);
                credit = quote.Overpayment;
            }
        }
        return result;
    }

    private static string BuildPricingSummary(IReadOnlyList<PriceRule> all)
    {
        if (all.Count == 0)
            return "Pricing (defaults): 1 session 3 500 · 4 sessions 9 000 · unlimited 12 000";

        var activeToday = all
            .Where(r => r.IsActiveOn(DateTime.Today))
            .OrderBy(r => r.SessionCount == 0 ? int.MaxValue : r.SessionCount)
            .ToList();

        if (activeToday.Count == 0)
            return "No active price rules for today — add one on the Prices tab.";

        return "Pricing: " + string.Join(" · ", activeToday.Select(r => r.SessionCount switch
        {
            0 => $"unlimited {r.FullPrice:N0}",
            1 => $"1 session {r.FullPrice:N0}",
            _ => $"{r.SessionCount} sessions {r.FullPrice:N0}"
        }));
    }

    private static string BuildPricingWarning(IReadOnlyList<PriceRule> all, DateTime today)
    {
        if (all.Count == 0) return "";

        static bool CoversMonth(IReadOnlyList<PriceRule> rules, int year, int month)
        {
            var from = new DateTime(year, month, 1);
            var to   = from.AddMonths(1).AddDays(-1);
            return rules.Any(r =>
                r.StartDate.Date <= to &&
                (r.EndDate is null || r.EndDate.Value.Date >= from));
        }

        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var nextMonth = thisMonth.AddMonths(1);

        var thisCovered = CoversMonth(all, thisMonth.Year, thisMonth.Month);
        var nextCovered = CoversMonth(all, nextMonth.Year, nextMonth.Month);

        if (thisCovered && nextCovered) return "";

        static string Label(DateTime d) =>
            d.ToString("MMM yyyy", CultureInfo.InvariantCulture);

        if (!thisCovered && !nextCovered)
            return $"⚠ No active price rule for {Label(thisMonth)} or {Label(nextMonth)} — using default prices.";

        if (!thisCovered)
            return $"⚠ No active price rule for {Label(thisMonth)} — using default prices.";

        return $"⚠ No active price rule for {Label(nextMonth)} — using default prices.";
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
        // AmountDue is post-credit, so summing it gives the right total even
        // when prior overpayments are still being drawn down.
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

        var topUp = row.AmountDue;

        var p = new Payment
        {
            FencerId = row.Fencer.Id,
            Year     = month.Year,
            Month    = month.Month,
            Amount   = topUp,
            PaidOn   = DateTime.Now
        };
        await _sheets.MarkPaidAsync(p);

        row.ApplyTopUp(topUp);
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