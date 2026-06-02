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
    [ObservableProperty] private string topic = "";
    [ObservableProperty] private string attendeeFilter = "";
    [ObservableProperty] private bool isLoading;

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
            AllFencers.Clear();
            foreach (var f in await _sheets.GetFencersAsync()) AllFencers.Add(f);

            RebuildAttendeeToggles();

            var trainings = await _sheets.GetTrainingsAsync();
            var notes = await _sheets.GetMonthNotesAsync();

            var noteByMonth = notes
                .GroupBy(n => (n.Year, n.Month))
                .ToDictionary(g => g.Key, g => g.Last().Note);

            var myId = _auth.CurrentFencer?.Id;

            Months.Clear();
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

                Months.Add(mvm);
            }
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
        var t = new TrainingSession
        {
            Date = TrainingDate,
            Topic = Topic,
            AttendeeFencerIds = NewTrainingAttendees
                .Where(x => x.IsAttending)
                .Select(x => x.Fencer.Id)
                .ToList()
        };
        await _sheets.UpsertTrainingAsync(t);

        await LoadAsync(showSpinner: false);

        Topic = "";
        AttendeeFilter = "";
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
}