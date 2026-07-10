// Stub for Microsoft.Maui.ApplicationModel.MainThread so the ViewModel
// compiles in the test project (no MAUI dependency).
// The ViewModel only uses BeginInvokeOnMainThread to marshal errors back
// to the UI thread, which we don't need in unit tests.

namespace Microsoft.Maui.ApplicationModel;

internal static class MainThread
{
    public static void BeginInvokeOnMainThread(Action action) => action();
}
