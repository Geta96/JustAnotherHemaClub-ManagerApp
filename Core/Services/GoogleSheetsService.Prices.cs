using System.Globalization;
using Google;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

// Split into its own partial so the main file stays focused.
public partial class GoogleSheetsService
{
    // --- Price rules ---
    // Columns: A=Id, B=SessionCount, C=FullPrice, D=StudentPrice, E=StartDate, F=EndDate, G=MonthCount, H=IsCustomPeriod
    public async Task<List<PriceRule>> GetPriceRulesAsync()
    {
        IList<IList<object>> rows;
        try
        {
            // UNFORMATTED + SheetRowMapper.TryParseDate so serial-stored dates can't be
            // re-read as locale-formatted text and flipped (see GetTrainingsAsync).
            rows = await ReadUnformattedAsync("Prices!A2:H");
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
            if (!SheetRowMapper.TryParseDate(PriceS(r, 4), out var start))
                start = DateTime.Today;
            start = start.Date;

            DateTime? end = null;
            var endStr = PriceS(r, 5);
            if (!string.IsNullOrWhiteSpace(endStr) &&
                SheetRowMapper.TryParseDate(endStr, out var e))
                end = e.Date;

            // Column G: MonthCount � defaults to 1 for rows written before this column existed.
            int monthCount = 1;
            var monthStr = PriceS(r, 6);
            if (!string.IsNullOrWhiteSpace(monthStr) &&
                int.TryParse(monthStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mc) && mc > 0)
                monthCount = mc;

            // Column H: IsCustomPeriod � defaults to false for rows written before this column existed.
            bool isCustomPeriod = ParseBool(PriceS(r, 7));

            list.Add(new PriceRule
            {
                Id             = id,
                SessionCount   = count,
                MonthCount     = monthCount,
                IsCustomPeriod = isCustomPeriod,
                FullPrice      = full,
                StudentPrice   = student,
                StartDate      = start,
                EndDate        = end
            });
        }
        return list;
    }

    public async Task UpsertPriceRuleAsync(PriceRule rule)
    {
        var rows = await ReadAsync("Prices!A2:H");
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
            rule.MonthCount,
            B(rule.IsCustomPeriod)
        };

        if (rowIndex >= 0)
            await UpdateAsync($"Prices!A{rowIndex + 2}:H{rowIndex + 2}", values);
        else
            await AppendAsync("Prices!A:H", values);
    }

    public async Task DeletePriceRuleAsync(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return;

        var rows = await ReadAsync("Prices!A2:H");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (PriceS(rows[i], 0) == ruleId) { rowIndex = i; break; }
        if (rowIndex < 0) return;

        var blanks = new List<object> { "", "", "", "", "", "", "", "" };
        await UpdateAsync($"Prices!A{rowIndex + 2}:H{rowIndex + 2}", blanks);
    }

    private static string PriceS(IList<object> row, int i) => SheetRowMapper.S(row, i);
}