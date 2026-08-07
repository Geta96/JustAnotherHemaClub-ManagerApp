using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ICacheControl _cache;

    // Cancels any in-flight LoadAsync when the user navigates away mid-refresh.
    private CancellationTokenSource? _loadCts;

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
        // NOTE: we intentionally do NOT throttle-skip here. A silent reload after
        // creating/editing a tournament must still rebuild the list, otherwise a
        // freshly-created (not-yet-started) tournament won't appear until the
        // window expires. Redundant network reads are already de-duplicated by
        // the caching layer, so an unconditional reload is cheap.

        // Cancel a previous in-flight load and start a fresh token for this one.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (showSpinner) IsLoading = true;
        try
        {
            var headers = await _sheets.GetTournamentHeadersAsync();

            // The page may have been navigated away from while we fetched.
            ct.ThrowIfCancellationRequested();

            Tournaments.Clear();
            foreach (var t in headers.OrderByDescending(h => h.CreatedAt))
                Tournaments.Add(new TournamentRow(t));
            ApplyFilter();
            OnPropertyChanged(nameof(HasNoTournaments));
            _lastLoadedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Expected when the user navigates away mid-refresh — abandon quietly.
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    /// <summary>
    /// Abandons any in-flight <see cref="LoadAsync"/>. Called when the page is
    /// disappearing so a manual refresh doesn't mutate the UI-bound collections
    /// after the CollectionView has been detached (crashes on Android when the
    /// RecyclerView has already been torn down).
    /// </summary>
    public void CancelLoad() => _loadCts?.Cancel();

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