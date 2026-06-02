using JustAnotherHemaClub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Views;

public partial class ProfilePage : ContentPage
{
    private readonly IServiceProvider _services;

    public ProfilePage(ProfileViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = vm;
        _services = services;
    }

    private async void OnOpenGdprTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<GdprPage>());

    private async void OnOpenLiabilityTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<LiabilityPage>());
}