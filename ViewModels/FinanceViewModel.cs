using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FinanceViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;

    public ObservableCollection<MonthFinanceVm> Months { get; } = new();

    public FinanceViewModel(IGoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    public async Task LoadAsync()
    {
        Months.Clear();

        var fencers = await _sheets.GetFencersAsync();

        var trainings = await _sheets.GetTrainingsAsync();

        // All months with sessions, plus the current month.
        var expensesAll = await _sheets.GetExpensesAsync(DateTime.MinValue.AddYears(1), DateTime.MaxValue.AddYears(-1));

        var today = DateTime.Today;
        var monthsSet = new HashSet<(int Y, int M)>
        {
            (today.Year, today.Month)
        };
        foreach (var s in trainings) monthsSet.Add((s.Date.Year, s.Date.Month));

        var ordered = monthsSet.OrderByDescending(t => t.Y).ThenByDescending(t => t.M);

        foreach (var (y, m) in ordered)
        {
            var monthVm = new MonthFinanceVm(y, m);

            // Attendance per fencer for the month
            var monthSessions = trainings.Where(s => s.Date.Year == y && s.Date.Month == m).ToList();
            var attendance = monthSessions
                .SelectMany(s => s.AttendeeFencerIds)
                .GroupBy(id => id)
                .ToDictionary(g => g.Key, g => g.Count());

            var payments = await _sheets.GetPaymentsAsync(y, m);

            foreach (var f in fencers.Where(f => f.Active))
            {
                attendance.TryGetValue(f.Id, out var count);
                var amount = DuesCalculator.Calculate(count, f.IsStudent);
                var paid = payments.Any(p => p.FencerId == f.Id);
                if (count == 0 && !paid) continue;

                monthVm.Dues.Add(new FencerDueRow(f, count, amount, paid));
            }

            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1).AddDays(-1);
            foreach (var e in expensesAll.Where(e => e.Date >= from && e.Date <= to))
                monthVm.Expenses.Add(e);

            monthVm.RaiseTotals();
            Months.Add(monthVm);
        }
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
        month.RaiseTotals();
    }
}