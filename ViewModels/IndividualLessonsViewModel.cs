using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class IndividualLessonRowVm : ObservableObject
{
    public IndividualLesson Lesson { get; }
    public string StudentName { get; }
    public string InstructorName { get; }
    public bool IsCurrentUserStudent { get; }
    public bool IsCurrentUserTargetedInstructor { get; }
    public bool IsViewerInstructor { get; }

    public string DateText => Lesson.Date.ToString("yyyy-MM-dd HH:mm");
    public bool IsPending => Lesson.Status == IndividualLessonStatus.Requested;
    public bool IsAccepted => Lesson.Status == IndividualLessonStatus.Accepted;

    // Notes & next-idea are visible only to instructors.
    public bool ShowPrivateFields => IsViewerInstructor;

    // Compact-by-default header; expand to edit.
    [ObservableProperty] private bool isExpanded;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    // Short topic line for the collapsed header.
    public string TopicPreview =>
        string.IsNullOrWhiteSpace(Lesson.Topic) ? "(no topic)" : Lesson.Topic;

    public IndividualLessonRowVm(IndividualLesson lesson,
                                 string studentName,
                                 string instructorName,
                                 bool isViewerInstructor,
                                 bool isCurrentUserStudent,
                                 bool isCurrentUserTargetedInstructor)
    {
        Lesson = lesson;
        StudentName = studentName;
        InstructorName = instructorName;
        IsViewerInstructor = isViewerInstructor;
        IsCurrentUserStudent = isCurrentUserStudent;
        IsCurrentUserTargetedInstructor = isCurrentUserTargetedInstructor;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
        => OnPropertyChanged(nameof(ExpandGlyph));
}

public partial class IndividualLessonsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public const string ModeAddDirect = "Add directly";
    public const string ModeRequest = "Request from instructor(s)";

    public ObservableCollection<Fencer> AllFencers { get; } = new();
    public ObservableCollection<Fencer> Students { get; } = new();           // for the "new lesson" picker (excludes self)
    public ObservableCollection<Fencer> FilterStudents { get; } = new();     // for the filter picker (everyone active)
    public ObservableCollection<Fencer> Instructors { get; } = new();
    public ObservableCollection<IndividualLessonRowVm> Lessons { get; } = new();

    public List<string> InstructorFormModes { get; } = new() { ModeAddDirect, ModeRequest };
    [ObservableProperty] private string instructorFormMode = ModeAddDirect;

    public bool IsInstructorAddMode => IsInstructor && InstructorFormMode == ModeAddDirect;
    public bool IsInstructorRequestMode => IsInstructor && InstructorFormMode == ModeRequest;

    // Shows the "request from instructor(s)" UI for either students or instructors-in-request-mode.
    public bool ShowRequestPickerSection => IsStudentViewer || IsInstructorRequestMode;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private Fencer? filterStudent;
    [ObservableProperty] private Fencer? filterInstructor;

    // New lesson / request form
    [ObservableProperty] private bool isFormVisible;
    [ObservableProperty] private DateTime newDate = DateTime.Today;
    [ObservableProperty] private TimeSpan newTime = new(18, 0, 0);
    [ObservableProperty] private Fencer? newStudent;
    [ObservableProperty] private Fencer? newInstructor; // instructor "add direct" flow
    public ObservableCollection<FencerToggle> RequestTargets { get; } = new(); // student / instructor "request" flow
    [ObservableProperty] private string newTopic = "";
    [ObservableProperty] private string newNotes = "";
    [ObservableProperty] private string newNextIdea = "";

    public bool IsInstructor => _auth.IsLoggedInInstructor;
    public bool IsStudentViewer => _auth.IsLoggedInFencer && !_auth.IsLoggedInInstructor;
    public string CurrentUserId => _auth.CurrentFencer?.Id ?? "";

    public IndividualLessonsViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    partial void OnFilterStudentChanged(Fencer? value) => Rebuild();
    partial void OnFilterInstructorChanged(Fencer? value) => Rebuild();

    partial void OnInstructorFormModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsInstructorAddMode));
        OnPropertyChanged(nameof(IsInstructorRequestMode));
        OnPropertyChanged(nameof(ShowRequestPickerSection));
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            // Fan out the two independent reads in parallel.
            var fencersTask = _sheets.GetFencersAsync();
            var lessonsTask = _sheets.GetIndividualLessonsAsync();
            await Task.WhenAll(fencersTask, lessonsTask);

            var all = fencersTask.Result;
            var meId = CurrentUserId;

            // Pre-build off the UI thread, then swap in one pass.
            var students       = all.Where(f => f.Active && f.Id != meId).OrderBy(f => f.Name).ToList();
            var filterStudents = all.Where(f => f.Active).OrderBy(f => f.Name).ToList();
            var instructors    = all.Where(f => f.Active && f.IsInstructor).OrderBy(f => f.Name).ToList();
            var requestTargets = instructors.Where(i => i.Id != meId)
                                            .Select(i => new FencerToggle(i, false))
                                            .ToList();

            ReplaceAll(AllFencers, all);
            ReplaceAll(Students, students);
            ReplaceAll(FilterStudents, filterStudents);
            ReplaceAll(Instructors, instructors);
            ReplaceAll(RequestTargets, requestTargets);

            _cache = lessonsTask.Result;
            Rebuild();
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IList<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private List<IndividualLesson> _cache = new();

    private async Task ReloadLessonsAsync()
    {
        _cache = await _sheets.GetIndividualLessonsAsync();
        Rebuild();
    }

    private void Rebuild()
    {
        Lessons.Clear();
        var meId = CurrentUserId;

        IEnumerable<IndividualLesson> q = _cache;

        if (IsStudentViewer)
        {
            // Students only ever see lessons that involve them.
            q = q.Where(l =>
                l.StudentId == meId &&
                (l.Status == IndividualLessonStatus.Accepted ||
                 (l.Status == IndividualLessonStatus.Requested && l.RequestedInstructorIds.Count > 0)));
        }
        else if (IsInstructor)
        {
            // Apply optional filters.
            if (FilterStudent is not null)
                q = q.Where(l => l.StudentId == FilterStudent.Id);

            if (FilterInstructor is not null)
                q = q.Where(l =>
                    l.InstructorId == FilterInstructor.Id ||
                    (l.Status == IndividualLessonStatus.Requested &&
                     l.RequestedInstructorIds.Contains(FilterInstructor.Id)));
        }

        foreach (var l in q.OrderByDescending(x => x.Date))
        {
            var student = AllFencers.FirstOrDefault(f => f.Id == l.StudentId);
            var instructor = AllFencers.FirstOrDefault(f => f.Id == l.InstructorId);

            var targeted = l.Status == IndividualLessonStatus.Requested &&
                           l.RequestedInstructorIds.Contains(meId);

            Lessons.Add(new IndividualLessonRowVm(
                l,
                student?.Name ?? "(unknown)",
                instructor?.Name ?? (l.Status == IndividualLessonStatus.Requested
                                     ? $"Requested ({l.RequestedInstructorIds.Count})"
                                     : "(unknown)"),
                isViewerInstructor: IsInstructor,
                isCurrentUserStudent: l.StudentId == meId,
                isCurrentUserTargetedInstructor: targeted));
        }
    }

    [RelayCommand]
    private void ShowForm()
    {
        // Default: instructor adding directly; students always request.
        InstructorFormMode = ModeAddDirect;
        if (IsInstructor && FilterStudent is not null) NewStudent = FilterStudent;
        if (IsInstructor) NewInstructor = AllFencers.FirstOrDefault(f => f.Id == CurrentUserId);
        IsFormVisible = true;
    }

    [RelayCommand]
    private void ShowAddLessonForm()
    {
        InstructorFormMode = ModeAddDirect;
        if (FilterStudent is not null) NewStudent = FilterStudent;
        NewInstructor = AllFencers.FirstOrDefault(f => f.Id == CurrentUserId);
        IsFormVisible = true;
    }

    [RelayCommand]
    private void ShowRequestLessonForm()
    {
        InstructorFormMode = ModeRequest;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void HideForm() => IsFormVisible = false;

    [RelayCommand]
    private async Task SubmitFormAsync()
    {
        var when = NewDate.Date + NewTime;

        if (IsInstructor && InstructorFormMode == ModeAddDirect)
        {
            if (NewStudent is null) return;
            var lesson = new IndividualLesson
            {
                Date = when,
                StudentId = NewStudent.Id,
                InstructorId = (NewInstructor ?? _auth.CurrentFencer!)?.Id ?? CurrentUserId,
                Topic = NewTopic,
                Notes = NewNotes,
                NextIdea = NewNextIdea,
                Status = IndividualLessonStatus.Accepted
            };
            await _sheets.UpsertIndividualLessonAsync(lesson);
        }
        else if (IsInstructor && InstructorFormMode == ModeRequest)
        {
            // Instructor requests a lesson from one or more other instructors;
            // the current instructor takes the "student" role on the request.
            var targets = RequestTargets.Where(t => t.IsAttending).Select(t => t.Fencer.Id).ToList();
            if (targets.Count == 0) return;

            var lesson = new IndividualLesson
            {
                Date = when,
                StudentId = CurrentUserId,
                InstructorId = "",
                Topic = NewTopic,
                Status = IndividualLessonStatus.Requested,
                RequestedInstructorIds = targets
            };
            await _sheets.UpsertIndividualLessonAsync(lesson);
        }
        else if (IsStudentViewer)
        {
            var targets = RequestTargets.Where(t => t.IsAttending).Select(t => t.Fencer.Id).ToList();
            if (targets.Count == 0) return;

            var lesson = new IndividualLesson
            {
                Date = when,
                StudentId = CurrentUserId,
                InstructorId = "",
                Topic = NewTopic,
                Status = IndividualLessonStatus.Requested,
                RequestedInstructorIds = targets
            };
            await _sheets.UpsertIndividualLessonAsync(lesson);
        }

        ResetForm();
        IsFormVisible = false;
        await ReloadLessonsAsync();
    }

    private void ResetForm()
    {
        NewTopic = "";
        NewNotes = "";
        NewNextIdea = "";
        foreach (var t in RequestTargets) t.IsAttending = false;
    }

    [RelayCommand]
    private async Task AcceptRequestAsync(IndividualLessonRowVm row)
    {
        if (row is null) return;
        if (!IsInstructor) return;

        var l = row.Lesson;
        l.InstructorId = CurrentUserId;
        l.Status = IndividualLessonStatus.Accepted;
        l.RequestedInstructorIds = new();
        await _sheets.UpsertIndividualLessonAsync(l);
        await ReloadLessonsAsync();
    }

    [RelayCommand]
    private async Task RejectRequestAsync(IndividualLessonRowVm row)
    {
        if (row is null) return;
        if (!IsInstructor) return;

        var l = row.Lesson;
        // Remove the rejecting instructor from the target list.
        l.RequestedInstructorIds = l.RequestedInstructorIds
            .Where(id => id != CurrentUserId).ToList();

        // If nobody else can accept, the request is deleted.
        if (l.RequestedInstructorIds.Count == 0)
            l.Status = IndividualLessonStatus.Rejected;

        await _sheets.UpsertIndividualLessonAsync(l);
        await ReloadLessonsAsync();
    }

    [RelayCommand]
    private async Task SaveLessonAsync(IndividualLessonRowVm row)
    {
        if (row is null || !IsInstructor) return;
        await _sheets.UpsertIndividualLessonAsync(row.Lesson);
        row.IsExpanded = false;
        await ReloadLessonsAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterStudent = null;
        FilterInstructor = null;
    }

    [RelayCommand]
    private async Task DeleteLessonAsync(IndividualLessonRowVm row)
    {
        if (row is null || !IsInstructor) return;

        var page = Services.AppNavigationHelper.RootPage;
        if (page is not null)
        {
            var ok = await page.DisplayAlert(
                "Delete 1 on 1 lesson",
                $"Delete this lesson with {row.StudentName} on {row.DateText}?",
                "Delete", "Cancel");
            if (!ok) return;
        }

        // Reuse the Rejected status to mean "deleted" — cached reads skip these rows.
        row.Lesson.Status = IndividualLessonStatus.Rejected;
        row.Lesson.RequestedInstructorIds = new();

        try
        {
            await _sheets.UpsertIndividualLessonAsync(row.Lesson);
            Lessons.Remove(row);
            _cache.RemoveAll(l => l.Id == row.Lesson.Id);
        }
        catch (Exception ex)
        {
            if (page is not null)
                await page.DisplayAlert("Couldn't delete", ex.Message, "OK");
        }
    }
}