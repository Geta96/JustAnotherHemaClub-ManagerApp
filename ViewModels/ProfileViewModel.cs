using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IGoogleSheetsService _sheets;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool isInstructor;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isGuest;

    public bool IsLoggedInFencer => !IsGuest && _auth.CurrentFencer is not null;

    public ProfileViewModel(AuthService auth, IGoogleSheetsService sheets)
    {
        _auth = auth;
        _sheets = sheets;
    }

    [RelayCommand]
    public Task LoadAsync()
    {
        StatusMessage = null;
        IsGuest = _auth.IsGuest;

        var f = _auth.CurrentFencer;
        if (f is null)
        {
            Name = IsGuest ? "Guest" : "";
            Username = IsGuest ? "guest" : "";
            Email = "";
            IsStudent = false;
            IsInstructor = false;
            IsActive = false;
            GdprAccepted = false;
            LiabilityAccepted = false;
        }
        else
        {
            Name = f.Name ?? "";
            Username = f.Username ?? "";
            Email = f.Email ?? "";
            IsStudent = f.IsStudent;
            IsInstructor = f.IsInstructor;
            IsActive = f.Active;
            GdprAccepted = f.GdprAccepted;
            LiabilityAccepted = f.LiabilityAccepted;
        }

        OnPropertyChanged(nameof(IsLoggedInFencer));
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var f = _auth.CurrentFencer;
        if (f is null)
        {
            StatusMessage = "You need to be logged in to save your profile.";
            return;
        }

        try
        {
            f.Name = (Name ?? "").Trim();
            f.Email = (Email ?? "").Trim();
            f.IsStudent = IsStudent;
            f.GdprAccepted = GdprAccepted;
            f.LiabilityAccepted = LiabilityAccepted;
            // Username, IsInstructor and Active are not editable from this page.

            await _sheets.UpsertFencerAsync(f);
            StatusMessage = "Profile saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}