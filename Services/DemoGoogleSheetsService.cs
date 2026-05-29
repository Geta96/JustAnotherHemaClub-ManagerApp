using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// In-memory <see cref="IGoogleSheetsService"/> used in Guest mode.
/// Data is seeded with a handful of demo rows and persists for the app session only.
/// </summary>
public class DemoGoogleSheetsService : IGoogleSheetsService
{
    private readonly List<Fencer> _fencers;
    private readonly List<TrainingSession> _trainings = new();
    private readonly List<Payment> _payments = new();
    private readonly List<Expense> _expenses = new();
    private readonly List<Instructor> _instructors;
    private readonly List<MonthNote> _monthNotes = new();

    public DemoGoogleSheetsService()
    {
        _fencers = new()
        {
            new Fencer { Id = "f1", Name = "Alice Sword",   Nickname = "Ali",   Email = "alice@example.com", Active = true,  IsStudent = false, GdprAccepted = true,  LiabilityAccepted = true  },
            new Fencer { Id = "f2", Name = "Bob Longsword", Nickname = "Bobby", Email = "bob@example.com",   Active = true,  IsStudent = true,  GdprAccepted = true,  LiabilityAccepted = true  },
            new Fencer { Id = "f3", Name = "Cara Rapier",   Nickname = "Cas",   Email = "cara@example.com",  Active = true,  IsStudent = false, GdprAccepted = true,  LiabilityAccepted = false },
            new Fencer { Id = "f4", Name = "Dan Messer",    Nickname = "Danny", Email = "dan@example.com",   Active = false, IsStudent = false, GdprAccepted = false, LiabilityAccepted = false },
        };

        // Seed three months: current, previous, two-months-ago
        var today = DateTime.Today;
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        SeedMonth(thisMonth,               attendance: new() { ["f1"] = 8, ["f2"] = 5, ["f3"] = 2 }, expensesFor: false);
        SeedMonth(thisMonth.AddMonths(-1), attendance: new() { ["f1"] = 7, ["f2"] = 4, ["f3"] = 6 }, expensesFor: true,  paid: new() { "f1", "f2" });
        SeedMonth(thisMonth.AddMonths(-2), attendance: new() { ["f1"] = 8, ["f2"] = 8, ["f3"] = 3 }, expensesFor: true,  paid: new() { "f1", "f2", "f3" });

        _monthNotes.Add(new MonthNote
        {
            Year = thisMonth.AddMonths(-1).Year,
            Month = thisMonth.AddMonths(-1).Month,
            Note = "Focus month: longsword fundamentals."
        });

        _instructors = new()
        {
            new Instructor { Username = "guest", PasswordHash = "", DisplayName = "Guest" }
        };
    }

    private void SeedMonth(DateTime firstOfMonth,
                           Dictionary<string, int> attendance,
                           bool expensesFor,
                           List<string>? paid = null)
    {
        var last = firstOfMonth.AddMonths(1).AddDays(-1);
        var dates = Enumerable
            .Range(0, (last - firstOfMonth).Days + 1)
            .Select(o => firstOfMonth.AddDays(o))
            .Where(d => d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Friday)
            .Take(8)
            .ToList();

        for (int i = 0; i < dates.Count; i++)
        {
            var attendees = attendance
                .Where(kvp => i < kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            _trainings.Add(new TrainingSession
            {
                Id = $"t_{firstOfMonth:yyyyMM}_{i + 1}",
                Date = dates[i],
                Topic = (i % 2 == 0) ? "Longsword drills" : "Sparring",
                AttendeeFencerIds = attendees
            });
        }

        if (expensesFor)
        {
            _expenses.Add(new Expense { Id = $"e_{firstOfMonth:yyyyMM}_rent", Date = firstOfMonth.AddDays(4),  Category = "Venue", Description = "Hall rental",        Amount = 40000m });
            _expenses.Add(new Expense { Id = $"e_{firstOfMonth:yyyyMM}_gear", Date = firstOfMonth.AddDays(12), Category = "Gear",  Description = "Replacement gloves", Amount = 8500m });
        }

        if (paid is not null)
        {
            foreach (var fencerId in paid)
            {
                if (!attendance.TryGetValue(fencerId, out var count) || count == 0) continue;
                _payments.Add(new Payment
                {
                    FencerId = fencerId,
                    Year = firstOfMonth.Year,
                    Month = firstOfMonth.Month,
                    Amount = DuesCalculator.Calculate(count),
                    PaidOn = firstOfMonth.AddDays(20)
                });
            }
        }
    }

    // --- Fencers ---
    public Task<List<Fencer>> GetFencersAsync() => Task.FromResult(_fencers.ToList());

    public Task AddFencerAsync(Fencer fencer)
    {
        if (string.IsNullOrEmpty(fencer.Id)) fencer.Id = Guid.NewGuid().ToString("N");
        _fencers.Add(fencer);
        return Task.CompletedTask;
    }

    // --- Trainings ---
    public Task<List<TrainingSession>> GetTrainingsAsync() => Task.FromResult(_trainings.ToList());

    public Task UpsertTrainingAsync(TrainingSession training)
    {
        if (string.IsNullOrEmpty(training.Id)) training.Id = Guid.NewGuid().ToString("N");
        var existing = _trainings.FirstOrDefault(s => s.Id == training.Id);
        if (existing is not null) _trainings.Remove(existing);
        _trainings.Add(training);
        return Task.CompletedTask;
    }

    // --- Payments ---
    public Task<List<Payment>> GetPaymentsAsync(int year, int month) =>
        Task.FromResult(_payments.Where(p => p.Year == year && p.Month == month).ToList());

    public Task MarkPaidAsync(Payment payment) { _payments.Add(payment); return Task.CompletedTask; }

    // --- Expenses ---
    public Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to) =>
        Task.FromResult(_expenses.Where(e => e.Date >= from && e.Date <= to).ToList());

    public Task AddExpenseAsync(Expense expense)
    {
        if (string.IsNullOrEmpty(expense.Id)) expense.Id = Guid.NewGuid().ToString("N");
        _expenses.Add(expense);
        return Task.CompletedTask;
    }

    // --- Instructors ---
    public Task<List<Instructor>> GetInstructorsAsync() => Task.FromResult(_instructors.ToList());

    // --- Month notes ---
    public Task<List<MonthNote>> GetMonthNotesAsync() => Task.FromResult(_monthNotes.ToList());

    public Task UpsertMonthNoteAsync(MonthNote note)
    {
        var existing = _monthNotes.FirstOrDefault(n => n.Year == note.Year && n.Month == note.Month);
        if (existing is not null) _monthNotes.Remove(existing);
        _monthNotes.Add(note);
        return Task.CompletedTask;
    }
}