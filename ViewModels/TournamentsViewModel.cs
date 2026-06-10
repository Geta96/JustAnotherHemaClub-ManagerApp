using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;

    public ObservableCollection<TournamentRow> Tournaments { get; } = new();
    public ObservableCollection<TournamentRow> FilteredTournaments { get; } = new();

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string filter = "";

    public bool HasNoTournaments => !IsLoading && Tournaments.Count == 0;

    public TournamentsViewModel(IGoogleSheetsService sheets, ICacheControl cache)
    {
        _sheets = sheets;
        _cache = cache;
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            var headers = await _sheets.GetTournamentHeadersAsync();
            Tournaments.Clear();
            foreach (var t in headers.OrderByDescending(h => h.CreatedAt))
                Tournaments.Add(new TournamentRow(t));
            ApplyFilter();
            OnPropertyChanged(nameof(HasNoTournaments));
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _cache.InvalidateTournaments();
        await LoadAsync(showSpinner: true);
    }

    /// <summary>
    /// Outcome of a password-gated delete attempt — the page renders the matching alert.
    /// </summary>
    public enum DeleteOutcome { Deleted, WrongPassword, NotFound, Error }

    /// <summary>
    /// Validates <paramref name="passwordAttempt"/> against the tournament's current
    /// organiser password, then deletes every Tournaments/TournamentFencers/Pools/Matches/
    /// FinalStandings row belonging to it. Called from the list page after the
    /// confirmation + password prompt.
    /// </summary>
    public async Task<(DeleteOutcome Outcome, string? Error)> DeleteWithPasswordAsync(
        string tournamentId, string passwordAttempt)
    {
        try
        {
            // Refetch so we validate against the latest password — someone may have
            // rotated it since this list was last loaded.
            var fresh = await _sheets.GetTournamentAsync(tournamentId);
            if (fresh is null) return (DeleteOutcome.NotFound, null);

            var expected = (fresh.PasswordPlain ?? "").Trim();
            var entered  = (passwordAttempt ?? "").Trim();
            if (entered.Length == 0 || entered != expected)
                return (DeleteOutcome.WrongPassword, null);

            await _sheets.DeleteTournamentAsync(tournamentId);
            _cache.InvalidateTournaments();

            // Drop the row locally so the UI updates without a full refresh round-trip.
            var row = Tournaments.FirstOrDefault(r => r.Id == tournamentId);
            if (row is not null) Tournaments.Remove(row);
            ApplyFilter();
            OnPropertyChanged(nameof(HasNoTournaments));

            return (DeleteOutcome.Deleted, null);
        }
        catch (Exception ex)
        {
            return (DeleteOutcome.Error, ex.Message);
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredTournaments.Clear();
        var q = (Filter ?? "").Trim();
        IEnumerable<TournamentRow> src = Tournaments;
        if (q.Length > 0)
            src = src.Where(r => (r.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var r in src) FilteredTournaments.Add(r);
    }
}