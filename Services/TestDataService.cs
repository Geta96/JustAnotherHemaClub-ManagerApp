using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// A fully in-memory implementation of <see cref="IGoogleSheetsService"/> used when
/// the user logs in as the test account (username: testuser, password: testuser).
/// All operations read/write to in-memory lists — nothing touches the real backend.
/// Pre-populated with realistic dummy data so the UI can be exercised end-to-end.
/// </summary>
public sealed class TestDataService : IGoogleSheetsService, ICacheControl
{
    public const string TestUsername = "testuser";
    public const string TestPassword = "testuser";

    private readonly List<Fencer> _fencers;
    private readonly List<TrainingSession> _trainings;
    private readonly List<Payment> _payments;
    private readonly List<Expense> _expenses;
    private readonly List<Income> _incomes;
    private readonly List<MonthNote> _monthNotes;
    private readonly List<IndividualLesson> _individualLessons;
    private readonly List<RecurringTrainingRule> _recurringTrainings;
    private readonly List<PriceRule> _priceRules;
    private readonly List<Tournament> _tournaments;

    public TestDataService()
    {
        _fencers = BuildFencers();
        _trainings = BuildTrainings();
        _payments = new List<Payment>();
        _expenses = BuildExpenses();
        _incomes = BuildIncomes();
        _monthNotes = new List<MonthNote>();
        _individualLessons = new List<IndividualLesson>();
        _recurringTrainings = BuildRecurringTrainings();
        _priceRules = BuildPriceRules();
        _tournaments = new List<Tournament>();
    }

    // ======================== IGoogleSheetsService ========================

    public Task<List<Fencer>> GetFencersAsync() => Task.FromResult(new List<Fencer>(_fencers));

    public Task AddFencerAsync(Fencer fencer) { _fencers.Add(fencer); return Task.CompletedTask; }

    public Task UpsertFencerAsync(Fencer fencer)
    {
        var idx = _fencers.FindIndex(f => f.Id == fencer.Id);
        if (idx >= 0) _fencers[idx] = fencer; else _fencers.Add(fencer);
        return Task.CompletedTask;
    }

    public Task<List<TrainingSession>> GetTrainingsAsync() => Task.FromResult(new List<TrainingSession>(_trainings));

    public Task UpsertTrainingAsync(TrainingSession training)
    {
        var idx = _trainings.FindIndex(t => t.Id == training.Id);
        if (idx >= 0) _trainings[idx] = training; else _trainings.Add(training);
        return Task.CompletedTask;
    }

    public Task DeleteTrainingAsync(string trainingId)
    {
        _trainings.RemoveAll(t => t.Id == trainingId);
        return Task.CompletedTask;
    }

    public Task<List<Payment>> GetPaymentsAsync(int year, int month) =>
        Task.FromResult(_payments.Where(p => p.Year == year && p.Month == month).ToList());

    public Task MarkPaidAsync(Payment payment) { _payments.Add(payment); return Task.CompletedTask; }

    public Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to) =>
        Task.FromResult(_expenses.Where(e => e.Date >= from && e.Date <= to).ToList());

    public Task AddExpenseAsync(Expense expense) { _expenses.Add(expense); return Task.CompletedTask; }

    public Task<List<Income>> GetIncomesAsync(DateTime from, DateTime to) =>
        Task.FromResult(_incomes.Where(i => i.Date >= from && i.Date <= to).ToList());

    public Task AddIncomeAsync(Income income) { _incomes.Add(income); return Task.CompletedTask; }

    public Task<List<MonthNote>> GetMonthNotesAsync() => Task.FromResult(new List<MonthNote>(_monthNotes));

    public Task UpsertMonthNoteAsync(MonthNote note)
    {
        var idx = _monthNotes.FindIndex(n => n.Year == note.Year && n.Month == note.Month);
        if (idx >= 0) _monthNotes[idx] = note; else _monthNotes.Add(note);
        return Task.CompletedTask;
    }

    public Task<List<IndividualLesson>> GetIndividualLessonsAsync() =>
        Task.FromResult(new List<IndividualLesson>(_individualLessons));

    public Task UpsertIndividualLessonAsync(IndividualLesson lesson)
    {
        var idx = _individualLessons.FindIndex(l => l.Id == lesson.Id);
        if (idx >= 0) _individualLessons[idx] = lesson; else _individualLessons.Add(lesson);
        return Task.CompletedTask;
    }

    public Task<List<RecurringTrainingRule>> GetRecurringTrainingsAsync() =>
        Task.FromResult(new List<RecurringTrainingRule>(_recurringTrainings));

    public Task UpsertRecurringTrainingAsync(RecurringTrainingRule rule)
    {
        var idx = _recurringTrainings.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) _recurringTrainings[idx] = rule; else _recurringTrainings.Add(rule);
        return Task.CompletedTask;
    }

    public Task DeleteRecurringTrainingAsync(string ruleId)
    {
        _recurringTrainings.RemoveAll(r => r.Id == ruleId);
        return Task.CompletedTask;
    }

    public Task<List<PriceRule>> GetPriceRulesAsync() => Task.FromResult(new List<PriceRule>(_priceRules));

    public Task UpsertPriceRuleAsync(PriceRule rule)
    {
        var idx = _priceRules.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) _priceRules[idx] = rule; else _priceRules.Add(rule);
        return Task.CompletedTask;
    }

    public Task DeletePriceRuleAsync(string ruleId)
    {
        _priceRules.RemoveAll(r => r.Id == ruleId);
        return Task.CompletedTask;
    }

    // ---- Tournaments ----

    public Task<List<Tournament>> GetTournamentHeadersAsync() =>
        Task.FromResult(_tournaments.Select(t => new Tournament
        {
            Id = t.Id, Name = t.Name, PasswordPlain = t.PasswordPlain,
            CreatedAt = t.CreatedAt, State = t.State, Version = t.Version,
            Fencers = t.Fencers
        }).ToList());

    public Task<Tournament?> GetTournamentAsync(string tournamentId) =>
        Task.FromResult(_tournaments.FirstOrDefault(t => t.Id == tournamentId));

    public Task<List<Match>> GetMatchesAsync(string tournamentId)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.FromResult(new List<Match>());
        // Deduplicate by ID (in-memory store can have same object added multiple times by reference)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<Match>();
        foreach (var pool in t.Pools)
            foreach (var m in pool.Matches)
                if (seen.Add(m.Id))
                    all.Add(m);
        if (t.Bracket is not null)
        {
            foreach (var round in t.Bracket.Rounds)
                foreach (var m in round.Matches)
                    if (seen.Add(m.Id))
                        all.Add(m);
            if (t.Bracket.BronzeMatch is not null && seen.Add(t.Bracket.BronzeMatch.Id))
                all.Add(t.Bracket.BronzeMatch);
        }
        return Task.FromResult(all);
    }

    public Task UpsertTournamentHeaderAsync(Tournament tournament)
    {
        var idx = _tournaments.FindIndex(t => t.Id == tournament.Id);
        if (idx >= 0) _tournaments[idx] = tournament;
        else if (!_tournaments.Contains(tournament)) _tournaments.Add(tournament);
        tournament.Version++;
        return Task.CompletedTask;
    }

    public Task DeleteTournamentAsync(string tournamentId)
    {
        _tournaments.RemoveAll(t => t.Id == tournamentId);
        return Task.CompletedTask;
    }

    public Task UpsertTournamentFencerAsync(string tournamentId, TournamentFencer fencer)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.CompletedTask;
        var idx = t.Fencers.FindIndex(f => f.Id == fencer.Id);
        if (idx >= 0) t.Fencers[idx] = fencer;
        else if (!t.Fencers.Contains(fencer)) t.Fencers.Add(fencer);
        return Task.CompletedTask;
    }

    public Task DeleteTournamentFencerAsync(string tournamentId, string fencerId)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        t?.Fencers.RemoveAll(f => f.Id == fencerId);
        return Task.CompletedTask;
    }

    public Task UpsertPoolAsync(string tournamentId, Pool pool)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.CompletedTask;
        var idx = t.Pools.FindIndex(p => p.Id == pool.Id);
        if (idx >= 0) t.Pools[idx] = pool;
        else if (!t.Pools.Contains(pool)) t.Pools.Add(pool);
        pool.Version++;
        return Task.CompletedTask;
    }

    public Task UpsertMatchAsync(string tournamentId, Match match)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.CompletedTask;
        foreach (var pool in t.Pools)
        {
            var idx = pool.Matches.FindIndex(m => m.Id == match.Id);
            if (idx >= 0) { pool.Matches[idx] = match; match.Version++; return Task.CompletedTask; }
        }
        if (t.Bracket is not null)
        {
            foreach (var round in t.Bracket.Rounds)
            {
                var idx = round.Matches.FindIndex(m => m.Id == match.Id);
                if (idx >= 0) { round.Matches[idx] = match; match.Version++; return Task.CompletedTask; }
            }
            if (t.Bracket.BronzeMatch?.Id == match.Id) { t.Bracket.BronzeMatch = match; match.Version++; }
        }
        return Task.CompletedTask;
    }

    public Task DeleteMatchAsync(string tournamentId, string matchId)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.CompletedTask;
        foreach (var pool in t.Pools) pool.Matches.RemoveAll(m => m.Id == matchId);
        return Task.CompletedTask;
    }

    public Task AppendPoolsAsync(string tournamentId, IList<Pool> pools)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is not null) foreach (var p in pools) { p.Version = 1; t.Pools.Add(p); }
        return Task.CompletedTask;
    }

    public Task AppendMatchesAsync(string tournamentId, IList<Match> matches)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is null) return Task.CompletedTask;
        foreach (var m in matches)
        {
            m.Version = 1;
            var pool = t.Pools.FirstOrDefault(p => p.Id == m.PoolId);
            if (pool is null) continue;
            // Skip if already present (same object or same ID — by-reference storage)
            if (!pool.Matches.Any(existing => existing.Id == m.Id))
                pool.Matches.Add(m);
        }
        return Task.CompletedTask;
    }

    public Task SaveFinalStandingsAsync(string tournamentId, IList<string> orderedFencerIds)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is not null) t.FinalStandingFencerIds = orderedFencerIds.ToList();
        return Task.CompletedTask;
    }

    public Task AppendTournamentFencersAsync(string tournamentId, IList<TournamentFencer> fencers)
    {
        var t = _tournaments.FirstOrDefault(t => t.Id == tournamentId);
        if (t is not null)
        {
            foreach (var f in fencers)
            {
                // Skip if already present (same object stored by reference from the VM)
                if (!t.Fencers.Any(existing => existing.Id == f.Id))
                    t.Fencers.Add(f);
            }
        }
        return Task.CompletedTask;
    }

    // ======================== ICacheControl (no-op for in-memory) ========================

    public Task WarmAsync() => Task.CompletedTask;
    public void InvalidateAll() { }
    public void InvalidateFencers() { }
    public void InvalidateTrainings() { }
    public void InvalidateExpenses() { }
    public void InvalidateIncomes() { }
    public void InvalidatePayments(int? year = null, int? month = null) { }
    public void InvalidateMonthNotes() { }
    public void InvalidateIndividualLessons() { }
    public void InvalidateRecurringTrainings() { }
    public void InvalidatePrices() { }
    public void InvalidateTournaments() { }

    // ======================== DUMMY DATA BUILDERS ========================

    private static List<Fencer> BuildFencers()
    {
        var testHash = AuthService.Hash(TestPassword);
        return new List<Fencer>
        {
            new() { Id = "test-001", Name = "Test User", Username = TestUsername, PasswordHash = testHash,
                    Email = "test@example.com", Active = true, IsStudent = false, IsInstructor = true,
                    GdprAccepted = true, LiabilityAccepted = true },
            new() { Id = "demo-f1", Name = "Anna Varga", Username = "anna", PasswordHash = AuthService.Hash("demo1"),
                    Email = "anna@demo.com", Active = true, IsStudent = true },
            new() { Id = "demo-f2", Name = "Béla Kovács", Username = "bela", PasswordHash = AuthService.Hash("demo1"),
                    Email = "bela@demo.com", Active = true, IsStudent = false },
            new() { Id = "demo-f3", Name = "Csaba Tóth", Username = "csaba", PasswordHash = AuthService.Hash("demo1"),
                    Email = "csaba@demo.com", Active = true, IsStudent = false },
            new() { Id = "demo-f4", Name = "Dóra Szabó", Username = "dora", PasswordHash = AuthService.Hash("demo1"),
                    Email = "dora@demo.com", Active = true, IsStudent = true },
            new() { Id = "demo-f5", Name = "Erik Nagy", Username = "erik", PasswordHash = AuthService.Hash("demo1"),
                    Email = "erik@demo.com", Active = true, IsStudent = false },
            new() { Id = "demo-f6", Name = "Fanni Kiss", Username = "fanni", PasswordHash = AuthService.Hash("demo1"),
                    Email = "fanni@demo.com", Active = true, IsStudent = true },
            new() { Id = "demo-f7", Name = "Gábor Horváth", Username = "gabor", PasswordHash = AuthService.Hash("demo1"),
                    Email = "gabor@demo.com", Active = true, IsStudent = false },
            new() { Id = "demo-f8", Name = "Hanna Molnár", Username = "hanna", PasswordHash = AuthService.Hash("demo1"),
                    Email = "hanna@demo.com", Active = false, IsStudent = false },
        };
    }

    private static List<TrainingSession> BuildTrainings()
    {
        var sessions = new List<TrainingSession>();
        var topics = new[] { "Longsword", "Messer & Buckler", "Sabre", "Wrestling", "Dagger" };
        var fencerIds = new[] { "demo-f1", "demo-f2", "demo-f3", "demo-f4", "demo-f5", "demo-f6", "demo-f7" };
        var rng = new Random(42);

        // Generate 3 months of weekly Tuesday + Thursday trainings
        var start = DateTime.Today.AddMonths(-3);
        for (var d = start; d <= DateTime.Today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday)
            {
                var attendees = fencerIds.Where(_ => rng.NextDouble() > 0.3).ToList();
                attendees.Add("test-001"); // test user always attends
                sessions.Add(new TrainingSession
                {
                    Id = $"ts-{d:yyyyMMdd}",
                    Date = d.AddHours(18),
                    EndDate = d.AddHours(20),
                    Topic = topics[rng.Next(topics.Length)],
                    AttendeeFencerIds = attendees.Distinct().ToList()
                });
            }
        }
        return sessions;
    }

    private static List<Expense> BuildExpenses()
    {
        var today = DateTime.Today;
        return new List<Expense>
        {
            new() { Id = "exp-1", Date = today.AddDays(-45), Amount = 25000, Description = "Venue rent (March)" },
            new() { Id = "exp-2", Date = today.AddDays(-15), Amount = 25000, Description = "Venue rent (April)" },
            new() { Id = "exp-3", Date = today.AddDays(-30), Amount = 8500, Description = "New training swords (×2)" },
            new() { Id = "exp-4", Date = today.AddDays(-10), Amount = 3200, Description = "First aid supplies" },
        };
    }

    private static List<Income> BuildIncomes()
    {
        var today = DateTime.Today;
        return new List<Income>
        {
            new() { Id = "inc-1", Date = today.AddDays(-40), Amount = 15000, Description = "Workshop fee (guest)" },
            new() { Id = "inc-2", Date = today.AddDays(-5), Amount = 5000, Description = "Tournament entry (external)" },
        };
    }

    private static List<RecurringTrainingRule> BuildRecurringTrainings()
    {
        var start = new DateTime(DateTime.Today.Year, 1, 1);
        return new List<RecurringTrainingRule>
        {
            new()
            {
                Id = "rec-1", DayOfWeek = DayOfWeek.Tuesday,
                TimeOfDay = new TimeSpan(18, 0, 0), EndTimeOfDay = new TimeSpan(20, 0, 0),
                Topic = "Longsword", StartDate = start
            },
            new()
            {
                Id = "rec-2", DayOfWeek = DayOfWeek.Thursday,
                TimeOfDay = new TimeSpan(18, 0, 0), EndTimeOfDay = new TimeSpan(20, 0, 0),
                Topic = "Messer & Buckler", StartDate = start
            },
        };
    }

    private static List<PriceRule> BuildPriceRules()
    {
        var start = new DateTime(DateTime.Today.Year, 1, 1);
        return new List<PriceRule>
        {
            new() { Id = "pr-1", SessionCount = 1, MonthCount = 1, FullPrice = 3500,  StudentPrice = 2000, StartDate = start },
            new() { Id = "pr-2", SessionCount = 4, MonthCount = 1, FullPrice = 9000,  StudentPrice = 5500, StartDate = start },
            new() { Id = "pr-3", SessionCount = 0, MonthCount = 1, FullPrice = 12000, StudentPrice = 7000, StartDate = start },
            new() { Id = "pr-4", SessionCount = 0, MonthCount = 2, FullPrice = 20000, StudentPrice = 12000, StartDate = start },
        };
    }
}
