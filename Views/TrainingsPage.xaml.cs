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

    private void OnAttendeeRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is FencerToggle t)
            t.IsAttending = !t.IsAttending;
    }
}