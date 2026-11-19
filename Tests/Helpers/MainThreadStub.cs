// Stub for Microsoft.Maui.ApplicationModel.MainThread so the ViewModel
// compiles in the test project (no MAUI dependency).
// The ViewModel only uses BeginInvokeOnMainThread to marshal errors back
// to the UI thread, which we don't need in unit tests.

namespace Microsoft.Maui.ApplicationModel
{
    internal static class MainThread
    {
        public static void BeginInvokeOnMainThread(Action action) => action();
    }
}

namespace Microsoft.Maui.Storage
{
    internal static class Preferences
    {
        public static PreferencesStore Default { get; } = new();
    }

    internal sealed class PreferencesStore
    {
        private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

        public T Get<T>(string key, T defaultValue)
        {
            if (_store.TryGetValue(key, out var val) && val is T typed)
                return typed;
            return defaultValue;
        }

        public void Set<T>(string key, T value) => _store[key] = value;

        public void Clear() => _store.Clear();
    }
}
