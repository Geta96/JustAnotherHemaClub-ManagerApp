using System.Globalization;
using Google;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

// Split into its own partial so the main file stays focused.
public partial class GoogleSheetsService
{
    // --- Price rules ---
    // Columns: A=Id, B=SessionCount, C=FullPrice, D=StudentPrice, E=StartDate, F=EndDate, G=MonthCount
    public async Task<List<PriceRule>> GetPriceRulesAsync()
    {
        IList<IList<object>> rows;
        try
        {
            rows = await ReadAsync("Prices!A2:G");
        }
        catch (GoogleApiException)
        {
            return new List<PriceRule>();
        }

        var list = new List<PriceRule>();
        foreach (var r in rows)
        {
            var id = PriceS(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!int.TryParse(PriceS(r, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) continue;
            if (!decimal.TryParse(PriceS(r, 2), NumberStyles.Number, CultureInfo.InvariantCulture, out var full)) continue;
            if (!decimal.TryParse(PriceS(r, 3), NumberStyles.Number, CultureInfo.InvariantCulture, out var student))
                student = full;
            if (!DateTime.TryParse(PriceS(r, 4), CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out var start))
                start = DateTime.Today;

            DateTime? end = null;
            var endStr = PriceS(r, 5);
            if (!string.IsNullOrWhiteSpace(endStr) &&
                DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out var e))
                end = e;

            // Column G: MonthCount — defaults to 1 for rows written before this column existed.
            int monthCount = 1;
            var monthStr = PriceS(r, 6);
            if (!string.IsNullOrWhiteSpace(monthStr) &&
                int.TryParse(monthStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mc) && mc > 0)
                monthCount = mc;

            list.Add(new PriceRule
            {
                Id           = id,
                SessionCount = count,
                MonthCount   = monthCount,
                FullPrice    = full,
                StudentPrice = student,
                StartDate    = start,
                EndDate      = end
            });
        }
        return list;
    }

    public async Task UpsertPriceRuleAsync(PriceRule rule)
    {
        var rows = await ReadAsync("Prices!A2:G");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (PriceS(rows[i], 0) == rule.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            rule.Id,
            rule.SessionCount,
            rule.FullPrice.ToString(CultureInfo.InvariantCulture),
            rule.StudentPrice.ToString(CultureInfo.InvariantCulture),
            rule.StartDate.ToString("o", CultureInfo.InvariantCulture),
            rule.EndDate?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            rule.MonthCount
        };

        if (rowIndex >= 0)
            await UpdateAsync($"Prices!A{rowIndex + 2}:G{rowIndex + 2}", values);
        else
            await AppendAsync("Prices!A:G", values);
    }

    public async Task DeletePriceRuleAsync(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return;

        var rows = await ReadAsync("Prices!A2:G");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (PriceS(rows[i], 0) == ruleId) { rowIndex = i; break; }
        if (rowIndex < 0) return;

        var blanks = new List<object> { "", "", "", "", "", "", "" };
        await UpdateAsync($"Prices!A{rowIndex + 2}:G{rowIndex + 2}", blanks);
    }

    private static string PriceS(IList<object> row, int i) =>
        i < row.Count ? row[i]?.ToString() ?? "" : "";
}