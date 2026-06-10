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
        var confirm = await DisplayAlert(
            "Start tournament",
            $"This will create the pools and lock the roster. Continue?",
            "Start", "Cancel");
        if (!confirm) return;

        await _vm.StartTournamentCommand.ExecuteAsync(null);

        if (_vm.Tournament?.State == TournamentState.PoolsInProgress)
        {
            // We're the organiser by definition (we just started it). Open the hub
            // and remove ourselves from the stack so 'back' returns to the list.
            _session.Open(_vm.Tournament, TournamentRole.Organiser);

            var hub = _services.GetRequiredService<TournamentHubPage>();
            Navigation.InsertPageBefore(hub, this);
            await Navigation.PopAsync();
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
}