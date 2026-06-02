using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class NewTrainingPage : ContentPage
{
    private readonly TrainingsViewModel _vm;

    public NewTrainingPage(TrainingsViewModel vm)
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

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        try
        {
            await _vm.SaveTrainingCommand.ExecuteAsync(null);
            await DisplayAlert("Created", "Training has been created.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Could not create training", ex.Message, "OK");
        }
    }
}