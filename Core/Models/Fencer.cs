using System.ComponentModel.DataAnnotations;

namespace JustAnotherHemaClub.Models;

public class Fencer
{
    public const string DeletedPlaceholder = "[Deleted User]";

    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, ErrorMessage = "Username cannot be longer than 100 characters")]
    public string? Username { get; set; }

    [StringLength(255, ErrorMessage = "Password hash cannot be longer than 255 characters")]
    public string? PasswordHash { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
    public string Name { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Invalid email address")]
    [StringLength(255, ErrorMessage = "Email cannot be longer than 255 characters")]
    public string? Email { get; set; }

    public bool Active { get; set; } = true;
    public bool IsStudent { get; set; }
    public bool GdprAccepted { get; set; }
    public bool LiabilityAccepted { get; set; }
    public bool IsInstructor { get; set; }

    /// <summary>
    /// Instructor-mediated password reset: when a fencer requests a reset they
    /// supply the NEW password up front; its PBKDF2 hash is parked here (the live
    /// <see cref="PasswordHash"/> is left untouched) until an instructor approves.
    /// On approval this value is copied into <see cref="PasswordHash"/> and cleared;
    /// on rejection it is simply cleared. Stored on the shared sheet so a request
    /// filed in either the MAUI or web app is visible to instructors in both.
    /// </summary>
    [StringLength(255)]
    public string? PendingPasswordHash { get; set; }

    /// <summary>UTC time the pending reset was requested (null when none is pending).</summary>
    public DateTime? PasswordResetRequestedAtUtc { get; set; }

    /// <summary>True when this fencer has a reset awaiting instructor review.</summary>
    public bool HasPendingPasswordReset => !string.IsNullOrEmpty(PendingPasswordHash);

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? DeletedPlaceholder : Name;
}