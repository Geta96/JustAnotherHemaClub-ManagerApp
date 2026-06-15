using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly GoogleSheetsService _sheets;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string confirmEmail = "";

    [ObservableProperty] private string loginUsername = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string confirmPassword = "";

    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;

    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;

    /// <summary>
    /// True only once both fields are non-empty and don't match. Used to show a
    /// live red hint under the confirm-email field without nagging on first focus.
    /// </summary>
    public bool EmailMismatch =>
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(ConfirmEmail) &&
        !string.Equals(Email.Trim(), ConfirmEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True only once both password fields are non-empty and don't match.</summary>
    public bool PasswordMismatch =>
        !string.IsNullOrEmpty(Password) &&
        !string.IsNullOrEmpty(ConfirmPassword) &&
        Password != ConfirmPassword;

    public RegisterViewModel(GoogleSheetsService sheets) => _sheets = sheets;

    partial void OnEmailChanged(string value)           => OnPropertyChanged(nameof(EmailMismatch));
    partial void OnConfirmEmailChanged(string value)    => OnPropertyChanged(nameof(EmailMismatch));
    partial void OnPasswordChanged(string value)        => OnPropertyChanged(nameof(PasswordMismatch));
    partial void OnConfirmPasswordChanged(string value) => OnPropertyChanged(nameof(PasswordMismatch));

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = StatusMessage = null;

        var trimmedEmail        = (Email ?? "").Trim();
        var trimmedConfirmEmail = (ConfirmEmail ?? "").Trim();

        // Validate
        string? validation =
            string.IsNullOrWhiteSpace(Name)            ? "Name is required." :
            string.IsNullOrWhiteSpace(trimmedEmail)    ? "Email is required." :
            !IsValidEmail(trimmedEmail)                ? "Please enter a valid email address (e.g. you@example.com)." :
            string.IsNullOrWhiteSpace(trimmedConfirmEmail)
                                                       ? "Please confirm your email address." :
            !string.Equals(trimmedEmail, trimmedConfirmEmail, StringComparison.OrdinalIgnoreCase)
                                                       ? "Email addresses do not match." :
            string.IsNullOrWhiteSpace(LoginUsername)   ? "Login username is required." :
            !IsStrongPassword(Password)                ? "Password must be at least 6 characters and include at least one number." :
            Password != ConfirmPassword                ? "Passwords do not match." :
            !GdprAccepted                              ? "You must accept the GDPR policy." :
            !LiabilityAccepted                         ? "You must accept the liability statement." :
            null;

        if (validation is not null)
        {
            ErrorMessage = validation;
            await ShowAsync("Cannot register", validation);
            return;
        }

        var desiredUser = LoginUsername.Trim();

        try
        {
            IsBusy = true;

            var existingFencers = await _sheets.GetFencersAsync();

            bool usernameTaken = existingFencers.Any(f =>
                !string.IsNullOrEmpty(f.Username) &&
                string.Equals(f.Username.Trim(), desiredUser, StringComparison.OrdinalIgnoreCase));

            if (usernameTaken)
            {
                ErrorMessage = "That username is already taken. Please choose another.";
                await ShowAsync("Username taken", ErrorMessage);
                return;
            }

            bool emailTaken = existingFencers.Any(f =>
                !string.IsNullOrEmpty(f.Email) &&
                string.Equals(f.Email.Trim(), trimmedEmail, StringComparison.OrdinalIgnoreCase));

            if (emailTaken)
            {
                ErrorMessage = "That email is already registered. Please use a different email or log in.";
                await ShowAsync("Email already registered", ErrorMessage);
                return;
            }

            await _sheets.AddFencerAsync(new Fencer
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = desiredUser,
                PasswordHash = AuthService.Hash(Password),
                Name = Name.Trim(),
                Email = trimmedEmail,
                Active = true,
                IsStudent = IsStudent,
                GdprAccepted = GdprAccepted,
                LiabilityAccepted = LiabilityAccepted,
                IsInstructor = false
            });

            StatusMessage = $"Welcome, {Name}! You can now log in with \"{desiredUser}\".";

            // Reset form
            Name = Email = ConfirmEmail = LoginUsername = Password = ConfirmPassword = "";
            IsStudent = GdprAccepted = LiabilityAccepted = false;

            await ShowAsync("Registration complete", StatusMessage);
            await GoBackAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await ShowAsync("Registration failed", ex.ToString());
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Stricter than <see cref="System.Net.Mail.MailAddress"/> alone: also requires
    /// a real-looking TLD (a dot in the host with ≥2 chars after it), so inputs
    /// like "a@b" or "user@localhost" are rejected.
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (email.Contains(' '))             return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            if (addr.Address != email) return false;

            var atIdx = email.LastIndexOf('@');
            if (atIdx < 1) return false;

            var host   = email[(atIdx + 1)..];
            var dotIdx = host.LastIndexOf('.');
            if (dotIdx < 1) return false;                          // need a dot in the host
            if (host.Length - dotIdx - 1 < 2) return false;        // TLD ≥ 2 chars

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>At least 6 characters and at least one digit.</summary>
    private static bool IsStrongPassword(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 6 &&
        password.Any(char.IsDigit);

    [RelayCommand]
    private Task BackToLoginAsync() => GoBackAsync();

    private static async Task GoBackAsync()
    {
        var nav = Application.Current?.MainPage?.Navigation;
        if (nav is not null && nav.NavigationStack.Count > 1)
            await nav.PopAsync();
    }

    private static Task ShowAsync(string title, string message)
    {
        var page = Application.Current?.MainPage;
        return page is null ? Task.CompletedTask : page.DisplayAlert(title, message, "OK");
    }
}