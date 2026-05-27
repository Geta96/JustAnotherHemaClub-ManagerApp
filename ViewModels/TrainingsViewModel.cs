using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TrainingsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;

    public ObservableCollection<Fencer> AllFencers { get; } = new();
    public ObservableCollection<Fencer> Selected { get; } = new();

    [ObservableProperty] private DateTime trainingDate = DateTime.Today;
    [ObservableProperty] private string topic = "";

    public ObservableCollection<PastMonthVm> Months { get; } = new();

    public TrainingsViewModel(IGoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    public async Task LoadAsync()
    {
        AllFencers.Clear();
        foreach (var f in await _sheets.GetFencersAsync()) AllFencers.Add(f);

        var trainings = await _sheets.GetTrainingsAsync();
        var notes = await _sheets.GetMonthNotesAsync();

        var noteByMonth = notes
            .GroupBy(n => (n.Year, n.Month))
            .ToDictionary(g => g.Key, g => g.Last().Note);

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
                mvm.Trainings.Add(new EditableTrainingRow(t, AllFencers));

            Months.Add(mvm);
        }
    }

    [RelayCommand]
    public async Task SaveTrainingAsync()
    {
        var t = new TrainingSession
        {
            Date = TrainingDate,
            Topic = Topic,
            AttendeeFencerIds = Selected.Select(f => f.Id).ToList()
        };
        await _sheets.UpsertTrainingAsync(t);

        await LoadAsync();

        Selected.Clear();
        Topic = "";
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
}