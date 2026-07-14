namespace JustAnotherHemaClub.Services;

/// <summary>
/// Allows the app to swap the active <see cref="IGoogleSheetsService"/> and
/// <see cref="ICacheControl"/> at runtime — used exclusively for the test user
/// mode where all data stays in memory.
///
/// The singleton <c>ServiceProxy</c> registered in DI delegates to
/// whichever implementation is currently active.
/// </summary>
public static class ServiceSwap
{
    private static IGoogleSheetsService? _override;
    private static ICacheControl? _cacheOverride;

    /// <summary>Route all future service calls to the given in-memory implementation.</summary>
    public static void Activate(TestDataService testService)
    {
        _override = testService;
        _cacheOverride = testService;
    }

    /// <summary>Revert to the real backend (called on logout).</summary>
    public static void Deactivate()
    {
        _override = null;
        _cacheOverride = null;
    }

    public static bool IsActive => _override is not null;

    public static IGoogleSheetsService? CurrentSheets => _override;
    public static ICacheControl? CurrentCache => _cacheOverride;
}
