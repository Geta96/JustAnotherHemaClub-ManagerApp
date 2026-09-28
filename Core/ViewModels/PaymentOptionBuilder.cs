using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using System.Globalization;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// A single computed payment option (before it's wrapped into a tappable
/// <see cref="PaymentOptionVm"/>). Exposed as its own type so the option-building
/// logic can be unit-tested without constructing the whole FinanceViewModel.
/// </summary>
public readonly record struct PaymentOptionSpec(string Key, decimal Amount, string MenuText, string Kind);

/// <summary>
/// Pure logic that decides which inline payment buttons a fencer/month row
/// should show. Kept free of any UI / service dependencies so it can be
/// exercised directly by unit tests.
///
/// Rules encoded here:
///   ? Every pass/tier amount is a *top-up* over funds already applied
///     (cash paid this month + credit carried in), so no tier is charged twice.
///   ? A pass/tier button is hidden when its top-up would be *less* than the
///     current outstanding balance (paying it would leave a confusing residual).
///   ? When the fencer has already applied funds, pass buttons read
///     "Upgrade to ?" instead of "Pay ?".
///   ? "Pay Remaining" (kind "remaining") clears the post-credit balance and is
///     added only when it doesn't duplicate an existing pass amount.
///   ? "Pay Custom Amount" is added by the caller (kind "primary") and is not
///     produced here.
/// </summary>
public static class PaymentOptionBuilder
{
    // Currency amounts are formatted with a fixed culture so the produced menu
    // text is deterministic (comma-grouped) regardless of the machine's current
    // culture — otherwise "20,000" could render as "20 000" and confuse both
    // users and tests.
    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    public static List<PaymentOptionSpec> Build(
        bool isStudent,
        decimal alreadyPaid,
        decimal creditIn,
        int sessionsAttended,
        decimal totalCost,
        decimal amountDue,
        IReadOnlyList<PriceRule> activeRules)
    {
        var applied = alreadyPaid + creditIn;
        var options = new List<PaymentOptionSpec>();

        // When the fencer has already put money toward this month (cash paid or
        // credit carried in), a pass payment is really an *upgrade* to that tier
        // for the remaining balance.
        var hasApplied = applied > 0m;

        decimal TopUp(decimal target) => Math.Max(0m, target - applied);

        // Pay attended — cheapest applicable tier for sessions attended so far.
        if (sessionsAttended > 0)
        {
            var c = TopUp(totalCost);
            if (c > 0m && c != amountDue && c >= amountDue)
                options.Add(new("attended", c, $"Pay attended ({Money(c)} Ft)", "pass"));
        }

        // One enabled option per active per-month tier (newest rule wins).
        var tiers = activeRules
            .Where(r => !r.IsCustomPeriod && r.SessionCount != 1) // single tickets = "attended"
            .GroupBy(r => (r.SessionCount, r.MonthCount))
            .Select(g => g.OrderByDescending(r => r.StartDate.Date)
                          .ThenByDescending(r => r.FullPrice).First());

        foreach (var r in tiers)
        {
            var price = DuesCalculator.PriceFor(r, isStudent);
            var c = TopUp(price);
            if (c <= 0m) continue;
            // Skip tiers that are cheaper than the outstanding balance.
            if (c < amountDue) continue;

            var tierName = r.SessionCount == 0
                ? (r.MonthCount <= 1 ? "Full month" : $"{r.MonthCount}-month pass")
                : $"{r.SessionCount} sessions";
            var text = hasApplied
                ? $"Upgrade to {tierName} ({Money(c)} Ft)"
                : $"Pay {tierName} ({Money(c)} Ft)";
            options.Add(new(tierName, c, text, "pass"));
        }

        // Pay Custom period — one-off pass covering the month.
        var period = activeRules.FirstOrDefault(r => r.IsCustomPeriod);
        if (period is not null)
        {
            var price = DuesCalculator.PriceFor(period, isStudent);
            var c = TopUp(price);
            if (c > 0m && c >= amountDue)
            {
                var text = hasApplied
                    ? $"Upgrade to Custom period ({Money(c)} Ft)"
                    : $"Pay Custom period ({Money(c)} Ft)";
                options.Add(new("period", c, text, "pass"));
            }
        }

        // Pay Remaining — clears the current post-credit balance. Added last, and
        // only when its amount doesn't duplicate an already-listed pass/tier option.
        if (amountDue > 0m && options.All(o => o.Amount != amountDue))
            options.Insert(0, new("remaining", amountDue, $"Pay Remaining ({Money(amountDue)} Ft)", "remaining"));

        return options;
    }
}
