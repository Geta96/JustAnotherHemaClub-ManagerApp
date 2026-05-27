using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerToggle : ObservableObject
{
    public Fencer Fencer { get; }

    [ObservableProperty] private bool isAttending;

    public FencerToggle(Fencer fencer, bool attending)
    {
        Fencer = fencer;
        isAttending = attending;
    }
}

public partial class EditableTrainingRow : ObservableObject
{
    public TrainingSession Training { get; }

    [ObservableProperty] private string topic;
    public ObservableCollection<FencerToggle> Fencers { get; }

    [ObservableProperty] private bool isDirty;

    public EditableTrainingRow(TrainingSession training, IEnumerable<Fencer> allFencers)
    {
        Training = training;
        topic = training.Topic;
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

    partial void OnTopicChanged(string value) => IsDirty = true;

    public TrainingSession ToUpdatedTraining() => new()
    {
        Id = Training.Id,
        Date = Training.Date,
        Topic = Topic,
        AttendeeFencerIds = Fencers.Where(f => f.IsAttending).Select(f => f.Fencer.Id).ToList()
    };
}