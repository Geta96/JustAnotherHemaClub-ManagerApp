// Stub for Microsoft.Maui.Controls.Application so PricesViewModel compiles.
// The ViewModel only accesses Application.Current?.MainPage for DisplayAlert,
// which returns null in tests (skipping alert logic gracefully).

namespace Microsoft.Maui.Controls;

internal class Application
{
    public static Application? Current { get; }
    public Page? MainPage { get; }
}

internal class Page
{
    public Task DisplayAlert(string title, string message, string cancel)
        => Task.CompletedTask;

    public Task<bool> DisplayAlert(string title, string message, string accept, string cancel)
        => Task.FromResult(true);
}
