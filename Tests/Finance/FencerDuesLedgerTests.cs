using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Guards the single-source-of-truth dues engine (<see cref="FencerDuesLedger"/>)
/// that both the Finance page and the Home payment-status card now delegate to.
///
/// These cover the corner cases that previously slipped through because Home and
/// Finance each re-implemented the math over DIFFERENT month sets:
///   • a payment-only month (payment but no attendance) must stay in the carry,
///   • custom-period passes flow through the ledger exactly like any other month,
///   • the per-fencer <c>Compute</c> and the bulk <c>ComputeAll</c> agree, so the
///     "owes X" figure can never diverge between the two screens.
/// </summary>
public class FencerDuesLedgerTests
{
    private static List<PriceRule> Rules() => new()
    {
        new() { SessionCount = 1, FullPrice = 3500m,  StudentPrice = 2000m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 4, FullPrice = 9000m,  StudentPrice = 5500m, StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 0, MonthCount = 1, FullPrice = 12000m, StudentPrice = 7000m, StartDate = new DateTime(2024, 1, 1) },
    };

    private static FencerDuesLedger.MonthInput M(int y, int m, int sessions, decimal paid) => new(y, m, sessions, paid);

    private static decimal Outstanding(IReadOnlyList<FencerDuesLedger.MonthResult> res) =>
        res.Sum(r => r.Contribution.Quote.Outstanding);

    // ---------- The live-data bug: a prior overpayment must reduce later dues ----------

    [Fact]
    public void PriorOverpayment_CarriesForward_AndReducesLaterOwed()
    {
        // Aug: attends once (due 3,500) but pays 10,000 ? 6,500 credit forward.
        // Sep: takes the full month (12,000); 6,500 credit applied ? owes 5,500.
        var res = FencerDuesLedger.Compute(false, new[]
        {
            M(2026, 8, 1, 10000m),
            M(2026, 9, 8, 0m),      // 8 sessions ? cheapest tier is the 12,000 month pass
        }, Rules());

        res[0].Contribution.Quote.Overpayment.Should().Be(6500m);
        res[1].Contribution.CreditIn.Should().Be(6500m);
        res[1].Contribution.Quote.Outstanding.Should().Be(5500m);
    }

    // ---------- Payment-only month (the month-seeding bug) ----------

    [Fact]
    public void PaymentOnlyMonth_NoAttendance_StillCarriesCreditForward()
    {
        // A pre-payment lands in a month with zero attendance. Its funds must NOT
        // vanish — they become credit that covers the next attended month.
        var res = FencerDuesLedger.Compute(false, new[]
        {
            M(2026, 8, 0, 12000m),  // paid ahead, attended nothing
            M(2026, 9, 1, 0m),      // due 3,500 ? fully covered by carried credit
        }, Rules());

        res[0].Contribution.Quote.Overpayment.Should().Be(12000m);
        res[1].Contribution.Quote.Outstanding.Should().Be(0m);
        res[1].Contribution.Quote.Overpayment.Should().Be(8500m); // 12,000 - 3,500
    }

    // ---------- Custom period is "just another period" ----------

    [Fact]
    public void CustomPeriod_ChargedOnce_ThenCoveredByCarriedCredit()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, IsCustomPeriod = true, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 8, 31) },
        };

        // Attends in both July and August; pays the whole 10,000 in July.
        var res = FencerDuesLedger.Compute(false, new[]
        {
            M(2026, 7, 2, 10000m),
            M(2026, 8, 3, 0m),
        }, rules);

        // July carries the full period charge, August is covered at 0.
        res[0].Contribution.Quote.TotalDue.Should().Be(10000m);
        res[0].Contribution.Quote.Outstanding.Should().Be(0m);
        res[1].Contribution.Quote.TotalDue.Should().Be(0m);
        Outstanding(res).Should().Be(0m);
    }

    [Fact]
    public void CustomPeriod_Unpaid_OwesFullPriceOnce_NotPerMonth()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, IsCustomPeriod = true, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 8, 31) },
        };

        var res = FencerDuesLedger.Compute(false, new[]
        {
            M(2026, 7, 2, 0m),
            M(2026, 8, 4, 0m),
        }, rules);

        // Total owed across the window is the single period price, not 2×.
        Outstanding(res).Should().Be(10000m);
    }

    // ---------- Compute and ComputeAll must never disagree ----------

    [Fact]
    public void ComputeAll_MatchesPerFencerCompute_ForTheSameData()
    {
        // This is the regression guard: Finance (ComputeAll) and Home (Compute)
        // must produce identical outstanding totals for the same fencer/data.
        var rules = Rules();
        var months = new (int Y, int M)[] { (2026, 8), (2026, 9) };

        var fencer = new Fencer { Id = "f1", Name = "Gergely" };

        var attendanceByMonth = new Dictionary<(int, int), Dictionary<string, int>>
        {
            [(2026, 8)] = new() { ["f1"] = 1 },
            [(2026, 9)] = new() { ["f1"] = 8 },
        };
        var paidByMonth = new Dictionary<(int, int), Dictionary<string, decimal>>
        {
            [(2026, 8)] = new() { ["f1"] = 10000m },
            [(2026, 9)] = new(),
        };

        var bulk = FencerDuesLedger.ComputeAll(
            new[] { fencer }, months, attendanceByMonth, paidByMonth, rules);

        var single = FencerDuesLedger.Compute(false, new[]
        {
            M(2026, 8, 1, 10000m),
            M(2026, 9, 8, 0m),
        }, rules);

        foreach (var r in single)
        {
            bulk.TryGetValue(("f1", r.Year, r.Month), out var b).Should().BeTrue();
            b.Quote.Outstanding.Should().Be(r.Contribution.Quote.Outstanding);
            b.Quote.Overpayment.Should().Be(r.Contribution.Quote.Overpayment);
            b.CreditIn.Should().Be(r.Contribution.CreditIn);
        }
    }

    [Fact]
    public void ComputeAll_IsolatesFencers_NoCreditBleedBetweenThem()
    {
        var rules = Rules();
        var months = new (int Y, int M)[] { (2026, 8), (2026, 9) };

        var f1 = new Fencer { Id = "f1", Name = "Overpayer" };
        var f2 = new Fencer { Id = "f2", Name = "Owes" };

        var attendanceByMonth = new Dictionary<(int, int), Dictionary<string, int>>
        {
            [(2026, 8)] = new() { ["f1"] = 1, ["f2"] = 1 },
            [(2026, 9)] = new() { ["f1"] = 1, ["f2"] = 1 },
        };
        var paidByMonth = new Dictionary<(int, int), Dictionary<string, decimal>>
        {
            [(2026, 8)] = new() { ["f1"] = 10000m }, // f1 overpays
            [(2026, 9)] = new(),
        };

        var bulk = FencerDuesLedger.ComputeAll(
            new[] { f1, f2 }, months, attendanceByMonth, paidByMonth, rules);

        // f1's overpayment must not cover f2's September dues.
        bulk[("f2", 2026, 9)].Quote.Outstanding.Should().Be(3500m);
        bulk[("f1", 2026, 9)].Quote.Outstanding.Should().Be(0m); // credit covers it
    }

    // ---------- Cheaper-wins fallback still honoured inside the ledger ----------

    [Fact]
    public void CustomPeriod_SkippedWhenNormalBillingIsCheaper()
    {
        var rules = new List<PriceRule>
        {
            new() { SessionCount = 0, IsCustomPeriod = true, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 8, 31) },
            new() { SessionCount = 1, FullPrice = 3500m, StudentPrice = 2000m,
                    StartDate = new DateTime(2024, 1, 1) },
        };

        // A single attendance would cost 3,500 as a single ticket — cheaper than
        // the 10,000 period, so the period pass is not applied.
        var res = FencerDuesLedger.Compute(false, new[] { M(2026, 7, 1, 0m) }, rules);

        Outstanding(res).Should().Be(3500m);
    }
}
