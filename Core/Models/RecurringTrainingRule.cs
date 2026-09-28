namespace JustAnotherHemaClub.Models;

public class RecurringTrainingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public TimeSpan EndTimeOfDay { get; set; }

    public string Topic { get; set; } = string.Empty;

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }

    public string CreatedByFencerId { get; set; } = string.Empty;

    public bool IsActiveOn(DateTime date) =>
        date.Date >= StartDate.Date &&
        (EndDate is null || date.Date <= EndDate.Value.Date) &&
        date.DayOfWeek == DayOfWeek;
}