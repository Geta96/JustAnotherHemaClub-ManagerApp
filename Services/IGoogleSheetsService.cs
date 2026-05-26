using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public interface IGoogleSheetsService
{
    Task<List<Fencer>> GetFencersAsync();
    Task AddFencerAsync(Fencer fencer);

    Task<List<TrainingSession>> GetSessionsAsync();
    Task UpsertSessionAsync(TrainingSession session);

    Task<List<Payment>> GetPaymentsAsync(int year, int month);
    Task MarkPaidAsync(Payment payment);

    Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to);
    Task AddExpenseAsync(Expense expense);

    Task<List<Instructor>> GetInstructorsAsync();
}