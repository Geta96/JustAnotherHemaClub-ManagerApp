using System.ComponentModel.DataAnnotations;

namespace JustAnotherHemaClub.Models;

public class Fencer
{
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
}