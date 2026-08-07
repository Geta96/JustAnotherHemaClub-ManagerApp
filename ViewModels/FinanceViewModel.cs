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

    // Cancels any in-flight LoadAsync when the user navigates away mid-refresh.
    private CancellationTokenSource? _loadCts;

    // Suppress silent re-loads for 30 seconds after the last successful fetch.
    private static readonly TimeSpan SilentReloadThrottle = TimeSpan.FromSeconds(30);
    private DateTime _lastLoadedUtc = DateTime.MinValue;

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
    [ObservableProperty] private decimal allTimeUnpaid;

    // Year aggregates
    [ObservableProperty] private int     year = DateTime.Today.Year;
    [ObservableProperty] private decimal yearIncome;
    [ObservableProperty] private decimal yearExpenses;
    [ObservableProperty] private decimal yearBalance;
    [ObservableProperty] private int     yearSessions;
    [ObservableProperty] private double  yearAvgAttendance;
    [ObservableProperty] private decimal yearUnpaid;
    
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
        // Skip silent refreshes within the throttle window (rapid back-navigation).
        if (!showSpinner && DateTime.UtcNow - _lastLoadedUtc < SilentReloadThrottle)
            return;

        // Cancel a previous in-flight load and start a fresh token for this one.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (showSpinner) IsLoading = true;
        try
        {
            var rangeFrom = new DateTime(2000, 1, 1);
            var rangeTo   = new DateTime(DateTime.Today.Year + 5, 12, 31);

            var isInstructorEarly = _auth.IsLoggedInInstructor;

            // Kick off the Prices load concurrently with the finance aggregation —
            // it hits a different sheet range and doesn't depend on any of the
            // finance math, so there's no reason to await it serially at the end.
            var pricesLoadTask = isInstructorEarly
                ? PricesVm.LoadAsync(showSpinner: false)
                : Task.CompletedTask;

            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var expensesTask  = _sheets.GetExpensesAsync(rangeFrom, rangeTo);
            var incomesTask   = _sheets.GetIncomesAsync(rangeFrom, rangeTo);
            var rulesTask     = _sheets.GetPriceRulesAsync();
            await Task.WhenAll(fencersTask, trainingsTask, expensesTask, incomesTask, rulesTask);

            ct.ThrowIfCancellationRequested();

            var fencers     = fencersTask.Result;
            var trainings   = trainingsTask.Result;
            var expensesAll = expensesTask.Result;
            var incomesAll  = incomesTask.Result;
            var allRules    = rulesTask.Result;

            PricingSummary = BuildPricingSummary(allRules);
            PricingWarning = BuildPricingWarning(allRules, DateTime.Today);

            var today           = DateTime.Today;
            var isInstructor    = _auth.IsLoggedInInstructor;
            var currentFencerId = _auth.CurrentFencer?.Id;

            var monthsSet = new HashSet<(int Y, int M)> { (today.Year, today.Month) };
            foreach (var s in trainings)    monthsSet.Add((s.Date.Year, s.Date.Month));
            foreach (var e in expensesAll)  monthsSet.Add((e.Date.Year, e.Date.Month));
            foreach (var i in incomesAll)   monthsSet.Add((i.Date.Year, i.Date.Month));

            var ordered = monthsSet
                .OrderByDescending(t => t.Y).ThenByDescending(t => t.M)
                .ToList();

            var paymentTasks = ordered
                .Select(t => _sheets.GetPaymentsAsync(t.Y, t.M))
                .ToArray();
            await Task.WhenAll(paymentTasks);

            ct.ThrowIfCancellationRequested();

            // Everything from here to the UI assignment is pure CPU work
            // (grouping, the credit-carry pre-pass, per-month VM building and the
            // reduction). Run it OFF the UI thread so the parallel loops don't
            // block the dispatcher — otherwise the UI freezes even though the
            // work itself is parallelised. Only the final collection mutation is
            // marshalled back onto the UI thread.
            var payments = paymentTasks.Select(t => t.Result).ToArray();

            var computed = await Task.Run(() => ComputeFinance(
                fencers, trainings, expensesAll, incomesAll, allRules,
                ordered, payments, today, isInstructor, currentFencerId, Year), ct);

            // The page may have been navigated away from while we computed.
            // Publishing now would mutate a detached CollectionView, which
            // crashes on Android when the RecyclerView has already been torn down.
            ct.ThrowIfCancellationRequested();

            // ----- UI-thread section: publish the results -----
            var built = computed.Months;
            if (built.Count > 0) built[0].IsExpanded = true;

            Months.Clear();
            foreach (var mv in built) Months.Add(mv);

            AllTimeIncome        = computed.TotalIncome;
            AllTimeExpenses      = computed.TotalExpenses;
            AllTimeBalance       = computed.TotalIncome - computed.TotalExpenses;
            AllTimeSessions      = computed.TotalSessions;
            ActiveFencers        = fencers.Count(f => f.Active);
            AllTimeAvgAttendance = computed.WeightedAttCount == 0 ? 0 : computed.WeightedAttSum / computed.WeightedAttCount;
            AllTimeUnpaid        = computed.TotalUnpaid;

            YearIncome        = computed.YIncome;
            YearExpenses      = computed.YExpenses;
            YearBalance       = computed.YIncome - computed.YExpenses;
            YearSessions      = computed.YSessions;
            YearAvgAttendance = computed.YWeightedAttCount == 0 ? 0 : computed.YWeightedAttSum / computed.YWeightedAttCount;
            YearUnpaid        = computed.YUnpaid;

            RecomputePersonalSummary();
            OnPropertyChanged(nameof(ShowPersonalSummary));
            OnPropertyChanged(nameof(IsLoggedInInstructor));

            _lastLoadedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Expected when the user navigates away mid-refresh — abandon quietly.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FinanceViewModel.LoadAsync] {ex}");

            var page = Services.AppNavigationHelper.RootPage;
            if (page is not null)
                await page.DisplayAlert("Couldn't load Finance",
                                        ex.Message,
                                        "OK");
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    /// <summary>Aggregated result of the off-UI-thread finance computation.</summary>
    private sealed class FinanceComputation
    {
        public List<MonthFinanceVm> Months = new();
        public decimal TotalIncome, TotalExpenses;
        public int TotalSessions;
        public double WeightedAttSum;
        public int WeightedAttCount;
        public decimal YIncome, YExpenses;
        public int YSessions;
        public double YWeightedAttSum;
        public int YWeightedAttCount;
        public decimal TotalUnpaid, YUnpaid;
    }

    /// <summary>
    /// Pure CPU aggregation — safe to run on a background thread. Builds the
    /// per-month view models (in parallel) and reduces the running totals. No
    /// UI-bound state is touched here; the caller publishes the result on the UI
    /// thread.
    /// </summary>
    private FinanceComputation ComputeFinance(
        List<Fencer> fencers,
        List<TrainingSession> trainings,
        List<Expense> expensesAll,
        List<Income> incomesAll,
        List<PriceRule> allRules,
        List<(int Y, int M)> ordered,
        List<Payment>[] payments,
        DateTime today,
        bool isInstructor,
        string? currentFencerId,
        int yearSnapshot)
    {
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

            paidByMonth[ym] = payments[i]
                .GroupBy(p => p.FencerId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        }

        // ===== Credit-carry pre-pass =====
        var ascending = ordered.OrderBy(t => t.Y).ThenBy(t => t.M).ToList();
        var creditByFencerMonth = BuildCreditCarry(
            fencers.Where(f => f.Active),
            ascending,
            attendanceByMonth,
            paidByMonth,
            rulesByMonth);

        var perMonth = new (MonthFinanceVm Vm,
                            decimal Income, decimal Expenses, int Sessions,
                            double Avg, decimal Unpaid)[ordered.Count];

        var knownFencerIds = fencers.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        // Leave a core free for the UI thread + renderer. On the Android emulator
        // (few shared vCPUs) an unbounded Parallel.For grabs every core and starves
        // the dispatcher, so the hamburger menu / tabs stop responding while a
        // refresh is running. Capping this keeps the emulator interactive and is
        // harmless on a real device where there are more cores.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.For(0, ordered.Count, parallelOptions, i =>
        {
            var ym = ordered[i];
            var (y, m) = ym;
            var monthVm = new MonthFinanceVm(y, m);

            var monthSessionsList = trainings
                .Where(s => s.Date.Year == y && s.Date.Month == m)
                .ToList();
            var attendance   = attendanceByMonth[ym];
            var monthPayments = payments[i];
            var paidByFencer = paidByMonth[ym];
            var monthRules   = rulesByMonth[ym];

            var from         = new DateTime(y, m, 1);
            var to           = from.AddMonths(1).AddDays(-1);
            var monthOneOffIncomes = incomesAll
                .Where(x => x.Date >= from && x.Date <= to)
                .Sum(x => x.Amount);

            var monthIncome   = monthPayments.Sum(p => p.Amount) + monthOneOffIncomes;
            var monthExpenses = expensesAll
                .Where(e => e.Date >= from && e.Date <= to)
                .Sum(e => e.Amount);

            var avg = monthSessionsList.Count == 0
                ? 0
                : monthSessionsList.Average(s => s.AttendeeFencerIds.Count);

            var fencersForMonth = isInstructor
                ? fencers.Where(f => f.Active)
                : fencers.Where(f => f.Active && f.Id == currentFencerId);

            foreach (var f in fencersForMonth)
            {
                attendance.TryGetValue(f.Id, out var count);
                paidByFencer.TryGetValue(f.Id, out var cashPaid);
                creditByFencerMonth.TryGetValue((f.Id, y, m), out var creditIn);

                var quote = DuesCalculator.Calculate(
                    count, f.IsStudent, monthRules, cashPaid + creditIn);

                var isMineThisMonth = !isInstructor
                                      && f.Id == currentFencerId
                                      && y == today.Year && m == today.Month;

                if (count == 0 && cashPaid == 0m && creditIn == 0m && !isMineThisMonth)
                    continue;

                monthVm.Dues.Add(new FencerDueRow(f, quote, cashPaid));
            }

            if (isInstructor)
            {
                var orphanedPaid = monthPayments
                    .Where(p => !string.IsNullOrWhiteSpace(p.FencerId) &&
                                !knownFencerIds.Contains(p.FencerId))
                    .GroupBy(p => p.FencerId, StringComparer.Ordinal);

                foreach (var g in orphanedPaid)
                {
                    var paid = g.Sum(p => p.Amount);
                    if (paid == 0m) continue;

                    var ghost = new Fencer { Id = g.Key, Name = "" }; // DisplayName → "[Deleted User]"
                    var quote = DuesCalculator.Calculate(0, ghost.IsStudent, monthRules, paid);
                    monthVm.Dues.Add(new FencerDueRow(ghost, quote, paid));
                }
            }

            if (isInstructor)
            {
                foreach (var e in expensesAll.Where(e => e.Date >= from && e.Date <= to))
                    monthVm.Expenses.Add(e);
                foreach (var inc in incomesAll.Where(x => x.Date >= from && x.Date <= to))
                    monthVm.Incomes.Add(inc);
            }

            monthVm.RaiseTotals();
            var monthUnpaid = monthVm.Dues
                .Where(d => !d.IsPaid)
                .Sum(d => d.AmountDue);
            perMonth[i] = (monthVm, monthIncome, monthExpenses, monthSessionsList.Count, avg, monthUnpaid);
        });

        var result = new FinanceComputation
        {
            Months = new List<MonthFinanceVm>(ordered.Count)
        };

        for (int i = 0; i < perMonth.Length; i++)
        {
            var (vm, monthIncome, monthExpenses, sessions, avg, unpaid) = perMonth[i];
            var y = vm.Year;

            result.TotalIncome   += monthIncome;
            result.TotalExpenses += monthExpenses;
            result.TotalSessions += sessions;
            result.TotalUnpaid   += unpaid;
            if (sessions > 0)
            {
                result.WeightedAttSum   += avg * sessions;
                result.WeightedAttCount += sessions;
            }
            if (y == yearSnapshot)
            {
                result.YIncome   += monthIncome;
                result.YExpenses += monthExpenses;
                result.YSessions += sessions;
                result.YUnpaid   += unpaid;

                if (sessions > 0)
                {
                    result.YWeightedAttSum   += avg * sessions;
                    result.YWeightedAttCount += sessions;
                }
            }

            result.Months.Add(vm);
        }

        return result;
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
        var fencerList = activeFencers as IList<Fencer> ?? activeFencers.ToList();

        // Each fencer's credit chain is fully independent, so we can fan the
        // computation out across CPU cores. Each parallel body writes only to
        // its own partial dictionary; results are merged single-threaded after.
        var partials = new Dictionary<(string, int, int), decimal>[fencerList.Count];

        // Cap parallelism so the credit-carry pre-pass leaves a core free for the
        // UI thread (see note in ComputeFinance) — keeps the Android emulator
        // responsive while a refresh runs.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.For(0, fencerList.Count, parallelOptions, idx =>
        {
            var f = fencerList[idx];
            var local = new Dictionary<(string, int, int), decimal>(monthsAscending.Count);

            decimal credit = 0m;
            foreach (var ym in monthsAscending)
            {
                local[(f.Id, ym.Y, ym.M)] = credit;

                attendanceByMonth[ym].TryGetValue(f.Id, out var att);
                paidByMonth[ym].TryGetValue(f.Id, out var paid);

                // No activity AND no carried credit → nothing changes.
                if (att == 0 && paid == 0m && credit == 0m) continue;

                var quote = DuesCalculator.Calculate(att, f.IsStudent, rulesByMonth[ym], paid + credit);
                credit = quote.Overpayment;
            }

            partials[idx] = local;
        });

        var result = new Dictionary<(string Fid, int Y, int M), decimal>(
            fencerList.Count * Math.Max(1, monthsAscending.Count));
        foreach (var local in partials)
            foreach (var kv in local)
                result[kv.Key] = kv.Value;

        return result;
    }

    private static string BuildPricingSummary(IReadOnlyList<PriceRule> all)
    {
        if (all.Count == 0)
            return "Pricing (defaults): 1 session 3 500 · 4 sessions 9 000 · monthly pass 12 000";

        var activeToday = all
            .Where(r => r.IsActiveOn(DateTime.Today))
            .OrderBy(r => r.SessionCount == 0 ? int.MaxValue : r.SessionCount)
            .ToList();

        if (activeToday.Count == 0)
            return "No active price rules for today — add one on the Prices tab.";

        return "Pricing: " + string.Join(" · ", activeToday.Select(r => r.SessionCount switch
        {
            0 => Math.Max(1, r.MonthCount) == 1
                    ? $"monthly pass {r.FullPrice:N0}"
                    : $"{Math.Max(1, r.MonthCount)} months pass {r.FullPrice:N0}",
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

    [RelayCommand]
    public async Task AddIncomeAsync(MonthFinanceVm month)
    {
        if (month is null) return;
        if (string.IsNullOrWhiteSpace(month.NewIncomeDescription) && month.NewIncomeAmount <= 0)
            return;

        var date = new DateTime(month.Year, month.Month,
            Math.Min(DateTime.Today.Day, DateTime.DaysInMonth(month.Year, month.Month)));

        var i = new Income
        {
            Date = date,
            Category = month.NewIncomeCategory,
            Description = month.NewIncomeDescription,
            Amount = month.NewIncomeAmount
        };
        await _sheets.AddIncomeAsync(i);
        month.Incomes.Add(i);

        month.NewIncomeCategory = "";
        month.NewIncomeDescription = "";
        month.NewIncomeAmount = 0;
        month.IsAddingIncome = false;
        month.RaiseTotals();
    }

    /// <summary>
    /// Abandons any in-flight <see cref="LoadAsync"/>. Called when the page is
    /// disappearing so a manual refresh doesn't mutate the UI-bound collections
    /// after the CollectionView has been detached (crashes on Android when the
    /// RecyclerView has already been torn down).
    /// </summary>
    public void CancelLoad() => _loadCts?.Cancel();
}