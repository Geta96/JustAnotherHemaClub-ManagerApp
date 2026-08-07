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

    /// <summary>
    /// How many calendar months this rule's price covers (only meaningful when
    /// SessionCount == 0). Default is 1 (standard single-month unlimited pass).
    /// Set to 2 for a two-month pass: DuesCalculator amortizes the price as
    /// FullPrice ÷ MonthCount per month, offering a built-in discount.
    /// </summary>
    public int MonthCount { get; set; } = 1;

    /// <summary>
    /// When true this is a one-off "custom period" unlimited pass: a fencer who
    /// attends at least once anywhere inside <see cref="StartDate"/>..<see cref="EndDate"/>
    /// owes the full unlimited price a single time for the whole window (charged
    /// on their first attended month), rather than being billed per calendar
    /// month. The period does NOT repeat — once <see cref="EndDate"/> passes,
    /// normal rules take over. <see cref="EndDate"/> is mandatory for these rules
    /// and <see cref="MonthCount"/> is ignored. Only meaningful when
    /// <see cref="SessionCount"/> == 0.
    /// </summary>
    public bool IsCustomPeriod { get; set; }

    public decimal FullPrice { get; set; }
    public decimal StudentPrice { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }

    public bool IsActiveOn(DateTime date) =>
        date.Date >= StartDate.Date &&
        (EndDate is null || date.Date <= EndDate.Value.Date);
}