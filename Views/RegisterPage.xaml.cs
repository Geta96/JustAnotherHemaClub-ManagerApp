using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _vm;
    private readonly IServiceProvider _services;

    public RegisterPage(RegisterViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _services = services;
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        try
        {
            await _vm.RegisterCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Register threw", ex.ToString(), "OK");
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    private async void OnOpenGdprTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<GdprPage>());

    private async void OnOpenLiabilityTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<LiabilityPage>());
}