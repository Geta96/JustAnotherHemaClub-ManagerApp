namespace JustAnotherHemaClub.Services;

/// <summary>
/// Pricing model:
///   * Single session ticket:   3 500
///   * Half monthly pass:       9 000 (covers every Tue OR every Fri ~4 sessions/month)
///   * Full unlimited monthly: 12 000
/// Students pay 60% of every tier (set via <see cref="Models.Fencer.IsStudent"/>).
/// </summary>
public static class DuesCalculator
{
    public const decimal SinglePrice = 3500m;
    public const decimal HalfPassPrice = 9000m;
    public const decimal FullPassPrice = 12000m;
    public const decimal StudentMultiplier = 0.60m;

    public static decimal Calculate(int sessionsAttended, bool isStudent = false)
    {
        decimal price = sessionsAttended switch
        {
            <= 0 => 0m,
            <= 2 => SinglePrice * sessionsAttended, // per-piece is cheaper
            <= 4 => HalfPassPrice,                  // 3-4 sessions: half-pass wins
            _   => FullPassPrice                    // 5+ sessions: full pass
        };
        return isStudent ? Math.Round(price * StudentMultiplier, 0) : price;
    }

    public static string TierLabel(int sessionsAttended) => sessionsAttended switch
    {
        <= 0 => "—",
        <= 2 => "single ticket",
        <= 4 => "half pass",
        _    => "full monthly pass"
    };
}