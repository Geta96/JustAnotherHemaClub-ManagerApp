namespace JustAnotherHemaClub.Models;

public class Fencer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
}