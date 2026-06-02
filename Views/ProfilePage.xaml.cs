using JustAnotherHemaClub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _vm;
    private readonly IServiceProvider _services;

    public ProfilePage(ProfileViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnOpenGdprTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<GdprPage>());

    private async void OnOpenLiabilityTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<LiabilityPage>());
}