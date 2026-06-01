namespace JustAnotherHemaClub.Models;

public class Profile
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsStudent { get; set; }
    public bool GdprAccepted { get; set; }
    public bool LiabilityAccepted { get; set; }
}