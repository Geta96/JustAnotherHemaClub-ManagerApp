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

    /// <summary>True when there is a status message to show — used to collapse the label otherwise.</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>True when there is an error message to show — used to collapse the label otherwise.</summary>
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// True only once both fields are non-empty and don't match. Used to show a
    /// live red hint under the confirm-email field without nagging on first focus.
    /// </summary>
    public bool EmailMismatch => RegistrationValidator.ComputeEmailMismatch(Email, ConfirmEmail);

    /// <summary>True only once both password fields are non-empty and don't match.</summary>
    public bool PasswordMismatch => RegistrationValidator.ComputePasswordMismatch(Password, ConfirmPassword);

    public RegisterViewModel(GoogleSheetsService sheets) => _sheets = sheets;

    partial void OnEmailChanged(string value)           => OnPropertyChanged(nameof(EmailMismatch));
    partial void OnConfirmEmailChanged(string value)    => OnPropertyChanged(nameof(EmailMismatch));
    partial void OnPasswordChanged(string value)        => OnPropertyChanged(nameof(PasswordMismatch));
    partial void OnConfirmPasswordChanged(string value) => OnPropertyChanged(nameof(PasswordMismatch));

    partial void OnStatusMessageChanged(string? value)  => OnPropertyChanged(nameof(HasStatusMessage));
    partial void OnErrorMessageChanged(string? value)   => OnPropertyChanged(nameof(HasErrorMessage));

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = StatusMessage = null;

        var trimmedEmail = (Email ?? "").Trim();

        // Validate (shared, host-agnostic rules live in Core).
        var validation = RegistrationValidator.Validate(
            Name, Email, ConfirmEmail, LoginUsername,
            Password, ConfirmPassword, GdprAccepted, LiabilityAccepted);

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

            if (RegistrationValidator.IsDuplicateUsername(desiredUser, existingFencers))
            {
                ErrorMessage = "That username is already taken. Please choose another.";
                await ShowAsync("Username taken", ErrorMessage);
                return;
            }

            if (RegistrationValidator.IsDuplicateEmail(trimmedEmail, existingFencers))
            {
                ErrorMessage = "That email is already registered. Please use a different email or log in.";
                await ShowAsync("Email already registered", ErrorMessage);
                return;
            }

            await _sheets.AddFencerAsync(RegistrationValidator.BuildRegistrationFencer(
                name: Name,
                email: trimmedEmail,
                username: desiredUser,
                password: Password,
                isStudent: IsStudent,
                gdprAccepted: GdprAccepted,
                liabilityAccepted: LiabilityAccepted));

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

    [RelayCommand]
    private Task BackToLoginAsync() => GoBackAsync();

    private static async Task GoBackAsync()
    {
        var nav = Services.AppNavigationHelper.RootPage?.Navigation;
        if (nav is not null && nav.NavigationStack.Count > 1)
            await nav.PopAsync();
    }

    private static Task ShowAsync(string title, string message)
    {
        var page = Services.AppNavigationHelper.RootPage;
        return page is null ? Task.CompletedTask : page.DisplayAlert(title, message, "OK");
    }
}