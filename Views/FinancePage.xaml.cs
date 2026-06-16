using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class FinancePage : ContentPage
{
    private readonly FinanceViewModel _vm;
    private readonly ICacheControl _cache;

    public FinancePage(FinanceViewModel vm, ICacheControl cache)
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
        _cache.InvalidateFencers();
        _cache.InvalidateTrainings();
        _cache.InvalidateExpenses();
        _cache.InvalidateIncomes();
        _cache.InvalidatePayments();
        _cache.InvalidatePrices();
        await _vm.LoadAsync(showSpinner: true);
    }
}