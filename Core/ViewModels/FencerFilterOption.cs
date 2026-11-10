namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// One entry in the Finance Monthly-tab "filter by fencer" picker. A null
/// <see cref="Id"/> represents the "All fencers" sentinel that clears the filter.
/// </summary>
public sealed class FencerFilterOption
{
    public string? Id { get; }
    public string Name { get; }

    public FencerFilterOption(string? id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>The "show everyone" sentinel shown at the top of the picker.</summary>
    public static FencerFilterOption All { get; } = new(null, "All fencers");

    public override string ToString() => Name;
}
