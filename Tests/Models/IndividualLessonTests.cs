using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

public class IndividualLessonTests
{
    [Fact]
    public void NewIndividualLesson_HasUniqueId()
    {
        var lesson1 = new IndividualLesson();
        var lesson2 = new IndividualLesson();

        lesson1.Id.Should().NotBe(lesson2.Id);
    }

    [Fact]
    public void NewIndividualLesson_HasDefaultAcceptedStatus()
    {
        var lesson = new IndividualLesson();

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
    }

    [Fact]
    public void NewIndividualLesson_HasEmptyStrings()
    {
        var lesson = new IndividualLesson();

        lesson.StudentId.Should().BeEmpty();
        lesson.InstructorId.Should().BeEmpty();
        lesson.Topic.Should().BeEmpty();
        lesson.Notes.Should().BeEmpty();
        lesson.NextIdea.Should().BeEmpty();
    }

    [Fact]
    public void NewIndividualLesson_HasEmptyRequestedInstructorIds()
    {
        var lesson = new IndividualLesson();

        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void IndividualLesson_CanSetProperties()
    {
        var date = new DateTime(2024, 5, 15, 14, 0, 0);
        var lesson = new IndividualLesson
        {
            Date = date,
            StudentId = "student1",
            InstructorId = "instructor1",
            Topic = "Longsword basics",
            Notes = "Good progress on Zufechten",
            NextIdea = "Work on Absetzen",
            Status = IndividualLessonStatus.Requested,
            RequestedInstructorIds = new List<string> { "inst1", "inst2" }
        };

        lesson.Date.Should().Be(date);
        lesson.StudentId.Should().Be("student1");
        lesson.InstructorId.Should().Be("instructor1");
        lesson.Topic.Should().Be("Longsword basics");
        lesson.Notes.Should().Be("Good progress on Zufechten");
        lesson.NextIdea.Should().Be("Work on Absetzen");
        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.RequestedInstructorIds.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(IndividualLessonStatus.Accepted)]
    [InlineData(IndividualLessonStatus.Requested)]
    [InlineData(IndividualLessonStatus.Rejected)]
    public void IndividualLesson_CanSetAllStatusValues(IndividualLessonStatus status)
    {
        var lesson = new IndividualLesson { Status = status };

        lesson.Status.Should().Be(status);
    }
}
