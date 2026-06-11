using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public sealed partial class CachedGoogleSheetsService
{
    private List<Tournament>? _tournamentHeaders;
    private readonly Dictionary<string, Tournament> _tournamentById = new();

    public async Task<List<Tournament>> GetTournamentHeadersAsync()
    {
        if (_tournamentHeaders is not null) return new List<Tournament>(_tournamentHeaders);
        await _gate.WaitAsync();
        try
        {
            _tournamentHeaders ??= await _inner.GetTournamentHeadersAsync();
            return new List<Tournament>(_tournamentHeaders);
        }
        finally { _gate.Release(); }
    }

    public async Task<Tournament?> GetTournamentAsync(string tournamentId)
    {
        if (_tournamentById.TryGetValue(tournamentId, out var cached)) return cached;
        await _gate.WaitAsync();
        try
        {
            if (!_tournamentById.TryGetValue(tournamentId, out cached))
            {
                cached = await _inner.GetTournamentAsync(tournamentId);
                if (cached is not null) _tournamentById[tournamentId] = cached;
            }
            return cached;
        }
        finally { _gate.Release(); }
    }

    public Task<List<Match>> GetMatchesAsync(string tournamentId) =>
        // Polling always wants fresh data — never cache.
        _inner.GetMatchesAsync(tournamentId);

    public async Task UpsertTournamentHeaderAsync(Tournament tournament)
    {
        await _inner.UpsertTournamentHeaderAsync(tournament);
        _tournamentHeaders?.RemoveAll(t => t.Id == tournament.Id);
        _tournamentHeaders?.Add(tournament);
        _tournamentById[tournament.Id] = tournament;
    }

    public async Task DeleteTournamentAsync(string tournamentId)
    {
        await _inner.DeleteTournamentAsync(tournamentId);
        _tournamentHeaders?.RemoveAll(t => t.Id == tournamentId);
        _tournamentById.Remove(tournamentId);
    }

    public async Task UpsertTournamentFencerAsync(string tournamentId, TournamentFencer fencer)
    {
        await _inner.UpsertTournamentFencerAsync(tournamentId, fencer);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
        {
            var idx = t.Fencers.FindIndex(f => f.Id == fencer.Id);
            if (idx >= 0) t.Fencers[idx] = fencer;
            else t.Fencers.Add(fencer);
        }
    }

    public async Task DeleteTournamentFencerAsync(string tournamentId, string fencerId)
    {
        await _inner.DeleteTournamentFencerAsync(tournamentId, fencerId);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
            t.Fencers.RemoveAll(f => f.Id == fencerId);
    }

    public async Task UpsertPoolAsync(string tournamentId, Pool pool)
    {
        await _inner.UpsertPoolAsync(tournamentId, pool);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
        {
            var idx = t.Pools.FindIndex(p => p.Id == pool.Id);
            if (idx >= 0) t.Pools[idx] = pool;
            else t.Pools.Add(pool);
        }
    }

    public async Task AppendPoolsAsync(string tournamentId, IList<Pool> pools)
    {
        await _inner.AppendPoolsAsync(tournamentId, pools);
        // Force a fresh aggregate fetch next time — patching the cache for bulk inserts is brittle.
        _tournamentById.Remove(tournamentId);
    }

    public async Task AppendMatchesAsync(string tournamentId, IList<Match> matches)
    {
        await _inner.AppendMatchesAsync(tournamentId, matches);
        _tournamentById.Remove(tournamentId);
    }

    public async Task UpsertMatchAsync(string tournamentId, Match match)
    {
        // Backend bumps the Version; cache patches with the post-write instance.
        await _inner.UpsertMatchAsync(tournamentId, match);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
            PatchMatchInAggregate(t, match);
    }

    public async Task SaveFinalStandingsAsync(string tournamentId, IList<string> orderedFencerIds)
    {
        await _inner.SaveFinalStandingsAsync(tournamentId, orderedFencerIds);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
            t.FinalStandingFencerIds = orderedFencerIds.ToList();
    }

    private static void PatchMatchInAggregate(Tournament t, Match match)
    {
        foreach (var pool in t.Pools)
        {
            var idx = pool.Matches.FindIndex(m => m.Id == match.Id);
            if (idx >= 0) { pool.Matches[idx] = match; return; }
        }
        if (t.Bracket is null) return;

        foreach (var round in t.Bracket.Rounds)
        {
            var idx = round.Matches.FindIndex(m => m.Id == match.Id);
            if (idx >= 0) { round.Matches[idx] = match; return; }
        }
        if (t.Bracket.BronzeMatch?.Id == match.Id) t.Bracket.BronzeMatch = match;
    }

    public void InvalidateTournaments()
    {
        _tournamentHeaders = null;
        _tournamentById.Clear();
    }

    public async Task AppendTournamentFencersAsync(string tournamentId, IList<TournamentFencer> fencers)
    {
        await _inner.AppendTournamentFencersAsync(tournamentId, fencers);
        if (_tournamentById.TryGetValue(tournamentId, out var t))
            foreach (var f in fencers) t.Fencers.Add(f);
    }
}