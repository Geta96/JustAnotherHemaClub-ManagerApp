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
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    public ObservableCollection<EditableTrainingRow> Trainings { get; } = new();

    public PastMonthVm(int year, int month) { Year = year; Month = month; }

    partial void OnNoteChanged(string value) => IsNoteDirty = true;
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}