using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JustAnotherHemaClub.ViewModels;

public partial class PastMonthVm : ObservableObject
{
    public int Year { get; }
    public int Month { get; }
    public string Title => new DateTime(Year, Month, 1).ToString("yyyy MMMM");

    [ObservableProperty] private string note = "";
    [ObservableProperty] private bool isNoteDirty;

    [ObservableProperty] private bool isExpanded;
    public string ExpandGlyph => IsExpanded ? "\u25BE" : "\u25B8";

    public ObservableCollection<EditableTrainingRow> Trainings { get; } = new();

    /// <summary>
    /// Full, unfiltered set of trainings for the month. <see cref="Trainings"/>
    /// is a (possibly) filtered view of this list, driven by the Trainings page's
    /// "filter by fencer" picker.
    /// </summary>
    private readonly List<EditableTrainingRow> _allTrainings = new();

    /// <summary>Read-only view of the full, unfiltered trainings set for the month.</summary>
    public IReadOnlyList<EditableTrainingRow> AllTrainings => _allTrainings;

    /// <summary>True when the current (filtered) view has no trainings to show.</summary>
    public bool HasNoVisibleTrainings => Trainings.Count == 0;

    public PastMonthVm(int year, int month) { Year = year; Month = month; }

    partial void OnNoteChanged(string value) => IsNoteDirty = true;
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Adds a training row to both the master list and the currently-visible
    /// collection. Used while building the month so the filter has a full set
    /// to work from.
    /// </summary>
    public void AddTraining(EditableTrainingRow row)
    {
        _allTrainings.Add(row);
        Trainings.Add(row);
    }

    /// <summary>Removes a training row from both the master and visible lists.</summary>
    public bool RemoveTraining(EditableTrainingRow row)
    {
        _allTrainings.Remove(row);
        var removed = Trainings.Remove(row);
        if (removed) OnPropertyChanged(nameof(HasNoVisibleTrainings));
        return removed;
    }

    /// <summary>
    /// Rebuilds the visible <see cref="Trainings"/> collection from the master
    /// list, keeping only sessions the fencer with <paramref name="fencerId"/>
    /// attended (or all sessions when it is null/empty).
    /// </summary>
    public void ApplyFencerFilter(string? fencerId)
    {
        Trainings.Clear();
        var rows = string.IsNullOrWhiteSpace(fencerId)
            ? _allTrainings
            : _allTrainings.Where(r => r.Training.AttendeeFencerIds.Contains(fencerId));
        foreach (var row in rows) Trainings.Add(row);
        OnPropertyChanged(nameof(HasNoVisibleTrainings));
    }
}
