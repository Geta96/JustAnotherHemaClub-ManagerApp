using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IGoogleSheetsService _sheets;
    private readonly INotificationSettings? _notifications;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool isInstructor;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;
    [ObservableProperty] private bool notificationsEnabled = true;
    private bool _instructorNotificationsEnabled = true;
    public bool InstructorNotificationsEnabled
    {
        get => _instructorNotificationsEnabled;
        set => SetProperty(ref _instructorNotificationsEnabled, value);
    }
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isGuest;

    public bool IsLoggedInFencer => !IsGuest && _auth.CurrentFencer is not null;

    public ProfileViewModel(
        AuthService auth,
        IGoogleSheetsService sheets,
        INotificationSettings? notifications = null)
    {
        _auth = auth;
        _sheets = sheets;
        _notifications = notifications;
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
            NotificationsEnabled = false;
            InstructorNotificationsEnabled = false;
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
            NotificationsEnabled = _notifications?.IsEnabled(f.Id) ?? true;
            InstructorNotificationsEnabled = _notifications?.IsInstructorEnabled(f.Id) ?? true;
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

            if (_notifications is not null)
            {
                if (NotificationsEnabled || (f.IsInstructor && InstructorNotificationsEnabled))
                    await _notifications.EnsurePermissionAsync();
                _notifications.SetEnabled(f.Id, NotificationsEnabled);
                if (f.IsInstructor)
                    _notifications.SetInstructorEnabled(f.Id, InstructorNotificationsEnabled);
            }

            StatusMessage = "Profile saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}