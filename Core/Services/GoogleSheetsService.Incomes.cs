using System.Globalization;
using Google;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

// Split into its own partial so the main file stays focused.
// Mirrors the Expenses I/O — but tolerates a missing sheet (same pattern as Prices)
// so an existing spreadsheet without an "Incomes" tab keeps working until the
// instructor records the first one-off income.
public partial class GoogleSheetsService
{
    // --- Incomes ---
    // Columns: A=Id, B=Date, C=Category, D=Description, E=Amount
    public async Task<List<Income>> GetIncomesAsync(DateTime from, DateTime to)
    {
        IList<IList<object>> rows;
        try
        {
            // UNFORMATTED + SheetRowMapper.TryParseDate so serial-stored dates can't be
            // re-read as locale-formatted text and flipped (see GetTrainingsAsync).
            rows = await ReadUnformattedAsync("Incomes!A2:E");
        }
        catch (GoogleApiException)
        {
            // Sheet hasn't been created yet; treat as "no one-off incomes recorded"
            // instead of erroring out of the whole Finance page load.
            return new List<Income>();
        }

        var list = new List<Income>();
        foreach (var r in rows)
        {
            var id = IncomeS(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!SheetRowMapper.TryParseDate(IncomeS(r, 1), out var date)) continue;
            if (!decimal.TryParse(IncomeS(r, 4), NumberStyles.Number,
                                  CultureInfo.InvariantCulture, out var amount)) continue;

            list.Add(new Income
            {
                Id = id,
                Date = date,
                Category = IncomeS(r, 2),
                Description = IncomeS(r, 3),
                Amount = amount
            });
        }
        return list.Where(i => i.Date >= from && i.Date <= to).ToList();
    }

    public Task AddIncomeAsync(Income i) =>
        AppendAsync("Incomes!A1", new List<object>
        {
            i.Id,
            i.Date.ToString("o", CultureInfo.InvariantCulture),
            i.Category, i.Description,
            i.Amount.ToString(CultureInfo.InvariantCulture)
        });

    // Local cell-string helper (mirrors the private S() in the main file).
    // Newtonsoft (inside Google.Apis) auto-parses date cells into a boxed
    // DateTime/DateTimeOffset; a plain ToString() renders locale "MM/dd/yyyy…"
    // which the strict ISO reader rejects — silently dropping the income row.
    // Round-trip DateTime back to ISO "o" so TryParseDate accepts it.
    private static string IncomeS(IList<object> row, int i) => SheetRowMapper.S(row, i);
}