namespace JustAnotherHemaClub.Services;

public interface INotificationSettings
{
    bool IsEnabled(string fencerId);
    void SetEnabled(string fencerId, bool enabled);
    bool IsInstructorEnabled(string fencerId);
    void SetInstructorEnabled(string fencerId, bool enabled);
    Task<bool> EnsurePermissionAsync();
}