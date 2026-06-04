using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class IndividualLessonsPage : ContentPage
{
    private readonly IndividualLessonsViewModel _vm;
    private readonly ICacheControl _cache;

    public IndividualLessonsPage(IndividualLessonsViewModel vm, ICacheControl cache)
    {
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("XAML LOAD FAILED: " + ex);
            throw;
        }
        BindingContext = _vm = vm;
        _cache = cache;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Yield once so Shell finishes its navigation animation before we hit the network.
        await Task.Yield();
        await _vm.LoadAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        _cache.InvalidateIndividualLessons();
        _cache.InvalidateFencers();
        await _vm.LoadAsync(showSpinner: true);
    }
}