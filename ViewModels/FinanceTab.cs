namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Carousel item used by the Finance page. Holds a tag (for the
/// template selector) and a reference to the shared FinanceViewModel.
/// </summary>
public sealed class FinanceTab
{
    public string Key { get; }
    public FinanceViewModel Vm { get; }

    public FinanceTab(string key, FinanceViewModel vm)
    {
        Key = key;
        Vm  = vm;
    }
}