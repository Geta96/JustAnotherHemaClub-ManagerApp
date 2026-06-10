using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>Lightweight row for a match inside a pool's fight list.</summary>
public partial class PoolMatchRowVm : ObservableObject
{
    public Match Match { get; private set; }
    public int OrderNumber { get; }
    public string LeftName  { get; }
    public string RightName { get; }

    public PoolMatchRowVm(Match match, int orderNumber, string leftName, string rightName)
    {
        Match = match;
        OrderNumber = orderNumber;
        LeftName = leftName;
        RightName = rightName;
    }

    public string Title       => $"#{OrderNumber + 1}  {LeftName}  vs  {RightName}";
    public string ScoreText   => Match.Status == MatchStatus.Pending ? "—"
                              : $"{Match.LeftScore} – {Match.RightScore}";
    public string StatusText  => Match.Status switch
    {
        MatchStatus.Pending    => "Pending",
        MatchStatus.InProgress => "In progress",
        MatchStatus.Finished   => "Finished",
        _                      => ""
    };
    public string StatusColor => Match.Status switch
    {
        MatchStatus.Pending    => "#8A8A8A",
        MatchStatus.InProgress => "#476FB5",
        MatchStatus.Finished   => "#1F8A2E",
        _                      => "#8A8A8A"
    };
    public bool IsFinished => Match.Status == MatchStatus.Finished;
    public bool IsLockedByOther =>
        !string.IsNullOrEmpty(Match.LockedByUserId) &&
        Match.LockedAtUtc.HasValue &&
        DateTime.UtcNow - Match.LockedAtUtc.Value < TimeSpan.FromMinutes(2);
    public string LockText => IsLockedByOther ? "🔒 In use" : "";

    /// <summary>Replace the underlying match (e.g. after a polling update).</summary>
    public void Patch(Match latest)
    {
        Match = latest;
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsLockedByOther));
        OnPropertyChanged(nameof(LockText));
    }
}