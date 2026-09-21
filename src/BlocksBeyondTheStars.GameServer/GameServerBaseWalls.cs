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
/// <item>Cached per (base, feet level) like <c>_baseAir</c>: invalidated by a block set or a gate toggled inside
/// the box (#1367, with <see cref="WalledRecomputeInterval"/> as the backstop), a budget that scales with the
/// box (<see cref="WalledFillBudgetFor"/>), <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base never
/// drags chunk generation. <b>Fail-open</b>: an unloaded column reads as air, the fill leaks in, the spawn is
/// allowed — the same direction the air system fails; a fill that runs out of budget answers "open" for
/// everything at that level as well (logged once when a level flips to that state).</item>
/// <item><b>Closed doors count as walls</b> (a deliberate divergence from the air model, where mechanical
/// doors leak): a shut wooden gate keeps animals out; an open one is a gap. Proximity-operated doors (slide,
/// energy) count as walls whatever their state (#1358): they open only for a player and close by themselves,
/// so an animal never passes one — and a player standing at their yard's sliding gate used to hold it open
/// for the fill.</item>
/// <item>Fliers spawn above the walls and stay ungated; cave dwellers below them too; hostile machines are
/// out of scope on purpose — they SHOULD threaten a base, that is what the sentry post is for.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Cells the outside-in fill may visit per level, per column of its box (#1862: the budget scales
    /// with the box so a 200×200 fortress does not fail open on sight): the level's own slice, the walkable terrain
    /// within the band and a fair amount of cave on top of that. The minimum 97×97 box gets ~75k, the old flat 60k.</summary>
    private const int WalledFillBudgetPerColumn = 8;

    /// <summary>Hard cap on one fill's cell budget, whatever the box (a 385×385 box would otherwise ask for 1.2M).</summary>
    private const int WalledFillBudgetCap = 600000;

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
    /// set or a gate toggled inside the box marks the level dirty instead, so the interval only backs that up.</summary>
    private const double WalledRecomputeInterval = 8.0;

    /// <summary>One base's reachable-from-outside set at one feet level (everything else in the box is enclosed).
    /// The set is a bitset over the box's band rows (#1862: a 385×385 box holds up to 600k reachable cells, and a
    /// hilly base keeps two dozen levels — a hash set per level would be hundreds of MB).</summary>
    private sealed class WalledLevel
    {
        public string Body = string.Empty;
        public Vector3i Center;   // the base core — the fill's box is the reach cube around it
        public int FeetY;
        public int Reach;         // the box's Chebyshev half-extent at the time of the fill
        public int YMin;          // the band's lowest row (the bitset's row 0)
        public int Rows;          // band rows held by the bitset
        public System.Collections.BitArray? Reached; // box-relative ((y − YMin) · side + dx) · side + dz
        public int ReachableCount;
        public int Budget;        // cells the fill was allowed (the /basewalls report)
        public bool FailOpen; // the fill ran out of budget — nothing at this level reads as enclosed
        public bool Dirty = true; // a block changed / a gate toggled inside the box since the last fill
        public double ComputedAt = double.NegativeInfinity;
        public int Computes; // fills run for this level (test seam)

        /// <summary>Whether the fill reached a canonical cell (false for anything outside the box or the band).</summary>
        public bool Contains(Vector3i canonical, int circ)
        {
            if (Reached is null)
            {
                return false;
            }

            int side = 2 * Reach + 1;
            int dx = WorldConstants.WrapDeltaX(canonical.X - Center.X, circ) + Reach;
            int dz = WorldConstants.WrapDeltaZ(canonical.Z - Center.Z, circ) + Reach;
            int dy = canonical.Y - YMin;
            if (dx < 0 || dx >= side || dz < 0 || dz >= side || dy < 0 || dy >= Rows)
            {
                return false;
            }

            return Reached[(dy * side + dx) * side + dz];
        }
    }

    private readonly Dictionary<(int BaseId, int FeetY), WalledLevel> _baseWalls = new();

    /// <summary>Each base's fill-box half-extent (#1862), derived lazily from the player-built cells around its core
    /// (see <see cref="BaseWallReach"/>) and grown live by <see cref="GrowBaseWallReach"/>.</summary>
    private readonly Dictionary<int, int> _baseWallReach = new();

    /// <summary>Fills computed so far (test seam for the cache behaviour, #1367).</summary>
    public int WalledFillComputesForTest { get; private set; }

    /// <summary>Cells the fill may visit for a box of <paramref name="side"/> columns per axis (#1862).</summary>
    private static int WalledFillBudgetFor(int side)
        => System.Math.Min(WalledFillBudgetCap, side * side * WalledFillBudgetPerColumn);

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
    /// refilled — the seeds must move out past the new wall. A base whose reach is not computed yet needs nothing:
    /// its first <see cref="BaseWallReach"/> reads the edit from the store, which was written before this event.</summary>
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
    /// the base's reach cube, clipped to the walking band around its feet level (plus the one-row margins).</summary>
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
            if (level.Dirty || level.Body != world.LocationId)
            {
                continue;
            }

            if (System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - level.Center.X, circ)) <= level.Reach
                && System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - level.Center.Z, circ)) <= level.Reach
                && System.Math.Abs(canonical.Y - level.FeetY) <= WalledFillBand + 1
                && System.Math.Abs(canonical.Y - level.Center.Y) <= level.Reach + 1)
            {
                level.Dirty = true;
            }
        }
    }

    /// <summary>Shortest absolute latitude distance across the north–south seam (#1367: the reach test wrapped
    /// X only, so a base near the seam got a truncated box and its yards failed open).</summary>
    private int WrapAbsZ(int dz) => System.Math.Abs(WorldConstants.WrapDeltaZ(dz, _world.Circumference));

    /// <summary>Scratch classification of the fill's box (one byte per cell, reused across fills).</summary>
    private byte[]? _wallScratch;

    private static readonly (int Dx, int Dz)[] WallFillDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>True if the cell lies inside a founded base's reach box on this world and the outside-in fill
    /// at that feet level cannot reach it — i.e. walls (or shut doors) close it off from the open terrain.
    /// Ownership is ignored, like every base predicate: a visitor's yard is a yard too.</summary>
    private bool InWalledBaseArea(Vector3i cell)
    {
        string body = _world.LocationId;
        int circ = _world.Circumference;
        var canonical = WorldConstants.CanonicalBlock(cell, circ);
        foreach (var b in _bases)
        {
            if (b.Planet != body || !WithinBaseWallReach(b, canonical))
            {
                continue;
            }

            var level = RefreshBaseWalls(b, canonical.Y);
            if (!level.FailOpen && !level.Contains(canonical, circ))
            {
                return true;
            }
        }

        return false;
    }

    private WalledLevel RefreshBaseWalls(ServerBase b, int feetY)
    {
        string body = _world.LocationId;
        var key = (b.Id, feetY);
        if (!_baseWalls.TryGetValue(key, out var level))
        {
            // Keep the cache bounded: a base queried at many terrain levels drops its stalest ones.
            if (_baseWalls.Count(kv => kv.Key.BaseId == b.Id) >= WalledLevelsPerBase)
            {
                var stalest = _baseWalls.Where(kv => kv.Key.BaseId == b.Id).OrderBy(kv => kv.Value.ComputedAt).First().Key;
                _baseWalls.Remove(stalest);
            }

            _baseWalls[key] = level = new WalledLevel();
        }

        int reach = BaseWallReach(b);
        if (level.Body != body || level.Dirty || level.Center != b.Cell || level.Reach != reach
            || _uptime - level.ComputedAt >= WalledRecomputeInterval)
        {
            bool wasFailOpen = level.FailOpen;
            level.Body = body;
            level.Center = b.Cell;
            level.FeetY = feetY;
            level.Reach = reach;
            level.ComputedAt = _uptime;
            ComputeReachableFromOutside(b, feetY, reach, level);
            level.Dirty = false;
            level.Computes++;
            WalledFillComputesForTest++;
            if (level.FailOpen && !wasFailOpen)
            {
                _log.Info($"Base walls: the fill for base #{b.Id} ({b.Name}) at feet level {feetY} ran out of budget " +
                          $"({level.Budget} cells, reach {reach}) — nothing at that level reads as fenced in until it fits.");
            }
        }

        return level;
    }

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

    // Scratch cell classes: bits 0–1 = free (unknown / free / solid), bits 2–3 = carries feet (unknown / yes / no),
    // bits 4–5 = the cell is a fluid (unknown / yes / no) — read for the cell UNDER a candidate (#1862).
    private const byte WallFreeMask = 0x03, WallFree = 0x01, WallSolid = 0x02;
    private const byte WallSupportMask = 0x0C, WallSupports = 0x04, WallNoSupport = 0x08;
    private const byte WallFluidMask = 0x30, WallFluid = 0x10, WallNotFluid = 0x20;

    /// <summary>
    /// The walking flood from the fill box's boundary through cells an animal could pass (#1315, #1347):
    /// a cell is <i>free</i> when it is neither a colliding block nor a fluid (walk-through props pass, as
    /// the creature body gate reads them) and not covered by a door that counts as wall; the fill may stand in
    /// a free cell that is <i>supported</i> (a colliding block under it, or ONE cell of fluid over something
    /// that is not fluid — a pond is waded on its surface, never flooded through its volume, while fluid over
    /// fluid is a moat and carries no feet, #1862; grass, props and tree canopies carry no feet either, which
    /// also keeps the fill out of every tree crown) or that lies on the queried level itself (the original
    /// horizontal slice — a moat directly under that level is refused there too). From a
    /// cell it steps to the four neighbour columns at the same height, one up (onto a supported cell — a
    /// step, never a levitation) or one down (through a free cell above the landing). Seeds: every boundary
    /// column's free cells on the level, plus its supported free cells within <see cref="WalledFillBand"/>,
    /// so the fill can come down a slope from higher ground. The band also bounds the flood, so a deep cave
    /// system under the base cannot eat the budget. Writes the level's bitset, count, budget and fail-open flag.
    /// </summary>
    private void ComputeReachableFromOutside(ServerBase b, int feetY, int r, WalledLevel level)
    {
        int circ = _world.Circumference;
        int side = 2 * r + 1;
        int yMin = System.Math.Max(feetY - WalledFillBand, b.Cell.Y - r);
        int yMax = System.Math.Min(feetY + WalledFillBand, b.Cell.Y + r);
        int rows = yMax - yMin + 3; // the band plus the support row under it and the head row above it
        int cells = side * side * rows;
        if (_wallScratch is null || _wallScratch.Length < cells)
        {
            _wallScratch = new byte[cells];
        }

        var scratch = _wallScratch;
        System.Array.Clear(scratch, 0, cells);
        var wallDoors = DoorCellsWhere(d => !d.Open || !DoorBlocks.IsHandOperated(d.Kind)); // #1358: proximity doors are walls in any state
        var reached = new System.Collections.BitArray(side * side * (yMax - yMin + 1));
        var frontier = new Queue<Vector3i>();
        int count = 0;
        level.YMin = yMin;
        level.Rows = yMax - yMin + 1;
        level.Reached = reached;
        level.Budget = WalledFillBudgetFor(side);
        level.FailOpen = false;

        // dx/dz are box-relative (0..side-1); y is absolute and may run one row past the band on each side.
        Vector3i World(int dx, int y, int dz)
            => WorldConstants.CanonicalBlock(new Vector3i(b.Cell.X - r + dx, y, b.Cell.Z - r + dz), circ);

        bool Free(int dx, int y, int dz)
        {
            if (y < yMin - 1 || y > yMax + 1)
            {
                return false; // outside the band (and its one-row margins) — nothing is walked there
            }

            int i = ((y - (yMin - 1)) * side + dx) * side + dz;
            byte v = (byte)(scratch[i] & WallFreeMask);
            if (v == 0)
            {
                var c = World(dx, y, dz);
                bool free = !wallDoors.Contains(c)
                    && !IsCollidingBlock(_world.GetBlockIfLoaded(c), fluidsPass: false, foliagePasses: false);
                v = free ? WallFree : WallSolid;
                scratch[i] |= v;
            }

            return v == WallFree;
        }

        // Whether the cell (dx, y, dz) is a fluid (any y: a cell below the scratch rows is read from the world directly).
        bool Fluid(int dx, int y, int dz)
        {
            if (y < yMin - 1 || y > yMax + 1)
            {
                return IsFluid(_world.GetBlockIfLoaded(World(dx, y, dz)).Value);
            }

            int i = ((y - (yMin - 1)) * side + dx) * side + dz;
            byte v = (byte)(scratch[i] & WallFluidMask);
            if (v == 0)
            {
                v = IsFluid(_world.GetBlockIfLoaded(World(dx, y, dz)).Value) ? WallFluid : WallNotFluid;
                scratch[i] |= v;
            }

            return v == WallFluid;
        }

        // Whether the cell UNDER (dx, y, dz) carries feet: a colliding block, or one cell of fluid over something that
        // is not fluid (#1862: a pond is waded, a moat two or more deep is not) — not air, grass, a prop or a canopy.
        bool Supported(int dx, int y, int dz)
        {
            int by = y - 1;
            if (by < yMin - 1)
            {
                return false;
            }

            int i = ((by - (yMin - 1)) * side + dx) * side + dz;
            byte v = (byte)(scratch[i] & WallSupportMask);
            if (v == 0)
            {
                bool supports = IsCollidingBlock(_world.GetBlockIfLoaded(World(dx, by, dz)), fluidsPass: false, foliagePasses: true)
                    && !(Fluid(dx, by, dz) && Fluid(dx, by - 1, dz));
                v = supports ? WallSupports : WallNoSupport;
                scratch[i] |= v;
            }

            return v == WallSupports;
        }

        // Where the fill may stand: a free cell in the band that has something under it, or that is on the queried
        // level and not over a moat (the level slice passes over air and a one-deep pond, never fluid over fluid).
        bool Standable(int dx, int y, int dz)
            => y >= yMin && y <= yMax && Free(dx, y, dz)
               && (Supported(dx, y, dz) || (y == feetY && !Fluid(dx, y - 1, dz)));

        void Visit(int dx, int y, int dz)
        {
            int i = ((y - yMin) * side + dx) * side + dz;
            if (!reached[i])
            {
                reached[i] = true;
                count++;
                frontier.Enqueue(new Vector3i(dx, y, dz)); // box-relative on the queue
            }
        }

        void Seed(int dx, int dz)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                if (Standable(dx, y, dz))
                {
                    Visit(dx, y, dz);
                }
            }
        }

        for (int d = 0; d < side; d++)
        {
            Seed(d, 0);
            Seed(d, side - 1);
            Seed(0, d);
            Seed(side - 1, d);
        }

        int budget = level.Budget;
        while (frontier.Count > 0)
        {
            if (budget-- <= 0)
            {
                level.FailOpen = true; // too much open ground to walk — nothing at this level may read as fenced in
                break;
            }

            var cell = frontier.Dequeue();
            foreach (var (ddx, ddz) in WallFillDirs)
            {
                int nx = cell.X + ddx, nz = cell.Z + ddz, y = cell.Y;
                if (nx < 0 || nx >= side || nz < 0 || nz >= side)
                {
                    continue;
                }

                if (Standable(nx, y, nz))
                {
                    Visit(nx, y, nz); // a level step
                }

                if (Standable(nx, y + 1, nz))
                {
                    Visit(nx, y + 1, nz); // one step up onto something (a supported cell, or the level slice)
                }

                if (Free(nx, y, nz) && Standable(nx, y - 1, nz))
                {
                    Visit(nx, y - 1, nz); // one step down, through the free cell above the landing
                }
            }
        }

        level.ReachableCount = count;
    }

    /// <summary>Cells covered by the doors that satisfy <paramref name="pick"/> in the active world: the door's
    /// width along its wall axis × the 3-tall doorway column, canonical. The wall fill takes every SHUT door
    /// and every proximity-operated one (a gate keeps animals out — #1358); the air fill keeps its own
    /// energy-door map (the curtain seals open or shut).</summary>
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

    /// <summary>Drops a removed base's cached wall levels and its fill-box reach (called when its core is mined).</summary>
    private void ForgetBaseWalls(int baseId)
    {
        foreach (var key in _baseWalls.Keys.Where(k => k.BaseId == baseId).ToList())
        {
            _baseWalls.Remove(key);
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

        var level = RefreshBaseWalls(nearest, cell.Y);
        lines.Add(L("srv.basewalls.level")
            .Replace("{y}", cell.Y.ToString())
            .Replace("{cells}", level.ReachableCount.ToString())
            .Replace("{budget}", level.Budget.ToString())
            .Replace("{verdict}", L(level.FailOpen ? "srv.basewalls.fail_open" : "srv.basewalls.complete")));

        string state = InSealedBaseRoom(cell) ? L("srv.basewalls.here_sealed")
            : !level.FailOpen && !level.Contains(cell, _world.Circumference) ? L("srv.basewalls.here_enclosed")
            : L("srv.basewalls.here_open");
        lines.Add(L("srv.basewalls.here").Replace("{state}", state));
        lines.Add(L("srv.basewalls.rules"));
        return lines;
    }

    /// <summary>Test seam: the <c>/basewalls</c> report lines for a session (localized to its locale).</summary>
    public IReadOnlyList<string> BaseWallsReportForTest(PlayerSession session, bool ownOnly = false)
        => BaseWallsReport(session, ownOnly);

    /// <summary>Test seam: whether a cell reads as fenced in by a base's walls right now (cache refreshed).</summary>
    public bool InWalledBaseAreaForTest(int x, int y, int z) => InWalledBaseArea(new Vector3i(x, y, z));

    /// <summary>Test seam: the size of the outside-in fill at a cell's level for the first base in reach, and
    /// whether that fill ran out of budget (an answer of "open" that means nothing).</summary>
    public (int Reachable, bool FailOpen) WalledFillForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WithinBaseWallReach(b, canonical))
            {
                var level = RefreshBaseWalls(b, canonical.Y);
                return (level.ReachableCount, level.FailOpen);
            }
        }

        return (0, false);
    }
}
