using System.Globalization;
using Google;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService
{
    // --- News ---
    // Columns: A=Id, B=Title, C=Body, D=CreatedByFencerId, E=CreatedAtUtc
    private const string NewsRange = "News!A2:E";

    public async Task<List<NewsItem>> GetNewsAsync()
    {
        IList<IList<object>> rows;
        try
        {
            rows = await ReadUnformattedAsync(NewsRange);
        }
        catch (GoogleApiException)
        {
            return new List<NewsItem>();
        }

        var list = new List<NewsItem>();
        foreach (var r in rows)
        {
            var id = SheetRowMapper.S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;

            list.Add(new NewsItem
            {
                Id = id,
                Title = SheetRowMapper.S(r, 1),
                Body = SheetRowMapper.S(r, 2),
                CreatedByFencerId = SheetRowMapper.S(r, 3),
                CreatedAtUtc = SheetRowMapper.TryParseDate(SheetRowMapper.S(r, 4), out var created)
                    ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
                    : DateTime.UtcNow
            });
        }
        return list;
    }

    public Task AddNewsAsync(NewsItem news) =>
        AppendAsync("News!A1", new List<object>
        {
            news.Id,
            news.Title ?? "",
            news.Body ?? "",
            news.CreatedByFencerId ?? "",
            news.CreatedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
        });

    public async Task UpsertNewsAsync(NewsItem news)
    {
        IList<IList<object>> rows;
        try { rows = await ReadAsync(NewsRange); }
        catch (GoogleApiException) { rows = new List<IList<object>>(); }

        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (SheetRowMapper.S(rows[i], 0) == news.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            news.Id,
            news.Title ?? "",
            news.Body ?? "",
            news.CreatedByFencerId ?? "",
            news.CreatedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
        };

        if (rowIndex >= 0)
            await UpdateAsync($"News!A{rowIndex + 2}:E{rowIndex + 2}", values);
        else
            await AppendAsync("News!A1", values);
    }

    public async Task DeleteNewsAsync(string newsId)
    {
        if (string.IsNullOrWhiteSpace(newsId)) return;

        IList<IList<object>> rows;
        try { rows = await ReadAsync(NewsRange); }
        catch (GoogleApiException) { return; }

        var rangesToClear = new List<string>();
        for (int i = 0; i < rows.Count; i++)
            if (SheetRowMapper.S(rows[i], 0) == newsId)
                rangesToClear.Add($"News!A{i + 2}:E{i + 2}");
        if (rangesToClear.Count == 0) return;

        var svc = await GetServiceAsync();
        var batch = new Google.Apis.Sheets.v4.Data.BatchClearValuesRequest { Ranges = rangesToClear };
        await svc.Spreadsheets.Values.BatchClear(batch, _spreadsheetId).ExecuteAsync();
    }
}
