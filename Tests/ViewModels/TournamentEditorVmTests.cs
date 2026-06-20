using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Moq;
using Match = JustAnotherHemaClub.Models.Match;

namespace JustAnotherHemaClub.Tests.ViewModels;

/// <summary>
/// Tests the TournamentEditorVm to verify that when data is supplied to the VM,
/// it correctly exposes it to the UI (computed properties, collections) and that
/// commands drive the correct backend calls.
/// </summary>
public class TournamentEditorVmTests
{
    private readonly Mock<IGoogleSheetsService> _sheetsMock = new();
    private readonly Mock<ICacheControl> _cacheMock = new();
    private readonly TournamentAutoSaveService _autoSave;
    private readonly TournamentEditorVm _vm;

    public TournamentEditorVmTests()
    {
        _autoSave = new TournamentAutoSaveService(_sheetsMock.Object);
        _vm = new TournamentEditorVm(_sheetsMock.Object, _autoSave, _cacheMock.Object);
    }

    // -------- InitNew: UI state after creating a new tournament --------

    [Fact]
    public void InitNew_SetsIsNewAndSetupState()
    {
        _vm.InitNew();

        _vm.IsNew.Should().BeTrue();
        _vm.IsExisting.Should().BeFalse();
        _vm.IsSetupState.Should().BeTrue();
        _vm.Tournament.Should().NotBeNull();
        _vm.Tournament!.State.Should().Be(TournamentState.Setup);
    }

    [Fact]
    public void InitNew_FencersAndPoolsAreEmpty()
    {
        _vm.InitNew();

        _vm.Fencers.Should().BeEmpty();
        _vm.DraftPools.Should().BeEmpty();
        _vm.ActiveFencerCount.Should().Be(0);
    }

    [Fact]
    public void InitNew_CannotStart()
    {
        _vm.InitNew();

        _vm.CanStart.Should().BeFalse();
        _vm.CanAddFencers.Should().BeTrue(); // setup + not full
    }

    // -------- InitExistingAsync: VM binds loaded data to UI --------

    [Fact]
    public async Task InitExistingAsync_LoadsTournamentData()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);

        await _vm.InitExistingAsync("t1");

        _vm.Tournament.Should().BeSameAs(tournament);
        _vm.Name.Should().Be("Test Cup");
        _vm.Password.Should().Be("secret");
        _vm.IsExisting.Should().BeTrue();
        _vm.IsNew.Should().BeFalse();
    }

    [Fact]
    public async Task InitExistingAsync_PopulatesFencersCollection()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);

        await _vm.InitExistingAsync("t1");

        _vm.Fencers.Should().HaveCount(4);
        _vm.Fencers.Select(f => f.Name).Should().Contain("Alice");
        _vm.ActiveFencerCount.Should().Be(4);
    }

    [Fact]
    public async Task InitExistingAsync_NotFound_SetsErrorMessage()
    {
        _sheetsMock.Setup(s => s.GetTournamentAsync("missing")).ReturnsAsync((Tournament?)null);

        await _vm.InitExistingAsync("missing");

        _vm.ErrorMessage.Should().Contain("not found");
    }

    // -------- AddFencer: UI updates immediately --------

    [Fact]
    public async Task AddFencer_AddsToFencersCollection()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");
        int initialCount = _vm.Fencers.Count;

        _vm.NewFencerName = "Eve";
        await _vm.AddFencerAsync();

        _vm.Fencers.Should().HaveCount(initialCount + 1);
        _vm.Fencers.Last().Name.Should().Be("Eve");
        _vm.NewFencerName.Should().BeEmpty(); // cleared after add
    }

    [Fact]
    public async Task AddFencer_EmptyName_DoesNothing()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");
        int initialCount = _vm.Fencers.Count;

        _vm.NewFencerName = "   ";
        await _vm.AddFencerAsync();

        _vm.Fencers.Should().HaveCount(initialCount);
    }

    // -------- RemoveFencer: updates UI and fires backend call --------

    [Fact]
    public async Task RemoveFencer_RemovesFromCollection()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.DeleteTournamentFencerAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");
        var row = _vm.Fencers.First();

        await _vm.RemoveFencerAsync(row);

        _vm.Fencers.Should().NotContain(row);
        _vm.ActiveFencerCount.Should().Be(3);
    }

    // -------- WithdrawFencer: toggles flag, updates UI --------

    [Fact]
    public async Task WithdrawFencer_TogglesWithdrawnFlag()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");
        var row = _vm.Fencers.First();

        row.Fencer.IsWithdrawn.Should().BeFalse();
        await _vm.WithdrawFencerAsync(row);

        row.Fencer.IsWithdrawn.Should().BeTrue();
        _vm.ActiveFencerCount.Should().Be(3);
    }

    [Fact]
    public async Task WithdrawFencer_TwiceReinstates()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");
        var row = _vm.Fencers.First();

        await _vm.WithdrawFencerAsync(row); // withdraw
        await _vm.WithdrawFencerAsync(row); // reinstate

        row.Fencer.IsWithdrawn.Should().BeFalse();
        _vm.ActiveFencerCount.Should().Be(4);
    }

    // -------- SaveNew: validation --------

    [Fact]
    public async Task SaveNew_MissingName_SetsError()
    {
        _vm.InitNew();
        _vm.Name = "";
        _vm.Password = "abc";

        await _vm.SaveNewAsync();

        _vm.ErrorMessage.Should().Contain("Name is required");
    }

    [Fact]
    public async Task SaveNew_MissingPassword_SetsError()
    {
        _vm.InitNew();
        _vm.Name = "Cup";
        _vm.Password = " ";

        await _vm.SaveNewAsync();

        _vm.ErrorMessage.Should().Contain("Password is required");
    }

    [Fact]
    public async Task SaveNew_ValidInputs_PersistsAndTransitions()
    {
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendTournamentFencersAsync(It.IsAny<string>(), It.IsAny<IList<TournamentFencer>>()))
            .Returns(Task.CompletedTask);

        _vm.InitNew();
        _vm.Name = "Test Cup";
        _vm.Password = "secret123";

        await _vm.SaveNewAsync();

        _vm.IsNew.Should().BeFalse();
        _vm.IsExisting.Should().BeTrue();
        _vm.ErrorMessage.Should().BeEmpty();
        _sheetsMock.Verify(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>()), Times.Once);
        _cacheMock.Verify(c => c.InvalidateTournaments(), Times.Once);
    }

    // -------- StartTournament: validation & match generation --------

    [Fact]
    public async Task StartTournament_TooFewFencers_SetsError()
    {
        var tournament = new Tournament
        {
            Id = "t1",
            State = TournamentState.Setup,
            Fencers = new() { new() { Name = "A" }, new() { Name = "B" } }
        };
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().Contain("at least");
    }

    [Fact]
    public async Task StartTournament_PoolTooSmall_SetsError()
    {
        var tournament = CreateTournamentWithPools(poolSizes: new[] { 3, 5 });
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().Contain("too few fencers");
    }

    [Fact]
    public async Task StartTournament_PoolTooLarge_SetsError()
    {
        var tournament = CreateTournamentWithPools(poolSizes: new[] { 9 });
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().Contain("too many fencers");
    }

    [Fact]
    public async Task StartTournament_UnassignedFencers_SetsError()
    {
        // Create tournament with a pool that has 4 fencers but total is 6
        var fencers = Enumerable.Range(0, 6)
            .Select(i => new TournamentFencer { Id = $"F{i}", Name = $"Fencer {i}" })
            .ToList();
        var tournament = new Tournament
        {
            Id = "t1",
            State = TournamentState.Setup,
            Fencers = fencers,
            Pools = new() { new Pool { Index = 0, FencerIds = fencers.Take(4).Select(f => f.Id).ToList() } }
        };
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().Contain("not assigned");
    }

    [Fact]
    public async Task StartTournament_ValidPools_GeneratesMatchesAndTransitions()
    {
        var tournament = CreateTournamentWithPools(poolSizes: new[] { 5, 5 });
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendMatchesAsync(It.IsAny<string>(), It.IsAny<IList<Match>>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().BeEmpty();
        _vm.Tournament!.State.Should().Be(TournamentState.PoolsInProgress);
        _vm.Tournament.Pools.Should().HaveCount(2);
        _vm.Tournament.Pools.SelectMany(p => p.Matches).Should().HaveCount(20); // 10+10
        _sheetsMock.Verify(s => s.AppendMatchesAsync("t1", It.Is<IList<Match>>(m => m.Count == 20)), Times.Once);
    }

    [Fact]
    public async Task StartTournament_NoDraftPools_FallsBackToAutoBuild()
    {
        var fencers = Enumerable.Range(0, 8)
            .Select(i => new TournamentFencer { Id = $"F{i}", Name = $"Fencer {i}" })
            .ToList();
        var tournament = new Tournament
        {
            Id = "t1",
            State = TournamentState.Setup,
            Fencers = fencers,
            Pools = new() // empty — no draft pools
        };
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendMatchesAsync(It.IsAny<string>(), It.IsAny<IList<Match>>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        await _vm.StartTournamentAsync();

        _vm.ErrorMessage.Should().BeEmpty();
        _vm.Tournament!.State.Should().Be(TournamentState.PoolsInProgress);
        _vm.Tournament.Pools.Should().NotBeEmpty();
        _vm.Tournament.Pools.SelectMany(p => p.Matches).Should().NotBeEmpty();
    }

    // -------- Computed properties expose correct data for binding --------

    [Fact]
    public async Task ComputedProperties_ReflectFencerState()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        _vm.FencerCountText.Should().Contain("4/128");
        _vm.CanStart.Should().BeTrue(); // 4 >= MinFencersToStart
        _vm.ShowPoolAllocation.Should().BeTrue();
        _vm.StartHintText.Should().Contain("Ready to start");
    }

    [Fact]
    public async Task ComputedProperties_BelowMinimum_ShowsHint()
    {
        var tournament = new Tournament
        {
            Id = "t1", Name = "Cup", PasswordPlain = "x",
            State = TournamentState.Setup,
            Fencers = new() { new() { Name = "A" }, new() { Name = "B" } }
        };
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        await _vm.InitExistingAsync("t1");

        _vm.CanStart.Should().BeFalse();
        _vm.StartHintText.Should().Contain("Add at least");
    }

    // -------- DeleteTournament: fires backend call --------

    [Fact]
    public async Task DeleteTournament_CallsBackend()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.DeleteTournamentAsync("t1")).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        await _vm.DeleteTournamentAsync();

        _sheetsMock.Verify(s => s.DeleteTournamentAsync("t1"), Times.Once);
        _cacheMock.Verify(c => c.InvalidateTournaments(), Times.Once);
    }

    // -------- Pool Add/Remove: regression tests for duplicate-pool bug --------

    [Fact]
    public async Task AddPool_AddsExactlyOnePool()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Should start with zero visible pools
        _vm.DraftPools.Should().BeEmpty();

        await _vm.AddPoolAsync();

        _vm.DraftPools.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddPool_CalledThreeTimes_ShowsExactlyThreePools()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();

        _vm.DraftPools.Should().HaveCount(3);
        // Pool names should be sequential
        _vm.DraftPools[0].Title.Should().Be("Pool 1");
        _vm.DraftPools[1].Title.Should().Be("Pool 2");
        _vm.DraftPools[2].Title.Should().Be("Pool 3");
    }

    [Fact]
    public async Task RemovePool_RemovesExactlyOnePool()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Add 3 pools
        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();
        _vm.DraftPools.Should().HaveCount(3);

        // Remove the middle one
        await _vm.RemovePoolAsync(_vm.DraftPools[1]);

        _vm.DraftPools.Should().HaveCount(2);
    }

    [Fact]
    public async Task RemovePool_CanRemoveLastEmptyPool()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Add one pool, then remove it
        await _vm.AddPoolAsync();
        _vm.DraftPools.Should().HaveCount(1);

        await _vm.RemovePoolAsync(_vm.DraftPools[0]);

        _vm.DraftPools.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePool_AllPools_LeavesZeroPools()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();

        // Remove all three one by one
        await _vm.RemovePoolAsync(_vm.DraftPools[2]);
        await _vm.RemovePoolAsync(_vm.DraftPools[1]);
        await _vm.RemovePoolAsync(_vm.DraftPools[0]);

        _vm.DraftPools.Should().BeEmpty();
    }

    [Fact]
    public async Task AddPool_AfterRemoveAll_AddsExactlyOne()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Add 2, remove both, then add 1 again
        await _vm.AddPoolAsync();
        await _vm.AddPoolAsync();
        await _vm.RemovePoolAsync(_vm.DraftPools[1]);
        await _vm.RemovePoolAsync(_vm.DraftPools[0]);
        _vm.DraftPools.Should().BeEmpty();

        await _vm.AddPoolAsync();

        _vm.DraftPools.Should().HaveCount(1, "only one pool should exist after re-adding");
    }

    [Fact]
    public async Task AddAndRemovePool_Interleaved_CorrectCount()
    {
        var tournament = CreateSampleTournament();
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Add ? Remove ? Add ? Add ? Remove ? should be 1
        await _vm.AddPoolAsync();       // 1
        await _vm.RemovePoolAsync(_vm.DraftPools[0]); // 0
        await _vm.AddPoolAsync();       // 1
        await _vm.AddPoolAsync();       // 2
        await _vm.RemovePoolAsync(_vm.DraftPools[0]); // 1

        _vm.DraftPools.Should().HaveCount(1);
    }

    // -------- Fencer name resolution: regression for "?" bug --------

    [Fact]
    public async Task AddFencer_ThenAutoDistribute_ShowsNamesNotQuestionMarks()
    {
        // Reproduces the bug: add 13 fencers via AddFencerAsync, auto-distribute,
        // then verify every pool member has a real name (not "?").
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendTournamentFencersAsync(It.IsAny<string>(), It.IsAny<IList<TournamentFencer>>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);

        _vm.InitNew();
        _vm.Name = "Test";
        _vm.Password = "pass";
        await _vm.SaveNewAsync();

        // Add 13 fencers one by one (the way a user would)
        for (int i = 0; i < 13; i++)
        {
            _vm.NewFencerName = $"Fencer{i}";
            await _vm.AddFencerAsync();
        }

        _vm.ActiveFencerCount.Should().Be(13);

        // Auto-distribute into pools
        await _vm.AutoDistributePoolsAsync();

        // Verify: no pool member should have "?" as name
        _vm.DraftPools.Should().NotBeEmpty();
        _vm.DraftPools.SelectMany(p => p.Members)
            .Should().AllSatisfy(m => m.Name.Should().NotBe("?",
                "all fencers added via AddFencerAsync must be resolvable by RebuildDraftPools"));
    }

    [Fact]
    public async Task AddFencer_ThenAutoDistribute_ThenRedistribute_AllNamesResolved()
    {
        // Simulates: add fencers ? auto-distribute ? auto-distribute again (user reshuffles)
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendTournamentFencersAsync(It.IsAny<string>(), It.IsAny<IList<TournamentFencer>>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);

        _vm.InitNew();
        _vm.Name = "Cup";
        _vm.Password = "x";
        await _vm.SaveNewAsync();

        for (int i = 0; i < 10; i++)
        {
            _vm.NewFencerName = $"Player{i}";
            await _vm.AddFencerAsync();
        }

        await _vm.AutoDistributePoolsAsync();
        await _vm.AutoDistributePoolsAsync(); // second time

        _vm.DraftPools.SelectMany(p => p.Members)
            .Should().AllSatisfy(m => m.Name.Should().NotBe("?"));

        // Total assigned should equal total active fencers
        var totalAssigned = _vm.DraftPools.Sum(p => p.Members.Count);
        totalAssigned.Should().Be(10);
    }

    [Fact]
    public async Task AddFencer_MoveFencer_NameStillResolved()
    {
        _sheetsMock.Setup(s => s.UpsertTournamentHeaderAsync(It.IsAny<Tournament>())).Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.AppendTournamentFencersAsync(It.IsAny<string>(), It.IsAny<IList<TournamentFencer>>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertTournamentFencerAsync(It.IsAny<string>(), It.IsAny<TournamentFencer>()))
            .Returns(Task.CompletedTask);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);

        _vm.InitNew();
        _vm.Name = "Cup";
        _vm.Password = "x";
        await _vm.SaveNewAsync();

        // Add 5 fencers, auto-distribute, then add more and move them
        for (int i = 0; i < 5; i++)
        {
            _vm.NewFencerName = $"F{i}";
            await _vm.AddFencerAsync();
        }
        await _vm.AutoDistributePoolsAsync();

        // Add 3 more
        for (int i = 5; i < 8; i++)
        {
            _vm.NewFencerName = $"F{i}";
            await _vm.AddFencerAsync();
        }

        // Move new fencers into existing pools
        var poolId = _vm.DraftPools[0].PoolId;
        var unassigned = _vm.UnassignedFencers;
        foreach (var f in unassigned)
            await _vm.MoveFencerToPoolAsync(f.FencerId, poolId);

        // All members should have names
        _vm.DraftPools.SelectMany(p => p.Members)
            .Should().AllSatisfy(m => m.Name.Should().NotBe("?"));
    }

    // -------- UnassignAllFencers: reset button --------

    [Fact]
    public async Task UnassignAllFencers_ClearsAllPoolAssignments()
    {
        var tournament = CreateTournamentWithPools(poolSizes: new[] { 5, 5 });
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Pools initially have fencers
        _vm.DraftPools.Sum(p => p.Members.Count).Should().Be(10);

        await _vm.UnassignAllFencersAsync();

        // All pools should be empty now
        _vm.DraftPools.Should().AllSatisfy(p => p.Members.Should().BeEmpty());
        // All fencers should be unassigned
        _vm.UnassignedFencers.Should().HaveCount(10);
    }

    [Fact]
    public async Task UnassignAllFencers_ThenAutoDistribute_WorksCleanly()
    {
        var tournament = CreateTournamentWithPools(poolSizes: new[] { 5, 5 });
        _sheetsMock.Setup(s => s.GetTournamentAsync("t1")).ReturnsAsync(tournament);
        _sheetsMock.Setup(s => s.UpsertPoolAsync(It.IsAny<string>(), It.IsAny<Pool>())).Returns(Task.CompletedTask);
        await _vm.InitExistingAsync("t1");

        // Unassign all, then redistribute
        await _vm.UnassignAllFencersAsync();
        await _vm.AutoDistributePoolsAsync();

        // All fencers should be re-assigned with proper names
        var totalAssigned = _vm.DraftPools.Sum(p => p.Members.Count);
        totalAssigned.Should().Be(10);
        _vm.DraftPools.SelectMany(p => p.Members)
            .Should().AllSatisfy(m => m.Name.Should().NotBe("?"));
        _vm.UnassignedFencers.Should().BeEmpty();
    }

    // -------- Helpers --------

    private static Tournament CreateSampleTournament() => new()
    {
        Id = "t1",
        Name = "Test Cup",
        PasswordPlain = "secret",
        State = TournamentState.Setup,
        Fencers = new()
        {
            new() { Id = "f1", Name = "Alice", OrderIndex = 0 },
            new() { Id = "f2", Name = "Bob", OrderIndex = 1 },
            new() { Id = "f3", Name = "Charlie", OrderIndex = 2 },
            new() { Id = "f4", Name = "Diana", OrderIndex = 3 },
        },
        Pools = new()
    };

    private static Tournament CreateTournamentWithPools(int[] poolSizes)
    {
        var fencers = new List<TournamentFencer>();
        var pools = new List<Pool>();
        int fencerIdx = 0;

        for (int p = 0; p < poolSizes.Length; p++)
        {
            var pool = new Pool { Index = p };
            for (int i = 0; i < poolSizes[p]; i++)
            {
                var f = new TournamentFencer { Id = $"F{fencerIdx}", Name = $"Fencer {fencerIdx}" };
                fencers.Add(f);
                pool.FencerIds.Add(f.Id);
                fencerIdx++;
            }
            pools.Add(pool);
        }

        return new Tournament
        {
            Id = "t1",
            State = TournamentState.Setup,
            Fencers = fencers,
            Pools = pools
        };
    }
}
