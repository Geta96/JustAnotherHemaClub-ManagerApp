using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TournamentEditorPage : ContentPage
{
    private readonly TournamentEditorVm _vm;
    private readonly TournamentSession _session;
    private readonly IServiceProvider _services;

    public TournamentEditorPage(TournamentEditorVm vm,
                                TournamentSession session,
                                IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _session = session;
        _services = services;
    }

    public void PrepareForNew()
    {
        Title = "New Tournament";
        _vm.InitNew();
    }

    public async Task PrepareForExistingAsync(string tournamentId)
    {
        Title = "Edit Tournament";
        await _vm.InitExistingAsync(tournamentId);
    }

    private async void OnStartTournamentClicked(object? sender, EventArgs e)
    {
        if (_vm.ActiveFencerCount < TournamentEditorVm.MinFencersToStart)
        {
            await DisplayAlert(
                "Cannot start tournament",
                $"You need at least {TournamentEditorVm.MinFencersToStart} active fencers to start.\n\n" +
                $"Currently {_vm.ActiveFencerCount} active.",
                "OK");
            return;
        }

        var confirm = await DisplayAlert(
            "Start tournament",
            $"This will lock the roster and create the matches. Continue?",
            "Start", "Cancel");
        if (!confirm) return;

        await _vm.StartTournamentCommand.ExecuteAsync(null);

        if (_vm.Tournament?.State == TournamentState.PoolsInProgress)
        {
            _session.Open(_vm.Tournament, TournamentRole.Organiser);

            var hub = _services.GetRequiredService<TournamentHubPage>();
            Navigation.InsertPageBefore(hub, this);
            await Navigation.PopAsync(animated: false);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_vm.Tournament is null) return;
        var confirm = await DisplayAlert(
            "Delete tournament",
            $"Delete '{_vm.Tournament.Name}'? This removes all pools, matches and standings.",
            "Delete", "Cancel");
        if (!confirm) return;

        await _vm.DeleteTournamentCommand.ExecuteAsync(null);
        await Navigation.PopAsync();
    }

    private async void OnRestartClicked(object? sender, EventArgs e)
    {
        if (_vm.Tournament is null) return;

        // Ask for organiser password
        var enteredPassword = await DisplayPromptAsync(
            "Restart tournament",
            "This will delete ALL matches, standings, and pool assignments.\n\n" +
            "Enter the organiser password to confirm:",
            accept: "Restart", cancel: "Cancel",
            placeholder: "password",
            maxLength: 64);
        if (string.IsNullOrWhiteSpace(enteredPassword)) return;

        // Verify password
        if (!string.Equals(enteredPassword.Trim(), _vm.Tournament.PasswordPlain?.Trim(), StringComparison.Ordinal))
        {
            await DisplayAlert("Incorrect password", "The password you entered is incorrect.", "OK");
            return;
        }

        // Final confirmation
        var confirm = await DisplayAlert(
            "Are you sure?",
            "This cannot be undone. All match scores, pool standings, bracket results, " +
            "and final standings will be permanently deleted.\n\n" +
            "The roster will be kept and all fencers reinstated.",
            "Yes, restart", "Cancel");
        if (!confirm) return;

        await _vm.RestartTournamentCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Roster Withdraw / Reinstate button. During an active tournament, withdrawing
    /// walks-over every unfinished match this fencer is in, so we confirm first.
    /// </summary>
    private async void OnWithdrawFencerClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not TournamentFencerRow row) return;

        bool willBeWithdrawn = !row.IsWithdrawn;
        if (willBeWithdrawn && _vm.Tournament is not null &&
            _vm.Tournament.State is not TournamentState.Setup and not TournamentState.Finished)
        {
            var confirm = await DisplayAlert(
                "Withdraw fencer",
                $"Withdraw {row.Name}?\n\n" +
                "All of their remaining matches will end as 0–0 walkovers and their opponents will win automatically. " +
                "Already-finished matches keep their scores.",
                "Withdraw", "Cancel");
            if (!confirm) return;
        }

        await _vm.WithdrawFencerCommand.ExecuteAsync(row);
    }

    /// <summary>"Assign…" button on an unassigned fencer chip.</summary>
    private async void OnAssignFencerClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not EditorPoolFencerVm chip) return;
        await ShowMovePickerAsync(chip);
    }

    /// <summary>"Move…" button on a fencer chip inside a draft pool.</summary>
    private async void OnMoveFencerClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not EditorPoolFencerVm chip) return;
        await ShowMovePickerAsync(chip);
    }

    private async Task ShowMovePickerAsync(EditorPoolFencerVm chip)
    {
        if (_vm.Tournament is null) return;

        // Build the action sheet: every existing pool + Unassigned + Cancel.
        var pools = _vm.DraftPools.ToList();
        var labels = new List<string>(pools.Count + 1);
        foreach (var p in pools) labels.Add(p.Title);
        labels.Add("Unassigned");

        var choice = await DisplayActionSheet(
            $"Move {chip.Name} to…",
            "Cancel",
            null,
            labels.ToArray());
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        string targetPoolId = choice == "Unassigned"
            ? ""
            : pools.First(p => p.Title == choice).PoolId;

        await _vm.MoveFencerToPoolAsync(chip.FencerId, targetPoolId);
    }

    private async void OnCreateTournamentClicked(object? sender, EventArgs e)
    {
        await _vm.SaveNewCommand.ExecuteAsync(null);

        // After successful save, navigate to the Tournament Hub so the user
        // can manage the roster / start from there (via the ? editor button).
        if (_vm.IsExisting && _vm.Tournament is not null)
        {
            _session.Open(_vm.Tournament, TournamentRole.Organiser);

            var hub = _services.GetRequiredService<TournamentHubPage>();
            Navigation.InsertPageBefore(hub, this);
            await Navigation.PopAsync(animated: false);
        }
    }
}