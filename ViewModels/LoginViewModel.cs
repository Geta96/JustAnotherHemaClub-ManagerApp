using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IServiceProvider _services;

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string? error;

    public LoginViewModel(AuthService auth, IServiceProvider services)
    {
        _auth = auth;
        _services = services;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        Error = null;
        try
        {
            if (await _auth.LoginAsync(Username, Password))
                EnterShell();
            else
                Error = "Invalid credentials";
        }
        catch (Exception ex) { Error = ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task UseAsGuestAsync()
    {
        Error = null;
        try
        {
            _auth.LoginAsGuest();
            EnterShell();
        }
        catch (Exception ex) { Error = ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task GoToRegisterAsync()
    {
        var page = _services.GetRequiredService<Views.RegisterPage>();
        await Application.Current!.MainPage!.Navigation.PushAsync(page);
    }

    private void EnterShell()
    {
        var shell = _services.GetRequiredService<AppShell>();
        Application.Current!.MainPage = shell;
    }
}