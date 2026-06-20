using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

public class PriceRuleTests
{
    [Fact]
    public void IsActiveOn_ReturnsTrue_WhenDateIsOnStartDate()
    {
        var rule = new PriceRule { StartDate = new DateTime(2024, 3, 1), EndDate = null };

        rule.IsActiveOn(new DateTime(2024, 3, 1)).Should().BeTrue();
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_WhenDateIsAfterStartDate_NoEndDate()
    {
        var rule = new PriceRule { StartDate = new DateTime(2024, 1, 1), EndDate = null };

        rule.IsActiveOn(new DateTime(2024, 6, 15)).Should().BeTrue();
    }

    [Fact]
    public void IsActiveOn_ReturnsFalse_WhenDateIsBeforeStartDate()
    {
        var rule = new PriceRule { StartDate = new DateTime(2024, 3, 1), EndDate = null };

        rule.IsActiveOn(new DateTime(2024, 2, 28)).Should().BeFalse();
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_WhenDateIsOnEndDate()
    {
        var rule = new PriceRule
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        rule.IsActiveOn(new DateTime(2024, 12, 31)).Should().BeTrue();
    }

    [Fact]
    public void IsActiveOn_ReturnsFalse_WhenDateIsAfterEndDate()
    {
        var rule = new PriceRule
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 6, 30)
        };

        rule.IsActiveOn(new DateTime(2024, 7, 1)).Should().BeFalse();
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_WhenDateIsBetweenStartAndEnd()
    {
        var rule = new PriceRule
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        rule.IsActiveOn(new DateTime(2024, 6, 15)).Should().BeTrue();
    }

    [Fact]
    public void NewPriceRule_HasTodayAsStartDate()
    {
        var rule = new PriceRule();

        rule.StartDate.Date.Should().Be(DateTime.Today);
    }

    [Fact]
    public void NewPriceRule_HasNullEndDate()
    {
        var rule = new PriceRule();

        rule.EndDate.Should().BeNull();
    }

    [Fact]
    public void NewPriceRule_HasUniqueId()
    {
        var rule1 = new PriceRule();
        var rule2 = new PriceRule();

        rule1.Id.Should().NotBe(rule2.Id);
    }
}
