using System.Collections.ObjectModel;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>A single round (or the bronze slot) rendered as one column in the bracket view.</summary>
public sealed class ElimRoundColumnVm
{
    public string Title { get; }
    public ObservableCollection<ElimMatchRowVm> Matches { get; } = new();

    public ElimRoundColumnVm(string title) => Title = title;
}