using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class PreferencesProfileService : IProfileService
{
    private const string KName = "profile.name";
    private const string KNickname = "profile.nickname";
    private const string KEmail = "profile.email";
    private const string KStudent = "profile.student";
    private const string KGdpr = "profile.gdpr";
    private const string KLiability = "profile.liability";

    private readonly AuthService _auth;

    public PreferencesProfileService(AuthService auth) => _auth = auth;

    public Task<Profile> GetAsync()
    {
        var defaultEmail = _auth.IsGuest
            ? "guest@local"
            : (_auth.CurrentUser?.Username ?? "");

        var p = new Profile
        {
            Name = Preferences.Get(KName, ""),
            Nickname = Preferences.Get(KNickname, ""),
            Email = Preferences.Get(KEmail, defaultEmail),
            IsStudent = Preferences.Get(KStudent, false),
            GdprAccepted = Preferences.Get(KGdpr, false),
            LiabilityAccepted = Preferences.Get(KLiability, false),
        };
        return Task.FromResult(p);
    }

    public Task SaveAsync(Profile profile)
    {
        Preferences.Set(KName, profile.Name ?? "");
        Preferences.Set(KNickname, profile.Nickname ?? "");
        // Email is not editable from the UI; preserve whatever we have stored or auth-derived.
        if (!string.IsNullOrWhiteSpace(profile.Email))
            Preferences.Set(KEmail, profile.Email);
        Preferences.Set(KStudent, profile.IsStudent);
        Preferences.Set(KGdpr, profile.GdprAccepted);
        Preferences.Set(KLiability, profile.LiabilityAccepted);
        return Task.CompletedTask;
    }
}