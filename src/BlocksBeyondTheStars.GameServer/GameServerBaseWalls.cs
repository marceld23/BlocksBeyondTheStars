// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Walled base areas (#1315): wild animals do not spawn inside a closed ring of walls within a founded
/// base's reach. Lyxette's yard has high walls and no roof — correctly NOT an airtight room for the air
/// system (#794), and far outside the radius-8 build-protection cube — so neither existing predicate
/// answered "is this spot fenced in?". This one does, with an <b>outside-in fill</b>:
/// <list type="bullet">
/// <item>Take the base's fill box (Chebyshev <see cref="BaseWallReach"/> around the core — at least
/// <see cref="SealedRoomMaxReach"/> = 48, a 97×97 footprint, and since #1862 as wide as what the base's players
/// BUILT: a fortress 120 blocks across used to have the box edge inside its own walls, so every seed already stood
/// in the yard and nothing there ever read as fenced in), seed every boundary column at the query's feet level,
/// and flood INWARD through cells a walking animal could pass. Everything inside the box the fill never reaches
/// is <b>enclosed</b> — one fill answers every yard, courtyard and room at that level, however many there are.</item>
/// <item>The fill <b>walks</b> (#1347): it steps ±1 block vertically like a walker (<see cref="CreatureMotion.StepUpLimit"/>
/// = 1) — up onto a supported cell, down through a free one — so a one-block terrain step, a garden edge or
/// the slope of a hollow is passable and a 2+ block rise is a wall. The first version flooded a single
/// horizontal slice and read natural terrain as masonry: every hollow within 48 blocks of a base was
/// "fenced in" and no land animal spawned in it. The query's own level additionally passes through any
/// non-colliding cell (the original rule, kept): the boundary is 48 blocks out and rarely on the yard's
/// level, so the fill needs to cross lower ground to get there at all.</item>
/// <item><b>Deep fluid is a wall</b> (#1862): a column whose support is fluid over fluid — a moat two or more
/// deep — is not walked; a one-deep pond is waded, on its surface, exactly as
/// <see cref="CreatureBehaviour.TerrainStepBlocked"/> lets a walker wade one cell and refuses two. Before this
/// the fill crossed every pond on its surface and a hand-dug moat fenced nothing.</item>
/// <item>Cached per (base, feet level) like the air's <c>_baseAirGroups</c>: invalidated by a block set or a gate toggled inside
/// the box (#1367, with <see cref="WalledRecomputeInterval"/> as the backstop), a budget that scales with the
/// box (<see cref="WalledFillBudgetFor"/> — the full 8 cells per column up to the 385-wide box at the cap, #3209),
/// <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base never
/// drags chunk generation. <b>Fail-open</b>: an unloaded column reads as air, the fill leaks in, the spawn is
/// allowed — the same direction the air system fails; a fill that runs out of budget answers "open" for
/// everything at that level as well (logged once when a level flips to that state).</item>
/// <item><b>Time-sliced</b> (#3209, the air engine's model): a fill is a resumable job (<see cref="WalledFillJob"/>). The
/// query that starts one works a fixed minimum of it at once — the whole fill of the smallest box, so an ordinary base is
/// answered on the spot, deterministically — and <see cref="TickBaseWallFills"/> moves what is left within a per-tick time
/// budget plus a fixed minimum of cells. Meanwhile the level keeps answering from its last finished fill, which the new one
/// replaces in one step when done; a level that never finished a fill reads open (the fail-open direction). Memory is
/// capped: running fills share a scratch cap, finished levels a cache cap (see GameServerBaseWallsJobs.cs).</item>
/// <item><b>Closed doors count as walls</b> (a deliberate divergence from the air model, where every door holds air
/// open or shut, #2651): a shut wooden gate keeps animals out; an open one is a gap. Proximity-operated doors (slide,
/// energy) count as walls whatever their state (#1358): they open only for a player and close by themselves,
/// so an animal never passes one — and a player standing at their yard's sliding gate used to hold it open
/// for the fill.</item>
/// <item>Fliers spawn above the walls and stay ungated — except over the yards of a base that uses energy fences, whose
/// fence guards the air above it (#3148); cave dwellers below them too; hostile machines are
/// out of scope on purpose — they SHOULD threaten a base, that is what the sentry post is for.</item>
/// <item>A closed energy gate is a wall (#3148): its membrane is not solid, but no animal passes it.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Cells the outside-in fill may visit per level, per column of its box (#1862: the budget scales
    /// with the box so a 200×200 fortress does not fail open on sight): the level's own slice, the walkable terrain
    /// within the band and a fair amount of cave on top of that. The minimum 97×97 box gets ~75k, the old flat 60k.
    /// #3209: no total cap any more — the 600k cap squeezed every box wider than ~273 (reach ~136) below 8 per column, so a
    /// big fortress failed open sooner; time-slicing bounds the cost per tick instead, and the reach cap bounds the total
    /// (385² · 8 ≈ 1.19M cells at reach 192).</summary>
    private const int WalledFillBudgetPerColumn = 8;

    /// <summary>Blocks the fill box extends past the outermost player-built cell (#1862), so the seeds stand on
    /// open ground outside the outer wall, never on or inside it.</summary>
    private const int WalledReachMargin = 6;

    /// <summary>Largest Chebyshev half-extent a base's fill box may grow to (#1862): a 385×385 footprint. A build
    /// farther out than this from the core is not part of the base's enclosure.</summary>
    private const int WalledReachCap = 192;

    /// <summary>How far above/below the queried feet level the walking fill follows the terrain (#1347).</summary>
    private const int WalledFillBand = 12;

    /// <summary>Levels cached per base before the stalest are dropped (a hilly base is queried at a few dozen feet levels).</summary>
    private const int WalledLevelsPerBase = 24;

    /// <summary>Seconds a level's fill is trusted without a change inside its box (#1367). The air system's
    /// 1.5 s was shorter than the 1.5–4 s between spawn attempts, so nearly every attempt recomputed; a block
    /// set or a gate toggled inside the box marks the level dirty instead, so the interval only backs that up.
    /// Counted from the end of the last fill (#3209).</summary>
    private const double WalledRecomputeInterval = 8.0;

    /// <summary>The longest the backstop grows to for a level whose sliced (expensive) fills keep finding nothing new
    /// (#3209, like the air's backoff): it doubles from <see cref="WalledRecomputeInterval"/> after each such fill and falls
    /// back at the first change.</summary>
    private const double WalledRecomputeIntervalMax = 32.0;

    /// <summary>One base's reachable-from-outside set at one feet level (everything else in the box is enclosed).
    /// The set is a bitset over the box's band rows (#1862: a 385×385 box holds up to 1.2M reachable cells, and a
    /// hilly base keeps two dozen levels — a hash set per level would be hundreds of MB). The fields describe the last
    /// FINISHED fill — what every query answers from — while <see cref="Job"/> works on the next one (#3209).</summary>
    private sealed class WalledLevel
    {
        public int BaseId;
        public string Body = string.Empty;
        public Vector3i Center;   // the base core — the fill's box is the reach cube around it
        public int FeetY;
        public int Reach;         // the box's Chebyshev half-extent at the time of the fill
        public int YMin;          // the band's lowest row (the bitset's row 0)
        public int Rows;          // band rows held by the bitset
        public ulong[]? Reached;  // box-relative ((y − YMin) · side + dx) · side + dz; null until a fill finished
        public int ReachableCount;
        public int Budget;        // cells the fill was allowed (the /basewalls report)
        public bool BudgetOut;    // the last fill ran out of budget
        public bool FailOpen = true; // nothing at this level reads as enclosed: no fill finished yet, or it ran out of budget
        public bool Dirty = true; // a block changed / a gate toggled inside the box since the last fill started
        public double ComputedAt = double.NegativeInfinity; // when the last fill finished
        public double LastUsed;   // the last query (the cache drops the least recently used level first)
        public double Backstop = WalledRecomputeInterval;
        public int Computes; // fills finished for this level (test seam)
        public WalledFillJob? Job; // the next fill, in progress (#3209)

        /// <summary>Whether a canonical cell reads OPEN at this level: the finished fill reached it — or it lies outside that
        /// fill's box or band, or no fill finished yet. Only a cell inside the box that the fill never reached is fenced in.
        /// #3209 review: this used to answer false (= fenced in) outside the box; while a refill for a box that grew is still
        /// running, the last finished fill is the smaller old box, and the strip between the two read as walled — a bed placed
        /// in the open there counted as inside (and stuck, see the index's bulkhead rule). Unknown is open, never walled.</summary>
        public bool Contains(Vector3i canonical, int circ)
        {
            if (Reached is null)
            {
                return true;
            }

            int side = 2 * Reach + 1;
            int dx = WorldConstants.WrapDeltaX(canonical.X - Center.X, circ) + Reach;
            int dz = WorldConstants.WrapDeltaZ(canonical.Z - Center.Z, circ) + Reach;
            int dy = canonical.Y - YMin;
            if (dx < 0 || dx >= side || dz < 0 || dz >= side || dy < 0 || dy >= Rows)
            {
                return true;
            }

            int i = (dy * side + dx) * side + dz;
            return (Reached[i >> 6] & (1UL << (i & 63))) != 0;
        }

        /// <summary>Whether the last finished fill reads the canonical cell as fenced in: inside its box, never reached.</summary>
        public bool Encloses(Vector3i canonical, int circ) => !FailOpen && !Contains(canonical, circ);

        /// <summary>Whether the level's answer is current for base <paramref name="b"/> (#3209 review): a finished fill for the
        /// base's present box, no fill running and no change since — what may be written into durable state (the index's
        /// bulkhead rule). A level that is merely being refilled answers from its last fill, which can be out of date.</summary>
        public bool Settled(ServerBase b, int reach)
            => Reached is not null && Job is null && !Dirty && Reach == reach && Center == b.Cell;

        /// <summary>Whether a finished job found exactly what this level already holds (#3209: no news for the index).</summary>
        public bool SameResult(WalledFillJob job)
            => Reached is not null && Reach == job.Reach && Center == job.Center && YMin == job.YMin && Rows == job.Rows
               && BudgetOut == job.BudgetOut && ReachableCount == job.Count
               && new System.ReadOnlySpan<ulong>(Reached).SequenceEqual(job.Reached);

        /// <summary>Whether a canonical cell lies in this level's box — the finished fill's or the running one's.</summary>
        public bool BoxHolds(Vector3i canonical, int circ)
            => (Reached is not null && InBox(Center, Reach, canonical, circ))
               || (Job is { } job && InBox(job.Center, job.Reach, canonical, circ));

        private bool InBox(Vector3i center, int reach, Vector3i canonical, int circ)
            => System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - center.X, circ)) <= reach
               && System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - center.Z, circ)) <= reach
               && System.Math.Abs(canonical.Y - FeetY) <= WalledFillBand + 1
               && System.Math.Abs(canonical.Y - center.Y) <= reach + 1;
    }

    private readonly Dictionary<(int BaseId, int FeetY), WalledLevel> _baseWalls = new();

    /// <summary>Each base's fill-box half-extent (#1862), derived lazily from the player-built cells around its core
    /// (see <see cref="BaseWallReach"/>) and grown live by <see cref="GrowBaseWallReach"/>.</summary>
    private readonly Dictionary<int, int> _baseWallReach = new();

    /// <summary>Fills computed so far (test seam for the cache behaviour, #1367).</summary>
    public int WalledFillComputesForTest { get; private set; }

    /// <summary>Cells the fill may visit for a box of <paramref name="side"/> columns per axis (#1862) — the full per-column
    /// budget for every box up to the cap (#3209).</summary>
    private static int WalledFillBudgetFor(int side) => side * side * WalledFillBudgetPerColumn;

    /// <summary>The largest half-extent a box may have on this world: the cap, or a quarter lap on a small body so
    /// the box never wraps onto itself.</summary>
    private static int WalledReachCapFor(ServerWorld world)
        => System.Math.Min(WalledReachCap, System.Math.Max(SealedRoomMaxReach, world.Circumference / 4));

    /// <summary>
    /// The base's fill-box half-extent (#1862): the outermost player-built (or dug, or dyed) cell within
    /// <see cref="WalledReachCap"/> of the core plus <see cref="WalledReachMargin"/>, never less than
    /// <see cref="SealedRoomMaxReach"/>, never more than the cap. Asked of the block-edit store once per base and
    /// cached; every player block set afterwards grows it live (<see cref="GrowBaseWallReach"/>). A base near the
    /// longitude or latitude seam asks the store per canonical piece of its box, since the store holds canonical
    /// columns. Anything a player built that far out — a wall, a gate, a moat's bed — is what the box must hold.
    /// </summary>
    private int BaseWallReach(ServerBase b)
    {
        if (_baseWallReach.TryGetValue(b.Id, out int reach))
        {
            return reach;
        }

        int circ = _world.Circumference;
        int cap = WalledReachCapFor(_world);
        int farthest = 0;
        foreach (var (xLo, xHi) in CanonicalRanges(b.Cell.X - cap, b.Cell.X + cap, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(b.Cell.Z - cap, b.Cell.Z + cap,
                         -WorldConstants.LatitudePeriodFor(circ) / 2, WorldConstants.LatitudePeriodFor(circ)))
            {
                if (_repo.TryGetPlayerBlockEditBounds(b.Planet, new Vector3i(xLo, b.Cell.Y - cap, zLo),
                        new Vector3i(xHi, b.Cell.Y + cap, zHi), out var lo, out var hi))
                {
                    // Each piece lies on one side of its seam, so the wrapped distance peaks at the piece's corners.
                    farthest = System.Math.Max(farthest, System.Math.Max(WrapAbs(lo.X - b.Cell.X), WrapAbs(hi.X - b.Cell.X)));
                    farthest = System.Math.Max(farthest, System.Math.Max(WrapAbsZ(lo.Z - b.Cell.Z), WrapAbsZ(hi.Z - b.Cell.Z)));
                    farthest = System.Math.Max(farthest, System.Math.Max(System.Math.Abs(lo.Y - b.Cell.Y), System.Math.Abs(hi.Y - b.Cell.Y)));
                }
            }
        }

        reach = ReachFor(farthest, cap);
        _baseWallReach[b.Id] = reach;
        return reach;
    }

    /// <summary>The half-extent a build <paramref name="farthest"/> blocks from the core asks for.</summary>
    private static int ReachFor(int farthest, int cap)
        => System.Math.Min(cap, System.Math.Max(SealedRoomMaxReach, farthest + WalledReachMargin));

    /// <summary>Splits the inclusive range [<paramref name="lo"/>, <paramref name="hi"/>] into its pieces inside the
    /// canonical domain [<paramref name="domainLo"/>, domainLo + <paramref name="period"/>) — one piece, or two when
    /// the range straddles the seam; the whole domain when the range covers a full lap.</summary>
    private static IEnumerable<(int Lo, int Hi)> CanonicalRanges(int lo, int hi, int domainLo, int period)
    {
        if (hi - lo + 1 >= period)
        {
            yield return (domainLo, domainLo + period - 1);
            yield break;
        }

        int start = domainLo + (((lo - domainLo) % period) + period) % period;
        int end = start + (hi - lo);
        int domainHi = domainLo + period - 1;
        if (end <= domainHi)
        {
            yield return (start, end);
        }
        else
        {
            yield return (start, domainHi);
            yield return (domainLo, end - period);
        }
    }

    /// <summary>A player set a block on a resident world (#1862): every base on it whose reach is already known and
    /// within the cap of the cell grows its box to hold the cell (plus the margin), and its cached levels are
    /// refilled — the seeds must move out past the new wall. The base index follows the reach (#3210), so it is rebuilt
    /// too: the block-set event that marks it dirty fires before this one and measured the cell against the old box. A
    /// base whose reach is not computed yet needs nothing: its first <see cref="BaseWallReach"/> reads the edit from the
    /// store, which was written before this event.</summary>
    private void GrowBaseWallReach(ServerWorld world, Vector3i cell)
    {
        if (_baseWallReach.Count == 0)
        {
            return;
        }

        int circ = world.Circumference;
        int cap = WalledReachCapFor(world);
        var canonical = WorldConstants.CanonicalBlock(cell, circ);
        foreach (var b in _bases)
        {
            if (b.Planet != world.LocationId || !_baseWallReach.TryGetValue(b.Id, out int reach))
            {
                continue;
            }

            int d = System.Math.Max(System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - b.Cell.X, circ)),
                System.Math.Max(System.Math.Abs(canonical.Y - b.Cell.Y), System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - b.Cell.Z, circ))));
            int wanted = ReachFor(d, cap);
            if (d > cap || wanted <= reach)
            {
                continue;
            }

            _baseWallReach[b.Id] = wanted;
            foreach (var kv in _baseWalls)
            {
                if (kv.Key.BaseId == b.Id)
                {
                    kv.Value.Dirty = true;
                }
            }

            if (_baseIndex.TryGetValue(b.Id, out var idx))
            {
                idx.Dirty = true; // #3210: the index box grew with the reach
            }
        }
    }

    /// <summary>Whether a canonical cell lies inside a base's fill box (the reach cube around its core).</summary>
    private bool WithinBaseWallReach(ServerBase b, Vector3i canonical)
    {
        int reach = BaseWallReach(b);
        return WrapAbs(canonical.X - b.Cell.X) <= reach
            && System.Math.Abs(canonical.Y - b.Cell.Y) <= reach
            && WrapAbsZ(canonical.Z - b.Cell.Z) <= reach;
    }

    /// <summary>Marks every cached level whose fill box holds <paramref name="cell"/> for recomputation (#1367):
    /// called for every block set on a resident world and for every hand-operated gate toggled. A level's box is
    /// the base's reach cube, clipped to the walking band around its feet level (plus the one-row margins) — the box of
    /// its finished fill or of the one running (#3209: a change during a running fill refills once that one is done).</summary>
    private void MarkBaseWallsDirty(ServerWorld world, Vector3i cell)
    {
        if (_baseWalls.Count == 0)
        {
            return;
        }

        int circ = world.Circumference;
        var canonical = WorldConstants.CanonicalBlock(cell, circ);
        foreach (var level in _baseWalls.Values)
        {
            if (!level.Dirty && level.Body == world.LocationId && level.BoxHolds(canonical, circ))
            {
                level.Dirty = true;
            }
        }
    }

    /// <summary>Shortest absolute latitude distance across the north–south seam (#1367: the reach test wrapped
    /// X only, so a base near the seam got a truncated box and its yards failed open).</summary>
    private int WrapAbsZ(int dz) => System.Math.Abs(WorldConstants.WrapDeltaZ(dz, _world.Circumference));

    /// <summary>True if the cell lies inside a founded base's reach box on this world and the outside-in fill
    /// at that feet level cannot reach it — i.e. walls (or shut doors) close it off from the open terrain.
    /// Ownership is ignored, like every base predicate: a visitor's yard is a yard too. <paramref name="counts"/> limits
    /// the question to some bases (#3148: the fliers' rule asks only bases that use energy fences). Answers from the
    /// level's last finished fill (#3209); <paramref name="complete"/> (test seams) runs a pending fill to the end first.</summary>
    private bool InWalledBaseArea(Vector3i cell, System.Func<ServerBase, bool>? counts = null, bool complete = false)
        => InWalledBaseArea(cell, out _, counts, complete);

    /// <summary><see cref="InWalledBaseArea(Vector3i, System.Func{ServerBase, bool}?, bool)"/>, also saying whether every level
    /// it asked was <see cref="WalledLevel.Settled"/> — whether the answer may go into durable state (#3209 review).</summary>
    private bool InWalledBaseArea(Vector3i cell, out bool settled, System.Func<ServerBase, bool>? counts = null, bool complete = false)
    {
        string body = _world.LocationId;
        int circ = _world.Circumference;
        var canonical = WorldConstants.CanonicalBlock(cell, circ);
        settled = true;
        foreach (var b in _bases)
        {
            if (b.Planet != body || !WithinBaseWallReach(b, canonical) || (counts is not null && !counts(b)))
            {
                continue;
            }

            var level = RefreshBaseWalls(b, canonical.Y, complete);
            settled &= level.Settled(b, BaseWallReach(b));
            if (level.Encloses(canonical, circ))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The base's walled-yard level at a feet level, for answering (#3209): a new fill starts when the level has none yet,
    /// was marked dirty, its box moved or grew, or its backstop ran out — and the starting query works a fixed minimum of
    /// it at once (<see cref="StepStartedWalledFill"/>), so an ordinary base is answered right here. Whatever is left runs
    /// in <see cref="TickBaseWallFills"/>; until it is done the level answers from its last finished fill. With
    /// <paramref name="complete"/> (the first <c>/basewalls</c> look at a level, test seams) the fill runs to the end now —
    /// bounded by its cell budget.
    /// </summary>
    private WalledLevel RefreshBaseWalls(ServerBase b, int feetY, bool complete = false)
    {
        var level = WalledLevelFor(b, feetY);
        level.LastUsed = _uptime;
        int reach = BaseWallReach(b);
        bool started = level.Job is null && NeedsWalledFill(level, b, reach) && StartWalledFill(level, b, reach, force: complete);
        if (level.Job is { } job)
        {
            if (complete)
            {
                job.Step(-1, 0);
                job.LastStepped = _uptime;
            }
            else
            {
                StepStartedWalledFill(job, started, firstFill: level.Reached is null);
            }

            if (job.Done)
            {
                CommitWalledFill(level);
            }
        }

        return level;
    }

    /// <summary>The cached level of a base at a feet level — a new, empty one when there is none (the base's least recently
    /// used level makes room when it already holds <see cref="WalledLevelsPerBase"/>).</summary>
    private WalledLevel WalledLevelFor(ServerBase b, int feetY)
    {
        var key = (b.Id, feetY);
        if (!_baseWalls.TryGetValue(key, out var level))
        {
            // Keep the cache bounded: a base queried at many terrain levels drops the ones asked least recently.
            if (_baseWalls.Count(kv => kv.Key.BaseId == b.Id) >= WalledLevelsPerBase)
            {
                var stalest = _baseWalls.Where(kv => kv.Key.BaseId == b.Id).OrderBy(kv => kv.Value.LastUsed).First().Key;
                RemoveWalledLevel(stalest);
            }

            _baseWalls[key] = level = new WalledLevel { BaseId = b.Id, Body = _world.LocationId, FeetY = feetY, LastUsed = _uptime };
        }

        return level;
    }

    /// <summary>Whether a level needs a new fill: none finished yet, a change inside its box, its box moved or grew, another
    /// body, or the backstop ran out (counted from the end of the last fill).</summary>
    private bool NeedsWalledFill(WalledLevel level, ServerBase b, int reach)
        => level.Reached is null || level.Dirty || level.Body != _world.LocationId || level.Center != b.Cell
           || level.Reach != reach || _uptime - level.ComputedAt >= level.Backstop;

    /// <summary>Test seam (#1367): how many times the fill for the level a cell lies on has been computed for
    /// the first base in reach — WITHOUT refreshing it. 0 when no level is cached there.</summary>
    public int WalledLevelComputesForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WithinBaseWallReach(b, canonical))
            {
                return _baseWalls.TryGetValue((b.Id, canonical.Y), out var level) ? level.Computes : 0;
            }
        }

        return 0;
    }

    /// <summary>Test seam (#1862): the fill-box half-extent of the first base whose box holds the cell; 0 when none does.</summary>
    public int WalledReachForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WithinBaseWallReach(b, canonical))
            {
                return BaseWallReach(b);
            }
        }

        return 0;
    }

    /// <summary>Cells covered by the doors that satisfy <paramref name="pick"/> in the active world: the door's
    /// width along its wall axis × the 3-tall doorway column, canonical. The wall fill takes every SHUT door
    /// and every proximity-operated one (a gate keeps animals out — #1358); the air fill takes every door of every
    /// kind, open or shut (<see cref="AllDoorCells"/>, #2651).</summary>
    private HashSet<Vector3i> DoorCellsWhere(System.Func<ServerDoor, bool> pick)
    {
        var cells = new HashSet<Vector3i>();
        int circ = _world.Circumference;
        foreach (var d in _doors)
        {
            if (!pick(d))
            {
                continue;
            }

            int by = (int)System.Math.Floor(d.Pos.Y);
            int low = (int)System.Math.Round((d.AxisX ? d.Pos.X : d.Pos.Z) - 0.5f - (d.Width - 1f) * 0.5f);
            int fixedAxis = d.AxisX ? (int)System.Math.Floor(d.Pos.Z) : (int)System.Math.Floor(d.Pos.X);
            for (int w = 0; w < (int)d.Width; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    cells.Add(WorldConstants.CanonicalBlock(d.AxisX
                        ? new Vector3i(low + w, by + dy, fixedAxis)
                        : new Vector3i(fixedAxis, by + dy, low + w), circ));
                }
            }
        }

        return cells;
    }

    /// <summary>Drops a removed base's cached wall levels (and their running fills) and its fill-box reach (called when its
    /// core is mined).</summary>
    private void ForgetBaseWalls(int baseId)
    {
        foreach (var key in _baseWalls.Keys.Where(k => k.BaseId == baseId).ToList())
        {
            RemoveWalledLevel(key);
        }

        _baseWallReach.Remove(baseId);
    }

    /// <summary><c>/basewalls</c> (#1452): the admin's window into the enclosure fill. A yard that "should be
    /// fenced in" but still gets animals used to be undiagnosable — the fill fails open silently (budget,
    /// unloaded chunks, a one-high segment, an open hinge door) and nothing logs a spawn. The report names the
    /// nearest core, the fill at the admin's feet level (size, budget, fail-open), what the admin's own cell
    /// reads as, and the rules the texts used to skip.</summary>
    private void AdminBaseWalls(PlayerSession session)
    {
        // #1728: a non-admin owner may run this on their OWN walls only. The report names a core by name and
        // exact cell and says where the ring fails open — read as "here is the hole in this stranger's fence",
        // that is a reconnaissance tool, so everyone but an admin is restricted to bases they own.
        bool ownOnly = !session.State.IsAdmin && !session.IsFleetAdmin;
        foreach (string line in BaseWallsReport(session, ownOnly))
        {
            Send(session, new ServerMessage { Text = line });
        }
    }

    /// <summary>Builds the report. <paramref name="ownOnly"/> restricts the core search to bases the caller
    /// owns (#1728) — an admin sees whichever core is nearest, an owner only their own.</summary>
    private List<string> BaseWallsReport(PlayerSession session, bool ownOnly = false)
    {
        var p = session.State;
        var cell = WorldConstants.CanonicalBlock(p.Position.ToBlock(), _world.Circumference);
        string L(string key) => Localize(session.Locale, key);
        var lines = new List<string>();

        ServerBase? nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId || (ownOnly && b.OwnerId != p.PlayerId))
            {
                continue;
            }

            int d = System.Math.Max(WrapAbs(cell.X - b.Cell.X), System.Math.Max(System.Math.Abs(cell.Y - b.Cell.Y), WrapAbsZ(cell.Z - b.Cell.Z)));
            if (d <= BaseWallReach(b) && d < nearestDist)
            {
                nearest = b;
                nearestDist = d;
            }
        }

        if (nearest is null)
        {
            lines.Add(L("srv.basewalls.none").Replace("{reach}", SealedRoomMaxReach.ToString()));
            return lines;
        }

        lines.Add(L("srv.basewalls.base")
            .Replace("{name}", string.IsNullOrWhiteSpace(nearest.Name) ? "#" + nearest.Id : nearest.Name)
            .Replace("{x}", nearest.Cell.X.ToString()).Replace("{y}", nearest.Cell.Y.ToString()).Replace("{z}", nearest.Cell.Z.ToString())
            .Replace("{dist}", nearestDist.ToString()).Replace("{reach}", BaseWallReach(nearest).ToString()));

        // #3209: the first look at a level runs its fill to the end (bounded by the cell budget, once — the result is cached);
        // after that the report reads the last finished fill like every other query and says when a refresh is running.
        var level = RefreshBaseWalls(nearest, cell.Y, complete: !WalledFillFinished(nearest, cell.Y));
        string verdict = L(level.BudgetOut ? "srv.basewalls.fail_open" : "srv.basewalls.complete");
        lines.Add(L("srv.basewalls.level")
            .Replace("{y}", cell.Y.ToString())
            .Replace("{cells}", level.ReachableCount.ToString())
            .Replace("{budget}", level.Budget.ToString())
            .Replace("{verdict}", level.Job is null ? verdict : verdict + " · " + L("srv.basewalls.refilling")));

        // The animal verdict (does anything spawn here?) — "walled in" is about the wall ring, not air, so the line says
        // "for animals" (#2615): it used to stand right above "Air at your cell: none — leaky" and read as a contradiction.
        string state = InSealedBaseRoom(cell) ? L("srv.basewalls.here_sealed")
            : level.Encloses(cell, _world.Circumference) ? L("srv.basewalls.here_enclosed")
            : L("srv.basewalls.here_open");
        lines.Add(L("srv.basewalls.here").Replace("{state}", state));
        lines.Add(L("srv.basewalls.rules"));

        // #2542/#2654: the air at the caller's cell and why there is none — a leak (named at its opening) or a closed room no
        // door joins to the base. No size or reach limit is left to report (#2649). The feet cell, like the oxygen tick asks.
        var air = DiagnoseBaseAir(cell);
        string airState = air.Status switch
        {
            BaseAirStatus.Zone => L("srv.basewalls.air_zone"),
            BaseAirStatus.Supplied => L("srv.basewalls.air_supplied")
                .Replace("{cells}", air.Cells.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{rooms}", air.Rooms.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            // #2614/#2654: name where the air gets out — the opening, its cell and its height from you.
            BaseAirStatus.Leak when air.LeakCell is not null => BaseAirLeakText(session.Locale,
                "srv.basewalls.air_leak_at", "srv.basewalls.air_leak_open_at", air, p.Position.ToBlock()),
            BaseAirStatus.Leak => L("srv.basewalls.air_leak"),
            _ => L("srv.basewalls.air_unknown"), // closed, but no door leads into it from the base
        };
        lines.Add(L("srv.basewalls.air").Replace("{state}", airState));
        lines.Add(L("srv.basewalls.air_rules")
            .Replace("{reach}", BaseWallReach(nearest).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return lines;
    }

    /// <summary>Test seam: the <c>/basewalls</c> report lines for a session (localized to its locale).</summary>
    public IReadOnlyList<string> BaseWallsReportForTest(PlayerSession session, bool ownOnly = false)
        => BaseWallsReport(session, ownOnly);

    /// <summary>Test seam: whether a cell reads as fenced in by a base's walls right now (cache refreshed, a pending fill run
    /// to the end — the slicing has its own tests, #3209).</summary>
    public bool InWalledBaseAreaForTest(int x, int y, int z) => InWalledBaseArea(new Vector3i(x, y, z), complete: true);

    /// <summary>Test seam: the size of the outside-in fill at a cell's level for the first base in reach, and
    /// whether that fill ran out of budget (an answer of "open" that means nothing). Refreshed and run to the end.</summary>
    public (int Reachable, bool FailOpen) WalledFillForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WithinBaseWallReach(b, canonical))
            {
                var level = RefreshBaseWalls(b, canonical.Y, complete: true);
                return (level.ReachableCount, level.FailOpen);
            }
        }

        return (0, false);
    }

    /// <summary>Whether a level of the base has finished a fill (the report runs only a level's first fill to the end).</summary>
    private bool WalledFillFinished(ServerBase b, int feetY)
        => _baseWalls.TryGetValue((b.Id, feetY), out var level) && level.Reached is not null;
}
