using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class MonthFinanceVm : ObservableObject
{
    public int Year { get; }
    public int Month { get; }
    public string Title => new DateTime(Year, Month, 1).ToString("yyyy MMMM");

    public ObservableCollection<FencerDueRow> Dues { get; } = new();
    public ObservableCollection<Expense> Expenses { get; } = new();
    public ObservableCollection<Income> Incomes { get; } = new();

    [ObservableProperty] private string newExpenseCategory = "";
    [ObservableProperty] private string newExpenseDescription = "";
    [ObservableProperty] private decimal newExpenseAmount;

    [ObservableProperty] private string newIncomeCategory = "";
    [ObservableProperty] private string newIncomeDescription = "";
    [ObservableProperty] private decimal newIncomeAmount;

    [ObservableProperty] private bool isExpanded;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    [ObservableProperty] private bool isAddingExpense;
    [ObservableProperty] private bool isAddingIncome;

    public MonthFinanceVm(int year, int month)
    {
        Year = year;
        Month = month;
        Dues.CollectionChanged += (_, __) => RaiseTotals();
        Expenses.CollectionChanged += (_, __) => RaiseTotals();
        Incomes.CollectionChanged += (_, __) => RaiseTotals();
    }

    // Expected revenue from members for this month (sum of cheapest tier per fencer).
    public decimal TotalDue       => Dues.Sum(d => d.TotalCost);

    // Actual income already collected this month (sum of recorded payments).
    public decimal TotalPaid      => Dues.Sum(d => d.AlreadyPaid);

    // Still to collect from members this month.
    public decimal Outstanding    => Dues.Sum(d => d.AmountDue);

    public decimal TotalExpenses  => Expenses.Sum(e => e.Amount);

    /// <summary>One-off, non-dues income (donations, gear sales, fencers paying for extras).</summary>
    public decimal TotalIncomes   => Incomes.Sum(i => i.Amount);

    // Cash on hand for the month: dues collected + one-offs - expenses.
    public decimal Balance        => TotalPaid + TotalIncomes - TotalExpenses;

    public string Summary =>
        $"Dues {TotalDue:N0} · Paid {TotalPaid:N0} · Income {TotalIncomes:N0} · Expenses {TotalExpenses:N0} · Balance {Balance:N0}";

    public void RaiseTotals()
    {
        OnPropertyChanged(nameof(TotalDue));
        OnPropertyChanged(nameof(TotalPaid));
        OnPropertyChanged(nameof(Outstanding));
        OnPropertyChanged(nameof(TotalExpenses));
        OnPropertyChanged(nameof(TotalIncomes));
        OnPropertyChanged(nameof(Balance));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand] private void BeginAddExpense() => IsAddingExpense = true;

    [RelayCommand]
    private void CancelAddExpense()
    {
        NewExpenseCategory = "";
        NewExpenseDescription = "";
        NewExpenseAmount = 0;
        IsAddingExpense = false;
    }

    [RelayCommand] private void BeginAddIncome() => IsAddingIncome = true;

    [RelayCommand]
    private void CancelAddIncome()
    {
        NewIncomeCategory = "";
        NewIncomeDescription = "";
        NewIncomeAmount = 0;
        IsAddingIncome = false;
    }
}