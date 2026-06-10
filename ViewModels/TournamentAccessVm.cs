using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentAccessVm : ObservableObject
{
    private readonly TournamentSession _session;
    private Tournament? _tournament;

    [ObservableProperty] private string tournamentName = "";
    [ObservableProperty] private string tournamentInfo = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool isPasswordPromptVisible;
    [ObservableProperty] private string errorMessage = "";

    /// <summary>Page sets this so it can pop itself once a role is chosen.</summary>
    public Action<TournamentRole>? OnAccessGranted { get; set; }

    public TournamentAccessVm(TournamentSession session) => _session = session;

    public void Init(Tournament tournament)
    {
        _tournament = tournament;
        TournamentName = string.IsNullOrWhiteSpace(tournament.Name) ? "(unnamed)" : tournament.Name;
        TournamentInfo = $"{tournament.Fencers.Count} fencers - {StateLabel(tournament.State)}";
        Password = "";
        IsPasswordPromptVisible = false;
        ErrorMessage = "";
    }

    [RelayCommand]
    private void EnterAsFencer()
    {
        if (_tournament is null) return;
        _session.Open(_tournament, TournamentRole.Fencer);
        OnAccessGranted?.Invoke(TournamentRole.Fencer);
    }

    [RelayCommand]
    private void ShowOrganiserPrompt()
    {
        IsPasswordPromptVisible = true;
        ErrorMessage = "";
    }

    [RelayCommand]
    private void CancelOrganiserPrompt()
    {
        IsPasswordPromptVisible = false;
        Password = "";
        ErrorMessage = "";
    }

    [RelayCommand]
    private void ConfirmOrganiser()
    {
        if (_tournament is null) return;
        var expected = (_tournament.PasswordPlain ?? "").Trim();
        var entered  = (Password ?? "").Trim();
        if (entered.Length == 0 || entered != expected)
        {
            ErrorMessage = "Incorrect password.";
            return;
        }
        _session.Open(_tournament, TournamentRole.Organiser);
        OnAccessGranted?.Invoke(TournamentRole.Organiser);
    }

    private static string StateLabel(TournamentState s) => s switch
    {
        TournamentState.Setup                 => "Setup",
        TournamentState.PoolsInProgress       => "Pools in progress",
        TournamentState.PoolsClosed           => "Pools closed",
        TournamentState.EliminationInProgress => "Elimination in progress",
        TournamentState.Finished              => "Finished",
        _                                     => "-"
    };
}