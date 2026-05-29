using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class FencersPage : ContentPage
{
    private readonly FencersViewModel _vm;

    public FencersPage(FencersViewModel vm)
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