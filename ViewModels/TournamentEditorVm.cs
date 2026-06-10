using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentEditorVm : ObservableObject
{
    public const int MaxFencers = 128;
    public const int MinFencersToStart = 4;

    private readonly IGoogleSheetsService _sheets;
    private readonly TournamentAutoSaveService _autoSave;
    private readonly ICacheControl _cache;

    private bool _isInitialSave;
    private bool _suppressAutoSave;

    [ObservableProperty] private Tournament? tournament;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string newFencerName = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isStarting;
    [ObservableProperty] private string errorMessage = "";

    public ObservableCollection<TournamentFencerRow> Fencers { get; } = new();

    public bool IsNew => _isInitialSave;
    public bool IsExisting => !_isInitialSave && Tournament is not null;
    public bool IsSetupState => Tournament?.State == TournamentState.Setup;
    public bool CanAddFencers => IsSetupState && Fencers.Count < MaxFencers;
    public bool CanRemoveFencers => IsSetupState;
    public bool CanStart => IsExisting && IsSetupState && ActiveFencerCount >= MinFencersToStart;
    public int ActiveFencerCount => Fencers.Count(f => !f.IsWithdrawn);
    public string FencerCountText => $"{Fencers.Count}/{MaxFencers} fencers ({ActiveFencerCount} active)";
    public string StartHintText => ActiveFencerCount < MinFencersToStart
        ? $"Add at least {MinFencersToStart} fencers to start ({ActiveFencerCount}/{MinFencersToStart})"
        : "Ready to start. This will lock the roster and create the pools.";

    public TournamentEditorVm(IGoogleSheetsService sheets,
                              TournamentAutoSaveService autoSave,
                              ICacheControl cache)
    {
        _sheets = sheets;
        _autoSave = autoSave;
        _cache = cache;
    }

    // -------- Initialisation --------

    public void InitNew()
    {
        _suppressAutoSave = true;
        _isInitialSave = true;
        Tournament = new Tournament { State = TournamentState.Setup, CreatedAt = DateTime.UtcNow };
        Name = "";
        Password = "";
        NewFencerName = "";
        ErrorMessage = "";
        Fencers.Clear();
        NotifyStateChanged();
        _suppressAutoSave = false;
    }

    public async Task InitExistingAsync(string tournamentId)
    {
        _suppressAutoSave = true;
        IsLoading = true;
        try
        {
            _isInitialSave = false;
            Tournament = await _sheets.GetTournamentAsync(tournamentId);
            if (Tournament is null) { ErrorMessage = "Tournament not found."; return; }

            Name = Tournament.Name;
            Password = Tournament.PasswordPlain;
            NewFencerName = "";
            ErrorMessage = "";
            Fencers.Clear();
            foreach (var f in Tournament.Fencers.OrderBy(f => f.OrderIndex))
                Fencers.Add(new TournamentFencerRow(f));

            NotifyStateChanged();
        }
        finally
        {
            IsLoading = false;
            _suppressAutoSave = false;
        }
    }

    // -------- Auto-save for header changes (existing tournaments only) --------

    partial void OnNameChanged(string value)
    {
        if (_suppressAutoSave || _isInitialSave || Tournament is null) return;
        Tournament.Name = (value ?? "").Trim();
        _autoSave.ScheduleTournament(Tournament, latest => latest.Name = Tournament.Name);
    }

    partial void OnPasswordChanged(string value)
    {
        if (_suppressAutoSave || _isInitialSave || Tournament is null) return;
        Tournament.PasswordPlain = (value ?? "").Trim();
        _autoSave.ScheduleTournament(Tournament, latest => latest.PasswordPlain = Tournament.PasswordPlain);
    }

    // -------- Initial save (new tournaments) --------

    [RelayCommand]
    public async Task SaveNewAsync()
    {
        if (Tournament is null || !_isInitialSave) return;

        if (string.IsNullOrWhiteSpace(Name))     { ErrorMessage = "Name is required."; return; }
        if (string.IsNullOrWhiteSpace(Password)) { ErrorMessage = "Password is required."; return; }

        ErrorMessage = "";
        IsLoading = true;
        try
        {
            Tournament.Name = Name.Trim();
            Tournament.PasswordPlain = Password.Trim();

            // Header first so we have a row to attach fencers to.
            await _sheets.UpsertTournamentHeaderAsync(Tournament);

            // Persist any roster the user entered before saving.
            for (int i = 0; i < Fencers.Count; i++)
            {
                Fencers[i].Fencer.OrderIndex = i;
                await _sheets.UpsertTournamentFencerAsync(Tournament.Id, Fencers[i].Fencer);
            }

            _isInitialSave = false;
            _cache.InvalidateTournaments();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Save failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // -------- Roster operations --------

    [RelayCommand]
    public async Task AddFencerAsync()
    {
        if (Tournament is null || !CanAddFencers) return;
        var n = (NewFencerName ?? "").Trim();
        if (n.Length == 0) return;

        var fencer = new TournamentFencer { Name = n, OrderIndex = Fencers.Count };
        Fencers.Add(new TournamentFencerRow(fencer));
        NewFencerName = "";
        NotifyStateChanged();

        if (!_isInitialSave)
        {
            try { await _sheets.UpsertTournamentFencerAsync(Tournament.Id, fencer); }
            catch (Exception ex) { ErrorMessage = $"Add failed: {ex.Message}"; }
        }
    }

    [RelayCommand]
    public async Task RemoveFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || !CanRemoveFencers) return;

        Fencers.Remove(row);
        NotifyStateChanged();

        if (!_isInitialSave)
        {
            try { await _sheets.DeleteTournamentFencerAsync(Tournament.Id, row.Fencer.Id); }
            catch (Exception ex) { ErrorMessage = $"Remove failed: {ex.Message}"; }
        }
    }

    [RelayCommand]
    public async Task WithdrawFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || _isInitialSave) return;
        row.Fencer.IsWithdrawn = !row.Fencer.IsWithdrawn;
        row.RaiseStatusChanged();
        NotifyStateChanged();

        try { await _sheets.UpsertTournamentFencerAsync(Tournament.Id, row.Fencer); }
        catch (Exception ex) { ErrorMessage = $"Update failed: {ex.Message}"; }
    }

    // -------- Lifecycle --------

    [RelayCommand]
    public async Task StartTournamentAsync()
    {
        if (Tournament is null || !IsExisting || !IsSetupState) return;

        // Always re-check; the page also alerts, but the banner is the source of truth.
        if (ActiveFencerCount < MinFencersToStart)
        {
            ErrorMessage =
                $"Cannot start: need at least {MinFencersToStart} active fencers " +
                $"(currently {ActiveFencerCount}).";
            return;
        }

        ErrorMessage = "";
        IsStarting = true;
        try
        {
            var activeFencers = Fencers
                .Where(r => !r.Fencer.IsWithdrawn)
                .Select(r => r.Fencer)
                .ToList();

            var pools = TournamentEngine.BuildPools(activeFencers, new Random());

            // Bulk-append in two HTTP calls regardless of size.
            await _sheets.AppendPoolsAsync(Tournament.Id, pools);
            await _sheets.AppendMatchesAsync(Tournament.Id, pools.SelectMany(p => p.Matches).ToList());

            // Transition state — header save uses CAS so it's safe even if someone else read meanwhile.
            Tournament.Pools = pools;
            Tournament.State = TournamentState.PoolsInProgress;
            await _sheets.UpsertTournamentHeaderAsync(Tournament);

            _cache.InvalidateTournaments();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Start failed: {ex.Message}"; }
        finally { IsStarting = false; }
    }

    [RelayCommand]
    public async Task DeleteTournamentAsync()
    {
        if (Tournament is null || _isInitialSave) return;
        IsLoading = true;
        try
        {
            await _sheets.DeleteTournamentAsync(Tournament.Id);
            _cache.InvalidateTournaments();
        }
        catch (Exception ex) { ErrorMessage = $"Delete failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsExisting));
        OnPropertyChanged(nameof(IsSetupState));
        OnPropertyChanged(nameof(CanAddFencers));
        OnPropertyChanged(nameof(CanRemoveFencers));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ActiveFencerCount));
        OnPropertyChanged(nameof(FencerCountText));
        OnPropertyChanged(nameof(StartHintText));
    }
}