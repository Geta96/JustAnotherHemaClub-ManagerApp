using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class PoolRowVm : ObservableObject
{
    public Pool Pool { get; }
    public ObservableCollection<PoolMatchRowVm> Matches { get; } = new();

    /// <summary>Display-ready, comma-separated roster for this pool (top-of-card header).</summary>
    public string FencerNamesText { get; }

    /// <summary>True when the current user is allowed to edit (organiser). Set by the parent VM.</summary>
    public bool CanEdit { get; }

    /// <summary>
    /// True once the elimination bracket has been generated. Locks pools from being reopened,
    /// because the bracket was seeded from the pool standings and changing them would corrupt it.
    /// </summary>
    public bool BracketStarted { get; }

    [ObservableProperty] private bool isExpanded = false;

    public PoolRowVm(Pool pool, IEnumerable<string> fencerNames, bool canEdit, bool bracketStarted)
    {
        Pool = pool;
        FencerNamesText = string.Join(", ", fencerNames);
        CanEdit = canEdit;
        BracketStarted = bracketStarted;
    }

    public string Title => Pool.Name;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>Inverse of <see cref="IsExpanded"/>, used to show the roster on collapsed cards.</summary>
    public bool IsCollapsed => !IsExpanded;

    public string ProgressText
    {
        get
        {
            int done = Matches.Count(m => m.IsFinished);
            int total = Matches.Count;
            return total == 0 ? "(no matches)" : $"{done}/{total} matches done";
        }
    }
    public string NextMatchText
    {
        get
        {
            var next = Matches.FirstOrDefault(m => !m.IsFinished);
            return next is null ? "All matches complete" : $"Next: {next.LeftName}  vs  {next.RightName}";
        }
    }
    public bool IsClosed => Pool.IsClosed;
    public string ClosedLabel => Pool.IsClosed ? "Closed" : "Open";
    public string CloseButtonText => Pool.IsClosed ? "Reopen Pool" : "Close Pool";
    public bool CanClose
    {
        get
        {
            // Closing a pool is only allowed once every match is finished.
            return !Pool.IsClosed && Matches.Count > 0 && Matches.All(m => m.IsFinished);
        }
    }

    /// <summary>
    /// Reopening a closed pool is forbidden once the elimination bracket exists —
    /// the bracket's seeding depends on the pool's final results.
    /// </summary>
    public bool CanReopen => Pool.IsClosed && !BracketStarted;

    /// <summary>True when the Close/Reopen button should be tappable.</summary>
    public bool CanToggleClosed => CanClose || CanReopen;

    /// <summary>True when the Close/Reopen button should even be shown to the user.</summary>
    public bool CanShowCloseButton => CanEdit && CanToggleClosed;

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandGlyph));
        OnPropertyChanged(nameof(IsCollapsed));
    }

    public void RaiseProgressChanged()
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(NextMatchText));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanReopen));
        OnPropertyChanged(nameof(CanToggleClosed));
        OnPropertyChanged(nameof(CanShowCloseButton));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(ClosedLabel));
        OnPropertyChanged(nameof(CloseButtonText));
    }
}