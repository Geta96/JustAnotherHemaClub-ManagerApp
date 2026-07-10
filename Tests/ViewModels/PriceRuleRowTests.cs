using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Tests.ViewModels;

/// <summary>
/// Tests PriceRuleRow — the UI-facing wrapper around PriceRule that drives
/// the Prices page. Verifies computed properties, auto-student-price, and
/// dirty-tracking behave correctly when data is loaded or modified.
/// </summary>
public class PriceRuleRowTests
{
    [Fact]
    public void Constructor_InitializesFromRule()
    {
        var rule = new PriceRule
        {
            SessionCount = 4,
            FullPrice = 9000m,
            StudentPrice = 5500m,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        var row = new PriceRuleRow(rule);

        row.SessionCount.Should().Be(4);
        row.FullPrice.Should().Be(9000m);
        row.StudentPrice.Should().Be(5500m);
        row.StartDate.Should().Be(new DateTime(2024, 1, 1));
        row.HasEndDate.Should().BeTrue();
        row.EndDate.Should().Be(new DateTime(2024, 12, 31));
        row.IsDirty.Should().BeFalse();
        row.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void TierName_SingleSession()
    {
        var row = new PriceRuleRow(new PriceRule { SessionCount = 1 });
        row.TierName.Should().Be("Single-session ticket");
    }

    [Fact]
    public void TierName_Pack()
    {
        var row = new PriceRuleRow(new PriceRule { SessionCount = 4 });
        row.TierName.Should().Be("4-session pass");
    }

    [Fact]
    public void TierName_Unlimited()
    {
        var row = new PriceRuleRow(new PriceRule { SessionCount = 0 });
        row.TierName.Should().Be("Unlimited monthly pass");
    }

    [Fact]
    public void IsActiveNow_True_WhenStartBeforeToday_NoEndDate()
    {
        var rule = new PriceRule
        {
            StartDate = DateTime.Today.AddDays(-10),
            EndDate = null
        };
        var row = new PriceRuleRow(rule);

        row.IsActiveNow.Should().BeTrue();
    }

    [Fact]
    public void IsActiveNow_False_WhenStartInFuture()
    {
        var rule = new PriceRule { StartDate = DateTime.Today.AddDays(5) };
        var row = new PriceRuleRow(rule);

        row.IsActiveNow.Should().BeFalse();
    }

    [Fact]
    public void IsActiveNow_False_WhenEndDatePassed()
    {
        var rule = new PriceRule
        {
            StartDate = DateTime.Today.AddMonths(-6),
            EndDate = DateTime.Today.AddDays(-1)
        };
        var row = new PriceRuleRow(rule);

        row.IsActiveNow.Should().BeFalse();
    }

    [Fact]
    public void ChangingFullPrice_AutoSuggestsStudentPrice()
    {
        var rule = new PriceRule { FullPrice = 10000m, StudentPrice = 6000m };
        var row = new PriceRuleRow(rule);

        row.FullPrice = 15000m;

        // SuggestStudentPrice(15000) = 60% = 9000
        row.StudentPrice.Should().Be(9000m);
        row.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ChangingSessionCount_MarksDirty()
    {
        var row = new PriceRuleRow(new PriceRule { SessionCount = 1 });

        row.SessionCount = 4;

        row.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ChangingStartDate_MarksDirty()
    {
        var row = new PriceRuleRow(new PriceRule { StartDate = DateTime.Today });

        row.StartDate = DateTime.Today.AddDays(1);

        row.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ToUpdatedRule_AppliesChangesToUnderlying()
    {
        var rule = new PriceRule
        {
            SessionCount = 1,
            FullPrice = 3500m,
            StudentPrice = 2000m,
            StartDate = new DateTime(2024, 1, 1)
        };
        var row = new PriceRuleRow(rule);

        row.SessionCount = 0;
        row.FullPrice = 12000m;
        row.StartDate = new DateTime(2025, 1, 1);
        row.HasEndDate = true;
        row.EndDate = new DateTime(2025, 6, 30);

        var updated = row.ToUpdatedRule();

        updated.SessionCount.Should().Be(0);
        updated.FullPrice.Should().Be(12000m);
        updated.StartDate.Should().Be(new DateTime(2025, 1, 1));
        updated.EndDate.Should().Be(new DateTime(2025, 6, 30));
    }

    [Fact]
    public void PriceSummary_FormatsCorrectly()
    {
        var row = new PriceRuleRow(new PriceRule { FullPrice = 12000m, StudentPrice = 7000m });

        row.PriceSummary.Should().Contain("12");
        row.PriceSummary.Should().Contain("7");
        row.PriceSummary.Should().Contain("Ft");
    }

    [Fact]
    public void DateRange_OpenEnded()
    {
        var row = new PriceRuleRow(new PriceRule { StartDate = new DateTime(2024, 3, 1), EndDate = null });

        row.DateRange.Should().Contain("from 2024-03-01");
    }

    [Fact]
    public void DateRange_WithEndDate()
    {
        var row = new PriceRuleRow(new PriceRule
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 6, 30)
        });

        row.DateRange.Should().Contain("2024-01-01");
        row.DateRange.Should().Contain("2024-06-30");
    }

    [Fact]
    public void ActiveBadge_Active()
    {
        var row = new PriceRuleRow(new PriceRule { StartDate = DateTime.Today.AddDays(-1) });
        row.ActiveBadge.Should().Contain("Active");
    }

    [Fact]
    public void ActiveBadge_Inactive()
    {
        var row = new PriceRuleRow(new PriceRule { StartDate = DateTime.Today.AddDays(5) });
        row.ActiveBadge.Should().Contain("Inactive");
    }

    [Fact]
    public void ExpandGlyph_Collapsed()
    {
        var row = new PriceRuleRow(new PriceRule());
        row.IsExpanded.Should().BeFalse();
        row.ExpandGlyph.Should().NotBeEmpty();
    }

    [Fact]
    public void ExpandGlyph_Expanded()
    {
        var row = new PriceRuleRow(new PriceRule());
        row.IsExpanded = true;
        row.ExpandGlyph.Should().NotBeEmpty();
        row.ExpandGlyph.Should().NotBe(new PriceRuleRow(new PriceRule()).ExpandGlyph,
            "expanded glyph should differ from collapsed glyph");
    }
}
