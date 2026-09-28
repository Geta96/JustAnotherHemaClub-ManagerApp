namespace JustAnotherHemaClub.Models;

public class Income
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string Category { get; set; } = string.Empty;    // e.g. "Donation", "Equipment sale"
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}