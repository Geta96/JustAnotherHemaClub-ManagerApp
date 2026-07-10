using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;

    // Suppress silent re-loads for 30 seconds after the last successful fetch.
    private static readonly TimeSpan SilentReloadThrottle = TimeSpan.FromSeconds(30);
    private DateTime _lastLoadedUtc = DateTime.MinValue;

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
        // Skip silent refreshes that arrive within the throttle window — e.g.
        // rapid back-navigation that fires OnAppearing multiple times.
        if (!showSpinner && DateTime.UtcNow - _lastLoadedUtc < SilentReloadThrottle)
            return;

        if (showSpinner) IsLoading = true;
        try
        {
            var headers = await _sheets.GetTournamentHeadersAsync();
            Tournaments.Clear();
            foreach (var t in headers.OrderByDescending(h => h.CreatedAt))
                Tournaments.Add(new TournamentRow(t));
            ApplyFilter();
            OnPropertyChanged(nameof(HasNoTournaments));
            _lastLoadedUtc = DateTime.UtcNow;
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _cache.InvalidateTournaments();
        _lastLoadedUtc = DateTime.MinValue; // force a real fetch
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
            var fresh = await _sheets.GetTournamentAsync(tournamentId);
            if (fresh is null) return (DeleteOutcome.NotFound, null);

            var expected = (fresh.PasswordPlain ?? "").Trim();
            var entered  = (passwordAttempt ?? "").Trim();
            if (entered.Length == 0 || entered != expected)
                return (DeleteOutcome.WrongPassword, null);

            var row = Tournaments.FirstOrDefault(r => r.Id == tournamentId);
            if (row is not null) Tournaments.Remove(row);
            ApplyFilter();
            OnPropertyChanged(nameof(HasNoTournaments));
            _cache.InvalidateTournaments();
            _lastLoadedUtc = DateTime.MinValue;

            _ = Task.Run(async () =>
            {
                try { await _sheets.DeleteTournamentAsync(tournamentId); }
                catch { /* Reconciled by the next refresh / cache miss. */ }
            });

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