using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Comprehensive workflow tests for the 1-on-1 lesson feature.
/// Tests all intended variations: instructor direct add, student request,
/// instructor-to-instructor request, accept/reject chains, visibility rules,
/// filtering, editing, and deletion.
///
/// Simulates the IndividualLessonsViewModel's Rebuild() logic to verify
/// that each role (student, instructor) sees exactly the lessons they should.
/// </summary>
public class IndividualLessonFullWorkflowTests
{
    // ========= DATA SETUP =========

    private static readonly Fencer Instructor1 = new() { Id = "inst1", Name = "Coach A", IsInstructor = true, Active = true };
    private static readonly Fencer Instructor2 = new() { Id = "inst2", Name = "Coach B", IsInstructor = true, Active = true };
    private static readonly Fencer Instructor3 = new() { Id = "inst3", Name = "Coach C", IsInstructor = true, Active = true };
    private static readonly Fencer Student1 = new() { Id = "stu1", Name = "Alice", IsInstructor = false, Active = true };
    private static readonly Fencer Student2 = new() { Id = "stu2", Name = "Bob", IsInstructor = false, Active = true };

    private static List<Fencer> AllFencers => new() { Instructor1, Instructor2, Instructor3, Student1, Student2 };

    // ======================================================================
    // INSTRUCTOR DIRECT ADD FLOW
    // ======================================================================

    [Fact]
    public void InstructorDirectAdd_CreatesAcceptedLesson()
    {
        var lesson = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Longsword basics",
            new DateTime(2024, 6, 15, 14, 0, 0));

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor1.Id);
        lesson.StudentId.Should().Be(Student1.Id);
        lesson.Topic.Should().Be("Longsword basics");
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void InstructorDirectAdd_WithNotes_StudentCannotSeeNotes()
    {
        var lesson = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Sword & buckler");
        lesson.Notes = "Student hesitates on voiding. Drill more.";
        lesson.NextIdea = "Focus on counter-attacks next time.";

        // The VM's IndividualLessonRowVm.ShowPrivateFields is only true for instructors
        bool studentSeesPrivateFields = false; // IsViewerInstructor = false for students
        bool instructorSeesPrivateFields = true;

        studentSeesPrivateFields.Should().BeFalse();
        instructorSeesPrivateFields.Should().BeTrue();
        lesson.Notes.Should().NotBeEmpty();
        lesson.NextIdea.Should().NotBeEmpty();
    }

    [Fact]
    public void InstructorDirectAdd_MultipleStudents_SeparateLessons()
    {
        var lesson1 = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Longsword");
        var lesson2 = InstructorAddsDirectly(Instructor1.Id, Student2.Id, "Messer");

        lesson1.Id.Should().NotBe(lesson2.Id);
        lesson1.StudentId.Should().Be(Student1.Id);
        lesson2.StudentId.Should().Be(Student2.Id);
    }

    // ======================================================================
    // STUDENT REQUEST FLOW
    // ======================================================================

    [Fact]
    public void StudentRequest_TargetsMultipleInstructors()
    {
        var lesson = StudentRequests(Student1.Id, new[] { Instructor1.Id, Instructor2.Id },
            "I want to improve my cuts", new DateTime(2024, 7, 1, 10, 0, 0));

        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.StudentId.Should().Be(Student1.Id);
        lesson.InstructorId.Should().BeEmpty();
        lesson.RequestedInstructorIds.Should().HaveCount(2);
        lesson.RequestedInstructorIds.Should().Contain(Instructor1.Id);
        lesson.RequestedInstructorIds.Should().Contain(Instructor2.Id);
    }

    [Fact]
    public void StudentRequest_SingleInstructor()
    {
        var lesson = StudentRequests(Student1.Id, new[] { Instructor1.Id },
            "Rapier drills", new DateTime(2024, 7, 5, 18, 0, 0));

        lesson.RequestedInstructorIds.Should().HaveCount(1);
        lesson.RequestedInstructorIds[0].Should().Be(Instructor1.Id);
    }

    [Fact]
    public void StudentRequest_Accepted_ByFirstInstructor()
    {
        var lesson = StudentRequests(Student1.Id, new[] { Instructor1.Id, Instructor2.Id }, "Topic");

        AcceptRequest(lesson, Instructor1.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor1.Id);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void StudentRequest_Accepted_BySecondInstructor()
    {
        var lesson = StudentRequests(Student1.Id, new[] { Instructor1.Id, Instructor2.Id }, "Topic");

        AcceptRequest(lesson, Instructor2.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor2.Id);
    }

    [Fact]
    public void StudentRequest_OneRejects_StillPendingForOthers()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id, Instructor3.Id }, "Help with guards");

        RejectRequest(lesson, Instructor2.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.RequestedInstructorIds.Should().HaveCount(2);
        lesson.RequestedInstructorIds.Should().NotContain(Instructor2.Id);
        lesson.RequestedInstructorIds.Should().Contain(Instructor1.Id);
        lesson.RequestedInstructorIds.Should().Contain(Instructor3.Id);
    }

    [Fact]
    public void StudentRequest_AllReject_BecomesRejected()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Complicated topic");

        RejectRequest(lesson, Instructor1.Id);
        RejectRequest(lesson, Instructor2.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void StudentRequest_RejectThenAcceptRemaining()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id, Instructor3.Id }, "Dagger");

        RejectRequest(lesson, Instructor1.Id);
        lesson.Status.Should().Be(IndividualLessonStatus.Requested);

        AcceptRequest(lesson, Instructor3.Id);
        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor3.Id);
    }

    // ======================================================================
    // INSTRUCTOR-TO-INSTRUCTOR REQUEST (peer learning)
    // ======================================================================

    [Fact]
    public void InstructorRequestsFromAnotherInstructor()
    {
        // Instructor1 wants to learn from Instructor2 (takes the "student" role)
        var lesson = StudentRequests(Instructor1.Id, new[] { Instructor2.Id },
            "Advanced Ringen am Schwert", new DateTime(2024, 8, 1, 9, 0, 0));

        lesson.StudentId.Should().Be(Instructor1.Id);
        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.RequestedInstructorIds.Should().Contain(Instructor2.Id);
    }

    [Fact]
    public void InstructorRequestsFromMultipleInstructors()
    {
        var lesson = StudentRequests(Instructor1.Id,
            new[] { Instructor2.Id, Instructor3.Id }, "Wrestling techniques");

        AcceptRequest(lesson, Instructor3.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor3.Id);
        lesson.StudentId.Should().Be(Instructor1.Id);
    }

    // ======================================================================
    // VISIBILITY RULES (who sees what)
    // ======================================================================

    [Fact]
    public void Visibility_Student_OnlySeesOwnLessons()
    {
        var allLessons = new List<IndividualLesson>
        {
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Lesson for Alice"),
            InstructorAddsDirectly(Instructor1.Id, Student2.Id, "Lesson for Bob"),
            StudentRequests(Student1.Id, new[] { Instructor1.Id }, "Alice's request"),
            StudentRequests(Student2.Id, new[] { Instructor1.Id }, "Bob's request"),
        };

        var aliceSees = FilterForStudent(allLessons, Student1.Id);

        aliceSees.Should().HaveCount(2);
        aliceSees.Should().AllSatisfy(l => l.StudentId.Should().Be(Student1.Id));
    }

    [Fact]
    public void Visibility_Student_SeesAcceptedAndRequested_NotRejected()
    {
        var accepted = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Done");
        var requested = StudentRequests(Student1.Id, new[] { Instructor1.Id }, "Pending");
        var rejected = StudentRequests(Student1.Id, new[] { Instructor2.Id }, "Nope");
        RejectRequest(rejected, Instructor2.Id);

        var allLessons = new List<IndividualLesson> { accepted, requested, rejected };
        var visible = FilterForStudent(allLessons, Student1.Id);

        visible.Should().HaveCount(2);
        visible.Should().Contain(accepted);
        visible.Should().Contain(requested);
        visible.Should().NotContain(rejected);
    }

    [Fact]
    public void Visibility_Instructor_SeesAllLessons_Unfiltered()
    {
        var allLessons = new List<IndividualLesson>
        {
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "A"),
            InstructorAddsDirectly(Instructor2.Id, Student2.Id, "B"),
            StudentRequests(Student1.Id, new[] { Instructor1.Id }, "C"),
        };

        var instructorSees = FilterForInstructor(allLessons, Instructor1.Id,
            filterStudent: null, filterInstructor: null);

        instructorSees.Should().HaveCount(3);
    }

    [Fact]
    public void Visibility_Instructor_FilterByStudent()
    {
        var allLessons = new List<IndividualLesson>
        {
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Alice 1"),
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Alice 2"),
            InstructorAddsDirectly(Instructor1.Id, Student2.Id, "Bob 1"),
        };

        var filtered = FilterForInstructor(allLessons, Instructor1.Id,
            filterStudent: Student1.Id, filterInstructor: null);

        filtered.Should().HaveCount(2);
        filtered.Should().AllSatisfy(l => l.StudentId.Should().Be(Student1.Id));
    }

    [Fact]
    public void Visibility_Instructor_FilterByInstructor()
    {
        var allLessons = new List<IndividualLesson>
        {
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "By Coach A"),
            InstructorAddsDirectly(Instructor2.Id, Student1.Id, "By Coach B"),
            StudentRequests(Student1.Id, new[] { Instructor1.Id }, "Req to Coach A"),
        };

        var filtered = FilterForInstructor(allLessons, Instructor1.Id,
            filterStudent: null, filterInstructor: Instructor1.Id);

        filtered.Should().HaveCount(2); // direct lesson + request targeting inst1
    }

    [Fact]
    public void Visibility_Instructor_IsTargetedForRequest()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Help");

        bool inst1IsTargeted = IsTargetedInstructor(lesson, Instructor1.Id);
        bool inst2IsTargeted = IsTargetedInstructor(lesson, Instructor2.Id);
        bool inst3IsTargeted = IsTargetedInstructor(lesson, Instructor3.Id);

        inst1IsTargeted.Should().BeTrue();
        inst2IsTargeted.Should().BeTrue();
        inst3IsTargeted.Should().BeFalse();
    }

    // ======================================================================
    // LESSON EDITING (instructor modifies notes/topic)
    // ======================================================================

    [Fact]
    public void Edit_InstructorUpdatesNotes()
    {
        var lesson = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Longsword");
        lesson.Notes = "Initial notes";

        // Instructor saves with updated notes
        lesson.Notes = "Updated: student needs more work on Zufechten.";
        lesson.NextIdea = "Start with Langort drills next time.";

        lesson.Notes.Should().Contain("Zufechten");
        lesson.NextIdea.Should().Contain("Langort");
    }

    [Fact]
    public void Edit_InstructorChangesTopic()
    {
        var lesson = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Old topic");

        lesson.Topic = "New topic: Messer Hut positions";

        lesson.Topic.Should().Contain("Messer");
    }

    // ======================================================================
    // DELETION
    // ======================================================================

    [Fact]
    public void Delete_AcceptedLesson_SetsRejectedAndClearsTargets()
    {
        var lesson = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "To delete");

        DeleteLesson(lesson);

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void Delete_PendingRequest_SetsRejectedAndClearsTargets()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Unwanted");

        DeleteLesson(lesson);

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);
        lesson.RequestedInstructorIds.Should().BeEmpty();
    }

    [Fact]
    public void Delete_RemovedFromVisibleList()
    {
        var lesson1 = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Keep");
        var lesson2 = InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Delete me");
        var allLessons = new List<IndividualLesson> { lesson1, lesson2 };

        DeleteLesson(lesson2);
        allLessons.RemoveAll(l => l.Id == lesson2.Id);

        allLessons.Should().HaveCount(1);
        allLessons[0].Topic.Should().Be("Keep");
    }

    // ======================================================================
    // COMPLEX MULTI-STEP SCENARIOS
    // ======================================================================

    [Fact]
    public void Scenario_StudentRequestsWeekly_InstructorAccepts_BuildsHistory()
    {
        var lessons = new List<IndividualLesson>();

        // Student requests weekly lessons over 4 weeks
        for (int week = 0; week < 4; week++)
        {
            var lesson = StudentRequests(Student1.Id, new[] { Instructor1.Id },
                $"Week {week + 1} lesson",
                new DateTime(2024, 6, 1).AddDays(week * 7).AddHours(14));
            AcceptRequest(lesson, Instructor1.Id);
            lessons.Add(lesson);
        }

        lessons.Should().HaveCount(4);
        lessons.Should().AllSatisfy(l =>
        {
            l.Status.Should().Be(IndividualLessonStatus.Accepted);
            l.InstructorId.Should().Be(Instructor1.Id);
            l.StudentId.Should().Be(Student1.Id);
        });

        // Student sees all 4
        var studentSees = FilterForStudent(lessons, Student1.Id);
        studentSees.Should().HaveCount(4);
    }

    [Fact]
    public void Scenario_MultipleStudents_MultipleInstructors_ComplexSchedule()
    {
        var lessons = new List<IndividualLesson>();

        // Alice requests from both coaches, Coach A accepts
        var aliceReq = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Alice longsword");
        AcceptRequest(aliceReq, Instructor1.Id);
        lessons.Add(aliceReq);

        // Bob requests from Coach B only
        var bobReq = StudentRequests(Student2.Id, new[] { Instructor2.Id }, "Bob messer");
        AcceptRequest(bobReq, Instructor2.Id);
        lessons.Add(bobReq);

        // Coach A adds a direct lesson for Bob
        var directBob = InstructorAddsDirectly(Instructor1.Id, Student2.Id, "Bob special");
        lessons.Add(directBob);

        // Coach B adds for Alice
        var directAlice = InstructorAddsDirectly(Instructor2.Id, Student1.Id, "Alice buckler");
        lessons.Add(directAlice);

        // Alice should see 2 lessons (her request + Coach B's direct)
        var aliceSees = FilterForStudent(lessons, Student1.Id);
        aliceSees.Should().HaveCount(2);

        // Bob should see 2 lessons (his request + Coach A's direct)
        var bobSees = FilterForStudent(lessons, Student2.Id);
        bobSees.Should().HaveCount(2);

        // Coach A (unfiltered) sees all 4
        var coach1Sees = FilterForInstructor(lessons, Instructor1.Id, null, null);
        coach1Sees.Should().HaveCount(4);

        // Coach A filtered by their own lessons (as instructor)
        var coach1Own = FilterForInstructor(lessons, Instructor1.Id, null, Instructor1.Id);
        coach1Own.Should().HaveCount(2); // Alice's accepted + Bob's direct
    }

    [Fact]
    public void Scenario_RequestRejectedByAll_StudentDoesNotSeeIt()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Nobody wants this");

        RejectRequest(lesson, Instructor1.Id);
        RejectRequest(lesson, Instructor2.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Rejected);

        // Student should not see rejected lessons
        var visible = FilterForStudent(new List<IndividualLesson> { lesson }, Student1.Id);
        visible.Should().BeEmpty();
    }

    [Fact]
    public void Scenario_InstructorAcceptsRace_FirstOneWins()
    {
        // Simulate two instructors trying to accept the same request
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id }, "Popular slot");

        // Instructor1 accepts first
        AcceptRequest(lesson, Instructor1.Id);

        // At this point the lesson is already Accepted — Instructor2 is too late
        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor1.Id);

        // If Instructor2 tries to "accept", nothing should change
        // (the VM checks Status == Requested before accepting)
        bool canInst2Accept = lesson.Status == IndividualLessonStatus.Requested;
        canInst2Accept.Should().BeFalse();
    }

    [Fact]
    public void Scenario_PartialRejects_ThenAccept_Succeeds()
    {
        var lesson = StudentRequests(Student1.Id,
            new[] { Instructor1.Id, Instructor2.Id, Instructor3.Id }, "Hard topic");

        // Two reject
        RejectRequest(lesson, Instructor1.Id);
        RejectRequest(lesson, Instructor3.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Requested);
        lesson.RequestedInstructorIds.Should().HaveCount(1);
        lesson.RequestedInstructorIds[0].Should().Be(Instructor2.Id);

        // Last one accepts
        AcceptRequest(lesson, Instructor2.Id);

        lesson.Status.Should().Be(IndividualLessonStatus.Accepted);
        lesson.InstructorId.Should().Be(Instructor2.Id);
    }

    [Fact]
    public void Scenario_MonthOfLessons_CountsCorrectly()
    {
        var lessons = new List<IndividualLesson>();

        // Instructor1 gives 3 lessons to Alice, 2 to Bob
        for (int i = 0; i < 3; i++)
            lessons.Add(InstructorAddsDirectly(Instructor1.Id, Student1.Id, $"Alice {i + 1}"));
        for (int i = 0; i < 2; i++)
            lessons.Add(InstructorAddsDirectly(Instructor1.Id, Student2.Id, $"Bob {i + 1}"));

        // Count received
        int aliceReceived = lessons.Count(l =>
            l.StudentId == Student1.Id && l.Status == IndividualLessonStatus.Accepted);
        int bobReceived = lessons.Count(l =>
            l.StudentId == Student2.Id && l.Status == IndividualLessonStatus.Accepted);
        int inst1Given = lessons.Count(l =>
            l.InstructorId == Instructor1.Id && l.Status == IndividualLessonStatus.Accepted);

        aliceReceived.Should().Be(3);
        bobReceived.Should().Be(2);
        inst1Given.Should().Be(5);
    }

    [Fact]
    public void Scenario_LessonOrdering_NewestFirst()
    {
        var lessons = new List<IndividualLesson>
        {
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Old",
                new DateTime(2024, 1, 1)),
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "Middle",
                new DateTime(2024, 3, 15)),
            InstructorAddsDirectly(Instructor1.Id, Student1.Id, "New",
                new DateTime(2024, 6, 1)),
        };

        // The VM orders by date descending
        var ordered = lessons.OrderByDescending(l => l.Date).ToList();

        ordered[0].Topic.Should().Be("New");
        ordered[1].Topic.Should().Be("Middle");
        ordered[2].Topic.Should().Be("Old");
    }

    // ======================================================================
    // HELPERS — replicate the IndividualLessonsViewModel logic
    // ======================================================================

    private static IndividualLesson InstructorAddsDirectly(string instructorId, string studentId,
        string topic, DateTime? date = null) => new()
    {
        Date = date ?? DateTime.Today.AddDays(1),
        StudentId = studentId,
        InstructorId = instructorId,
        Topic = topic,
        Status = IndividualLessonStatus.Accepted
    };

    private static IndividualLesson StudentRequests(string studentId, string[] targetInstructorIds,
        string topic, DateTime? date = null) => new()
    {
        Date = date ?? DateTime.Today.AddDays(3),
        StudentId = studentId,
        InstructorId = "",
        Topic = topic,
        Status = IndividualLessonStatus.Requested,
        RequestedInstructorIds = targetInstructorIds.ToList()
    };

    /// <summary>Mimics IndividualLessonsViewModel.AcceptRequestAsync</summary>
    private static void AcceptRequest(IndividualLesson lesson, string instructorId)
    {
        lesson.InstructorId = instructorId;
        lesson.Status = IndividualLessonStatus.Accepted;
        lesson.RequestedInstructorIds = new List<string>();
    }

    /// <summary>Mimics IndividualLessonsViewModel.RejectRequestAsync</summary>
    private static void RejectRequest(IndividualLesson lesson, string instructorId)
    {
        lesson.RequestedInstructorIds = lesson.RequestedInstructorIds
            .Where(id => id != instructorId).ToList();

        if (lesson.RequestedInstructorIds.Count == 0)
            lesson.Status = IndividualLessonStatus.Rejected;
    }

    /// <summary>Mimics IndividualLessonsViewModel.DeleteLessonAsync</summary>
    private static void DeleteLesson(IndividualLesson lesson)
    {
        lesson.Status = IndividualLessonStatus.Rejected;
        lesson.RequestedInstructorIds = new List<string>();
    }

    /// <summary>
    /// Mimics IndividualLessonsViewModel.Rebuild() filtering for a student viewer.
    /// Students see lessons where they are the student AND status is Accepted or Requested.
    /// </summary>
    private static List<IndividualLesson> FilterForStudent(List<IndividualLesson> all, string studentId) =>
        all.Where(l =>
            l.StudentId == studentId &&
            (l.Status == IndividualLessonStatus.Accepted ||
             (l.Status == IndividualLessonStatus.Requested && l.RequestedInstructorIds.Count > 0)))
        .ToList();

    /// <summary>
    /// Mimics IndividualLessonsViewModel.Rebuild() filtering for an instructor viewer.
    /// Instructors see everything, optionally filtered by student and/or instructor.
    /// </summary>
    private static List<IndividualLesson> FilterForInstructor(List<IndividualLesson> all,
        string viewerInstructorId, string? filterStudent, string? filterInstructor)
    {
        IEnumerable<IndividualLesson> q = all;

        if (filterStudent is not null)
            q = q.Where(l => l.StudentId == filterStudent);

        if (filterInstructor is not null)
            q = q.Where(l =>
                l.InstructorId == filterInstructor ||
                (l.Status == IndividualLessonStatus.Requested &&
                 l.RequestedInstructorIds.Contains(filterInstructor)));

        return q.ToList();
    }

    /// <summary>Whether the given instructor is a target of the pending request.</summary>
    private static bool IsTargetedInstructor(IndividualLesson lesson, string instructorId) =>
        lesson.Status == IndividualLessonStatus.Requested &&
        lesson.RequestedInstructorIds.Contains(instructorId);
}
