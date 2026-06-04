using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class WeeklyPage : ContentPage
{
    private readonly WeeklyViewModel _vm;
    private readonly ICacheControl _cache;
    private readonly IServiceProvider _services;

    public WeeklyPage(WeeklyViewModel vm, ICacheControl cache, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateRecurringTrainings();
        await _vm.LoadAsync(showSpinner: true);
    }

    private async void OnAddWeeklyTrainingClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<NewTrainingPage>();
        page.PrepareForWeekly();
        await Navigation.PushAsync(page);
    }
}