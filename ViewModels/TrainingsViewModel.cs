using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TrainingsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<Fencer> AllFencers { get; } = new();

    // Attendee toggles for the "New training" form
    public ObservableCollection<FencerToggle> NewTrainingAttendees { get; } = new();
    public ObservableCollection<FencerToggle> FilteredAttendees { get; } = new();

    [ObservableProperty] private DateTime trainingDate = DateTime.Today;
    [ObservableProperty] private TimeSpan trainingTime = new(18, 0, 0);
    [ObservableProperty] private TimeSpan trainingEndTime = new(20, 0, 0);
    [ObservableProperty] private string topic = "";
    [ObservableProperty] private string attendeeFilter = "";
    [ObservableProperty] private bool isLoading;

    // --- Recurring training options for the "New training" form ---
    [ObservableProperty] private bool isRecurring;
    [ObservableProperty] private TimeSpan recurringTime = new(18, 0, 0);
    [ObservableProperty] private TimeSpan recurringEndTime = new(20, 0, 0);

    /// <summary>
    /// The form's weekday is derived from the picked TrainingDate, so instructors
    /// pick "the first session on this date" and from then on it repeats weekly.
    /// </summary>
    public DayOfWeek RecurringDayOfWeek => TrainingDate.DayOfWeek;
    public string RecurringSummary =>
        IsRecurring
            ? $"Repeats every {RecurringDayOfWeek} {RecurringTime:hh\\:mm}–{RecurringEndTime:hh\\:mm}, starting {TrainingDate:yyyy-MM-dd}."
            : "";

    public int SelectedCount => NewTrainingAttendees.Count(t => t.IsAttending);
    public string SelectedCountLabel =>
        $"{SelectedCount} of {NewTrainingAttendees.Count} selected";

    public ObservableCollection<PastMonthVm> Months { get; } = new();

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;
    public bool IsLoggedInRegularFencer => _auth.IsLoggedInFencer && !_auth.IsLoggedInInstructor;

    public TrainingsViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            // 3 reads in parallel instead of one-after-another.
            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var notesTask     = _sheets.GetMonthNotesAsync();
            await Task.WhenAll(fencersTask, trainingsTask, notesTask);

            // Fencers first so attendee toggles can rebuild against the new list.
            AllFencers.Clear();
            foreach (var f in fencersTask.Result) AllFencers.Add(f);
            RebuildAttendeeToggles();

            var trainings = trainingsTask.Result;
            var notes     = notesTask.Result;

            var noteByMonth = notes
                .GroupBy(n => (n.Year, n.Month))
                .ToDictionary(g => g.Key, g => g.Last().Note);

            var myId = _auth.CurrentFencer?.Id;

            // Build months locally, swap once.
            var built = new List<PastMonthVm>();
            var grouped = trainings
                .GroupBy(s => (s.Date.Year, s.Date.Month))
                .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month);

            foreach (var g in grouped)
            {
                var mvm = new PastMonthVm(g.Key.Year, g.Key.Month);
                if (noteByMonth.TryGetValue(g.Key, out var n)) mvm.Note = n;
                mvm.IsNoteDirty = false;
                foreach (var t in g.OrderByDescending(s => s.Date))
                    mvm.Trainings.Add(new EditableTrainingRow(t, AllFencers, myId));
                built.Add(mvm);
            }

            Months.Clear();
            foreach (var mv in built) Months.Add(mv);
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    /// <summary>
    /// Pre-checks the given fencer IDs in the "New training" form
    /// (e.g. when navigating from another page).
    /// </summary>
    public void PreselectAttendees(IEnumerable<string> fencerIds)
    {
        var set = new HashSet<string>(fencerIds);
        foreach (var t in NewTrainingAttendees)
            t.IsAttending = set.Contains(t.Fencer.Id);
        RaiseSelectedCountChanged();
    }

    private void RebuildAttendeeToggles()
    {
        foreach (var t in NewTrainingAttendees)
            t.PropertyChanged -= OnToggleChanged;

        NewTrainingAttendees.Clear();
        foreach (var f in AllFencers.Where(f => f.Active).OrderBy(f => f.Name))
        {
            var t = new FencerToggle(f, false);
            t.PropertyChanged += OnToggleChanged;
            NewTrainingAttendees.Add(t);
        }

        ApplyFilter();
        RaiseSelectedCountChanged();
    }

    private void OnToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FencerToggle.IsAttending))
            RaiseSelectedCountChanged();
    }

    private void RaiseSelectedCountChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
    }

    partial void OnAttendeeFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredAttendees.Clear();
        var q = (AttendeeFilter ?? "").Trim();
        IEnumerable<FencerToggle> src = NewTrainingAttendees;
        if (q.Length > 0)
            src = src.Where(t =>
                (t.Fencer.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (t.Fencer.Username ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var t in src) FilteredAttendees.Add(t);
    }

    [RelayCommand]
    private void SelectAllVisibleAttendees()
    {
        foreach (var t in FilteredAttendees) t.IsAttending = true;
    }

    [RelayCommand]
    private void ClearAttendees()
    {
        foreach (var t in NewTrainingAttendees) t.IsAttending = false;
    }

    [RelayCommand]
    public async Task SaveTrainingAsync()
    {
        var startDate = TrainingDate.Date + TrainingTime;
        var endDate   = TrainingDate.Date + TrainingEndTime;

        var t = new TrainingSession
        {
            Date    = startDate,
            EndDate = endDate,
            Topic   = Topic,
            AttendeeFencerIds = NewTrainingAttendees
                .Where(x => x.IsAttending)
                .Select(x => x.Fencer.Id)
                .ToList()
        };
        await _sheets.UpsertTrainingAsync(t);

        if (IsRecurring)
        {
            try
            {
                var rule = new RecurringTrainingRule
                {
                    DayOfWeek          = TrainingDate.DayOfWeek,
                    TimeOfDay          = RecurringTime,
                    EndTimeOfDay       = RecurringEndTime,
                    Topic              = Topic,
                    StartDate          = TrainingDate.Date.AddDays(7),
                    CreatedByFencerId  = _auth.CurrentFencer?.Id ?? ""
                };
                await _sheets.UpsertRecurringTrainingAsync(rule);
            }
            catch (Exception ex)
            {
                var page = Application.Current?.MainPage;
                if (page is not null)
                    await page.DisplayAlert(
                        "Recurring rule not saved",
                        $"The training was created, but the weekly rule could not be saved:\n{ex.Message}",
                        "OK");
            }
        }

        await LoadAsync(showSpinner: false);

        Topic = "";
        AttendeeFilter = "";
        IsRecurring = false;
    }

    [RelayCommand]
    public async Task SaveTrainingEditAsync(EditableTrainingRow row)
    {
        if (row is null || !row.IsDirty) return;
        var updated = row.ToUpdatedTraining();
        await _sheets.UpsertTrainingAsync(updated);
        row.IsDirty = false;
    }

    [RelayCommand]
    public async Task SaveMonthNoteAsync(PastMonthVm month)
    {
        if (month is null || !month.IsNoteDirty) return;
        await _sheets.UpsertMonthNoteAsync(new MonthNote
        {
            Year = month.Year,
            Month = month.Month,
            Note = month.Note ?? ""
        });
        month.IsNoteDirty = false;
    }

    [RelayCommand]
    public async Task AttendTrainingAsync(EditableTrainingRow row)
    {
        if (row is null) return;
        if (_auth.IsLoggedInInstructor) return;
        var me = _auth.CurrentFencer;
        if (me is null) return;
        if (row.Training.AttendeeFencerIds.Contains(me.Id)) return;

        row.Training.AttendeeFencerIds.Add(me.Id);

        var wasDirty = row.IsDirty;

        try
        {
            await _sheets.UpsertTrainingAsync(row.Training);
            row.CurrentUserAttending = true;
            row.IsDirty = wasDirty; // never become dirty just from self-attending
        }
        catch
        {
            row.Training.AttendeeFencerIds.Remove(me.Id);
            throw;
        }
    }

    [RelayCommand]
    public async Task DeleteTrainingAsync(EditableTrainingRow row)
    {
        if (row is null || !_auth.IsLoggedInInstructor) return;

        await _sheets.DeleteTrainingAsync(row.Training.Id);

        // Remove from the in-memory month groups so the UI updates without a full reload.
        foreach (var month in Months)
        {
            if (month.Trainings.Remove(row)) break;
        }
    }
}