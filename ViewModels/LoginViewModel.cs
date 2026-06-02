using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IGoogleSheetsService _sheets;
    private readonly IBiometricService _biometrics;
    private readonly IServiceProvider _services;

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string? error;

    [ObservableProperty] private bool keepLoggedIn;
    [ObservableProperty] private bool useBiometric;

    [ObservableProperty] private bool canUseBiometric;
    [ObservableProperty] private bool canTryBiometricLogin;

    [ObservableProperty] private bool isSilentLoggingIn;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool ShowSignInForm => !IsSilentLoggingIn;

    public LoginViewModel(AuthService auth, IGoogleSheetsService sheets,
                          IBiometricService biometrics, IServiceProvider services)
    {
        _auth = auth;
        _sheets = sheets;
        _biometrics = biometrics;
        _services = services;
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsSilentLoggingInChanged(bool value) => OnPropertyChanged(nameof(ShowSignInForm));

    partial void OnKeepLoggedInChanged(bool value)
    {
        if (!value) UseBiometric = false;
    }

    /// <summary>Call from the page's OnAppearing.</summary>
    public async Task InitializeAsync()
    {
        CanUseBiometric = await _biometrics.IsAvailableAsync();
        CanTryBiometricLogin = CanUseBiometric && _auth.HasPersistedCredentials;

        // If we already have stored credentials, try to log in silently.
        // Biometric prompt is only shown if the user opted into it earlier.
        if (_auth.HasPersistedCredentials)
            await TryBiometricLoginAsync();
    }

    [RelayCommand]
    private async Task TryBiometricLoginAsync()
    {
        Error = null;
        try
        {
            var (u, h, bioEnabled) = await _auth.TryGetPersistedAsync();
            if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(h)) return;

            if (bioEnabled)
            {
                var ok = await _biometrics.AuthenticateAsync("Sign in to JAHC Manager");
                if (!ok) return;
            }
            else
            {
                // Pure silent login - show the "Logging in, please wait" message.
                IsSilentLoggingIn = true;
            }

            if (await _auth.LoginWithStoredHashAsync(u, h))
            {
                EnterShell();
            }
            else
            {
                _auth.ClearPersistedCredentials();
                Error = "Saved sign-in is no longer valid. Please log in again.";
            }
        }
        catch (Exception ex) { Error = $"Auto sign-in failed: {ex.Message}"; }
        finally { IsSilentLoggingIn = false; }
    }

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
            {
                if (KeepLoggedIn)
                {
                    var hash = AuthService.Hash(Password);
                    var enableBio = UseBiometric && CanUseBiometric;
                    if (enableBio)
                    {
                        // Verify the user can satisfy biometrics now, otherwise don't gate next launch.
                        enableBio = await _biometrics.AuthenticateAsync("Confirm biometrics to enable quick sign-in");
                    }
                    await _auth.PersistCredentialsAsync(Username.Trim(), hash, enableBio);
                    _auth.MarkPersisted(true);
                }
                else
                {
                    _auth.ClearPersistedCredentials();
                }

                EnterShell();
            }
            else
            {
                Error = "Incorrect username or password. Please try again.";
            }
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