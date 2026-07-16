namespace JustAnotherHemaClub.Services;

public interface IDialogService
{
    Task ShowAsync(string title, string message, string cancel = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
}