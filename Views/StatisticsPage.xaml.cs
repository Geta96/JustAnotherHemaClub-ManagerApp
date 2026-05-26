using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _vm;

    public StatisticsPage(StatisticsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}