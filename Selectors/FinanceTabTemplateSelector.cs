using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Selectors;

public class FinanceTabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MonthlyTemplate { get; set; }
    public DataTemplate? YearlyTemplate  { get; set; }
    public DataTemplate? AllTimeTemplate { get; set; }
    public DataTemplate? PricesTemplate  { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => (item as FinanceTab)?.Key switch
        {
            FinanceViewModel.TabYearly  => YearlyTemplate!,
            FinanceViewModel.TabAllTime => AllTimeTemplate!,
            FinanceViewModel.TabPrices  => PricesTemplate!,
            _                           => MonthlyTemplate!
        };
}