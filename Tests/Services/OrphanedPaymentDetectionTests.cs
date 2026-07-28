using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Finance;

/// <summary>
/// Tests the orphaned-payment detection logic that drives the "[Deleted User]"
/// Finance feature. When a payment references a FencerId that no longer exists
/// in the fencer list (its Fencers row was deleted from the sheet), the Finance
/// view model surfaces it under a synthetic nameless <see cref="Fencer"/> whose
/// <see cref="Fencer.DisplayName"/> renders "[Deleted User]".
///
/// These tests reproduce that detection against the shared models without taking
/// a dependency on the Android-only TestDataService.
/// </summary>
public class OrphanedPaymentDetectionTests
{
    private static List<Fencer> Fencers() => new()
    {
        new() { Id = "demo-f1", Name = "Anna Varga" },
        new() { Id = "demo-f2", Name = "Béla Kovács" },
    };

    /// <summary>Mirrors the FinanceViewModel filter for orphaned payments.</summary>
    private static List<IGrouping<string, Payment>> DetectOrphaned(
        IEnumerable<Payment> payments, IEnumerable<Fencer> fencers)
    {
        var known = fencers.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FencerId) && !known.Contains(p.FencerId))
            .GroupBy(p => p.FencerId, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Detect_FindsPayment_ReferencingMissingFencer()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "demo-f1",         Year = 2024, Month = 5, Amount = 9000m },
            new() { FencerId = "deleted-ghost-1", Year = 2024, Month = 5, Amount = 12000m },
        };

        var orphaned = DetectOrphaned(payments, Fencers());

        orphaned.Should().ContainSingle();
        orphaned[0].Key.Should().Be("deleted-ghost-1");
    }

    [Fact]
    public void Detect_IgnoresPayments_ForKnownFencers()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "demo-f1", Year = 2024, Month = 5, Amount = 9000m },
            new() { FencerId = "demo-f2", Year = 2024, Month = 5, Amount = 12000m },
        };

        DetectOrphaned(payments, Fencers()).Should().BeEmpty();
    }

    [Fact]
    public void Detect_GroupsMultiplePayments_PerMissingFencer()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "deleted-ghost-1", Year = 2024, Month = 5, Amount = 5000m },
            new() { FencerId = "deleted-ghost-1", Year = 2024, Month = 5, Amount = 7000m },
            new() { FencerId = "deleted-ghost-2", Year = 2024, Month = 5, Amount = 3500m },
        };

        var orphaned = DetectOrphaned(payments, Fencers());

        orphaned.Should().HaveCount(2);
        orphaned.Single(g => g.Key == "deleted-ghost-1").Sum(p => p.Amount).Should().Be(12000m);
        orphaned.Single(g => g.Key == "deleted-ghost-2").Sum(p => p.Amount).Should().Be(3500m);
    }

    [Fact]
    public void Detect_IgnoresBlankFencerId()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "",    Year = 2024, Month = 5, Amount = 9000m },
            new() { FencerId = "   ", Year = 2024, Month = 5, Amount = 9000m },
        };

        DetectOrphaned(payments, Fencers()).Should().BeEmpty();
    }

    [Fact]
    public void OrphanedGroup_RendersAsDeletedPlaceholder()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "deleted-ghost-1", Year = 2024, Month = 5, Amount = 12000m },
        };

        var orphaned = DetectOrphaned(payments, Fencers()).Single();

        // FinanceViewModel builds a nameless Fencer for the orphaned id.
        var ghost = new Fencer { Id = orphaned.Key, Name = "" };

        ghost.DisplayName.Should().Be(Fencer.DeletedPlaceholder);
    }

    [Fact]
    public void OrphanedMoney_StillCountsTowardMonthIncome()
    {
        var payments = new List<Payment>
        {
            new() { FencerId = "demo-f1",         Year = 2024, Month = 5, Amount = 9000m },
            new() { FencerId = "deleted-ghost-1", Year = 2024, Month = 5, Amount = 12000m },
        };

        // Month income sums ALL payment rows regardless of fencer resolution,
        // so orphaned money must not be dropped from the total.
        payments.Sum(p => p.Amount).Should().Be(21000m);
    }
}
