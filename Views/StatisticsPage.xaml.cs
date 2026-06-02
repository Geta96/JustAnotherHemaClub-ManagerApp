using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _vm;
    private readonly ICacheControl _cache;

    public StatisticsPage(StatisticsViewModel vm, ICacheControl cache)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateAll();
        await _vm.LoadAsync(showSpinner: true);
    }
}