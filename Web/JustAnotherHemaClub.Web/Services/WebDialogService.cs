using JustAnotherHemaClub.Services;
using Microsoft.JSInterop;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Browser implementation of the Core IDialogService using window.alert /
/// confirm / prompt via JS interop. ChooseAsync falls back to a prompt listing
/// the options by number; replace with a proper modal component when the UI
/// component library is in place.
/// </summary>
public sealed class WebDialogService : IDialogService
{
    private readonly IJSRuntime _js;

    public WebDialogService(IJSRuntime js) => _js = js;

    public async Task ShowAsync(string title, string message, string cancel = "OK")
        => await _js.InvokeVoidAsync("window.alert", $"{title}\n\n{message}");

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
        => await _js.InvokeAsync<bool>("window.confirm", $"{title}\n\n{message}");

    public async Task<string?> ChooseAsync(string title, string cancel, params string[] options)
    {
        var list = string.Join("\n", options.Select((o, i) => $"{i + 1}. {o}"));
        var raw = await _js.InvokeAsync<string?>("window.prompt", $"{title}\n{list}", "");
        if (int.TryParse(raw, out var idx) && idx >= 1 && idx <= options.Length)
            return options[idx - 1];
        return null;
    }

    public async Task<string?> PromptAsync(string title, string message, string accept, string cancel,
                                           string? initialValue = null)
        => await _js.InvokeAsync<string?>("window.prompt", $"{title}\n{message}", initialValue ?? "");
}