namespace JustAnotherHemaClub.Models;

public class Payment
{
    public string FencerId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidOn { get; set; }
}