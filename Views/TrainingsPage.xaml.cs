using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TrainingsPage : ContentPage
{
    private readonly TrainingsViewModel _vm;

    public TrainingsPage(TrainingsViewModel vm)
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