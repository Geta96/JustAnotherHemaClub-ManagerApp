using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

public class PoolTests
{
    [Fact]
    public void Pool_Name_ReturnsCorrectFormat()
    {
        var pool = new Pool { Index = 0 };
        pool.Name.Should().Be("Pool 1");

        pool = new Pool { Index = 2 };
        pool.Name.Should().Be("Pool 3");
    }

    [Fact]
    public void NewPool_HasUniqueId()
    {
        var pool1 = new Pool();
        var pool2 = new Pool();

        pool1.Id.Should().NotBe(pool2.Id);
    }

    [Fact]
    public void NewPool_HasEmptyFencerIdsAndMatches()
    {
        var pool = new Pool();

        pool.FencerIds.Should().BeEmpty();
        pool.Matches.Should().BeEmpty();
        pool.IsClosed.Should().BeFalse();
    }
}

public class TournamentTests
{
    [Fact]
    public void NewTournament_HasSetupState()
    {
        var t = new Tournament();

        t.State.Should().Be(TournamentState.Setup);
    }

    [Fact]
    public void NewTournament_HasUniqueId()
    {
        var t1 = new Tournament();
        var t2 = new Tournament();

        t1.Id.Should().NotBe(t2.Id);
    }

    [Fact]
    public void NewTournament_HasEmptyCollections()
    {
        var t = new Tournament();

        t.Fencers.Should().BeEmpty();
        t.Pools.Should().BeEmpty();
        t.FinalStandingFencerIds.Should().BeEmpty();
        t.Bracket.Should().BeNull();
    }

    [Fact]
    public void NewTournamentFencer_HasUniqueId()
    {
        var f1 = new TournamentFencer();
        var f2 = new TournamentFencer();

        f1.Id.Should().NotBe(f2.Id);
    }

    [Fact]
    public void NewTournamentFencer_IsNotWithdrawn()
    {
        var f = new TournamentFencer();

        f.IsWithdrawn.Should().BeFalse();
    }
}
