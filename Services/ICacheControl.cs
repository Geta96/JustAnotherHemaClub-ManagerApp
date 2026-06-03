namespace JustAnotherHemaClub.Services;

public interface ICacheControl
{
    /// <summary>Fetches every dataset once and stores it in memory.</summary>
    Task WarmAsync();

    /// <summary>Drops every cached value. Next call hits the backend.</summary>
    void InvalidateAll();

    void InvalidateFencers();
    void InvalidateTrainings();
    void InvalidateExpenses();
    void InvalidatePayments(int? year = null, int? month = null);
    void InvalidateMonthNotes();
    void InvalidateIndividualLessons();
}