using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Pure, host-agnostic registration logic shared by every front-end (MAUI today,
/// Blazor next). Holds the validation rules, duplicate detection, and the
/// <see cref="Fencer"/> construction that used to live privately inside
/// <c>RegisterViewModel</c> (and was mirrored again in the test project). Each
/// platform keeps its own thin view-model for navigation/dialogs but calls into
/// this for every decision, so the rules can never drift between clients.
/// </summary>
public static class RegistrationValidator
{
    /// <summary>
    /// Runs the full registration validation chain. Returns <c>null</c> when the
    /// input is valid, otherwise the first human-readable error message.
    /// </summary>
    public static string? Validate(
        string? name,
        string? email,
        string? confirmEmail,
        string? username,
        string? password,
        string? confirmPassword,
        bool gdprAccepted,
        bool liabilityAccepted)
    {
        var trimmedEmail        = (email ?? "").Trim();
        var trimmedConfirmEmail = (confirmEmail ?? "").Trim();

        return
            string.IsNullOrWhiteSpace(name)            ? "Name is required." :
            string.IsNullOrWhiteSpace(trimmedEmail)    ? "Email is required." :
            !IsValidEmail(trimmedEmail)                ? "Please enter a valid email address (e.g. you@example.com)." :
            string.IsNullOrWhiteSpace(trimmedConfirmEmail)
                                                       ? "Please confirm your email address." :
            !string.Equals(trimmedEmail, trimmedConfirmEmail, StringComparison.OrdinalIgnoreCase)
                                                       ? "Email addresses do not match." :
            string.IsNullOrWhiteSpace(username)        ? "Login username is required." :
            !IsStrongPassword(password)                ? "Password must be at least 6 characters and include at least one number." :
            password != confirmPassword                ? "Passwords do not match." :
            !gdprAccepted                              ? "You must accept the GDPR policy." :
            !liabilityAccepted                         ? "You must accept the liability statement." :
            null;
    }

    /// <summary>
    /// Stricter than <see cref="System.Net.Mail.MailAddress"/> alone: also requires
    /// a real-looking TLD (a dot in the host with ?2 chars after it), so inputs
    /// like "a@b" or "user@localhost" are rejected.
    /// </summary>
    public static bool IsValidEmail(string? email)
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
            if (host.Length - dotIdx - 1 < 2) return false;        // TLD ? 2 chars

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>At least 6 characters and at least one digit.</summary>
    public static bool IsStrongPassword(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= 6 &&
        password.Any(char.IsDigit);

    /// <summary>True when another fencer already uses <paramref name="username"/> (case-insensitive).</summary>
    public static bool IsDuplicateUsername(string username, IEnumerable<Fencer> existing) =>
        existing.Any(f =>
            !string.IsNullOrEmpty(f.Username) &&
            string.Equals(f.Username.Trim(), (username ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when another fencer already uses <paramref name="email"/> (case-insensitive).</summary>
    public static bool IsDuplicateEmail(string email, IEnumerable<Fencer> existing) =>
        existing.Any(f =>
            !string.IsNullOrEmpty(f.Email) &&
            string.Equals(f.Email.Trim(), (email ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Live "emails don't match" hint — true only once both fields are non-empty and differ.</summary>
    public static bool ComputeEmailMismatch(string? email, string? confirmEmail) =>
        !string.IsNullOrWhiteSpace(email) &&
        !string.IsNullOrWhiteSpace(confirmEmail) &&
        !string.Equals(email.Trim(), confirmEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Live "passwords don't match" hint — true only once both fields are non-empty and differ.</summary>
    public static bool ComputePasswordMismatch(string? password, string? confirmPassword) =>
        !string.IsNullOrEmpty(password) &&
        !string.IsNullOrEmpty(confirmPassword) &&
        password != confirmPassword;

    /// <summary>
    /// Builds the <see cref="Fencer"/> row persisted on successful registration.
    /// A fresh Id is generated and the password is hashed via <see cref="AuthService.Hash"/>.
    /// New registrations are always active, non-instructor members.
    /// </summary>
    public static Fencer BuildRegistrationFencer(
        string name,
        string email,
        string username,
        string password,
        bool isStudent,
        bool gdprAccepted = true,
        bool liabilityAccepted = true) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Username = (username ?? "").Trim(),
        PasswordHash = AuthService.Hash(password),
        Name = (name ?? "").Trim(),
        Email = (email ?? "").Trim(),
        Active = true,
        IsStudent = isStudent,
        GdprAccepted = gdprAccepted,
        LiabilityAccepted = liabilityAccepted,
        IsInstructor = false
    };
}
