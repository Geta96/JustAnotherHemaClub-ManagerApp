namespace JustAnotherHemaClub.ViewModels;

public class MonthStatRow
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string Title => new DateTime(Year, Month, 1).ToString("yyyy MMMM");

    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Balance => Income - Expenses;

    public int Sessions { get; init; }
    public double AvgAttendance { get; init; }
}