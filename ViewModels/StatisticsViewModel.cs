using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;

    [ObservableProperty] private int year = DateTime.Today.Year;

    // All-time
    [ObservableProperty] private decimal allTimeIncome;
    [ObservableProperty] private decimal allTimeExpenses;
    [ObservableProperty] private decimal allTimeBalance;
    [ObservableProperty] private int allTimeSessions;
    [ObservableProperty] private int activeFencers;
    [ObservableProperty] private double allTimeAvgAttendance;

    // Current year
    [ObservableProperty] private decimal yearIncome;
    [ObservableProperty] private decimal yearExpenses;
    [ObservableProperty] private decimal yearBalance;
    [ObservableProperty] private int yearSessions;
    [ObservableProperty] private double yearAvgAttendance;

    public ObservableCollection<MonthStatRow> Months { get; } = new();

    [ObservableProperty] private bool isLoading;

    public StatisticsViewModel(IGoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Months.Clear();

            var fencers = await _sheets.GetFencersAsync();
            var trainings = await _sheets.GetTrainingsAsync();
            var expenses = await _sheets.GetExpensesAsync(DateTime.MinValue.AddYears(1), DateTime.MaxValue.AddYears(-1));

            // Distinct months from data
            var months = trainings.Select(s => (s.Date.Year, s.Date.Month))
                .Concat(expenses.Select(e => (e.Date.Year, e.Date.Month)))
                .Distinct()
                .OrderByDescending(t => t.Year).ThenByDescending(t => t.Month)
                .ToList();

            decimal totalIncome = 0, totalExpenses = 0;
            int totalSessions = 0;
            double weightedAttendanceSum = 0;
            int weightedAttendanceCount = 0;

            decimal yIncome = 0, yExpenses = 0;
            int ySessions = 0;
            double yWeightedAttSum = 0;
            int yWeightedAttCount = 0;

            foreach (var (y, m) in months)
            {
                var payments = await _sheets.GetPaymentsAsync(y, m);
                var monthIncome = payments.Sum(p => p.Amount);

                var from = new DateTime(y, m, 1);
                var to = from.AddMonths(1).AddDays(-1);
                var monthExpenses = expenses.Where(e => e.Date >= from && e.Date <= to).Sum(e => e.Amount);

                var monthTrainings = trainings.Where(s => s.Date.Year == y && s.Date.Month == m).ToList();
                var avg = monthTrainings.Count == 0
                    ? 0
                    : monthTrainings.Average(s => s.AttendeeFencerIds.Count);

                Months.Add(new MonthStatRow
                {
                    Year = y,
                    Month = m,
                    Income = monthIncome,
                    Expenses = monthExpenses,
                    Sessions = monthTrainings.Count,
                    AvgAttendance = avg
                });

                totalIncome += monthIncome;
                totalExpenses += monthExpenses;
                totalSessions += monthTrainings.Count;
                if (monthTrainings.Count > 0)
                {
                    weightedAttendanceSum += avg * monthTrainings.Count;
                    weightedAttendanceCount += monthTrainings.Count;
                }

                if (y == Year)
                {
                    yIncome += monthIncome;
                    yExpenses += monthExpenses;
                    ySessions += monthTrainings.Count;
                    if (monthTrainings.Count > 0)
                    {
                        yWeightedAttSum += avg * monthTrainings.Count;
                        yWeightedAttCount += monthTrainings.Count;
                    }
                }
            }

            AllTimeIncome = totalIncome;
            AllTimeExpenses = totalExpenses;
            AllTimeBalance = totalIncome - totalExpenses;
            AllTimeSessions = totalSessions;
            ActiveFencers = fencers.Count(f => f.Active);
            AllTimeAvgAttendance = weightedAttendanceCount == 0 ? 0 : weightedAttendanceSum / weightedAttendanceCount;

            YearIncome = yIncome;
            YearExpenses = yExpenses;
            YearBalance = yIncome - yExpenses;
            YearSessions = ySessions;
            YearAvgAttendance = yWeightedAttCount == 0 ? 0 : yWeightedAttSum / yWeightedAttCount;
        }
        finally { IsLoading = false; }
    }
}