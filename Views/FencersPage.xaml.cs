using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class FencersPage : ContentPage
{
    private readonly FencersViewModel _vm;
    private readonly ICacheControl _cache;
    private bool _loadedOnce;

    public FencersPage(FencersViewModel vm, ICacheControl cache)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Let the Shell navigation transition paint before touching the cache.
        await Task.Yield();
        // Show the centered spinner on the first load; stay silent afterwards.
        await _vm.LoadAsync(showSpinner: !_loadedOnce);
        _loadedOnce = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Abandon any in-flight refresh so it doesn't mutate the detached
        // CollectionView after we've navigated away.
        _vm.CancelLoad();
    }

    private async void OnPromoteClicked(object? sender, EventArgs e)
    {
        if (_vm.SelectedFencer is null) return;

        var confirm = await DisplayAlert(
            "Promote to instructor",
            $"Promote {_vm.SelectedFencer.Name} (@{_vm.SelectedFencer.Username}) to instructor?",
            "Promote",
            "Cancel");

        if (!confirm) return;

        var error = await _vm.PromoteSelectedAsync("", "");
        if (error is null)
        {
            await DisplayAlert("Done",
                $"{_vm.SelectedFencer.Name} is now an instructor.",
                "OK");
            await _vm.LoadAsync();
        }
        else
        {
            await DisplayAlert("Couldn't promote", error, "OK");
        }
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateFencers();
        _cache.InvalidateTrainings();
        _cache.InvalidatePayments();
        await _vm.LoadAsync(showSpinner: true);
    }
}