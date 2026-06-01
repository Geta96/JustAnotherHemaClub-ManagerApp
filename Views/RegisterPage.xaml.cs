using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _vm;

    public RegisterPage(RegisterViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
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
}