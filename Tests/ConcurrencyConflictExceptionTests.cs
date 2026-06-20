using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests;

public class ConcurrencyConflictExceptionTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ex = new ConcurrencyConflictException("Match", "m123", 1, 2);

        ex.EntityKind.Should().Be("Match");
        ex.EntityId.Should().Be("m123");
        ex.ExpectedVersion.Should().Be(1);
        ex.ActualVersion.Should().Be(2);
    }

    [Fact]
    public void Constructor_ProducesDescriptiveMessage()
    {
        var ex = new ConcurrencyConflictException("Pool", "pool99", 3, 5);

        ex.Message.Should().Contain("Pool");
        ex.Message.Should().Contain("pool99");
        ex.Message.Should().Contain("v3");
        ex.Message.Should().Contain("v5");
    }

    [Fact]
    public void IsException()
    {
        var ex = new ConcurrencyConflictException("Match", "m1", 0, 1);

        ex.Should().BeAssignableTo<Exception>();
    }
}
