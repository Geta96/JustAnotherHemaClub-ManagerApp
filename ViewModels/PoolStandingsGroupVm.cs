using System.Collections.ObjectModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>A single pool's standings card: header + ordered <see cref="PoolStandingRowVm"/> rows.</summary>
public sealed class PoolStandingsGroupVm
{
    public string PoolId { get; }
    public string Title { get; }
    public ObservableCollection<PoolStandingRowVm> Rows { get; } = new();

    public PoolStandingsGroupVm(Pool pool)
    {
        PoolId = pool.Id;
        Title  = pool.Name;
    }
}