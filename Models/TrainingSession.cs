namespace JustAnotherHemaClub.Models;

public class TrainingSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string Topic { get; set; } = string.Empty;
    public List<string> AttendeeFencerIds { get; set; } = new();
}