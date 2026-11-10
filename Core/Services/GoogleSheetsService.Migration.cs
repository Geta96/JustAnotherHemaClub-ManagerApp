using Google.Apis.Sheets.v4;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService
{
    /// <summary>
    /// Runtime read. Uses UNFORMATTEDVALUE so a cell's stored value is returned
    /// verbatim: migrated rows come back as their ISO "o" text (the strict
    /// parser accepts them), and S() invariantly stringifies anything numeric.
    /// Does NOT request FORMATTEDSTRING — that would reshape any still-typed date
    /// into a locale display string and defeat the strict ISO reader.
    /// </summary>
    private async Task<IList<IList<object>>> ReadUnformattedAsync(string range)
    {
        var svc = await GetServiceAsync();
        var req = svc.Spreadsheets.Values.Get(_spreadsheetId, range);
        req.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var resp = await req.ExecuteAsync();
        return resp.Values ?? new List<IList<object>>();
    }
}