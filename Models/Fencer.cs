namespace JustAnotherHemaClub.Models;

public class Fencer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public bool IsStudent { get; set; }
    public bool GdprAccepted { get; set; }
    public bool LiabilityAccepted { get; set; }
}