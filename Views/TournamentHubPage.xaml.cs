using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TournamentHubPage : ContentPage
{
    private readonly TournamentHubViewModel _vm;
    private readonly TournamentSession _session;
    private readonly IServiceProvider _services;

    public TournamentHubPage(TournamentHubViewModel vm,
                             TournamentSession session,
                             IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _session = session;
        _services = services;

        _vm.PoolsVm.MatchSelected += OnPoolMatchSelected;
        _vm.ElimVm.MatchSelected  += OnElimMatchSelected;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_session.Current is null) { await Navigation.PopAsync(); return; }
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Stop the polling timer when the hub is not visible — Match page will start its own.
        _vm.PoolsVm.StopPolling();
    }

    private async void OnManageRosterClicked(object? sender, EventArgs e)
    {
        if (_session.Current is null) return;
        var page = _services.GetRequiredService<TournamentEditorPage>();
        await page.PrepareForExistingAsync(_session.Current.Id);
        await Navigation.PushAsync(page);
    }

    private async void OnPoolMatchSelected(PoolMatchRowVm row)
    {
        if (row is null) return;
        await PushMatchAsync(row.Match);
    }

    private async void OnElimMatchSelected(Match match)
    {
        if (match is null) return;
        await PushMatchAsync(match);
    }

    private async Task PushMatchAsync(Match match)
    {
        var page = _services.GetRequiredService<MatchPage>();
        await page.PrepareForMatchAsync(match.Id);
        await Navigation.PushAsync(page);
    }
}