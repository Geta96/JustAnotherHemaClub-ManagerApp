using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class EditableTrainingRow : ObservableObject
{
    public TrainingSession Training { get; }

    [ObservableProperty] private string topic;
    [ObservableProperty] private TimeSpan endTime;
    public ObservableCollection<FencerToggle> Fencers { get; }

    [ObservableProperty] private bool isDirty;

    /// <summary>True when the currently logged-in fencer is in the attendee list.</summary>
    [ObservableProperty] private bool currentUserAttending;

    /// <summary>Collapsed view by default; instructors expand to edit.</summary>
    [ObservableProperty] private bool isExpanded;

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>True when there is a logged-in non-instructor who could still attend this session.</summary>
    public bool CanCurrentUserAttend => !string.IsNullOrEmpty(_currentUserFencerId) && !CurrentUserAttending;

    public string TimeRangeText =>
        $"{Training.Date:HH\\:mm}–{EndTime:hh\\:mm}";

    private readonly string? _currentUserFencerId;

    public EditableTrainingRow(TrainingSession training, IEnumerable<Fencer> allFencers, string? currentUserFencerId = null)
    {
        Training = training;
        topic = training.Topic;
        endTime = training.EndDate == default ? training.Date.TimeOfDay.Add(TimeSpan.FromMinutes(90))
                                              : training.EndDate.TimeOfDay;
        _currentUserFencerId = currentUserFencerId;
        currentUserAttending = !string.IsNullOrEmpty(currentUserFencerId) &&
                               training.AttendeeFencerIds.Contains(currentUserFencerId);

        Fencers = new ObservableCollection<FencerToggle>(
            allFencers.Select(f =>
            {
                var t = new FencerToggle(f, training.AttendeeFencerIds.Contains(f.Id));
                t.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FencerToggle.IsAttending))
                    {
                        IsDirty = true;

                        // If the instructor (the current user) toggles their own
                        // row, keep CurrentUserAttending in sync so the header
                        // Attend button / green tick reflect it immediately.
                        if (!string.IsNullOrEmpty(_currentUserFencerId) &&
                            f.Id == _currentUserFencerId)
                        {
                            CurrentUserAttending = t.IsAttending;
                        }
                    }
                };
                return t;
            }));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnTopicChanged(string value) => IsDirty = true;
    partial void OnEndTimeChanged(TimeSpan value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(TimeRangeText));
    }

    partial void OnCurrentUserAttendingChanged(bool value)
        => OnPropertyChanged(nameof(CanCurrentUserAttend));

    partial void OnIsExpandedChanged(bool value)
        => OnPropertyChanged(nameof(ExpandGlyph));

    public TrainingSession ToUpdatedTraining() => new()
    {
        Id    = Training.Id,
        Date  = Training.Date,
        EndDate = Training.Date.Date + EndTime,
        Topic = Topic,
        AttendeeFencerIds = Fencers.Where(f => f.IsAttending).Select(f => f.Fencer.Id).ToList()
    };
}