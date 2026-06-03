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

    private async Task UpdateAsync(string range, IList<object> row)
    {
        var svc = await GetServiceAsync();
        var body = new ValueRange { Values = new List<IList<object>> { row } };
        var req = svc.Spreadsheets.Values.Update(body, _spreadsheetId, range);
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    // --- Fencers ---
    // Columns: A=Id, B=Username, C=PasswordHash, D=Name, E=Email,
    //          F=Active, G=IsStudent, H=GdprAccepted, I=LiabilityAccepted, J=IsInstructor
    public async Task<List<Fencer>> GetFencersAsync()
    {
        var rows = await ReadAsync("Fencers!A2:J");
        return rows.Select(r => new Fencer
        {
            Id = S(r, 0),
            Username = S(r, 1),
            PasswordHash = S(r, 2),
            Name = S(r, 3),
            Email = S(r, 4),
            Active = ParseBool(S(r, 5)),
            IsStudent = ParseBool(S(r, 6)),
            GdprAccepted = ParseBool(S(r, 7)),
            LiabilityAccepted = ParseBool(S(r, 8)),
            IsInstructor = ParseBool(S(r, 9))
        }).ToList();
    }

    public Task AddFencerAsync(Fencer f) =>
        AppendAsync("Fencers!A:J", new List<object>
        {
            f.Id, f.Username ?? "", f.PasswordHash ?? "",
            f.Name, f.Email ?? "",
            f.Active, f.IsStudent, f.GdprAccepted, f.LiabilityAccepted, f.IsInstructor
        });

    public async Task UpsertFencerAsync(Fencer f)
    {
        var rows = await ReadAsync("Fencers!A2:J");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == f.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            f.Id, f.Username ?? "", f.PasswordHash ?? "",
            f.Name, f.Email ?? "",
            f.Active, f.IsStudent, f.GdprAccepted, f.LiabilityAccepted, f.IsInstructor
        };

        if (rowIndex >= 0)
            await UpdateAsync($"Fencers!A{rowIndex + 2}:J{rowIndex + 2}", values);
        else
            await AppendAsync("Fencers!A:J", values);
    }

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

    // --- Individual lessons ---
    // Columns: A=Id, B=Date, C=StudentId, D=InstructorId, E=Topic,
    //          F=Notes, G=NextIdea, H=Status, I=RequestedInstructorIds (CSV)
    public async Task<List<IndividualLesson>> GetIndividualLessonsAsync()
    {
        var rows = await ReadAsync("IndividualLessons!A2:I");
        var list = new List<IndividualLesson>();
        foreach (var r in rows)
        {
            var id = S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;

            var statusStr = S(r, 7);
            if (!Enum.TryParse<IndividualLessonStatus>(statusStr, true, out var status))
                status = IndividualLessonStatus.Accepted;

            // Rejected rows are treated as deleted.
            if (status == IndividualLessonStatus.Rejected) continue;

            list.Add(new IndividualLesson
            {
                Id = id,
                Date = DateTime.TryParse(S(r, 1), CultureInfo.InvariantCulture,
                                         DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue,
                StudentId = S(r, 2),
                InstructorId = S(r, 3),
                Topic = S(r, 4),
                Notes = S(r, 5),
                NextIdea = S(r, 6),
                Status = status,
                RequestedInstructorIds = S(r, 8)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .ToList()
            });
        }
        return list;
    }

    public async Task UpsertIndividualLessonAsync(IndividualLesson l)
    {
        var rows = await ReadAsync("IndividualLessons!A2:I");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == l.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            l.Id,
            l.Date.ToString("o", CultureInfo.InvariantCulture),
            l.StudentId,
            l.InstructorId ?? "",
            l.Topic ?? "",
            l.Notes ?? "",
            l.NextIdea ?? "",
            l.Status.ToString(),
            string.Join(",", l.RequestedInstructorIds ?? new())
        };

        if (rowIndex >= 0)
            await UpdateAsync($"IndividualLessons!A{rowIndex + 2}:I{rowIndex + 2}", values);
        else
            await AppendAsync("IndividualLessons!A:I", values);
    }

    private static string S(IList<object> row, int i) =>
        i < row.Count ? row[i]?.ToString() ?? "" : "";

    private static bool ParseBool(string s) =>
        s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);
}