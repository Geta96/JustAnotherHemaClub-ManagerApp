namespace JustAnotherHemaClub.Services;

public interface IDialogService
{
    Task ShowAsync(string title, string message, string cancel = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <summary>Shows an action sheet; returns the chosen button text, or null/cancel text if dismissed.</summary>
    Task<string?> ChooseAsync(string title, string cancel, params string[] options);

    /// <summary>Prompts for free-text (numeric) input; returns null if cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string accept, string cancel,
                              string? initialValue = null);
}
