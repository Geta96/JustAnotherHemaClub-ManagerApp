using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class SessionsPage : ContentPage
{
    private readonly SessionsViewModel _vm;

    public SessionsPage(SessionsViewModel vm)
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