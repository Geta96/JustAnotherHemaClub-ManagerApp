using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class NewsPage : ContentPage
{
    private readonly NewsViewModel _vm;
    private readonly NotificationSyncService _notificationSync;

    public NewsPage(NewsViewModel vm, NotificationSyncService notificationSync)
    {
        InitializeComponent();
        _vm = vm;
        _notificationSync = notificationSync;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        _ = Task.Run(async () =>
        {
            try { await _notificationSync.CheckAndNotifyAsync(); }
            catch { }
        });
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        await _vm.LoadAsync();
    }
}
