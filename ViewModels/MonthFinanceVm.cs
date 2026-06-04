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

    [ObservableProperty] private string newExpenseCategory = "";
    [ObservableProperty] private string newExpenseDescription = "";
    [ObservableProperty] private decimal newExpenseAmount;

    [ObservableProperty] private bool isExpanded;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    // Controls the inline "Add expense" form visibility.
    [ObservableProperty] private bool isAddingExpense;

    public MonthFinanceVm(int year, int month)
    {
        Year = year;
        Month = month;
        Dues.CollectionChanged += (_, __) => RaiseTotals();
        Expenses.CollectionChanged += (_, __) => RaiseTotals();
    }

    public decimal TotalDue => Dues.Sum(d => d.AmountDue);
    public decimal TotalPaid => Dues.Where(d => d.IsPaid).Sum(d => d.AmountDue);
    public decimal TotalExpenses => Expenses.Sum(e => e.Amount);
    public decimal Balance => TotalPaid - TotalExpenses;

    public string Summary =>
        $"Dues {TotalDue:N0} · Paid {TotalPaid:N0} · Expenses {TotalExpenses:N0} · Balance {Balance:N0}";

    public void RaiseTotals()
    {
        OnPropertyChanged(nameof(TotalDue));
        OnPropertyChanged(nameof(TotalPaid));
        OnPropertyChanged(nameof(TotalExpenses));
        OnPropertyChanged(nameof(Balance));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void BeginAddExpense() => IsAddingExpense = true;

    [RelayCommand]
    private void CancelAddExpense()
    {
        NewExpenseCategory = "";
        NewExpenseDescription = "";
        NewExpenseAmount = 0;
        IsAddingExpense = false;
    }
}