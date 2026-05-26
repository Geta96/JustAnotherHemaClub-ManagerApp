namespace JustAnotherHemaClub.Models;

public class Expense
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string Category { get; set; } = string.Empty; // e.g. "Hall fee"
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}