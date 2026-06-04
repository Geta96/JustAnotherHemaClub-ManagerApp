using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FinanceViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<MonthFinanceVm> Months { get; } = new();

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;

    public bool ShowPersonalSummary => !_auth.IsLoggedInInstructor && _auth.CurrentFencer is not null;

    [ObservableProperty] private decimal personalTotalDue;
    [ObservableProperty] private bool personalAllPaid;
    [ObservableProperty] private string personalSummary = "";
    [ObservableProperty] private bool isLoading;

    public FinanceViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            // Independent reads in parallel.
            var fencersTask  = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var expensesTask = _sheets.GetExpensesAsync(
                DateTime.MinValue.AddYears(1), DateTime.MaxValue.AddYears(-1));
            await Task.WhenAll(fencersTask, trainingsTask, expensesTask);

            var fencers     = fencersTask.Result;
            var trainings   = trainingsTask.Result;
            var expensesAll = expensesTask.Result;

            var today = DateTime.Today;
            var monthsSet = new HashSet<(int Y, int M)> { (today.Year, today.Month) };
            foreach (var s in trainings) monthsSet.Add((s.Date.Year, s.Date.Month));

            var ordered = monthsSet
                .OrderByDescending(t => t.Y).ThenByDescending(t => t.M)
                .ToList();

            // Fetch every month's payments in parallel.
            var paymentTasks = ordered
                .Select(t => _sheets.GetPaymentsAsync(t.Y, t.M))
                .ToArray();
            await Task.WhenAll(paymentTasks);

            var isInstructor    = _auth.IsLoggedInInstructor;
            var currentFencerId = _auth.CurrentFencer?.Id;

            // Build all month VMs locally first, then publish in one swap.
            var built = new List<MonthFinanceVm>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                var (y, m) = ordered[i];
                var monthVm = new MonthFinanceVm(y, m);

                var monthSessions = trainings.Where(s => s.Date.Year == y && s.Date.Month == m);
                var attendance = monthSessions
                    .SelectMany(s => s.AttendeeFencerIds)
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count());

                var payments = paymentTasks[i].Result;

                var fencersForMonth = isInstructor
                    ? fencers.Where(f => f.Active)
                    : fencers.Where(f => f.Active && f.Id == currentFencerId);

                foreach (var f in fencersForMonth)
                {
                    attendance.TryGetValue(f.Id, out var count);
                    var amount = DuesCalculator.Calculate(count, f.IsStudent);
                    var paid = payments.Any(p => p.FencerId == f.Id);
                    if (count == 0 && !paid) continue;

                    monthVm.Dues.Add(new FencerDueRow(f, count, amount, paid));
                }

                if (isInstructor)
                {
                    var from = new DateTime(y, m, 1);
                    var to = from.AddMonths(1).AddDays(-1);
                    foreach (var e in expensesAll.Where(e => e.Date >= from && e.Date <= to))
                        monthVm.Expenses.Add(e);
                }

                monthVm.RaiseTotals();
                built.Add(monthVm);
            }

            // Single visual update at the end instead of N incremental Adds.
            Months.Clear();
            foreach (var mv in built) Months.Add(mv);

            RecomputePersonalSummary();
            OnPropertyChanged(nameof(ShowPersonalSummary));
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