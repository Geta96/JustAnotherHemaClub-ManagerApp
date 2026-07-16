namespace JustAnotherHemaClub.Services;

public sealed class DialogService : IDialogService
{
    public Task ShowAsync(string title, string message, string cancel = "OK")
    {
        var page = AppNavigationHelper.RootPage;
        return page is null ? Task.CompletedTask : page.DisplayAlert(title, message, cancel);
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var page = AppNavigationHelper.RootPage;
        return page is null ? Task.FromResult(true) : page.DisplayAlert(title, message, accept, cancel);
    }
}