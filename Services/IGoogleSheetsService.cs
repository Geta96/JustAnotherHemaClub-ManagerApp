using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public interface IGoogleSheetsService
{
    Task<List<Fencer>> GetFencersAsync();
    Task AddFencerAsync(Fencer fencer);
    Task UpsertFencerAsync(Fencer fencer);

    Task<List<TrainingSession>> GetTrainingsAsync();
    Task UpsertTrainingAsync(TrainingSession training);
    Task DeleteTrainingAsync(string trainingId);

    Task<List<Payment>> GetPaymentsAsync(int year, int month);
    Task MarkPaidAsync(Payment payment);

    Task<List<Expense>> GetExpensesAsync(DateTime from, DateTime to);
    Task AddExpenseAsync(Expense expense);

    Task<List<MonthNote>> GetMonthNotesAsync();
    Task UpsertMonthNoteAsync(MonthNote note);

    Task<List<IndividualLesson>> GetIndividualLessonsAsync();
    Task UpsertIndividualLessonAsync(IndividualLesson lesson);

    Task<List<RecurringTrainingRule>> GetRecurringTrainingsAsync();
    Task UpsertRecurringTrainingAsync(RecurringTrainingRule rule);
    Task DeleteRecurringTrainingAsync(string ruleId);

    // ---- Tournaments (normalised + versioned) ----

    Task<List<Tournament>> GetTournamentHeadersAsync();
    Task<Tournament?> GetTournamentAsync(string tournamentId);
    Task<List<Match>> GetMatchesAsync(string tournamentId);

    Task UpsertTournamentHeaderAsync(Tournament tournament);
    Task DeleteTournamentAsync(string tournamentId);

    Task UpsertTournamentFencerAsync(string tournamentId, TournamentFencer fencer);
    Task DeleteTournamentFencerAsync(string tournamentId, string fencerId);

    Task UpsertPoolAsync(string tournamentId, Pool pool);
    Task UpsertMatchAsync(string tournamentId, Match match);

    /// <summary>One HTTP call regardless of how many pools — used by Start Tournament.</summary>
    Task AppendPoolsAsync(string tournamentId, IList<Pool> pools);

    /// <summary>One HTTP call regardless of how many matches — used by Start Tournament.</summary>
    Task AppendMatchesAsync(string tournamentId, IList<Match> matches);

    Task SaveFinalStandingsAsync(string tournamentId, IList<string> orderedFencerIds);

    /// <summary>One HTTP call regardless of how many fencers — used by Save Tournament for new tournaments.</summary>
    Task AppendTournamentFencersAsync(string tournamentId, IList<TournamentFencer> fencers);
}