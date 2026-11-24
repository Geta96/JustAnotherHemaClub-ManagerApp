using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly IGoogleSheetsService _sheets;
    private readonly INotificationSettings? _notifications;

    // Guards the auto-save hooks below from firing while LoadAsync is
    // populating the bound properties (each setter would otherwise trigger an
    // immediate, pointless save against stale/incoming data).
    private bool _isLoaded;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool isInstructor;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;
    [ObservableProperty] private bool notificationsEnabled = true;
    [ObservableProperty] private string hemaRatingsId = "";
    public string HemaRatingsBaseUrl => Fencer.HemaRatingsBaseUrl;
    public bool HasSavedHemaRatingsLink => _auth.CurrentFencer?.HasHemaRatingsProfile ?? false;

    private bool _instructorNotificationsEnabled = true;
    public bool InstructorNotificationsEnabled
    {
        get => _instructorNotificationsEnabled;
        set
        {
            if (SetProperty(ref _instructorNotificationsEnabled, value) && _isLoaded)
                _ = SaveNotificationsAsync();
        }
    }
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isGuest;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    public bool IsLoggedInFencer => !IsGuest && _auth.CurrentFencer is not null;

    // --- "Statements" expandable card (GDPR + liability) — expanded by default ---
    private bool _isStatementsExpanded = true;
    public bool IsStatementsExpanded
    {
        get => _isStatementsExpanded;
        set
        {
            if (SetProperty(ref _isStatementsExpanded, value))
                OnPropertyChanged(nameof(StatementsExpandGlyph));
        }
    }

    public string StatementsExpandGlyph => IsStatementsExpanded ? "\u25BE" : "\u25B8";

    private IRelayCommand? _toggleStatementsCommand;
    public IRelayCommand ToggleStatementsCommand =>
        _toggleStatementsCommand ??= new RelayCommand(() => IsStatementsExpanded = !IsStatementsExpanded);

    // --- "Settings" expandable card (student flag + notifications) — expanded by default ---
    private bool _isSettingsExpanded = true;
    public bool IsSettingsExpanded
    {
        get => _isSettingsExpanded;
        set
        {
            if (SetProperty(ref _isSettingsExpanded, value))
                OnPropertyChanged(nameof(SettingsExpandGlyph));
        }
    }

    public string SettingsExpandGlyph => IsSettingsExpanded ? "\u25BE" : "\u25B8";

    private IRelayCommand? _toggleSettingsCommand;
    public IRelayCommand ToggleSettingsCommand =>
        _toggleSettingsCommand ??= new RelayCommand(() => IsSettingsExpanded = !IsSettingsExpanded);

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
        _isLoaded = false;
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
            HemaRatingsId = f.HemaRatingsId ?? "";
        }

        OnPropertyChanged(nameof(IsLoggedInFencer));
        OnPropertyChanged(nameof(HasSavedHemaRatingsLink));
        // Only now start reacting to further (user-driven) property changes.
        _isLoaded = true;
        return Task.CompletedTask;
    }

    // --- Instant-save hooks: GDPR / liability / student toggle immediately,
    // mirroring the web Profile page (no shared Save button for these). ---
    partial void OnGdprAcceptedChanged(bool value)
    {
        if (!_isLoaded) return;
        _ = SaveFieldAsync(f => f.GdprAccepted = value);
    }

    partial void OnLiabilityAcceptedChanged(bool value)
    {
        if (!_isLoaded) return;
        _ = SaveFieldAsync(f => f.LiabilityAccepted = value);
    }

    partial void OnIsStudentChanged(bool value)
    {
        if (!_isLoaded) return;
        _ = SaveFieldAsync(f => f.IsStudent = value);
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (!_isLoaded) return;
        _ = SaveNotificationsAsync();
    }

    /// <summary>
    /// Applies a single field change to the current fencer and immediately
    /// persists it (no shared Save button for GDPR / liability / student,
    /// mirroring the web Profile page's auto-save checkboxes).
    /// </summary>
    private async Task SaveFieldAsync(Action<Models.Fencer> apply)
    {
        var f = _auth.CurrentFencer;
        if (f is null) return;

        try
        {
            apply(f);
            await _sheets.UpsertFencerAsync(f);
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveNameAsync()
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
            await _sheets.UpsertFencerAsync(f);
            StatusMessage = "Name saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves the fencer's HEMA Ratings fighter id. Accepts either the bare
    /// numeric id (e.g. "9343") or a pasted full URL, extracting just the
    /// digits so the stored value stays a plain id regardless of what the
    /// user pastes.
    /// </summary>
    [RelayCommand]
    public async Task SaveHemaRatingsAsync()
    {
        var f = _auth.CurrentFencer;
        if (f is null)
        {
            StatusMessage = "You need to be logged in to save your profile.";
            return;
        }

        try
        {
            var raw = (HemaRatingsId ?? "").Trim();
            // Pull out digits in case a full link was pasted instead of just the number.
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            f.HemaRatingsId = string.IsNullOrEmpty(digits) ? null : digits;
            HemaRatingsId = f.HemaRatingsId ?? "";
            await _sheets.UpsertFencerAsync(f);
            OnPropertyChanged(nameof(HasSavedHemaRatingsLink));
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private async Task SaveNotificationsAsync()
    {
        if (!_isLoaded) return;
        var f = _auth.CurrentFencer;
        if (f is null || _notifications is null) return;

        try
        {
            if (NotificationsEnabled || (f.IsInstructor && InstructorNotificationsEnabled))
                await _notifications.EnsurePermissionAsync();
            _notifications.SetEnabled(f.Id, NotificationsEnabled);
            if (f.IsInstructor)
                _notifications.SetInstructorEnabled(f.Id, InstructorNotificationsEnabled);
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}