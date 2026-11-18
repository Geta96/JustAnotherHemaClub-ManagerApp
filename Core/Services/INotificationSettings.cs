namespace JustAnotherHemaClub.Services;

public interface INotificationSettings
{
    bool IsEnabled(string fencerId);
    void SetEnabled(string fencerId, bool enabled);
    Task<bool> EnsurePermissionAsync();
}