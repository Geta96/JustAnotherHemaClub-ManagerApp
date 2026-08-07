using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _vm;
    private readonly ICacheControl _cache;
    private bool _loadedOnce;

    public StatisticsPage(StatisticsViewModel vm, ICacheControl cache)
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

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateFencers();
        _cache.InvalidateTrainings();
        _cache.InvalidateExpenses();
        _cache.InvalidateMonthNotes();
        _cache.InvalidateIndividualLessons();
        _cache.InvalidatePayments();
        await _vm.LoadAsync(showSpinner: true);
    }
}