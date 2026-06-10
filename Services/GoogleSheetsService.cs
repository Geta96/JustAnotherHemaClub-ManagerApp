using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService : IGoogleSheetsService
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
        // Fail faster than the default 100 s, so users get a clearer error sooner.
        _service.HttpClient.Timeout = TimeSpan.FromSeconds(20);
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
    // Columns: A=Id, B=Date, C=Topic, D=AttendeeFencerIds, E=EndDate
    public async Task<List<TrainingSession>> GetTrainingsAsync()
    {
        var rows = await ReadAsync("Trainings!A2:E");
        var list = new List<TrainingSession>();
        foreach (var r in rows)
        {
            var id = S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;

            var date = DateTime.Parse(S(r, 1), CultureInfo.InvariantCulture);

            DateTime end;
            var endStr = S(r, 4);
            if (!string.IsNullOrWhiteSpace(endStr) &&
                DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out var parsedEnd))
                end = parsedEnd;
            else
                end = date.AddMinutes(90); // legacy rows default to a 90-minute session

            list.Add(new TrainingSession
            {
                Id = id,
                Date = date,
                EndDate = end,
                Topic = S(r, 2),
                AttendeeFencerIds = S(r, 3).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            });
        }
        return list;
    }

    public Task UpsertTrainingAsync(TrainingSession t) =>
        AppendAsync("Trainings!A:E", new List<object>
        {
            t.Id,
            t.Date.ToString("o", CultureInfo.InvariantCulture),
            t.Topic,
            string.Join(",", t.AttendeeFencerIds),
            t.EndDate.ToString("o", CultureInfo.InvariantCulture)
        });

    public async Task DeleteTrainingAsync(string trainingId)
    {
        if (string.IsNullOrWhiteSpace(trainingId)) return;

        var rows = await ReadAsync("Trainings!A2:E");
        var svc = await GetServiceAsync();

        for (int i = 0; i < rows.Count; i++)
        {
            if (S(rows[i], 0) != trainingId) continue;

            var range = $"Trainings!A{i + 2}:E{i + 2}";
            await svc.Spreadsheets.Values.Clear(new Google.Apis.Sheets.v4.Data.ClearValuesRequest(),
                                                _spreadsheetId, range).ExecuteAsync();
        }
    }

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

    // --- Recurring trainings ---
    // Columns: A=Id, B=DayOfWeek, C=TimeOfDay, D=Topic,
    //          E=StartDate, F=EndDate, G=CreatedByFencerId, H=EndTimeOfDay
    public async Task<List<RecurringTrainingRule>> GetRecurringTrainingsAsync()
    {
        var rows = await ReadAsync("RecurringTrainings!A2:H");
        var list = new List<RecurringTrainingRule>();
        foreach (var r in rows)
        {
            var id = S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (!Enum.TryParse<DayOfWeek>(S(r, 1), true, out var dow)) continue;
            if (!TimeSpan.TryParse(S(r, 2), CultureInfo.InvariantCulture, out var tod)) tod = TimeSpan.Zero;
            if (!DateTime.TryParse(S(r, 4), CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out var start)) start = DateTime.Today;

            DateTime? end = null;
            var endStr = S(r, 5);
            if (!string.IsNullOrWhiteSpace(endStr) &&
                DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out var e))
                end = e;

            TimeSpan endTod;
            var endTodStr = S(r, 7);
            if (!string.IsNullOrWhiteSpace(endTodStr) &&
                TimeSpan.TryParse(endTodStr, CultureInfo.InvariantCulture, out var parsedEnd))
                endTod = parsedEnd;
            else
                endTod = tod.Add(TimeSpan.FromMinutes(90)); // legacy rules default to 90 min

            list.Add(new RecurringTrainingRule
            {
                Id = id,
                DayOfWeek = dow,
                TimeOfDay = tod,
                EndTimeOfDay = endTod,
                Topic = S(r, 3),
                StartDate = start,
                EndDate = end,
                CreatedByFencerId = S(r, 6)
            });
        }
        return list;
    }

    public async Task UpsertRecurringTrainingAsync(RecurringTrainingRule rule)
    {
        var rows = await ReadAsync("RecurringTrainings!A2:H");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == rule.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            rule.Id,
            rule.DayOfWeek.ToString(),
            rule.TimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            rule.Topic ?? "",
            rule.StartDate.ToString("o", CultureInfo.InvariantCulture),
            rule.EndDate?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            rule.CreatedByFencerId ?? "",
            rule.EndTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
        };

        if (rowIndex >= 0)
            await UpdateAsync($"RecurringTrainings!A{rowIndex + 2}:H{rowIndex + 2}", values);
        else
            await AppendAsync("RecurringTrainings!A:H", values);
    }

    public async Task DeleteRecurringTrainingAsync(string ruleId)
    {
        var rows = await ReadAsync("RecurringTrainings!A2:H");
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == ruleId) { rowIndex = i; break; }
        if (rowIndex < 0) return;

        var blanks = new List<object> { "", "", "", "", "", "", "", "" };
        await UpdateAsync($"RecurringTrainings!A{rowIndex + 2}:H{rowIndex + 2}", blanks);
    }

    private static string S(IList<object> row, int i) =>
        i < row.Count ? row[i]?.ToString() ?? "" : "";

    private static bool ParseBool(string s) =>
        s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);
}