using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;

    public ObservableCollection<Fencer> AllFencers { get; } = new();
    public ObservableCollection<Fencer> Selected { get; } = new();
    public ObservableCollection<TrainingSession> Sessions { get; } = new();

    [ObservableProperty] private DateTime sessionDate = DateTime.Today;
    [ObservableProperty] private string topic = "";

    public SessionsViewModel(IGoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    public async Task LoadAsync()
    {
        AllFencers.Clear();
        foreach (var f in await _sheets.GetFencersAsync()) AllFencers.Add(f);
        Sessions.Clear();
        foreach (var s in (await _sheets.GetSessionsAsync()).OrderByDescending(s => s.Date))
            Sessions.Add(s);
    }

    [RelayCommand]
    public void ToggleAttendee(Fencer f)
    {
        if (Selected.Contains(f)) Selected.Remove(f);
        else Selected.Add(f);
    }

    [RelayCommand]
    public async Task SaveSessionAsync()
    {
        var s = new TrainingSession
        {
            Date = SessionDate,
            Topic = Topic,
            AttendeeFencerIds = Selected.Select(f => f.Id).ToList()
        };
        await _sheets.UpsertSessionAsync(s);
        Sessions.Insert(0, s);
        Selected.Clear();
        Topic = "";
    }
}