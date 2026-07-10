namespace JustAnotherHemaClub.Services;

public interface ICacheControl
{
    /// <summary>Fetches every dataset once and stores it in memory.</summary>
    Task WarmAsync();

    /// <summary>
    /// Fills any datasets that aren't cached yet WITHOUT invalidating existing
    /// ones. Safe to call repeatedly (e.g. as a background prefetch while the
    /// user is idle on the home page). Only issues network calls for the slices
    /// that are still empty.
    /// </summary>
    Task PrefetchAsync();

    /// <summary>Drops every cached value. Next call hits the backend.</summary>
    void InvalidateAll();

    void InvalidateFencers();
    void InvalidateTrainings();
    void InvalidateExpenses();
    void InvalidateIncomes();
    void InvalidatePayments(int? year = null, int? month = null);
    void InvalidateMonthNotes();
    void InvalidateIndividualLessons();
    void InvalidateRecurringTrainings();
    void InvalidatePrices();
    void InvalidateTournaments();
}