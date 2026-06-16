namespace JustAnotherHemaClub.Models;

public class PriceRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// How many training sessions this rule applies to.
    ///   0 = unlimited monthly pass (always applicable when attendance ≥ 1).
    ///   1 = single-session ticket (cost = price × attendance).
    ///   N > 1 = N-session pack (applicable iff attendance ≤ N; cost = flat price).
    /// </summary>
    public int SessionCount { get; set; }

    public decimal FullPrice { get; set; }
    public decimal StudentPrice { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }

    public bool IsActiveOn(DateTime date) =>
        date.Date >= StartDate.Date &&
        (EndDate is null || date.Date <= EndDate.Value.Date);
}