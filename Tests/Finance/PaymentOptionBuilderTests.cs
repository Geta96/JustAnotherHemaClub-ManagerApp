using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Covers the payment-option building rules introduced for the Finance page:
///   ? top-up amounts (price minus funds already applied),
///   ? "Upgrade to ?" wording once funds have been applied,
///   ? hiding pass/tier buttons that are cheaper than the outstanding balance,
///   ? the "Pay Remaining" primary action + its de-duplication,
///   ? custom-period pass options.
/// These mirror <c>FinanceViewModel.BuildPaymentOptions</c>, which now delegates
/// to <see cref="PaymentOptionBuilder"/>.
/// </summary>
public class PaymentOptionBuilderTests
{
    private static List<PriceRule> StandardRules() => new()
    {
        new() { SessionCount = 1, FullPrice = 3500m,  StudentPrice = 2000m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 4, FullPrice = 9000m,  StudentPrice = 5500m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 0, MonthCount = 1, FullPrice = 12000m, StudentPrice = 7000m, StartDate = new DateTime(2024, 1, 1) },
    };

    private static List<PaymentOptionSpec> Build(
        decimal alreadyPaid, decimal creditIn, int sessions,
        decimal totalCost, decimal amountDue,
        IReadOnlyList<PriceRule>? rules = null, bool isStudent = false) =>
        PaymentOptionBuilder.Build(
            isStudent, alreadyPaid, creditIn, sessions, totalCost, amountDue,
            rules ?? StandardRules());

    // ---------- Pay Remaining ----------

    [Fact]
    public void FreshBill_IncludesPayRemaining_AsPrimary()
    {
        // 1 session attended, nothing applied: cost 3500, due 3500.
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 1,
                         totalCost: 3500m, amountDue: 3500m);

        var remaining = opts.Single(o => o.Kind == "remaining");
        remaining.Amount.Should().Be(3500m);
        remaining.MenuText.Should().Be("Pay Remaining (3,500 Ft)");
    }

    [Fact]
    public void PayRemaining_IsSuppressed_WhenItDuplicatesATierAmount()
    {
        // 4 sessions, nothing applied: 4-pack = 9000 which equals the balance,
        // so a separate "Pay Remaining (9,000)" would be redundant.
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 4,
                         totalCost: 9000m, amountDue: 9000m);

        opts.Should().NotContain(o => o.Kind == "remaining");
        opts.Should().Contain(o => o.Key == "4 sessions" && o.Amount == 9000m);
    }

    [Fact]
    public void NothingOwed_ProducesNoRemainingOption()
    {
        // Fully covered: due 0. No "Pay Remaining" and no passes cheaper than 0.
        var opts = Build(alreadyPaid: 12000m, creditIn: 0m, sessions: 5,
                         totalCost: 12000m, amountDue: 0m);

        opts.Should().NotContain(o => o.Kind == "remaining");
    }

    // ---------- Upgrade wording ----------

    [Fact]
    public void AlreadyPaidSingle_UpgradeToFullMonth_ShowsTopUpAndUpgradeWording()
    {
        // Paid 3500 for a single session; full month is 12000 ? top-up 8500.
        var opts = Build(alreadyPaid: 3500m, creditIn: 0m, sessions: 1,
                         totalCost: 3500m, amountDue: 0m);

        var full = opts.Single(o => o.Key == "Full month");
        full.Amount.Should().Be(8500m);
        full.MenuText.Should().Be("Upgrade to Full month (8,500 Ft)");
    }

    [Fact]
    public void CreditApplied_AlsoTriggersUpgradeWording()
    {
        // 4000 carried-in credit, no cash: full month top-up = 12000 - 4000 = 8000.
        var opts = Build(alreadyPaid: 0m, creditIn: 4000m, sessions: 1,
                         totalCost: 3500m, amountDue: 0m);

        var full = opts.Single(o => o.Key == "Full month");
        full.Amount.Should().Be(8000m);
        full.MenuText.Should().StartWith("Upgrade to Full month");
    }

    [Fact]
    public void NoFundsApplied_UsesPayWording_NotUpgrade()
    {
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 1,
                         totalCost: 3500m, amountDue: 3500m);

        opts.Where(o => o.Kind == "pass")
            .Should().OnlyContain(o => o.MenuText.StartsWith("Pay "));
        opts.Should().NotContain(o => o.MenuText.Contains("Upgrade"));
    }

    // ---------- Hiding cheaper-than-balance options ----------

    [Fact]
    public void OwesMoreThanAPass_HidesThatCheaperPass()
    {
        // Fencer owes 10000 (e.g. via carried debt) but the 4-session pass is
        // only 9000 — paying it wouldn't clear the balance, so it's hidden.
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 4,
                         totalCost: 9000m, amountDue: 10000m);

        opts.Should().NotContain(o => o.Key == "4 sessions");
        // Full month (12000) still covers it and remains visible.
        opts.Should().Contain(o => o.Key == "Full month" && o.Amount == 12000m);
    }

    [Fact]
    public void PassEqualToBalance_IsShown()
    {
        // Owes exactly 9000 ? the 4-session pass equals the balance and shows.
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 4,
                         totalCost: 9000m, amountDue: 9000m);

        opts.Should().Contain(o => o.Key == "4 sessions" && o.Amount == 9000m);
    }

    // ---------- Custom period ----------

    [Fact]
    public void CustomPeriodRule_ProducesAPeriodOption()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, MonthCount = 1, FullPrice = 12000m, StudentPrice = 7000m,
                    StartDate = new DateTime(2024, 1, 1) },
            new() { SessionCount = 0, IsCustomPeriod = true, FullPrice = 20000m, StudentPrice = 12000m,
                    StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 3, 31) },
        };

        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 2,
                         totalCost: 7000m, amountDue: 7000m, rules: rules);

        var period = opts.Single(o => o.Key == "period");
        period.Amount.Should().Be(20000m);
        period.MenuText.Should().Be("Pay Custom period (20,000 Ft)");
    }

    [Fact]
    public void CustomPeriod_WithFundsApplied_UsesUpgradeWording()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, IsCustomPeriod = true, FullPrice = 20000m, StudentPrice = 12000m,
                    StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 3, 31) },
        };

        // 5000 already paid ? period top-up = 15000, upgrade wording.
        var opts = Build(alreadyPaid: 5000m, creditIn: 0m, sessions: 2,
                         totalCost: 0m, amountDue: 0m, rules: rules);

        var period = opts.Single(o => o.Key == "period");
        period.Amount.Should().Be(15000m);
        period.MenuText.Should().Be("Upgrade to Custom period (15,000 Ft)");
    }

    // ---------- Student pricing ----------

    [Fact]
    public void StudentPricing_UsesStudentPrices()
    {
        // Student full month = 7000 (not 12000).
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 1,
                         totalCost: 2000m, amountDue: 2000m, isStudent: true);

        opts.Single(o => o.Key == "Full month").Amount.Should().Be(7000m);
    }

    // ---------- Multi-month passes ----------

    [Fact]
    public void MultiMonthPass_NamedWithMonthCount()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, MonthCount = 2, FullPrice = 20000m, StudentPrice = 12000m,
                    StartDate = new DateTime(2024, 1, 1) },
        };

        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 1,
                         totalCost: 10000m, amountDue: 10000m, rules: rules);

        opts.Should().Contain(o => o.Key == "2-month pass" &&
                                   o.MenuText == "Pay 2-month pass (20,000 Ft)");
    }

    // ---------- Single tickets excluded from tier buttons ----------

    [Fact]
    public void SingleSessionRule_NotShownAsATierButton()
    {
        // The 1-session tier is handled via "Pay attended", never as its own
        // "Pay 1 sessions" tier button.
        var opts = Build(alreadyPaid: 0m, creditIn: 0m, sessions: 1,
                         totalCost: 3500m, amountDue: 3500m);

        opts.Should().NotContain(o => o.Key == "1 sessions");
    }

    // ---------- Pay attended ----------

    [Fact]
    public void PayAttended_ShownWhenCheaperTierDiffersFromBalance()
    {
        // 3 sessions: cheapest tier is the 4-pack (9000). With 2000 already
        // applied: attended top-up = 7000, and balance (amountDue) = 7000 too,
        // so "Pay attended" is suppressed (equals balance ? Remaining covers it).
        var opts = Build(alreadyPaid: 2000m, creditIn: 0m, sessions: 3,
                         totalCost: 9000m, amountDue: 7000m);

        // attended top-up equals amountDue ? not added as a distinct "attended".
        opts.Should().NotContain(o => o.Key == "attended");
    }
}
