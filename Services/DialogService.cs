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

    public Task<string?> ChooseAsync(string title, string cancel, params string[] options)
    {
        var page = AppNavigationHelper.RootPage;
        return page is null
            ? Task.FromResult<string?>(null)
            : page.DisplayActionSheet(title, cancel, destruction: null, options)!;
    }

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel,
                                     string? initialValue = null)
    {
        var page = AppNavigationHelper.RootPage;
        return page is null
            ? Task.FromResult<string?>(null)
            : page.DisplayPromptAsync(title, message, accept, cancel,
                                      initialValue: initialValue,
                                      keyboard: Keyboard.Numeric)!;
    }
}