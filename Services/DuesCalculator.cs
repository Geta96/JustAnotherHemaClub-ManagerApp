namespace JustAnotherHemaClub.Services;

/// <summary>
/// Monthly training fee tiers:
///  * 1 - 3 sessions: 3,500 per session
///  * 4 - 7 sessions: 9,000 flat
///  * 8+   sessions:  12,000 flat
/// </summary>
public static class DuesCalculator
{
    public const decimal SinglePrice = 3500m;
    public const decimal FourPackPrice = 9000m;
    public const decimal FullMonthPrice = 12000m;

    public static decimal Calculate(int sessionsAttended) => sessionsAttended switch
    {
        <= 0 => 0m,
        < 4 => SinglePrice * sessionsAttended,
        < 8 => FourPackPrice,
        _   => FullMonthPrice
    };
}