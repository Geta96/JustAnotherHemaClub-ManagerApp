using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

/// <summary>
/// Tests the <see cref="Fencer.DisplayName"/> fallback used to render
/// "[Deleted User]" wherever a fencer reference resolves to a record with no
/// name (e.g. an orphaned payment whose Fencers row was deleted from the sheet).
/// </summary>
public class FencerDisplayNameTests
{
    [Fact]
    public void DisplayName_ReturnsName_WhenNameSet()
    {
        var fencer = new Fencer { Name = "Anna Varga" };

        fencer.DisplayName.Should().Be("Anna Varga");
    }

    [Fact]
    public void DisplayName_ReturnsPlaceholder_WhenNameEmpty()
    {
        var fencer = new Fencer { Name = "" };

        fencer.DisplayName.Should().Be(Fencer.DeletedPlaceholder);
    }

    [Fact]
    public void DisplayName_ReturnsPlaceholder_WhenNameDefault()
    {
        // Name defaults to string.Empty when never assigned.
        var fencer = new Fencer { Id = "deleted-ghost-1" };

        fencer.DisplayName.Should().Be(Fencer.DeletedPlaceholder);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void DisplayName_ReturnsPlaceholder_WhenNameWhitespace(string name)
    {
        var fencer = new Fencer { Name = name };

        fencer.DisplayName.Should().Be(Fencer.DeletedPlaceholder);
    }

    [Fact]
    public void DisplayName_PreservesLeadingTrailingContent_WhenNameHasRealChars()
    {
        var fencer = new Fencer { Name = " Béla Kovács " };

        // Only whitespace-only names fall back; a real name is returned verbatim.
        fencer.DisplayName.Should().Be(" Béla Kovács ");
    }

    [Fact]
    public void DeletedPlaceholder_HasExpectedText()
    {
        Fencer.DeletedPlaceholder.Should().Be("[Deleted User]");
    }
}
