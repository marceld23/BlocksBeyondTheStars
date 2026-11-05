// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The pacing of the walled-yard fills (#3209), modelled on the air engine (<c>GameServerAirSpace</c>): the fill of a
/// fortress at the reach cap is up to ~1.2M cells and used to run inside one tick.
/// <list type="bullet">
/// <item><b>Starting query:</b> the query that starts a fill works at least <see cref="WalledStartMinCells"/> of it at once —
/// the whole fill of the smallest (97 × 97) box — plus what is left of the tick's time budget. Every base whose fill fits in
/// that (every ordinary base) is answered on the spot and deterministically, as it always was — per query, like the air's
/// guarantee, so the answer never depends on what else was asked in the same tick; a big fill's start costs no more than an
/// ordinary fill did, and the scratch cap below bounds how many big fills can be running at once. A level's FIRST fill has
/// no earlier answer to fall back on, so it may also use a separate <see cref="WalledFirstFillBudgetMs"/> window per tick
/// (the air's first-fill figure) — a big base's level is then usually answered at once on a fast machine too.</item>
/// <item><b>Tick driver:</b> <see cref="TickBaseWallFills"/> moves the running fills of the active world in start order
/// within <see cref="WalledTickBudgetMs"/> per tick, but always for at least <see cref="WalledMinCellsPerTick"/> cells in all
/// — a busy machine's clock never stalls a fill, and a test's outcome never depends on the clock.</item>
/// <item><b>Answers:</b> until a fill is done, its level answers from the last finished one; then the new result replaces it
/// in one step (<see cref="CommitWalledFill"/>). A level that never finished a fill reads open — the fail-open direction of an
/// unloaded column or an exhausted budget — and so does every cell outside the last finished fill's box (a box that grew is
/// open in its new strip until the refill is done, never walled). A finished fill that changed the yard rebuilds the base
/// index (its beds and posts are judged by the yard), and so does any finished fill after an index build that read a level
/// still being refilled: such a build counts from the old fill but writes nothing of it into its bulkhead memory.</item>
/// <item><b>Memory:</b> a running fill holds a scratch byte per box cell (~4 MB at reach 192); the running fills together hold
/// at most <see cref="WalledScratchCapBytes"/> (one always may run), a later start waits — its level keeps answering — and
/// the finished bitsets of every base together at most <see cref="WalledCacheCapBytes"/> (least recently asked dropped first).
/// A fill nobody steps for <see cref="WalledJobIdleSeconds"/> (its body is no longer ticked) gives its scratch back.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Milliseconds of wall filling per server tick, over every running fill — the air's figure.</summary>
    private const double WalledTickBudgetMs = 2.0;

    /// <summary>Cells the tick driver works per tick in all, whatever the clock says (about a millisecond on a desktop).</summary>
    private const int WalledMinCellsPerTick = 4096;

    /// <summary>Milliseconds per tick that the first fills of levels may work at once beyond their guaranteed cells — the
    /// air's <c>AirFirstFillBudgetMs</c>; shared by every first fill started in the tick.</summary>
    private const double WalledFirstFillBudgetMs = 20.0;

    /// <summary>Scratch bytes the running fills may hold together (four reach-192 fills); one fill may always run.</summary>
    private const long WalledScratchCapBytes = 16L << 20;

    /// <summary>Bitset bytes the finished levels of all bases may hold together (~70 levels at reach 192; ~3600 small ones).</summary>
    private const long WalledCacheCapBytes = 32L << 20;

    /// <summary>Seconds a running fill may go unstepped (its body no longer ticks) before it is dropped.</summary>
    private const double WalledJobIdleSeconds = 30.0;

    /// <summary>Cells a starting query always works: the whole fill of the smallest box — its seeding (four columns per
    /// round) and its cell budget — so an ordinary base is answered at once, as before the slicing.</summary>
    private static readonly int WalledStartMinCells
        = WalledFillBudgetFor((2 * SealedRoomMaxReach) + 1) + (4 * ((2 * SealedRoomMaxReach) + 1));

    private double _wallTickBudgetMs = WalledTickBudgetMs;
    private double _wallFirstFillBudgetMs = WalledFirstFillBudgetMs;
    private int _wallMinCellsPerTick = WalledMinCellsPerTick;
    private int _wallStartMinCells = WalledStartMinCells;
    private double _wallBudgetStamp = double.NaN;
    private long _wallBudgetLeft;
    private long _wallFirstFillLeft;
    private long _wallScratchInUse;
    private long _wallCacheBytes;
    private byte[]? _wallScratchSpare;

    /// <summary>Levels with a running fill, in start order (the driver's order).</summary>
    private readonly List<WalledLevel> _wallJobs = new();

    /// <summary>Scratch list of the tick driver (reused, so a tick allocates nothing).</summary>
    private readonly List<WalledLevel> _wallTickLevels = new();

    /// <summary>Renews this tick's wall time budgets (the shared one and the first fills' window) when the uptime moved on.</summary>
    private void RenewWallBudget()
    {
        if (_uptime != _wallBudgetStamp)
        {
            _wallBudgetStamp = _uptime;
            _wallBudgetLeft = (long)(_wallTickBudgetMs * Stopwatch.Frequency / 1000.0);
            _wallFirstFillLeft = (long)(_wallFirstFillBudgetMs * Stopwatch.Frequency / 1000.0);
        }
    }

    /// <summary>Starts a new fill for a level. Waits (returns false, the level keeps answering from what it has) while the
    /// running fills already hold <see cref="WalledScratchCapBytes"/> of scratch — unless <paramref name="force"/> or nothing
    /// runs at all.</summary>
    private bool StartWalledFill(WalledLevel level, ServerBase b, int reach, bool force)
    {
        int cells = WalledFillJob.ScratchCellsFor(b, level.FeetY, reach);
        if (!force && _wallJobs.Count > 0 && _wallScratchInUse + cells > WalledScratchCapBytes)
        {
            return false;
        }

        byte[] scratch;
        if (_wallScratchSpare is { } spare && spare.Length >= cells)
        {
            scratch = spare;
            _wallScratchSpare = null;
        }
        else
        {
            scratch = new byte[cells];
        }

        level.Job = new WalledFillJob(this, b, level.FeetY, reach, scratch, _uptime);
        level.Body = _world.LocationId;
        level.Dirty = false; // a change from here on refills once this fill is done
        _wallJobs.Add(level);
        _wallScratchInUse += scratch.Length;
        return true;
    }

    /// <summary>The query's share of a level's running fill: the fixed start minimum when it just started it, then whatever is
    /// left of the tick's time budget — of the first-fill window for a level's <paramref name="firstFill"/>.</summary>
    private void StepStartedWalledFill(WalledFillJob job, bool started, bool firstFill)
    {
        RenewWallBudget();
        int guarantee = started ? _wallStartMinCells : 0;
        bool window = started && firstFill && _wallFirstFillLeft > _wallBudgetLeft;
        if (guarantee <= 0 && (window ? _wallFirstFillLeft : _wallBudgetLeft) <= 0)
        {
            return;
        }

        StepWalledJob(job, guarantee, window);
    }

    /// <summary>Steps a fill within what is left of the tick's time budget (or of the first-fill window), at least
    /// <paramref name="minWork"/> cells.</summary>
    private int StepWalledJob(WalledFillJob job, int minWork, bool firstFillWindow = false)
    {
        long t0 = Stopwatch.GetTimestamp();
        int work = job.Step(System.Math.Max(0, firstFillWindow ? _wallFirstFillLeft : _wallBudgetLeft), minWork);
        long spent = Stopwatch.GetTimestamp() - t0;
        if (firstFillWindow)
        {
            _wallFirstFillLeft -= spent;
        }
        else
        {
            _wallBudgetLeft -= spent;
        }

        job.LastStepped = _uptime;
        return work;
    }

    /// <summary>
    /// A level's fill is done: its result replaces the level's in one step. An expensive (sliced) fill that found nothing new
    /// backs the level's backstop off; a changed yard rebuilds the base's index (its beds and posts are judged by the yard), and
    /// the cache gives the least recently asked levels up when it holds more than its cap.
    /// </summary>
    private void CommitWalledFill(WalledLevel level)
    {
        var job = level.Job!;
        bool changed = !level.SameResult(job);
        bool wasBudgetOut = level.BudgetOut;
        _wallCacheBytes += ((long)job.Reached.Length - (level.Reached?.LongLength ?? 0)) * sizeof(ulong);
        level.Center = job.Center;
        level.Reach = job.Reach;
        level.YMin = job.YMin;
        level.Rows = job.Rows;
        level.Reached = job.Reached;
        level.ReachableCount = job.Count;
        level.Budget = job.Budget;
        level.BudgetOut = job.BudgetOut;
        level.FailOpen = job.BudgetOut;
        level.ComputedAt = _uptime;
        level.Computes++;
        WalledFillComputesForTest++;
        level.Backstop = changed || job.Slices <= 1
            ? WalledRecomputeInterval
            : System.Math.Min(WalledRecomputeIntervalMax, level.Backstop * 2);
        EndWalledJob(level);

        if (level.BudgetOut && !wasBudgetOut)
        {
            var b = _bases.FirstOrDefault(x => x.Id == level.BaseId);
            _log.Info($"Base walls: the fill for base #{level.BaseId} ({b?.Name}) at feet level {level.FeetY} ran out of budget " +
                      $"({level.Budget} cells, reach {level.Reach}) — nothing at that level reads as fenced in until it fits.");
        }

        // A changed yard, or an index built while a refill ran (it counted from the old fill and kept nothing of it in its
        // bulkhead memory), is rebuilt.
        if (_baseIndex.TryGetValue(level.BaseId, out var idx) && (changed || idx.WallsPending))
        {
            idx.Dirty = true;
        }

        TrimWalledCache(level);
    }

    /// <summary>Ends a level's running fill (done or dropped) and gives its scratch back.</summary>
    private void EndWalledJob(WalledLevel level)
    {
        if (level.Job is not { } job)
        {
            return;
        }

        _wallJobs.Remove(level);
        _wallScratchInUse -= job.Scratch.Length;
        if (_wallScratchSpare is null || _wallScratchSpare.Length < job.Scratch.Length)
        {
            _wallScratchSpare = job.Scratch; // one spare, the biggest, so a refill allocates nothing
        }

        level.Job = null;
    }

    /// <summary>Drops a cached level, its running fill and its bitset.</summary>
    private void RemoveWalledLevel((int BaseId, int FeetY) key)
    {
        if (!_baseWalls.TryGetValue(key, out var level))
        {
            return;
        }

        EndWalledJob(level);
        _wallCacheBytes -= (level.Reached?.LongLength ?? 0) * sizeof(ulong);
        _baseWalls.Remove(key);
    }

    /// <summary>Gives the least recently asked finished levels up while the cache holds more than its cap — never
    /// <paramref name="keep"/> (just finished) and never a level whose fill is running.</summary>
    private void TrimWalledCache(WalledLevel keep)
    {
        while (_wallCacheBytes > WalledCacheCapBytes)
        {
            (int, int)? stalest = null;
            double oldest = double.PositiveInfinity;
            foreach (var kv in _baseWalls)
            {
                if (kv.Value != keep && kv.Value.Job is null && kv.Value.Reached is not null && kv.Value.LastUsed < oldest)
                {
                    oldest = kv.Value.LastUsed;
                    stalest = kv.Key;
                }
            }

            if (stalest is not { } key)
            {
                return;
            }

            RemoveWalledLevel(key);
        }
    }

    /// <summary>
    /// Per-tick driver (#3209): moves the running wall fills of the active world in start order — within the tick's time
    /// budget, but at least <see cref="WalledMinCellsPerTick"/> cells in all — and commits the ones that finish. A fill of
    /// a body nobody ticks any more is dropped after <see cref="WalledJobIdleSeconds"/>.
    /// </summary>
    private void TickBaseWallFills(double dt)
    {
        _ = dt;
        if (_wallJobs.Count == 0)
        {
            return;
        }

        RenewWallBudget();
        string body = _world.LocationId;
        int guarantee = _wallMinCellsPerTick;
        _wallTickLevels.Clear();
        _wallTickLevels.AddRange(_wallJobs);
        foreach (var level in _wallTickLevels)
        {
            if (level.Job is not { } job)
            {
                continue;
            }

            if (level.Body != body)
            {
                if (_uptime - job.LastStepped > WalledJobIdleSeconds)
                {
                    EndWalledJob(level);
                    level.Dirty = true; // asked again, it starts afresh
                }

                continue;
            }

            if (guarantee <= 0 && _wallBudgetLeft <= 0)
            {
                continue;
            }

            int work = StepWalledJob(job, System.Math.Max(0, guarantee));
            guarantee -= work;
            WalledCellsWorkedForTest += work;
            if (job.Done)
            {
                CommitWalledFill(level);
            }
        }
    }

    /// <summary>Test seam (#3209): cells the tick driver worked since the counter was last reset.</summary>
    internal int WalledCellsWorkedForTest { get; set; }

    /// <summary>Test seam (#3209): the fill pacing — the tick's time budget and the first fills' window (ms), the driver's
    /// guaranteed cells per tick and the starting query's guaranteed cells.</summary>
    internal void SetWalledPacingForTest(double tickBudgetMs, double firstFillBudgetMs, int minCellsPerTick, int startMinCells)
    {
        _wallTickBudgetMs = tickBudgetMs;
        _wallFirstFillBudgetMs = firstFillBudgetMs;
        _wallMinCellsPerTick = minCellsPerTick;
        _wallStartMinCells = startMinCells;
        _wallBudgetStamp = double.NaN;
    }

    /// <summary>Test seam (#3209): one tick of the wall-fill driver on the active world, in a fresh budget window; returns the
    /// cells it worked.</summary>
    internal int TickBaseWallFillsForTest()
    {
        _wallBudgetStamp = double.NaN;
        int before = WalledCellsWorkedForTest;
        TickBaseWallFills(0);
        return WalledCellsWorkedForTest - before;
    }

    /// <summary>Test seam (#3209): running wall fills, their scratch bytes and the finished levels' bitset bytes.</summary>
    internal (int Jobs, long ScratchBytes, long CacheBytes) WalledMemoryForTest => (_wallJobs.Count, _wallScratchInUse, _wallCacheBytes);

    /// <summary>Test seam (#3209): what the production path answers for a cell right now — WITHOUT running a pending fill to
    /// the end: whether it reads fenced in, and whether a fill for its level is still running.</summary>
    internal (bool Enclosed, bool Pending) WalledVerdictForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        bool enclosed = InWalledBaseArea(canonical);
        bool pending = _bases.Any(b => b.Planet == _world.LocationId && WithinBaseWallReach(b, canonical)
            && _baseWalls.TryGetValue((b.Id, canonical.Y), out var level) && level.Job is not null);
        return (enclosed, pending);
    }

    /// <summary>Test seam (#3209): the finished fill a level answers from, for the first base whose box holds the cell — its
    /// reach, reached cells, budget, budget exhaustion and a copy of its bitset (null bits: none finished). No refresh.</summary>
    internal (int Reach, int Reachable, int Budget, bool BudgetOut, ulong[]? Bits) WalledLevelForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        var b = _bases.FirstOrDefault(o => o.Planet == _world.LocationId && WithinBaseWallReach(o, canonical));
        return b is not null && _baseWalls.TryGetValue((b.Id, canonical.Y), out var level)
            ? (level.Reach, level.ReachableCount, level.Budget, level.BudgetOut, (ulong[]?)level.Reached?.Clone())
            : (0, 0, 0, false, null);
    }

    /// <summary>Test seam (#3209): the reference — a fresh fill of the cell's level run to the end in one call, the way the
    /// fill ran before the slicing, beside the cache (it changes nothing there).</summary>
    internal (int Reach, int Reachable, int Budget, bool BudgetOut, ulong[] Bits) WalledOneShotFillForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        var b = _bases.First(o => o.Planet == _world.LocationId && WithinBaseWallReach(o, canonical));
        int reach = BaseWallReach(b);
        var job = new WalledFillJob(this, b, canonical.Y, reach, new byte[WalledFillJob.ScratchCellsFor(b, canonical.Y, reach)], _uptime);
        job.Step(-1, 0);
        return (job.Reach, job.Count, job.Budget, job.BudgetOut, job.Reached);
    }
}
