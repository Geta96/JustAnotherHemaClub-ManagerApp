using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Forwards every <see cref="IGoogleSheetsService"/> call to either the real Google Sheets
/// implementation or the in-memory demo, based on <see cref="AuthService.IsGuest"/>.
/// </summary>
public class GoogleSheetsServiceProxy : IGoogleSheetsService
{
    private readonly GoogleSheetsService _real;
    private readonly DemoGoogleSheetsService _demo;
    private readonly AuthService _auth;

    public GoogleSheetsServiceProxy(GoogleSheetsService real, DemoGoogleSheetsService demo, AuthService auth)
    {
        _real = real;
        _demo = demo;
        _auth = auth;
    }

    private IGoogleSheetsService Active => _auth.IsGuest ? _demo : _real;

    public Task<List<Fencer>> GetFencersAsync() => Active.GetFencersAsync();
    public Task AddFencerAsync(Fencer fencer) => Active.AddFencerAsync(fencer);

    public Task<List<TrainingSession>> GetTrainingsAsync() => Active.GetTrainingsAsync();
    public Task UpsertTrainingAsync(TrainingSession training) => Active.UpsertTrainingAsync(training);

    public Task<List<Payment>> GetPaymentsAsync(int year, int month) => Active.GetPaymentsAsync(year, month);
    public Task MarkPaidAsync(Payment payment) => Active.MarkPaidAsync(payment);

    public Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to) => Active.GetExpensesAsync(from, to);
    public Task AddExpenseAsync(Expense expense) => Active.AddExpenseAsync(expense);

    public Task<List<Instructor>> GetInstructorsAsync() => Active.GetInstructorsAsync();

    public Task<List<MonthNote>> GetMonthNotesAsync() => Active.GetMonthNotesAsync();
    public Task UpsertMonthNoteAsync(MonthNote note) => Active.UpsertMonthNoteAsync(note);
}