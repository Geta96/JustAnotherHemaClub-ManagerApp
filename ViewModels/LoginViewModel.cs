using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IGoogleSheetsService _sheets;
    private readonly IServiceProvider _services;

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string? error;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public LoginViewModel(AuthService auth, IGoogleSheetsService sheets, IServiceProvider services)
    {
        _auth = auth;
        _sheets = sheets;
        _services = services;
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task LoginAsync()
    {
        Error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                Error = "Please enter your username and password.";
                return;
            }

            if (await _auth.LoginAsync(Username, Password))
                EnterShell();
            else
                Error = "Incorrect username or password. Please try again.";
        }
        catch (Exception ex) { Error = $"Login failed: {ex.Message}"; }
    }

    [RelayCommand]
    private Task UseAsGuestAsync()
    {
        Error = null;
        try
        {
            _auth.LoginAsGuest();
            EnterShell();
        }
        catch (Exception ex) { Error = ex.Message; }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task GoToRegisterAsync()
    {
        var page = _services.GetRequiredService<Views.RegisterPage>();
        await Application.Current!.MainPage!.Navigation.PushAsync(page);
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        Error = null;
        var page = Application.Current?.MainPage;
        if (page is null) return;

        try
        {
            var user = await page.DisplayPromptAsync(
                "Reset password",
                "Enter your username:",
                accept: "Next", cancel: "Cancel",
                placeholder: "username",
                initialValue: Username ?? "");
            if (string.IsNullOrWhiteSpace(user)) return;

            var email = await page.DisplayPromptAsync(
                "Reset password",
                "Enter the email address on file for this account:",
                accept: "Next", cancel: "Cancel",
                placeholder: "you@example.com",
                keyboard: Keyboard.Email);
            if (string.IsNullOrWhiteSpace(email)) return;

            var fencers = await _sheets.GetFencersAsync();
            var match = fencers.FirstOrDefault(f =>
                !string.IsNullOrWhiteSpace(f.Username) &&
                string.Equals(f.Username.Trim(), user.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(f.Email) &&
                string.Equals(f.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                await page.DisplayAlert(
                    "No match",
                    "We couldn't find an account with that username and email. " +
                    "Ask an instructor if you need help recovering your account.",
                    "OK");
                return;
            }

            var newPassword = await page.DisplayPromptAsync(
                "Reset password",
                $"Set a new password for @{match.Username}:",
                accept: "Set password", cancel: "Cancel",
                placeholder: "new password");
            if (string.IsNullOrWhiteSpace(newPassword)) return;

            if (newPassword.Length < 6)
            {
                await page.DisplayAlert("Password too short",
                    "Please choose at least 6 characters.", "OK");
                return;
            }

            var confirm = await page.DisplayPromptAsync(
                "Reset password",
                "Re-enter the new password to confirm:",
                accept: "Confirm", cancel: "Cancel",
                placeholder: "confirm new password");
            if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
            {
                await page.DisplayAlert("Mismatch", "The two passwords did not match.", "OK");
                return;
            }

            match.PasswordHash = AuthService.Hash(newPassword);
            await _sheets.UpsertFencerAsync(match);

            await page.DisplayAlert("Password updated",
                "You can now sign in with the new password.", "OK");

            Username = match.Username ?? "";
            Password = "";
        }
        catch (Exception ex) { Error = $"Could not reset password: {ex.Message}"; }
    }

    private void EnterShell()
    {
        var shell = _services.GetRequiredService<AppShell>();
        Application.Current!.MainPage = shell;
    }
}