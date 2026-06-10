using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Side-effect-free tournament logic. Callers persist the resulting state via
/// <see cref="TournamentAutoSaveService"/> / <see cref="IGoogleSheetsService"/>.
/// </summary>
public static class TournamentEngine
{
    public const int DefaultMatchSeconds = 180;

    // ---------- Pool partitioning ----------

    /// <summary>
    /// Splits the field into pools of 4-6, as equal-sized as possible
    /// (prefers pools of 5). Returns indices into the original fencer list.
    /// </summary>
    public static List<List<int>> PartitionIntoPools(int fencerCount, Random? rng = null)
    {
        if (fencerCount < 4)
            throw new ArgumentException("At least 4 fencers are required.", nameof(fencerCount));

        int K = Math.Max(1, (int)Math.Round(fencerCount / 5.0));
        while (K > 1 && fencerCount / K < 4) K--;                              // no pool < 4
        while (fencerCount / K + (fencerCount % K > 0 ? 1 : 0) > 6) K++;       // no pool > 6

        int baseSize = fencerCount / K;
        int rem = fencerCount % K;

        var order = Enumerable.Range(0, fencerCount).ToList();
        if (rng is not null)
        {
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        var pools = new List<List<int>>(K);
        int idx = 0;
        for (int p = 0; p < K; p++)
        {
            int size = baseSize + (p < rem ? 1 : 0);
            var pool = new List<int>(size);
            for (int s = 0; s < size; s++) pool.Add(order[idx++]);
            pools.Add(pool);
        }
        return pools;
    }

    // ---------- Round-robin fight order ----------

    /// <summary>
    /// Round-robin pair order that tries to avoid a fencer appearing in two consecutive matches.
    /// Returns pairs of indices into the pool's fencer list.
    /// </summary>
    public static List<(int Left, int Right)> OrderPoolFights(int fencerCount)
    {
        var pairs = new List<(int, int)>();
        for (int i = 0; i < fencerCount; i++)
            for (int j = i + 1; j < fencerCount; j++)
                pairs.Add((i, j));

        var remaining = new List<(int, int)>(pairs);
        var ordered = new List<(int, int)>(pairs.Count);
        int lastA = -1, lastB = -1, prevA = -1, prevB = -1;

        while (remaining.Count > 0)
        {
            int pick = -1;

            // Strongest: not in either of the last two matches.
            for (int k = 0; k < remaining.Count; k++)
            {
                var (a, b) = remaining[k];
                if (a != lastA && a != lastB && b != lastA && b != lastB &&
                    a != prevA && a != prevB && b != prevA && b != prevB)
                { pick = k; break; }
            }
            // Fallback: only avoid the immediately preceding match.
            if (pick < 0)
                for (int k = 0; k < remaining.Count; k++)
                {
                    var (a, b) = remaining[k];
                    if (a != lastA && a != lastB && b != lastA && b != lastB)
                    { pick = k; break; }
                }
            if (pick < 0) pick = 0;

            var chosen = remaining[pick];
            remaining.RemoveAt(pick);
            ordered.Add(chosen);
            prevA = lastA; prevB = lastB;
            lastA = chosen.Item1; lastB = chosen.Item2;
        }
        return ordered;
    }

    // ---------- Pool building ----------

    public static List<Pool> BuildPools(IReadOnlyList<TournamentFencer> fencers, Random? rng = null)
    {
        var partitions = PartitionIntoPools(fencers.Count, rng);
        var pools = new List<Pool>(partitions.Count);

        for (int p = 0; p < partitions.Count; p++)
        {
            var poolFencers = partitions[p].Select(i => fencers[i]).ToList();
            var pool = new Pool
            {
                Index = p,
                FencerIds = poolFencers.Select(f => f.Id).ToList()
            };
            int order = 0;
            foreach (var (l, r) in OrderPoolFights(poolFencers.Count))
            {
                pool.Matches.Add(new Match
                {
                    PoolId = pool.Id,
                    OrderInPool = order++,
                    LeftFencerId = poolFencers[l].Id,
                    RightFencerId = poolFencers[r].Id,
                    RemainingTimeSeconds = DefaultMatchSeconds
                });
            }
            pools.Add(pool);
        }
        return pools;
    }

    // ---------- Draft pools (Setup state, before matches are generated) ----------

    /// <summary>
    /// Builds DRAFT pools that contain only fencer assignments — no matches yet.
    /// Used in the editor's Setup state so the organiser can rearrange fencers
    /// before <see cref="GeneratePoolMatches"/> creates the actual fight list.
    /// </summary>
    public static List<Pool> BuildDraftPools(IReadOnlyList<TournamentFencer> fencers, Random? rng = null)
    {
        var partitions = PartitionIntoPools(fencers.Count, rng);
        var pools = new List<Pool>(partitions.Count);
        for (int p = 0; p < partitions.Count; p++)
        {
            pools.Add(new Pool
            {
                Index = p,
                FencerIds = partitions[p].Select(i => fencers[i].Id).ToList()
            });
        }
        return pools;
    }

    /// <summary>
    /// Populates each pool's match list (round-robin) from its current
    /// <see cref="Pool.FencerIds"/>. Pools with fewer than 2 fencers get no matches.
    /// Existing matches are replaced.
    /// </summary>
    public static void GeneratePoolMatches(IList<Pool> pools)
    {
        foreach (var pool in pools)
        {
            pool.Matches.Clear();
            int n = pool.FencerIds.Count;
            if (n < 2) continue;

            int order = 0;
            foreach (var (l, r) in OrderPoolFights(n))
            {
                pool.Matches.Add(new Match
                {
                    PoolId = pool.Id,
                    OrderInPool = order++,
                    LeftFencerId  = pool.FencerIds[l],
                    RightFencerId = pool.FencerIds[r],
                    RemainingTimeSeconds = DefaultMatchSeconds
                });
            }
        }
    }

    // ---------- Pool standings ----------

    public sealed class PoolStandingRow
    {
        public string FencerId { get; init; } = string.Empty;
        public int MatchesPlayed { get; set; }
        public int MatchesWon { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }

        public double Windicator         => MatchesPlayed > 0 ? (double)MatchesWon    / MatchesPlayed : 0;
        public double AvgPointsFor     => MatchesPlayed > 0 ? (double)PointsFor     / MatchesPlayed : 0;
        public double AvgPointsAgainst => MatchesPlayed > 0 ? (double)PointsAgainst / MatchesPlayed : 0;
    }

    /// <summary>Ranking within a single pool.</summary>
    public static List<PoolStandingRow> ComputePoolStandings(Pool pool)
    {
        var rows = pool.FencerIds.ToDictionary(id => id, id => new PoolStandingRow { FencerId = id });

        foreach (var m in pool.Matches.Where(m => m.Status == MatchStatus.Finished))
        {
            if (!rows.TryGetValue(m.LeftFencerId, out var left)) continue;
            if (!rows.TryGetValue(m.RightFencerId, out var right)) continue;

            left.MatchesPlayed++;  right.MatchesPlayed++;
            left.PointsFor     += m.LeftScore;  left.PointsAgainst  += m.RightScore;
            right.PointsFor    += m.RightScore; right.PointsAgainst += m.LeftScore;
            if      (m.WinnerFencerId == m.LeftFencerId)  left.MatchesWon++;
            else if (m.WinnerFencerId == m.RightFencerId) right.MatchesWon++;
        }
        return SortStandings(rows.Values);
    }

    /// <summary>Combined cross-pool ranking, used to seed the elimination bracket.</summary>
    public static List<PoolStandingRow> ComputeGlobalStandings(Tournament tournament)
    {
        var all = new List<PoolStandingRow>();
        foreach (var pool in tournament.Pools)
            all.AddRange(ComputePoolStandings(pool));
        return SortStandings(all);
    }

    private static List<PoolStandingRow> SortStandings(IEnumerable<PoolStandingRow> rows) =>
        rows.OrderByDescending(r => r.Windicator)
            .ThenByDescending(r => r.AvgPointsFor)
            .ThenBy(r => r.AvgPointsAgainst)
            .ToList();

    // ---------- Elimination bracket ----------

    /// <summary>Builds the bracket from the pool standings (top 60%, rounded up).</summary>
    public static EliminationBracket BuildBracket(Tournament tournament)
    {
        var standings    = ComputeGlobalStandings(tournament);
        int seededCount  = Math.Max(1, (int)Math.Ceiling(standings.Count * 0.6));
        var seededIds    = standings.Take(seededCount).Select(s => s.FencerId).ToList();
        int size         = PickBracketSize(seededIds.Count);

        var bracket = new EliminationBracket { Size = size };

        int[] seedOrder = BuildBracketSeedOrder(size);
        var round1 = new EliminationRound { Index = 0, Name = RoundName(size) };

        for (int i = 0; i < size; i += 2)
        {
            int seedA = seedOrder[i];      // 1-based seed numbers
            int seedB = seedOrder[i + 1];
            string? leftId  = seedA <= seededIds.Count ? seededIds[seedA - 1] : null;
            string? rightId = seedB <= seededIds.Count ? seededIds[seedB - 1] : null;

            var match = new Match
            {
                BracketRound = 0,
                BracketSlot  = i / 2,
                LeftFencerId  = leftId  ?? "",
                RightFencerId = rightId ?? "",
                RemainingTimeSeconds = DefaultMatchSeconds
            };

            // Auto-resolve byes (one side empty).
            if (string.IsNullOrEmpty(leftId) ^ string.IsNullOrEmpty(rightId))
            {
                match.Status = MatchStatus.Finished;
                match.WinnerFencerId = string.IsNullOrEmpty(leftId) ? rightId : leftId;
            }
            round1.Matches.Add(match);
        }
        bracket.Rounds.Add(round1);

        // Placeholder later rounds; filled by PropagateAdvancements as winners emerge.
        int matchesInRound = size / 2;
        int roundIndex = 1;
        while (matchesInRound > 1)
        {
            matchesInRound /= 2;
            var next = new EliminationRound { Index = roundIndex, Name = RoundName(matchesInRound * 2) };
            for (int s = 0; s < matchesInRound; s++)
                next.Matches.Add(new Match
                {
                    BracketRound = roundIndex,
                    BracketSlot  = s,
                    RemainingTimeSeconds = DefaultMatchSeconds
                });
            bracket.Rounds.Add(next);
            roundIndex++;
        }

        // Bronze + tag the final.
        bracket.BronzeMatch = new Match { BracketTag = "Bronze", RemainingTimeSeconds = DefaultMatchSeconds };
        if (bracket.Rounds.Count > 0 && bracket.Rounds[^1].Matches.Count == 1)
            bracket.Rounds[^1].Matches[0].BracketTag = "Final";

        PropagateAdvancements(bracket);
        return bracket;
    }

    public static int PickBracketSize(int seededCount)
    {
        int[] sizes = { 8, 16, 32, 64, 128 };
        foreach (var s in sizes) if (s >= seededCount) return s;
        return 128;
    }

    public static string RoundName(int participants) => participants switch
    {
        2 => "Final",
        4 => "Semi-finals",
        8 => "Quarter-finals",
        _ => $"Round of {participants}"
    };

    /// <summary>
    /// Standard bracket pairing so seeds 1 and 2 only meet in the final, 1-4 only in semis, etc.
    /// Example size=8 → [1, 8, 4, 5, 2, 7, 3, 6].
    /// </summary>
    public static int[] BuildBracketSeedOrder(int size)
    {
        int[] order = { 1 };
        while (order.Length < size)
        {
            int sum = order.Length * 2 + 1;
            var next = new int[order.Length * 2];
            for (int i = 0; i < order.Length; i++)
            {
                next[i * 2]     = order[i];
                next[i * 2 + 1] = sum - order[i];
            }
            order = next;
        }
        return order;
    }

    /// <summary>
    /// After any elim match finishes, promote winners to the next round and seed the bronze
    /// match once both semis are decided. Safe to call after every save.
    /// </summary>
    public static void PropagateAdvancements(EliminationBracket bracket)
    {
        for (int r = 0; r < bracket.Rounds.Count - 1; r++)
        {
            var thisRound = bracket.Rounds[r];
            var nextRound = bracket.Rounds[r + 1];

            for (int slot = 0; slot < nextRound.Matches.Count; slot++)
            {
                var feedL  = thisRound.Matches[slot * 2];
                var feedR  = thisRound.Matches[slot * 2 + 1];
                var target = nextRound.Matches[slot];
                if (target.Status == MatchStatus.Finished) continue;

                target.LeftFencerId  = feedL.Status == MatchStatus.Finished ? feedL.WinnerFencerId ?? "" : "";
                target.RightFencerId = feedR.Status == MatchStatus.Finished ? feedR.WinnerFencerId ?? "" : "";

                // Auto-bye through if both feeders are decided and exactly one side is empty.
                if (feedL.Status == MatchStatus.Finished && feedR.Status == MatchStatus.Finished &&
                    (string.IsNullOrEmpty(target.LeftFencerId) ^ string.IsNullOrEmpty(target.RightFencerId)))
                {
                    target.Status = MatchStatus.Finished;
                    target.WinnerFencerId = string.IsNullOrEmpty(target.LeftFencerId)
                        ? target.RightFencerId
                        : target.LeftFencerId;
                }
            }
        }

        // Seed bronze from semi-final losers.
        if (bracket.BronzeMatch is not null && bracket.Rounds.Count >= 2)
        {
            var semis = bracket.Rounds[^2];
            if (semis.Matches.Count == 2 &&
                semis.Matches[0].Status == MatchStatus.Finished &&
                semis.Matches[1].Status == MatchStatus.Finished &&
                bracket.BronzeMatch.Status != MatchStatus.Finished)
            {
                bracket.BronzeMatch.LeftFencerId  = LoserOf(semis.Matches[0]) ?? "";
                bracket.BronzeMatch.RightFencerId = LoserOf(semis.Matches[1]) ?? "";
            }
        }
    }

    private static string? LoserOf(Match m)
    {
        if (m.Status != MatchStatus.Finished || string.IsNullOrEmpty(m.WinnerFencerId)) return null;
        return m.WinnerFencerId == m.LeftFencerId ? m.RightFencerId : m.LeftFencerId;
    }

    // ---------- Final standings ----------

    /// <summary>
    /// Final placement (best first) for every fencer who entered the bracket.
    /// 1 = gold, 2 = silver, 3 = bronze winner, 4 = bronze loser.
    /// Then, recursively per round (QF → R16 → R32 → R64 → R128):
    ///   the losers of that round are placed after all earlier-eliminated rounds,
    ///   ordered by the FINAL position of the fencer who knocked them out.
    ///   So in QF: the QF-loser whose conqueror finished 1st → 5th place,
    ///   conqueror finished 2nd → 6th, 3rd → 7th, 4th → 8th. The same logic
    ///   places 9–16, 17–32, 33–64, 65–128. Byes (no opponent) are skipped.
    /// </summary>
    public static List<string> ComputeFinalStandings(Tournament tournament)
    {
        var placement = new List<string>();
        var bracket = tournament.Bracket;
        if (bracket is null || bracket.Rounds.Count == 0) return placement;

        var final  = bracket.Rounds[^1].Matches.FirstOrDefault();
        var bronze = bracket.BronzeMatch;

        // Slots 1..4 are explicit.
        AddPlace(placement, final?.WinnerFencerId);
        AddPlace(placement, final is not null ? LoserOf(final) : null);
        AddPlace(placement, bronze?.WinnerFencerId);
        AddPlace(placement, bronze is not null ? LoserOf(bronze) : null);

        var placeOf = new Dictionary<string, int>();
        for (int i = 0; i < placement.Count; i++) placeOf[placement[i]] = i + 1;

        // Walk earlier rounds, latest-first (semis → QF → R16 → R32 → ...).
        // Skip the final round; skip the semis too — semi-finalists are 3rd/4th via the bronze.
        for (int r = bracket.Rounds.Count - 2; r >= 0; r--)
        {
            // Semi-final losers are already placed via the bronze match.
            if (r == bracket.Rounds.Count - 2) continue;

            var losers = new List<(string Loser, string Conqueror)>();
            foreach (var m in bracket.Rounds[r].Matches)
            {
                if (m.Status != MatchStatus.Finished) continue;
                if (string.IsNullOrEmpty(m.WinnerFencerId)) continue;

                var loser = LoserOf(m);
                if (string.IsNullOrEmpty(loser)) continue;             // bye / unresolved
                if (placeOf.ContainsKey(loser!))  continue;             // already placed (defensive)

                losers.Add((loser!, m.WinnerFencerId!));
            }

            // Order purely by how well the conqueror finished:
            //  - smaller PlaceOf is better (1 > 2 > 3 …)
            //  - unknown placements (shouldn't happen on a complete bracket) sort last
            //  - stable tiebreaker on the fencer id keeps the result deterministic
            var ordered = losers
                .OrderBy(x => placeOf.TryGetValue(x.Conqueror, out var p) ? p : int.MaxValue)
                .ThenBy(x => x.Loser, StringComparer.Ordinal);

            foreach (var (loser, _) in ordered)
            {
                placement.Add(loser);
                placeOf[loser] = placement.Count;
            }
        }
        return placement;
    }

    /// <summary>Computes <see cref="ComputeFinalStandings"/> and returns it as a fencer-to-place map.</summary>
    public static Dictionary<string, int> ComputePlacementOf(Tournament tournament)
    {
        var order = ComputeFinalStandings(tournament);
        var map = new Dictionary<string, int>(order.Count);
        for (int i = 0; i < order.Count; i++) map[order[i]] = i + 1;
        return map;
    }

    private static void AddPlace(List<string> placement, string? id)
    {
        if (!string.IsNullOrEmpty(id) && !placement.Contains(id))
            placement.Add(id!);
    }

    // ---------- Pool-standings-aware bracket build (Win% / AvgFor / AvgAgainst / RedCards) ----------

    /// <summary>
    /// Returns the IDs of fencers who qualify for the elimination, in global seed
    /// order (best first). Rules, in order:
    ///   1. Per-pool top 60% (rounded away-from-zero, min 1) qualify by default.
    ///   2. If the tournament has fewer than 8 fencers in total, ALL fencers qualify.
    ///   3. Otherwise, if the per-pool filter yields fewer than 8 qualifiers, the
    ///      next best fencers globally are added until 8 qualify.
    /// </summary>
    public static List<string> ComputeQualifyingFencerIds(Tournament t)
    {
        if (t.Pools.Count == 0) return new List<string>();

        var byPool = new List<List<ElimSeedStats>>(t.Pools.Count);
        var all    = new List<ElimSeedStats>();

        foreach (var pool in t.Pools)
        {
            var stats = pool.FencerIds.ToDictionary(id => id, id => new ElimSeedStats { FencerId = id });
            foreach (var m in pool.Matches.Where(m => m.Status == MatchStatus.Finished))
            {
                if (!stats.TryGetValue(m.LeftFencerId,  out var ls)) continue;
                if (!stats.TryGetValue(m.RightFencerId, out var rs)) continue;
                ls.MatchesDone++;                    rs.MatchesDone++;
                ls.PointsFor     += m.LeftScore;     rs.PointsFor     += m.RightScore;
                ls.PointsAgainst += m.RightScore;    rs.PointsAgainst += m.LeftScore;
                ls.RedCards      += m.LeftRedCards;  rs.RedCards      += m.RightRedCards;
                if      (m.WinnerFencerId == m.LeftFencerId)  ls.Wins++;
                else if (m.WinnerFencerId == m.RightFencerId) rs.Wins++;
            }

            var orderedPool = SortSeedStats(stats.Values);
            byPool.Add(orderedPool);
            all.AddRange(orderedPool);
        }

        int totalFencers = all.Count;
        if (totalFencers == 0) return new List<string>();

        var globallyOrdered = SortSeedStats(all);

        // Rule 2: < 8 fencers in total → every fencer enters the elimination.
        if (totalFencers < 8)
            return globallyOrdered.Select(s => s.FencerId).ToList();

        // Rule 1: per-pool top 60% baseline.
        var qualifierSet = new HashSet<string>();
        foreach (var poolOrdered in byPool)
        {
            int qCount = (int)Math.Round(poolOrdered.Count * 0.6, MidpointRounding.AwayFromZero);
            if (qCount < 1 && poolOrdered.Count > 0) qCount = 1;
            if (qCount > poolOrdered.Count)          qCount = poolOrdered.Count;
            foreach (var q in poolOrdered.Take(qCount)) qualifierSet.Add(q.FencerId);
        }

        // Rule 3: floor of 8. If 60% per pool didn't get us there, fill from the
        // global ranking (best non-qualifier first) until we have 8.
        int target = Math.Min(8, totalFencers);
        if (qualifierSet.Count < target)
        {
            foreach (var s in globallyOrdered)
            {
                if (qualifierSet.Count >= target) break;
                qualifierSet.Add(s.FencerId);
            }
        }

        // Return qualifiers in global-ranking order so callers can use them as seeds 1..N.
        return globallyOrdered
            .Where(s => qualifierSet.Contains(s.FencerId))
            .Select(s => s.FencerId)
            .ToList();
    }

    /// <summary>
    /// Build the bracket from per-pool standings using the same criteria the Pool
    /// Standings tab shows (Win% → AvgFor desc → AvgAgainst asc → RedCards asc).
    /// Qualification rules are owned by <see cref="ComputeQualifyingFencerIds"/>.
    /// </summary>
    public static EliminationBracket BuildBracketFromPoolStandings(Tournament t)
    {
        var seededIds = ComputeQualifyingFencerIds(t);
        int size      = PickBracketSize(seededIds.Count);

        var bracket = new EliminationBracket { Size = size };
        int[] seedOrder = BuildBracketSeedOrder(size);
        var round1 = new EliminationRound { Index = 0, Name = RoundName(size) };

        for (int i = 0; i < size; i += 2)
        {
            int seedA = seedOrder[i];
            int seedB = seedOrder[i + 1];
            string? leftId  = seedA <= seededIds.Count ? seededIds[seedA - 1] : null;
            string? rightId = seedB <= seededIds.Count ? seededIds[seedB - 1] : null;

            var match = new Match
            {
                BracketRound = 0,
                BracketSlot  = i / 2,
                LeftFencerId  = leftId  ?? "",
                RightFencerId = rightId ?? "",
                RemainingTimeSeconds = DefaultMatchSeconds
            };

            // Auto-resolve byes (top seed without a first-round opponent).
            if (string.IsNullOrEmpty(leftId) ^ string.IsNullOrEmpty(rightId))
            {
                match.Status = MatchStatus.Finished;
                match.WinnerFencerId = string.IsNullOrEmpty(leftId) ? rightId : leftId;
            }
            round1.Matches.Add(match);
        }
        bracket.Rounds.Add(round1);

        // Placeholder later rounds; PropagateAdvancements fills Left/Right as winners emerge.
        int matchesInRound = size / 2;
        int roundIndex     = 1;
        while (matchesInRound > 1)
        {
            matchesInRound /= 2;
            var next = new EliminationRound { Index = roundIndex, Name = RoundName(matchesInRound * 2) };
            for (int s = 0; s < matchesInRound; s++)
                next.Matches.Add(new Match
                {
                    BracketRound = roundIndex,
                    BracketSlot  = s,
                    RemainingTimeSeconds = DefaultMatchSeconds
                });
            bracket.Rounds.Add(next);
            roundIndex++;
        }

        bracket.BronzeMatch = new Match { BracketTag = "Bronze", RemainingTimeSeconds = DefaultMatchSeconds };
        if (bracket.Rounds.Count > 0 && bracket.Rounds[^1].Matches.Count == 1)
            bracket.Rounds[^1].Matches[0].BracketTag = "Final";

        PropagateAdvancements(bracket);
        return bracket;
    }

    private static List<ElimSeedStats> SortSeedStats(IEnumerable<ElimSeedStats> rows) =>
        rows.OrderByDescending(s => s.MatchesDone == 0 ? 0d : (double)s.Wins      / s.MatchesDone)
            .ThenByDescending (s => s.MatchesDone == 0 ? 0d : (double)s.PointsFor / s.MatchesDone)
            .ThenBy           (s => s.MatchesDone == 0
                                    ? double.PositiveInfinity
                                    : (double)s.PointsAgainst / s.MatchesDone)
            .ThenBy           (s => s.RedCards)
            .ToList();

    private sealed class ElimSeedStats
    {
        public string FencerId = "";
        public int MatchesDone, Wins, PointsFor, PointsAgainst, RedCards;
    }

    // ---------- Live bracket maintenance ----------

    /// <summary>
    /// Snapshot the bracket, run <see cref="PropagateAdvancements"/>, return every match whose
    /// Left/Right/Status/Winner changed. The caller persists each returned match.
    /// </summary>
    public static List<Match> PropagateAndCollectChanges(EliminationBracket bracket)
    {
        var snapshot = new Dictionary<string, (string L, string R, MatchStatus S, string? W)>();

        void Snap(Match m) =>
            snapshot[m.Id] = (m.LeftFencerId, m.RightFencerId, m.Status, m.WinnerFencerId);

        foreach (var round in bracket.Rounds)
            foreach (var m in round.Matches) Snap(m);
        if (bracket.BronzeMatch is not null) Snap(bracket.BronzeMatch);

        PropagateAdvancements(bracket);

        var changed = new List<Match>();
        void Diff(Match m)
        {
            if (!snapshot.TryGetValue(m.Id, out var before)) return;
            if (before.L != m.LeftFencerId  ||
                before.R != m.RightFencerId ||
                before.S != m.Status        ||
                before.W != m.WinnerFencerId)
                changed.Add(m);
        }

        foreach (var round in bracket.Rounds)
            foreach (var m in round.Matches) Diff(m);
        if (bracket.BronzeMatch is not null) Diff(bracket.BronzeMatch);

        return changed;
    }

    /// <summary>Replace the bracket's copy of a match with the given (typically just-finished) instance.</summary>
    public static void PatchInBracket(EliminationBracket bracket, Match match)
    {
        foreach (var round in bracket.Rounds)
        {
            var idx = round.Matches.FindIndex(m => m.Id == match.Id);
            if (idx >= 0) { round.Matches[idx] = match; return; }
        }
        if (bracket.BronzeMatch?.Id == match.Id) bracket.BronzeMatch = match;
    }

    /// <summary>Bracket is complete when the final is Finished and (if present) the bronze too.</summary>
    public static bool IsBracketComplete(EliminationBracket bracket)
    {
        if (bracket.Rounds.Count == 0) return false;
        var final = bracket.Rounds[^1].Matches.FirstOrDefault();
        if (final is null || final.Status != MatchStatus.Finished) return false;
        if (bracket.BronzeMatch is not null && bracket.BronzeMatch.Status != MatchStatus.Finished)
            return false;
        return true;
    }

    // ---------- Mid-tournament withdrawal cascade ----------

    /// <summary>
    /// Side-effect summary of <see cref="ApplyWithdrawalCascade"/>: lists of
    /// matches that the caller must persist.
    /// </summary>
    public sealed class WithdrawalCascade
    {
        public List<Match> ChangedPoolMatches { get; } = new();
        public List<Match> ChangedBracketMatches { get; } = new();
    }

    /// <summary>
    /// Walks every UNFINISHED pool match and bracket match the withdrawn fencer
    /// is in, marks them <see cref="MatchStatus.Finished"/> with the opponent as
    /// winner and a 0–0 scoreline (so the walkover doesn't affect averages), then
    /// propagates winners through the bracket. Returns the list of mutated matches
    /// so the caller can persist them.
    ///
    /// Already-finished matches are left untouched — historical results stand.
    /// </summary>
    public static WithdrawalCascade ApplyWithdrawalCascade(Tournament t, string withdrawnFencerId)
    {
        var result = new WithdrawalCascade();
        if (string.IsNullOrEmpty(withdrawnFencerId)) return result;

        // Pools: every pending/in-progress match involving the fencer becomes a
        // 0–0 walkover for the opponent.
        foreach (var pool in t.Pools)
        {
            foreach (var m in pool.Matches)
            {
                if (m.Status == MatchStatus.Finished) continue;
                bool isLeft  = m.LeftFencerId  == withdrawnFencerId;
                bool isRight = m.RightFencerId == withdrawnFencerId;
                if (!isLeft && !isRight) continue;

                m.Status        = MatchStatus.Finished;
                m.LeftScore     = 0;
                m.RightScore    = 0;
                m.WinnerFencerId = isLeft ? m.RightFencerId : m.LeftFencerId;
                m.FinishedAtUtc = DateTime.UtcNow;
                // If the opponent slot is empty (defensive), there's no winner;
                // leave the match finished with no winner so it doesn't block.
                if (string.IsNullOrEmpty(m.WinnerFencerId)) m.WinnerFencerId = null;

                result.ChangedPoolMatches.Add(m);
            }
        }

        // Bracket: same rule, then propagate winners.
        if (t.Bracket is not null)
        {
            void TryWalkover(Match m)
            {
                if (m.Status == MatchStatus.Finished) return;
                bool isLeft  = m.LeftFencerId  == withdrawnFencerId;
                bool isRight = m.RightFencerId == withdrawnFencerId;
                if (!isLeft && !isRight) return;

                // Need an opponent in the slot to award the walkover.
                var opponent = isLeft ? m.RightFencerId : m.LeftFencerId;
                if (string.IsNullOrEmpty(opponent))
                {
                    // No opponent yet — clear the withdrawn fencer's slot; propagation
                    // will treat the other feeder's winner as a bye when it arrives.
                    if (isLeft)  m.LeftFencerId  = "";
                    if (isRight) m.RightFencerId = "";
                    result.ChangedBracketMatches.Add(m);
                    return;
                }

                m.Status         = MatchStatus.Finished;
                m.LeftScore      = 0;
                m.RightScore     = 0;
                m.WinnerFencerId = opponent;
                m.FinishedAtUtc  = DateTime.UtcNow;
                result.ChangedBracketMatches.Add(m);
            }

            foreach (var round in t.Bracket.Rounds)
                foreach (var m in round.Matches)
                    TryWalkover(m);
            if (t.Bracket.BronzeMatch is not null)
                TryWalkover(t.Bracket.BronzeMatch);

            // Propagate the walkovers downstream. Anything PropagateAdvancements
            // additionally touches (e.g. auto-bye-through) also needs persisting.
            var propagated = PropagateAndCollectChanges(t.Bracket);
            foreach (var m in propagated)
                if (!result.ChangedBracketMatches.Contains(m))
                    result.ChangedBracketMatches.Add(m);
        }

        return result;
    }
}