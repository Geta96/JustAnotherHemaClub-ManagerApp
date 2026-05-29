using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;

namespace JustAnotherHemaClub.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public const string InstagramUrl =
        "https://www.instagram.com/just.another.hema.club?igsh=MXZudjI3MDJ4eWw1aA==";

    public const string FacebookUrl =
        "https://www.facebook.com/share/18VtVUQPW5/";

    public const string TelegramUrl =
        "https://t.me/+6EUfQu6kXPY4NWM8";

    [RelayCommand]
    private Task OpenInstagramAsync() =>
        Launcher.Default.OpenAsync(InstagramUrl);

    [RelayCommand]
    private Task OpenFacebookAsync() =>
        Launcher.Default.OpenAsync(FacebookUrl);

    [RelayCommand]
    private Task OpenTelegramAsync() =>
        Launcher.Default.OpenAsync(TelegramUrl);
}