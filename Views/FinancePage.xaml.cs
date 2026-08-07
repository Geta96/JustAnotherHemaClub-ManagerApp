using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class FinancePage : ContentPage
{
    private readonly FinanceViewModel _vm;
    private readonly ICacheControl _cache;
    private bool _loadedOnce;

    public FinancePage(FinanceViewModel vm, ICacheControl cache)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Let the Shell navigation transition paint this frame before we touch
        // the cache/network, so the page appears instantly (with its spinner)
        // instead of the previous page appearing to freeze.
        await Task.Yield();
        // Show the centered spinner on the very first load (nothing on screen
        // yet); subsequent silent re-loads reuse the throttle and don't flash it.
        await _vm.LoadAsync(showSpinner: !_loadedOnce);
        _loadedOnce = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Abandon any in-flight refresh so it doesn't mutate the detached
        // CollectionView after we've navigated away (crashes on Android when the
        // RecyclerView has already been torn down).
        _vm.CancelLoad();
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateFencers();
        _cache.InvalidateTrainings();
        _cache.InvalidateExpenses();
        _cache.InvalidateIncomes();
        _cache.InvalidatePayments();
        _cache.InvalidatePrices();
        await _vm.LoadAsync(showSpinner: true);
    }
}