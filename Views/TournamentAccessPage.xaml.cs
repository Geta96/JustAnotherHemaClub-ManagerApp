using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TournamentAccessPage : ContentPage
{
    private readonly TournamentAccessVm _vm;
    private TaskCompletionSource<TournamentRole?>? _tcs;

    public TournamentAccessPage(TournamentAccessVm vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>
    /// Pushes this page and returns once the user picks a role or backs out.
    /// On success the page is left on top of the navigation stack so the caller
    /// can decide how to replace it (e.g. atomically swap in the hub page).
    /// On cancel (hardware back) the page is already popped by the system.
    /// </summary>
    public async Task<TournamentRole?> ShowAsync(INavigation nav, Tournament tournament)
    {
        _tcs = new TaskCompletionSource<TournamentRole?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _vm.Init(tournament);
        _vm.OnAccessGranted = role => _tcs?.TrySetResult(role);

        await nav.PushAsync(this);
        return await _tcs.Task;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // If we got here without a role being set, the user cancelled (hardware back).
        _tcs?.TrySetResult(null);
    }
}