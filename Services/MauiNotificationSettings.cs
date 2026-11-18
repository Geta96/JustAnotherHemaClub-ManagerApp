using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Services;

public sealed class MauiNotificationSettings : INotificationSettings
{
    private static string Key(string fencerId) => $"notif_enabled_{fencerId}";

    public bool IsEnabled(string fencerId) =>
        !string.IsNullOrWhiteSpace(fencerId) && Preferences.Default.Get(Key(fencerId), true);

    public void SetEnabled(string fencerId, bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(fencerId))
            Preferences.Default.Set(Key(fencerId), enabled);
    }

    public async Task<bool> EnsurePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            return status == PermissionStatus.Granted;
        }
#endif
        return true;
    }
}