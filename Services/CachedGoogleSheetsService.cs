using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// In-memory caching decorator over <see cref="IGoogleSheetsService"/>.
/// Reads return cached lists; writes hit the backend and patch the cache.
/// </summary>
public sealed partial class CachedGoogleSheetsService : IGoogleSheetsService, ICacheControl
{
    private readonly GoogleSheetsService _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<Fencer>? _fencers;
    private List<TrainingSession>? _trainings;
    private List<Expense>? _expenses;
    private List<Income>? _incomes;
    private List<MonthNote>? _monthNotes;
    private readonly Dictionary<(int Y, int M), List<Payment>> _paymentsByMonth = new();
    private List<IndividualLesson>? _individualLessons;
    private List<RecurringTrainingRule>? _recurringTrainings;
    private List<PriceRule>? _prices;

    public CachedGoogleSheetsService(GoogleSheetsService inner) => _inner = inner;

    // ---------- Fencers ----------
    public async Task<List<Fencer>> GetFencersAsync()
    {
        if (_fencers is not null) return Clone(_fencers);
        await _gate.WaitAsync();
        try
        {
            _fencers ??= await _inner.GetFencersAsync();
            return Clone(_fencers);
        }
        finally { _gate.Release(); }
    }

    public async Task AddFencerAsync(Fencer fencer)
    {
        await _inner.AddFencerAsync(fencer);
        _fencers?.Add(fencer);
    }

    public async Task UpsertFencerAsync(Fencer fencer)
    {
        await _inner.UpsertFencerAsync(fencer);
        if (_fencers is not null)
        {
            var idx = _fencers.FindIndex(f => f.Id == fencer.Id);
            if (idx >= 0) _fencers[idx] = fencer;
            else _fencers.Add(fencer);
        }
    }

    // ---------- Trainings ----------
    public async Task<List<TrainingSession>> GetTrainingsAsync()
    {
        if (_trainings is not null) return Clone(_trainings);
        await _gate.WaitAsync();
        try
        {
            _trainings ??= await _inner.GetTrainingsAsync();
            return Clone(_trainings);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertTrainingAsync(TrainingSession training)
    {
        await _inner.UpsertTrainingAsync(training);
        if (_trainings is not null)
        {
            var idx = _trainings.FindIndex(t => t.Id == training.Id);
            if (idx >= 0) _trainings[idx] = training;
            else _trainings.Add(training);
        }
    }

    public async Task DeleteTrainingAsync(string trainingId)
    {
        await _inner.DeleteTrainingAsync(trainingId);
        _trainings?.RemoveAll(t => t.Id == trainingId);
    }

    // ---------- Payments (keyed per month) ----------
    public async Task<List<Payment>> GetPaymentsAsync(int year, int month)
    {
        if (_paymentsByMonth.TryGetValue((year, month), out var cached))
            return Clone(cached);

        await _gate.WaitAsync();
        try
        {
            if (!_paymentsByMonth.TryGetValue((year, month), out cached))
            {
                cached = await _inner.GetPaymentsAsync(year, month);
                _paymentsByMonth[(year, month)] = cached;
            }
            return Clone(cached);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkPaidAsync(Payment payment)
    {
        await _inner.MarkPaidAsync(payment);
        if (_paymentsByMonth.TryGetValue((payment.Year, payment.Month), out var list))
            list.Add(payment);
    }

    // ---------- Expenses ----------
    public async Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to)
    {
        if (_expenses is null)
        {
            await _gate.WaitAsync();
            try
            {
                _expenses ??= await _inner.GetExpensesAsync(DateTime.MinValue.AddYears(1),
                                                            DateTime.MaxValue.AddYears(-1));
            }
            finally { _gate.Release(); }
        }
        return _expenses.Where(e => e.Date >= from && e.Date <= to).ToList();
    }

    public async Task AddExpenseAsync(Expense expense)
    {
        await _inner.AddExpenseAsync(expense);
        _expenses?.Add(expense);
    }

    // ---------- Incomes (one-off, non-dues income) ----------
    public async Task<List<Income>> GetIncomesAsync(DateTime from, DateTime to)
    {
        if (_incomes is null)
        {
            await _gate.WaitAsync();
            try
            {
                _incomes ??= await _inner.GetIncomesAsync(DateTime.MinValue.AddYears(1),
                                                          DateTime.MaxValue.AddYears(-1));
            }
            finally { _gate.Release(); }
        }
        return _incomes.Where(i => i.Date >= from && i.Date <= to).ToList();
    }

    public async Task AddIncomeAsync(Income income)
    {
        await _inner.AddIncomeAsync(income);
        _incomes?.Add(income);
    }

    // ---------- Month notes ----------
    public async Task<List<MonthNote>> GetMonthNotesAsync()
    {
        if (_monthNotes is not null) return Clone(_monthNotes);
        await _gate.WaitAsync();
        try
        {
            _monthNotes ??= await _inner.GetMonthNotesAsync();
            return Clone(_monthNotes);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertMonthNoteAsync(MonthNote note)
    {
        await _inner.UpsertMonthNoteAsync(note);
        _monthNotes?.Add(note);
    }

    // ---------- Individual lessons ----------
    public async Task<List<IndividualLesson>> GetIndividualLessonsAsync()
    {
        if (_individualLessons is not null) return Clone(_individualLessons);
        await _gate.WaitAsync();
        try
        {
            _individualLessons ??= await _inner.GetIndividualLessonsAsync();
            return Clone(_individualLessons);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertIndividualLessonAsync(IndividualLesson lesson)
    {
        await _inner.UpsertIndividualLessonAsync(lesson);
        if (_individualLessons is not null)
        {
            var idx = _individualLessons.FindIndex(l => l.Id == lesson.Id);
            // Rejected = deleted from cached view.
            if (lesson.Status == IndividualLessonStatus.Rejected)
            {
                if (idx >= 0) _individualLessons.RemoveAt(idx);
            }
            else
            {
                if (idx >= 0) _individualLessons[idx] = lesson;
                else _individualLessons.Add(lesson);
            }
        }
    }

    // ---------- Recurring trainings ----------
    public async Task<List<RecurringTrainingRule>> GetRecurringTrainingsAsync()
    {
        if (_recurringTrainings is not null) return Clone(_recurringTrainings);
        await _gate.WaitAsync();
        try
        {
            _recurringTrainings ??= await _inner.GetRecurringTrainingsAsync();
            return Clone(_recurringTrainings);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertRecurringTrainingAsync(RecurringTrainingRule rule)
    {
        await _inner.UpsertRecurringTrainingAsync(rule);
        if (_recurringTrainings is not null)
        {
            var idx = _recurringTrainings.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0) _recurringTrainings[idx] = rule;
            else _recurringTrainings.Add(rule);
        }
    }

    public async Task DeleteRecurringTrainingAsync(string ruleId)
    {
        await _inner.DeleteRecurringTrainingAsync(ruleId);
        _recurringTrainings?.RemoveAll(r => r.Id == ruleId);
    }

    // ---------- Price rules ----------
    public async Task<List<PriceRule>> GetPriceRulesAsync()
    {
        if (_prices is not null) return Clone(_prices);
        await _gate.WaitAsync();
        try
        {
            _prices ??= await _inner.GetPriceRulesAsync();
            return Clone(_prices);
        }
        finally { _gate.Release(); }
    }

    public async Task UpsertPriceRuleAsync(PriceRule rule)
    {
        await _inner.UpsertPriceRuleAsync(rule);
        if (_prices is not null)
        {
            var idx = _prices.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0) _prices[idx] = rule;
            else _prices.Add(rule);
        }
    }

    public async Task DeletePriceRuleAsync(string ruleId)
    {
        await _inner.DeletePriceRuleAsync(ruleId);
        _prices?.RemoveAll(r => r.Id == ruleId);
    }

    // ---------- ICacheControl ----------
    public async Task WarmAsync()
    {
        InvalidateAll();
        var fencers = _inner.GetFencersAsync();
        var trainings = _inner.GetTrainingsAsync();
        var expenses = _inner.GetExpensesAsync(DateTime.MinValue.AddYears(1),
                                               DateTime.MaxValue.AddYears(-1));
        var incomes = _inner.GetIncomesAsync(DateTime.MinValue.AddYears(1),
                                             DateTime.MaxValue.AddYears(-1));
        var notes = _inner.GetMonthNotesAsync();
        var today = DateTime.Today;
        var currentMonthPayments = _inner.GetPaymentsAsync(today.Year, today.Month);
        var prices = _inner.GetPriceRulesAsync();

        await Task.WhenAll(fencers, trainings, expenses, incomes, notes, currentMonthPayments, prices);

        _fencers = fencers.Result;
        _trainings = trainings.Result;
        _expenses = expenses.Result;
        _incomes = incomes.Result;
        _monthNotes = notes.Result;
        _paymentsByMonth[(today.Year, today.Month)] = currentMonthPayments.Result;
        _prices = prices.Result;
    }

    public void InvalidateAll()
    {
        _fencers = null;
        _trainings = null;
        _expenses = null;
        _incomes = null;
        _monthNotes = null;
        _paymentsByMonth.Clear();
        _individualLessons = null;
        _recurringTrainings = null;
        _prices = null;
    }

    public void InvalidateFencers() => _fencers = null;
    public void InvalidateTrainings() => _trainings = null;
    public void InvalidateExpenses() => _expenses = null;
    public void InvalidateIncomes() => _incomes = null;
    public void InvalidateMonthNotes() => _monthNotes = null;
    public void InvalidateIndividualLessons() => _individualLessons = null;
    public void InvalidateRecurringTrainings() => _recurringTrainings = null;
    public void InvalidatePrices() => _prices = null;

    public void InvalidatePayments(int? year = null, int? month = null)
    {
        if (year is null || month is null) _paymentsByMonth.Clear();
        else _paymentsByMonth.Remove((year.Value, month.Value));
    }

    // Defensive copies so a caller can't mutate the cached lists.
    private static List<T> Clone<T>(List<T> source) => new(source);
}