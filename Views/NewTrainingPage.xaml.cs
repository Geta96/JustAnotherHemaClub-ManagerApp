using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class NewTrainingPage : ContentPage
{
    private readonly TrainingsViewModel _vm;

    // Set by the caller before pushing this page; consumed (and cleared) in OnAppearing.
    private bool _startAsWeekly;

    public NewTrainingPage(TrainingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>
    /// Call before navigating to pre-check the "Repeat weekly on this day" checkbox.
    /// </summary>
    public void PrepareForWeekly() => _startAsWeekly = true;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();

        if (_startAsWeekly)
        {
            _vm.IsRecurring = true;
            _startAsWeekly = false;
        }
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