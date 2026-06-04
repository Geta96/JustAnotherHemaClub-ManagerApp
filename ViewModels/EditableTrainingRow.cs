using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class EditableTrainingRow : ObservableObject
{
    public TrainingSession Training { get; }

    [ObservableProperty] private string topic;
    public ObservableCollection<FencerToggle> Fencers { get; }

    [ObservableProperty] private bool isDirty;

    /// <summary>True when the currently logged-in fencer is in the attendee list.</summary>
    [ObservableProperty] private bool currentUserAttending;

    /// <summary>Collapsed view by default; instructors expand to edit.</summary>
    [ObservableProperty] private bool isExpanded;

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>True when there is a logged-in non-instructor who could still attend this session.</summary>
    public bool CanCurrentUserAttend => !string.IsNullOrEmpty(_currentUserFencerId) && !CurrentUserAttending;

    private readonly string? _currentUserFencerId;

    public EditableTrainingRow(TrainingSession training, IEnumerable<Fencer> allFencers, string? currentUserFencerId = null)
    {
        Training = training;
        topic = training.Topic;
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
                        IsDirty = true;
                };
                return t;
            }));
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnTopicChanged(string value) => IsDirty = true;

    partial void OnCurrentUserAttendingChanged(bool value)
        => OnPropertyChanged(nameof(CanCurrentUserAttend));

    partial void OnIsExpandedChanged(bool value)
        => OnPropertyChanged(nameof(ExpandGlyph));

    public TrainingSession ToUpdatedTraining() => new()
    {
        Id = Training.Id,
        Date = Training.Date,
        Topic = Topic,
        AttendeeFencerIds = Fencers.Where(f => f.IsAttending).Select(f => f.Fencer.Id).ToList()
    };
}