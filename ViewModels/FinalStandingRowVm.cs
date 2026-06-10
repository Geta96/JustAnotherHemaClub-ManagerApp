namespace JustAnotherHemaClub.ViewModels;

/// <summary>One row of the final standings tab — pure display values.</summary>
public sealed class FinalStandingRowVm
{
    public int Place { get; }
    public string Name { get; }
    public string DefeatedByName { get; }
    public string EliminatedAt { get; }

    public FinalStandingRowVm(int place, string name, string defeatedByName, string eliminatedAt)
    {
        Place          = place;
        Name           = name;
        DefeatedByName = defeatedByName;
        EliminatedAt   = eliminatedAt;
    }

    public string PlaceText => Place switch
    {
        1 => "🥇 1",
        2 => "🥈 2",
        3 => "🥉 3",
        _ => $"#{Place}"
    };

    public bool HasDefeatedBy => !string.IsNullOrEmpty(DefeatedByName);
    public string DefeatedByText => HasDefeatedBy ? $"Lost to {DefeatedByName}" : "Champion";
}