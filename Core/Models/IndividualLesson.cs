namespace JustAnotherHemaClub.Models;

public enum IndividualLessonStatus
{
    Accepted,
    Requested,
    Rejected
}

public class IndividualLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string InstructorId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;

    /// <summary>Instructor-only. Hidden from students.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Instructor-only. Hidden from students.</summary>
    public string NextIdea { get; set; } = string.Empty;

    public IndividualLessonStatus Status { get; set; } = IndividualLessonStatus.Accepted;

    /// <summary>For Status=Requested: target instructors that can accept.</summary>
    public List<string> RequestedInstructorIds { get; set; } = new();
}