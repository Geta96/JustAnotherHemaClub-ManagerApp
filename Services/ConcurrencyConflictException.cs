namespace JustAnotherHemaClub.Services;

/// <summary>
/// Thrown when a versioned write detects that the row was modified by another
/// writer since it was read. Callers should reload the latest server state,
/// re-apply their change if it still makes sense, and try again.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public string EntityKind { get; }
    public string EntityId { get; }
    public int ExpectedVersion { get; }
    public int ActualVersion { get; }

    public ConcurrencyConflictException(string entityKind, string entityId,
                                        int expectedVersion, int actualVersion)
        : base($"{entityKind} '{entityId}' was modified elsewhere " +
               $"(expected v{expectedVersion}, found v{actualVersion}).")
    {
        EntityKind = entityKind;
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}