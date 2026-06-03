using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// In-memory caching decorator over <see cref="IGoogleSheetsService"/>.
/// Reads return cached lists; writes hit the backend and patch the cache.
/// </summary>
public sealed class CachedGoogleSheetsService : IGoogleSheetsService, ICacheControl
{
    private readonly GoogleSheetsService _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<Fencer>? _fencers;
    private List<TrainingSession>? _trainings;
    private List<Expense>? _expenses;
    private List<MonthNote>? _monthNotes;
    private readonly Dictionary<(int Y, int M), List<Payment>> _paymentsByMonth = new();
    private List<IndividualLesson>? _individualLessons;

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

    // ---------- ICacheControl ----------
    public async Task WarmAsync()
    {
        InvalidateAll();
        var fencers = _inner.GetFencersAsync();
        var trainings = _inner.GetTrainingsAsync();
        var expenses = _inner.GetExpensesAsync(DateTime.MinValue.AddYears(1),
                                               DateTime.MaxValue.AddYears(-1));
        var notes = _inner.GetMonthNotesAsync();
        var today = DateTime.Today;
        var currentMonthPayments = _inner.GetPaymentsAsync(today.Year, today.Month);

        await Task.WhenAll(fencers, trainings, expenses, notes, currentMonthPayments);

        _fencers = fencers.Result;
        _trainings = trainings.Result;
        _expenses = expenses.Result;
        _monthNotes = notes.Result;
        _paymentsByMonth[(today.Year, today.Month)] = currentMonthPayments.Result;
    }

    public void InvalidateAll()
    {
        _fencers = null;
        _trainings = null;
        _expenses = null;
        _monthNotes = null;
        _paymentsByMonth.Clear();
        _individualLessons = null;
    }

    public void InvalidateFencers() => _fencers = null;
    public void InvalidateTrainings() => _trainings = null;
    public void InvalidateExpenses() => _expenses = null;
    public void InvalidateMonthNotes() => _monthNotes = null;
    public void InvalidateIndividualLessons() => _individualLessons = null;

    public void InvalidatePayments(int? year = null, int? month = null)
    {
        if (year is null || month is null) _paymentsByMonth.Clear();
        else _paymentsByMonth.Remove((year.Value, month.Value));
    }

    // Defensive copies so a caller can't mutate the cached lists.
    private static List<T> Clone<T>(List<T> source) => new(source);
}