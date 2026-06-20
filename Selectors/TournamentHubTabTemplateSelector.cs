using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Selectors;

public class TournamentHubTabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PoolsTemplate { get; set; }
    public DataTemplate? ElimTemplate { get; set; }
    public DataTemplate? PoolStandingsTemplate { get; set; }
    public DataTemplate? FinalStandingsTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => (item as TournamentHubTab)?.Key switch
        {
            TournamentHubViewModel.TabElimination    => ElimTemplate!,
            TournamentHubViewModel.TabPoolStandings  => PoolStandingsTemplate!,
            TournamentHubViewModel.TabFinalStandings => FinalStandingsTemplate!,
            _                                        => PoolsTemplate!
        };
}