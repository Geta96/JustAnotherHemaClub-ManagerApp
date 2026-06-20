using System.Globalization;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService
{
    // Tournaments       : A=Id, B=Name, C=PasswordPlain, D=CreatedAt, E=State, F=Version
    // TournamentFencers : A=TournamentId, B=FencerId, C=Name, D=IsWithdrawn, E=OrderIndex
    // Pools             : A=TournamentId, B=PoolId, C=Index, D=FencerIdsCsv, E=IsClosed, F=Version
    // Matches           : A=TournamentId, B=MatchId, C=PoolId, D=BracketRound, E=BracketSlot,
    //                     F=BracketTag, G=OrderInPool, H=LeftFencerId, I=RightFencerId,
    //                     J=LeftScore, K=RightScore, L=LeftYellow, M=LeftRed,
    //                     N=RightYellow, O=RightRed, P=RemainingSec, Q=Status,
    //                     R=WinnerFencerId, S=StartedAtUtc, T=FinishedAtUtc,
    //                     U=Version, V=UpdatedAtUtc, W=UpdatedByUserId,
    //                     X=LockedByUserId, Y=LockedAtUtc
    // FinalStandings    : A=TournamentId, B=Position, C=FencerId

    private const string TournamentsRange     = "Tournaments!A2:F";
    private const string FencersRangeT        = "TournamentFencers!A2:E";
    private const string PoolsRange           = "Pools!A2:F";
    private const string MatchesRange         = "Matches!A2:Y";
    private const string FinalStandingsRange  = "FinalStandings!A2:C";

    /// <summary>
    /// Header rows including roster, so the list page can show "12 fencers" without
    /// a second round-trip per tournament. Two reads in parallel.
    /// </summary>
    public async Task<List<Tournament>> GetTournamentHeadersAsync()
    {
        var headersTask = ReadAsync(TournamentsRange);
        var fencersTask = ReadAsync(FencersRangeT);
        await Task.WhenAll(headersTask, fencersTask);

        var fencersByT = fencersTask.Result
            .Where(r => !string.IsNullOrWhiteSpace(S(r, 0)))
            .GroupBy(r => S(r, 0))
            .ToDictionary(g => g.Key,
                          g => g.Select(ParseFencer).OrderBy(f => f.OrderIndex).ToList());

        var list = new List<Tournament>();
        foreach (var r in headersTask.Result)
        {
            var id = S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var t = ParseTournamentHeader(r);
            if (fencersByT.TryGetValue(id, out var fencers)) t.Fencers = fencers;
            list.Add(t);
        }
        return list;
    }

    public async Task<Tournament?> GetTournamentAsync(string tournamentId)
    {
        if (string.IsNullOrWhiteSpace(tournamentId)) return null;

        // Five reads in parallel.
        var hdrTask     = ReadAsync(TournamentsRange);
        var fencersTask = ReadAsync(FencersRangeT);
        var poolsTask   = ReadAsync(PoolsRange);
        var matchesTask = ReadAsync(MatchesRange);
        var standTask   = ReadAsync(FinalStandingsRange);
        await Task.WhenAll(hdrTask, fencersTask, poolsTask, matchesTask, standTask);

        var hdrRow = hdrTask.Result.FirstOrDefault(r => S(r, 0) == tournamentId);
        if (hdrRow is null) return null;

        var t = ParseTournamentHeader(hdrRow);

        t.Fencers = fencersTask.Result
            .Where(r => S(r, 0) == tournamentId)
            .Select(ParseFencer)
            .OrderBy(f => f.OrderIndex)
            .ToList();

        var matches = matchesTask.Result
            .Where(r => S(r, 0) == tournamentId)
            .Select(ParseMatch)
            .ToList();

        t.Pools = poolsTask.Result
            .Where(r => S(r, 0) == tournamentId)
            .Select(ParsePool)
            .OrderBy(p => p.Index)
            .ToList();

        foreach (var pool in t.Pools)
            pool.Matches = matches
                .Where(m => m.PoolId == pool.Id)
                .OrderBy(m => m.OrderInPool)
                .ToList();

        var elimMatches = matches.Where(m => m.BracketRound.HasValue).ToList();
        if (elimMatches.Count > 0)
        {
            var bronze = elimMatches.FirstOrDefault(m => m.BracketTag == "Bronze");
            var rounds = elimMatches.Where(m => m.BracketTag != "Bronze")
                                    .GroupBy(m => m.BracketRound!.Value)
                                    .OrderBy(g => g.Key)
                                    .ToList();
            t.Bracket = new EliminationBracket
            {
                Size = rounds.Count > 0 ? rounds[0].Count() * 2 : 0,
                BronzeMatch = bronze,
                Rounds = rounds.Select(g => new EliminationRound
                {
                    Index = g.Key,
                    Name = TournamentEngine.RoundName(g.Count() * 2),
                    Matches = g.OrderBy(m => m.BracketSlot ?? 0).ToList()
                }).ToList()
            };
            TournamentEngine.PropagateAdvancements(t.Bracket);
        }

        t.FinalStandingFencerIds = standTask.Result
            .Where(r => S(r, 0) == tournamentId)
            .OrderBy(r => int.TryParse(S(r, 1), out var p) ? p : int.MaxValue)
            .Select(r => S(r, 2))
            .ToList();

        return t;
    }

    public async Task<List<Match>> GetMatchesAsync(string tournamentId)
    {
        var rows = await ReadAsync(MatchesRange);
        return rows.Where(r => S(r, 0) == tournamentId)
                   .Select(ParseMatch)
                   .ToList();
    }

    public async Task UpsertTournamentHeaderAsync(Tournament t)
    {
        var rows = await ReadAsync(TournamentsRange);
        int rowIndex = -1;
        int actualVersion = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (S(rows[i], 0) != t.Id) continue;
            rowIndex = i;
            int.TryParse(S(rows[i], 5), out actualVersion);
            break;
        }

        if (rowIndex >= 0 && actualVersion != t.Version)
            throw new ConcurrencyConflictException("Tournament", t.Id, t.Version, actualVersion);

        var nextVersion = (rowIndex >= 0 ? actualVersion : 0) + 1;
        var values = new List<object>
        {
            t.Id,
            t.Name ?? "",
            t.PasswordPlain ?? "",
            t.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
            t.State.ToString(),
            nextVersion
        };

        if (rowIndex >= 0) await UpdateAsync($"Tournaments!A{rowIndex + 2}:F{rowIndex + 2}", values);
        else                await AppendAsync("Tournaments!A:F", values);

        t.Version = nextVersion;
    }

    public async Task DeleteTournamentAsync(string tournamentId)
    {
        if (string.IsNullOrWhiteSpace(tournamentId)) return;

        // Five sheet clears in parallel — each is now ONE batched HTTP call regardless
        // of how many rows match, so total wall-time is roughly the slowest sheet.
        await Task.WhenAll(
            ClearRowsAsync(TournamentsRange,    "Tournaments!A{0}:F{0}",        0, tournamentId),
            ClearRowsAsync(FencersRangeT,       "TournamentFencers!A{0}:E{0}",  0, tournamentId),
            ClearRowsAsync(PoolsRange,          "Pools!A{0}:F{0}",              0, tournamentId),
            ClearRowsAsync(MatchesRange,        "Matches!A{0}:Y{0}",            0, tournamentId),
            ClearRowsAsync(FinalStandingsRange, "FinalStandings!A{0}:C{0}",     0, tournamentId));
    }

    public async Task UpsertTournamentFencerAsync(string tournamentId, TournamentFencer f)
    {
        var rows = await ReadAsync(FencersRangeT);
        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == tournamentId && S(rows[i], 1) == f.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            tournamentId, f.Id, f.Name ?? "", f.IsWithdrawn, f.OrderIndex
        };
        if (rowIndex >= 0) await UpdateAsync($"TournamentFencers!A{rowIndex + 2}:E{rowIndex + 2}", values);
        else                await AppendAsync("TournamentFencers!A:E", values);
    }

    public async Task DeleteTournamentFencerAsync(string tournamentId, string fencerId)
    {
        var rows = await ReadAsync(FencersRangeT);
        var rangesToClear = new List<string>();
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == tournamentId && S(rows[i], 1) == fencerId)
                rangesToClear.Add($"TournamentFencers!A{i + 2}:E{i + 2}");
        if (rangesToClear.Count == 0) return;

        var svc = await GetServiceAsync();
        var batch = new BatchClearValuesRequest { Ranges = rangesToClear };
        await svc.Spreadsheets.Values.BatchClear(batch, _spreadsheetId).ExecuteAsync();
    }

    public async Task UpsertPoolAsync(string tournamentId, Pool pool)
    {
        var rows = await ReadAsync(PoolsRange);
        int rowIndex = -1;
        int actualVersion = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (S(rows[i], 0) != tournamentId || S(rows[i], 1) != pool.Id) continue;
            rowIndex = i;
            int.TryParse(S(rows[i], 5), out actualVersion);
            break;
        }

        if (rowIndex >= 0 && actualVersion != pool.Version)
            throw new ConcurrencyConflictException("Pool", pool.Id, pool.Version, actualVersion);

        var nextVersion = (rowIndex >= 0 ? actualVersion : 0) + 1;
        var values = new List<object>
        {
            tournamentId, pool.Id, pool.Index,
            string.Join(",", pool.FencerIds),
            pool.IsClosed, nextVersion
        };
        if (rowIndex >= 0) await UpdateAsync($"Pools!A{rowIndex + 2}:F{rowIndex + 2}", values);
        else                await AppendAsync("Pools!A:F", values);

        pool.Version = nextVersion;
    }

    public async Task UpsertMatchAsync(string tournamentId, Match match)
    {
        var rows = await ReadAsync(MatchesRange);
        int rowIndex = -1;
        int actualVersion = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (S(rows[i], 0) != tournamentId || S(rows[i], 1) != match.Id) continue;
            rowIndex = i;
            int.TryParse(S(rows[i], 20), out actualVersion);
            break;
        }

        if (rowIndex >= 0 && actualVersion != match.Version)
            throw new ConcurrencyConflictException("Match", match.Id, match.Version, actualVersion);

        var nextVersion = (rowIndex >= 0 ? actualVersion : 0) + 1;
        match.UpdatedAtUtc = DateTime.UtcNow;

        var values = new List<object>
        {
            tournamentId, match.Id,
            match.PoolId ?? "",
            match.BracketRound?.ToString(CultureInfo.InvariantCulture) ?? "",
            match.BracketSlot?.ToString(CultureInfo.InvariantCulture) ?? "",
            match.BracketTag ?? "",
            match.OrderInPool,
            match.LeftFencerId ?? "", match.RightFencerId ?? "",
            match.LeftScore, match.RightScore,
            match.LeftYellowCards, match.LeftRedCards,
            match.RightYellowCards, match.RightRedCards,
            match.RemainingTimeSeconds,
            match.Status.ToString(),
            match.WinnerFencerId ?? "",
            match.StartedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            match.FinishedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            nextVersion,
            match.UpdatedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
            match.UpdatedByUserId ?? "",
            match.LockedByUserId ?? "",
            match.LockedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? ""
        };

        if (rowIndex >= 0) await UpdateAsync($"Matches!A{rowIndex + 2}:Y{rowIndex + 2}", values);
        else                await AppendAsync("Matches!A:Y", values);

        match.Version = nextVersion;
    }

    public async Task SaveFinalStandingsAsync(string tournamentId, IList<string> orderedFencerIds)
    {
        // Replace strategy: clear existing rows for this tournament, then append in order.
        await ClearRowsAsync(FinalStandingsRange, "FinalStandings!A{0}:C{0}", 0, tournamentId);
        if (orderedFencerIds.Count == 0) return;

        var svc = await GetServiceAsync();
        var body = new ValueRange
        {
            Values = orderedFencerIds
                .Select((id, i) => (IList<object>)new List<object> { tournamentId, i + 1, id })
                .ToList()
        };
        var req = svc.Spreadsheets.Values.Append(body, _spreadsheetId, "FinalStandings!A:C");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task DeleteMatchAsync(string tournamentId, string matchId)
    {
        if (string.IsNullOrWhiteSpace(tournamentId) || string.IsNullOrWhiteSpace(matchId)) return;

        var rows = await ReadAsync(MatchesRange);
        var rangesToClear = new List<string>();
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], 0) == tournamentId && S(rows[i], 1) == matchId)
                rangesToClear.Add($"Matches!A{i + 2}:Y{i + 2}");
        if (rangesToClear.Count == 0) return;

        var svc = await GetServiceAsync();
        var batch = new BatchClearValuesRequest { Ranges = rangesToClear };
        await svc.Spreadsheets.Values.BatchClear(batch, _spreadsheetId).ExecuteAsync();
    }

    public async Task AppendPoolsAsync(string tournamentId, IList<Pool> pools)
    {
        if (pools is null || pools.Count == 0) return;
        var svc = await GetServiceAsync();

        var rows = new List<IList<object>>(pools.Count);
        foreach (var p in pools)
        {
            p.Version = 1;
            rows.Add(new List<object>
            {
                tournamentId, p.Id, p.Index,
                string.Join(",", p.FencerIds),
                p.IsClosed, p.Version
            });
        }

        var body = new ValueRange { Values = rows };
        var req  = svc.Spreadsheets.Values.Append(body, _spreadsheetId, "Pools!A:F");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task AppendMatchesAsync(string tournamentId, IList<Match> matches)
    {
        if (matches is null || matches.Count == 0) return;
        var svc = await GetServiceAsync();
        var now = DateTime.UtcNow;

        var rows = new List<IList<object>>(matches.Count);
        foreach (var m in matches)
        {
            m.Version = 1;
            m.UpdatedAtUtc = now;
            rows.Add(new List<object>
            {
                tournamentId, m.Id,
                m.PoolId ?? "",
                m.BracketRound?.ToString(CultureInfo.InvariantCulture) ?? "",
                m.BracketSlot?.ToString(CultureInfo.InvariantCulture) ?? "",
                m.BracketTag ?? "",
                m.OrderInPool,
                m.LeftFencerId ?? "", m.RightFencerId ?? "",
                m.LeftScore, m.RightScore,
                m.LeftYellowCards, m.LeftRedCards,
                m.RightYellowCards, m.RightRedCards,
                m.RemainingTimeSeconds,
                m.Status.ToString(),
                m.WinnerFencerId ?? "",
                m.StartedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
                m.FinishedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
                m.Version,
                m.UpdatedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "",
                m.UpdatedByUserId ?? "",
                m.LockedByUserId ?? "",
                m.LockedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? ""
            });
        }

        var body = new ValueRange { Values = rows };
        var req  = svc.Spreadsheets.Values.Append(body, _spreadsheetId, "Matches!A:Y");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task AppendTournamentFencersAsync(string tournamentId, IList<TournamentFencer> fencers)
    {
        if (fencers is null || fencers.Count == 0) return;
        var svc = await GetServiceAsync();

        var rows = new List<IList<object>>(fencers.Count);
        foreach (var f in fencers)
            rows.Add(new List<object>
            {
                tournamentId, f.Id, f.Name ?? "", f.IsWithdrawn, f.OrderIndex
            });

        var body = new ValueRange { Values = rows };
        var req  = svc.Spreadsheets.Values.Append(body, _spreadsheetId, "TournamentFencers!A:E");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    // ---------- Row parsers ----------

    private static Tournament ParseTournamentHeader(IList<object> r) => new()
    {
        Id            = S(r, 0),
        Name          = S(r, 1),
        PasswordPlain = S(r, 2),
        CreatedAt     = DateTime.TryParse(S(r, 3), CultureInfo.InvariantCulture,
                                          DateTimeStyles.RoundtripKind, out var c) ? c : DateTime.UtcNow,
        State         = Enum.TryParse<TournamentState>(S(r, 4), true, out var st) ? st : TournamentState.Setup,
        Version       = int.TryParse(S(r, 5), out var v) ? v : 0
    };

    private static TournamentFencer ParseFencer(IList<object> r) => new()
    {
        Id          = S(r, 1),
        Name        = S(r, 2),
        IsWithdrawn = ParseBool(S(r, 3)),
        OrderIndex  = int.TryParse(S(r, 4), out var o) ? o : 0
    };

    private static Pool ParsePool(IList<object> r) => new()
    {
        Id        = S(r, 1),
        Index     = int.TryParse(S(r, 2), out var i) ? i : 0,
        FencerIds = S(r, 3).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        IsClosed  = ParseBool(S(r, 4)),
        Version   = int.TryParse(S(r, 5), out var v) ? v : 0
    };

    private static Match ParseMatch(IList<object> r) => new()
    {
        Id               = S(r, 1),
        PoolId           = string.IsNullOrEmpty(S(r, 2)) ? null : S(r, 2),
        BracketRound     = int.TryParse(S(r, 3), out var br) ? br : (int?)null,
        BracketSlot      = int.TryParse(S(r, 4), out var bs) ? bs : (int?)null,
        BracketTag       = string.IsNullOrEmpty(S(r, 5)) ? null : S(r, 5),
        OrderInPool      = int.TryParse(S(r, 6), out var ord) ? ord : 0,
        LeftFencerId     = S(r, 7),
        RightFencerId    = S(r, 8),
        LeftScore        = int.TryParse(S(r,  9), out var ls) ? ls : 0,
        RightScore       = int.TryParse(S(r, 10), out var rs) ? rs : 0,
        LeftYellowCards  = int.TryParse(S(r, 11), out var ly) ? ly : 0,
        LeftRedCards     = int.TryParse(S(r, 12), out var lr) ? lr : 0,
        RightYellowCards = int.TryParse(S(r, 13), out var ry) ? ry : 0,
        RightRedCards    = int.TryParse(S(r, 14), out var rr) ? rr : 0,
        RemainingTimeSeconds = int.TryParse(S(r, 15), out var sec) ? sec : 180,
        Status           = Enum.TryParse<MatchStatus>(S(r, 16), true, out var st) ? st : MatchStatus.Pending,
        WinnerFencerId   = string.IsNullOrEmpty(S(r, 17)) ? null : S(r, 17),
        StartedAtUtc     = DateTime.TryParse(S(r, 18), CultureInfo.InvariantCulture,
                                             DateTimeStyles.RoundtripKind, out var sa) ? sa : (DateTime?)null,
        FinishedAtUtc    = DateTime.TryParse(S(r, 19), CultureInfo.InvariantCulture,
                                             DateTimeStyles.RoundtripKind, out var fa) ? fa : (DateTime?)null,
        Version          = int.TryParse(S(r, 20), out var vv) ? vv : 0,
        UpdatedAtUtc     = DateTime.TryParse(S(r, 21), CultureInfo.InvariantCulture,
                                             DateTimeStyles.RoundtripKind, out var ua) ? ua : (DateTime?)null,
        UpdatedByUserId  = string.IsNullOrEmpty(S(r, 22)) ? null : S(r, 22),
        LockedByUserId   = string.IsNullOrEmpty(S(r, 23)) ? null : S(r, 23),
        LockedAtUtc      = DateTime.TryParse(S(r, 24), CultureInfo.InvariantCulture,
                                             DateTimeStyles.RoundtripKind, out var la) ? la : (DateTime?)null
    };

    private async Task ClearRowsAsync(string readRange, string rowRangeFormat,
                                      int matchColumn, string matchValue)
    {
        var rows = await ReadAsync(readRange);
        var rangesToClear = new List<string>();
        for (int i = 0; i < rows.Count; i++)
            if (S(rows[i], matchColumn) == matchValue)
                rangesToClear.Add(string.Format(CultureInfo.InvariantCulture, rowRangeFormat, i + 2));
        if (rangesToClear.Count == 0) return;

        // One HTTP call regardless of row count.
        var svc = await GetServiceAsync();
        var batch = new BatchClearValuesRequest { Ranges = rangesToClear };
        await svc.Spreadsheets.Values.BatchClear(batch, _spreadsheetId).ExecuteAsync();
    }
}