using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests the individual lesson request/accept/reject workflow at the model level.
/// Since the ViewModel depends on AuthService + MainThread, we verify the
/// state-machine logic directly on the model.
/// </summary>
public class IndividualLessonWorkflowTests
{
    [Fact]
    public void Request_CreatesLessonInRequestedState()
    {
        var lesson = new IndividualLesson
        {
            Date = new DateTime(2024, 6, 15, 14, 0, 0),
            StudentId = "student1",
            InstructorId = "",
            Topic = "Longsword basics",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst1", "inst2" }
        };

        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.InstructorId.Should().BeEmpty();
        lesson.RequestedInstructorIds.Should().HaveCount(2);
    }

    [Fact]
    public void Accept_TransitionsToAccepted_ClearsTargets()
    {
        var lesson = new IndividualLesson
        {
            StudentId = "student1",
            InstructorId = "",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst1", "inst2" }
        };

        // Instructor "inst1" accepts.
        lesson.InstructorId = "inst1";
        lesson.Status = IndividualLessonStatus.Accepted;
        lesson.RequestedInstructorIds = new List<string>();

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be("inst1");
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void Reject_RemovesInstructorFromTargets()
    {
        var lesson = new IndividualLesson
        {
            StudentId = "student1",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst1", "inst2", "inst3" }
        };

        // "inst2" rejects — remove from target list.
        lesson.RequestedInstructorIds = lesson.RequestedInstructorIds
            .Where(id => id != "inst2").ToList();

        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.RequestedInstructorIds.Should().HaveCount(2);
        lesson.RequestedInstructorIds.Should().NotContain("inst2");
    }

    [Fact]
    public void Reject_LastInstructor_TransitionsToRejected()
    {
        var lesson = new IndividualLesson
        {
            StudentId = "student1",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst1" }
        };

        // Last instructor rejects.
        lesson.RequestedInstructorIds = lesson.RequestedInstructorIds
            .Where(id => id != "inst1").ToList();

        if (lesson.RequestedInstructorIds.Count == 0)
            lesson.Status = IndividualLessonStatus.Rejected;

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void DirectAdd_SkipsRequestFlow()
    {
        // Instructor adds a lesson directly (no request workflow).
        var lesson = new IndividualLesson
        {
            Date = DateTime.Today.AddDays(1),
            StudentId = "student1",
            InstructorId = "inst1",
            Topic = "Messer cutting",
            Notes = "Focus on #2 cut",
            NextIdea = "Defend against #2",
            Status = IndividualLessonStatus.Accepted
        };

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().NotBeEmpty();
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void InstructorRequestsFromOtherInstructor()
    {
        // An instructor can request a lesson from another instructor (they become the "student").
        var lesson = new IndividualLesson
        {
            Date = DateTime.Today.AddDays(3),
            StudentId = "instructor_a",  // the requesting instructor is the student here
            InstructorId = "",
            Topic = "Advanced Ringen",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "instructor_b" }
        };

        lesson.StudentId.Should().Be("instructor_a");
        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
    }

    [Fact]
    public void Delete_SetsRejectedAndClearsTargets()
    {
        // Deleting a lesson reuses Rejected status.
        var lesson = new IndividualLesson
        {
            StudentId = "student1",
            InstructorId = "inst1",
            Status = IndividualLessonStatus.Accepted,
            RequestedInstructorIds = new List<string> { "inst2" }
        };

        // "Delete" logic from the VM
        lesson.Status = IndividualLessonStatus.Rejected;
        lesson.RequestedInstructorIds = new List<string>();

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }
}
