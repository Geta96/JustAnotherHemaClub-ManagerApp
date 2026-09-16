using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService
{
    /// <summary>
    /// Describes one sheet's date-bearing columns for the one-time migration:
    /// the read range plus each 0-based column index that holds a date and its
    /// A1 column letter (used to build the targeted write range).
    /// </summary>
    private sealed record DateColumnSpec(string Sheet, string ReadRange, (int Index, char Letter)[] DateColumns);

    /// <summary>
    /// Every sheet + column that stores a date. Times (RecurringTrainings C/H)
    /// are intentionally excluded — they need a different recovery path.
    /// Keep these in sync with the column comments in GoogleSheetsService.cs.
    /// </summary>
    private static readonly DateColumnSpec[] _dateColumnSpecs =
    {
        new("Trainings",          "Trainings!A2:E",          new[] { (1, 'B'), (4, 'E') }),
        new("IndividualLessons",  "IndividualLessons!A2:I",  new[] { (1, 'B') }),
        new("Payments",           "Payments!A2:E",           new[] { (4, 'E') }),
        new("Expenses",           "Expenses!A2:E",           new[] { (1, 'B') }),
        new("Incomes",            "Incomes!A2:E",            new[] { (1, 'B') }),
        new("Prices",             "Prices!A2:H",             new[] { (4, 'E'), (5, 'F') }),
        new("RecurringTrainings", "RecurringTrainings!A2:H", new[] { (4, 'E'), (5, 'F') }),
        new("Tournaments",        "Tournaments!A2:F",        new[] { (3, 'D') }),
        new("Matches",            "Matches!A2:Y",            new[] { (18, 'S'), (19, 'T'), (21, 'V'), (24, 'Y') }),
    };

    /// <summary>
    /// Reads a range with UNFORMATTED_VALUE so date cells corrupted by the old
    /// USER_ENTERED writer come back as Sheets serial numbers (doubles) instead
    /// of locale-formatted text — the only way to recover the true instant.
    /// </summary>
    private async Task<IList<IList<object>>> ReadUnformattedAsync(string range)
    {
        var svc = await GetServiceAsync();
        var req = svc.Spreadsheets.Values.Get(_spreadsheetId, range);
        req.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        // Ask for serials, not the sheet's display format.
        req.DateTimeRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.SERIALNUMBER;
        var resp = await req.ExecuteAsync();
        return resp.Values ?? new List<IList<object>>();
    }

    /// <summary>
    /// Recovers a date cell that may be either:
    ///   • a proper ISO "o" string (already-good / newly RAW-written rows), or
    ///   • a Sheets serial number (double) from the old corrupt writer.
    /// </summary>
    private static bool TryRecoverDate(object? cell, out DateTime value)
    {
        value = default;
        if (cell is null) return false;

        var s = cell.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Good rows: already ISO round-trippable.
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                              DateTimeStyles.RoundtripKind, out value))
            return true;

        // Corrupt rows: bare serial number → OLE Automation date.
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial))
        {
            try { value = DateTime.FromOADate(serial); return true; }
            catch { return false; }
        }

        return false;
    }

    /// <summary>
    /// ONE-TIME: rewrites every date column across all sheets as canonical ISO
    /// "o" text. Idempotent — already-good ISO rows are rewritten identically,
    /// blank cells are left blank. Returns a per-sheet count of cells rewritten
    /// so the caller can surface a summary. Remove after running in production.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> MigrateAllDatesToIsoAsync()
    {
        var svc = await GetServiceAsync();
        var summary = new Dictionary<string, int>();

        foreach (var spec in _dateColumnSpecs)
        {
            int fixedCells = 0;
            IList<IList<object>> rows;
            try
            {
                rows = await ReadUnformattedAsync(spec.ReadRange);
            }
            catch (Google.GoogleApiException)
            {
                // Optional sheet (e.g. Prices / Incomes) not present yet — skip.
                summary[spec.Sheet] = 0;
                continue;
            }

            var updates = new List<ValueRange>();

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                // Skip fully blank / key-less rows (column A is always the Id/key).
                if (r.Count == 0 || string.IsNullOrWhiteSpace(S(r, 0))) continue;

                int sheetRow = i + 2;

                foreach (var (index, letter) in spec.DateColumns)
                {
                    object? cell = r.Count > index ? r[index] : null;

                    // Leave genuinely empty optional dates (e.g. open-ended EndDate) blank.
                    if (cell is null || string.IsNullOrWhiteSpace(cell.ToString())) continue;

                    if (!TryRecoverDate(cell, out var date)) continue; // unrecoverable → leave untouched

                    updates.Add(new ValueRange
                    {
                        Range = $"{spec.Sheet}!{letter}{sheetRow}",
                        Values = new List<IList<object>>
                        {
                            new List<object> { date.ToString("o", CultureInfo.InvariantCulture) }
                        }
                    });
                    fixedCells++;
                }
            }

            if (updates.Count > 0)
            {
                var batch = new BatchUpdateValuesRequest
                {
                    // RAW so the ISO strings are stored verbatim, not re-parsed.
                    ValueInputOption = "RAW",
                    Data = updates
                };
                await svc.Spreadsheets.Values.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
            }

            summary[spec.Sheet] = fixedCells;
        }

        return summary;
    }
}