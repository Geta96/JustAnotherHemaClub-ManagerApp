using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TrainingsHubPage : ContentPage
{
    private readonly TrainingsHubViewModel _vm;
    private readonly ICacheControl _cache;
    private readonly IServiceProvider _services;

    public TrainingsHubPage(TrainingsHubViewModel vm, ICacheControl cache, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Let Shell finish its transition before hitting the cache layer.
        await Task.Yield();
        await _vm.LoadAllAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateTrainings();
        _cache.InvalidateFencers();
        _cache.InvalidateMonthNotes();
        _cache.InvalidateRecurringTrainings();
        _cache.InvalidateIndividualLessons();
        await _vm.LoadAllAsync(showSpinner: true);
    }

    // ----- Trainings tab handlers -----

    private async void OnNewTrainingClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<NewTrainingPage>();
        await Navigation.PushAsync(page);
    }

    private void OnAttendeeRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is FencerToggle t)
            t.IsAttending = !t.IsAttending;
    }

    private async void OnDeleteTrainingClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not EditableTrainingRow row)
            return;

        var confirm = await DisplayAlert(
            "Delete training",
            $"Delete the training on {row.Training.Date:yyyy-MM-dd}? This cannot be undone.",
            "Delete",
            "Cancel");

        if (!confirm) return;

        await _vm.TrainingsVm.DeleteTrainingCommand.ExecuteAsync(row);
    }

    // ----- Weekly tab handler -----

    private async void OnAddWeeklyTrainingClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<NewTrainingPage>();
        page.PrepareForWeekly();
        await Navigation.PushAsync(page);
    }
}