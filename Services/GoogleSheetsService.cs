using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class GoogleSheetsService : IGoogleSheetsService
{
    private readonly string _spreadsheetId;
    private SheetsService? _service;

    public GoogleSheetsService(string spreadsheetId)
    {
        _spreadsheetId = spreadsheetId;
    }

    private async Task<SheetsService> GetServiceAsync()
    {
        if (_service is not null) return _service;

        using var stream = await FileSystem.OpenAppPackageFileAsync("service-account.json");
        var credential = GoogleCredential.FromStream(stream)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "JAHC Manager"
        });
        return _service;
    }

    private async Task<IList<IList<object>>> ReadAsync(string range)
    {
        var svc = await GetServiceAsync();
        var resp = await svc.Spreadsheets.Values.Get(_spreadsheetId, range).ExecuteAsync();
        return resp.Values ?? new List<IList<object>>();
    }

    private async Task AppendAsync(string range, IList<object> row)
    {
        var svc = await GetServiceAsync();
        var body = new ValueRange { Values = new List<IList<object>> { row } };
        var req = svc.Spreadsheets.Values.Append(body, _spreadsheetId, range);
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    // --- Fencers ---
    public async Task<List<Fencer>> GetFencersAsync()
    {
        var rows = await ReadAsync("Fencers!A2:H");
        return rows.Select(r => new Fencer
        {
            Id = S(r, 0),
            Name = S(r, 1),
            Nickname = S(r, 2),
            Email = S(r, 3),
            Active = bool.TryParse(S(r, 4), out var a) && a,
            IsStudent = bool.TryParse(S(r, 5), out var st) && st,
            GdprAccepted = bool.TryParse(S(r, 6), out var g) && g,
            LiabilityAccepted = bool.TryParse(S(r, 7), out var l) && l
        }).ToList();
    }

    public Task AddFencerAsync(Fencer f) =>
        AppendAsync("Fencers!A:H", new List<object>
        {
            f.Id, f.Name, f.Nickname ?? "", f.Email ?? "",
            f.Active, f.IsStudent, f.GdprAccepted, f.LiabilityAccepted
        });

    // --- Trainings ---
    public async Task<List<TrainingSession>> GetTrainingsAsync()
    {
        var rows = await ReadAsync("Trainings!A2:D");
        return rows.Select(r => new TrainingSession
        {
            Id = S(r, 0),
            Date = DateTime.Parse(S(r, 1), CultureInfo.InvariantCulture),
            Topic = S(r, 2),
            AttendeeFencerIds = S(r, 3).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
        }).ToList();
    }

    public Task UpsertTrainingAsync(TrainingSession t) =>
        AppendAsync("Trainings!A:D", new List<object>
        {
            t.Id,
            t.Date.ToString("o", CultureInfo.InvariantCulture),
            t.Topic,
            string.Join(",", t.AttendeeFencerIds)
        });

    // --- Payments ---
    public async Task<List<Payment>> GetPaymentsAsync(int year, int month)
    {
        var rows = await ReadAsync("Payments!A2:E");
        return rows.Select(r => new Payment
        {
            FencerId = S(r, 0),
            Year = int.Parse(S(r, 1)),
            Month = int.Parse(S(r, 2)),
            Amount = decimal.Parse(S(r, 3), CultureInfo.InvariantCulture),
            PaidOn = DateTime.Parse(S(r, 4), CultureInfo.InvariantCulture)
        }).Where(p => p.Year == year && p.Month == month).ToList();
    }

    public Task MarkPaidAsync(Payment p) =>
        AppendAsync("Payments!A:E", new List<object>
        {
            p.FencerId, p.Year, p.Month,
            p.Amount.ToString(CultureInfo.InvariantCulture),
            p.PaidOn.ToString("o", CultureInfo.InvariantCulture)
        });

    // --- Expenses ---
    public async Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to)
    {
        var rows = await ReadAsync("Expenses!A2:E");
        return rows.Select(r => new Expense
        {
            Id = S(r, 0),
            Date = DateTime.Parse(S(r, 1), CultureInfo.InvariantCulture),
            Category = S(r, 2),
            Description = S(r, 3),
            Amount = decimal.Parse(S(r, 4), CultureInfo.InvariantCulture)
        }).Where(e => e.Date >= from && e.Date <= to).ToList();
    }

    public Task AddExpenseAsync(Expense e) =>
        AppendAsync("Expenses!A:E", new List<object>
        {
            e.Id,
            e.Date.ToString("o", CultureInfo.InvariantCulture),
            e.Category, e.Description,
            e.Amount.ToString(CultureInfo.InvariantCulture)
        });

    // --- Instructors ---
    public async Task<List<Instructor>> GetInstructorsAsync()
    {
        var rows = await ReadAsync("Instructors!A2:C");
        return rows.Select(r => new Instructor
        {
            Username = S(r, 0),
            PasswordHash = S(r, 1),
            DisplayName = S(r, 2)
        }).ToList();
    }

    // --- Month notes ---
    public async Task<List<MonthNote>> GetMonthNotesAsync()
    {
        var rows = await ReadAsync("MonthNotes!A2:C");
        return rows.Select(r => new MonthNote
        {
            Year = int.TryParse(S(r, 0), out var y) ? y : 0,
            Month = int.TryParse(S(r, 1), out var m) ? m : 0,
            Note = S(r, 2)
        }).ToList();
    }

    // Append-only; the latest row wins when read via GetMonthNotesAsync filtering by (Year, Month).
    public Task UpsertMonthNoteAsync(MonthNote note) =>
        AppendAsync("MonthNotes!A:C", new List<object> { note.Year, note.Month, note.Note });

    private static string S(IList<object> row, int i) =>
        i < row.Count ? row[i]?.ToString() ?? "" : "";
}