using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TrainingsPage : ContentPage
{
    private readonly TrainingsViewModel _vm;
    private readonly ICacheControl _cache;
    private readonly IServiceProvider _services;

    public TrainingsPage(TrainingsViewModel vm, ICacheControl cache, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _cache = cache;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateTrainings();
        _cache.InvalidateFencers();
        _cache.InvalidateMonthNotes();
        await _vm.LoadAsync(showSpinner: true);
    }

    private void OnAttendeeRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is FencerToggle t)
            t.IsAttending = !t.IsAttending;
    }

    private async void OnNewTrainingClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<NewTrainingPage>();
        await Navigation.PushAsync(page);
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

        await _vm.DeleteTrainingCommand.ExecuteAsync(row);
    }
}