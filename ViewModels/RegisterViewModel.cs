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

    [ObservableProperty] private string loginUsername = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string confirmPassword = "";

    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;

    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;

    public RegisterViewModel(GoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = StatusMessage = null;

        // Validate
        string? validation =
            string.IsNullOrWhiteSpace(Name)            ? "Name is required." :
            string.IsNullOrWhiteSpace(Email)           ? "Email is required." :
            string.IsNullOrWhiteSpace(LoginUsername)   ? "Login username is required." :
            string.IsNullOrWhiteSpace(Password) || Password.Length < 4
                                                       ? "Password must be at least 4 characters." :
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

            bool taken = existingFencers.Any(f =>
                !string.IsNullOrEmpty(f.Username) &&
                string.Equals(f.Username.Trim(), desiredUser, StringComparison.OrdinalIgnoreCase));

            if (taken)
            {
                ErrorMessage = "That username is already taken. Please choose another.";
                await ShowAsync("Username taken", ErrorMessage);
                return;
            }

            await _sheets.AddFencerAsync(new Fencer
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = desiredUser,
                PasswordHash = AuthService.Hash(Password),
                Name = Name.Trim(),
                Email = Email.Trim(),
                Active = true,
                IsStudent = IsStudent,
                GdprAccepted = GdprAccepted,
                LiabilityAccepted = LiabilityAccepted,
                IsInstructor = false
            });

            StatusMessage = $"Welcome, {Name}! You can now log in with \"{desiredUser}\".";

            // Reset form
            Name = Email = LoginUsername = Password = ConfirmPassword = "";
            IsStudent = GdprAccepted = LiabilityAccepted = false;

            await ShowAsync("Registration complete", StatusMessage);
            await GoBackAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await ShowAsync("Registration failed", ex.ToString());
        }
        finally { IsBusy = true; IsBusy = false; }
    }

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