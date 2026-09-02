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
/// <item>Take the base's reach box (Chebyshev <see cref="SealedRoomMaxReach"/> = 48 → a 97×97 footprint),
/// seed every boundary column at the query's feet level, and flood INWARD through cells a walking animal
/// could pass. Everything inside the box the fill never reaches is <b>enclosed</b> — one fill answers every
/// yard, courtyard and room at that level, however many there are.</item>
/// <item>The fill <b>walks</b> (#1347): it steps ±1 block vertically like a walker (<see cref="CreatureMotion.StepUpLimit"/>
/// = 1) — up onto a supported cell, down through a free one — so a one-block terrain step, a garden edge or
/// the slope of a hollow is passable and a 2+ block rise is a wall. The first version flooded a single
/// horizontal slice and read natural terrain as masonry: every hollow within 48 blocks of a base was
/// "fenced in" and no land animal spawned in it. The query's own level additionally passes through any
/// non-colliding cell (the original rule, kept): the boundary is 48 blocks out and rarely on the yard's
/// level, so the fill needs to cross lower ground to get there at all.</item>
/// <item>Cached per (base, feet level) like <c>_baseAir</c>: invalidated by a block set or a gate toggled inside
/// the box (#1367, with <see cref="WalledRecomputeInterval"/> as the backstop), a bounded budget,
/// <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base never drags chunk generation. <b>Fail-open</b>:
/// an unloaded column reads as air, the fill leaks in, the spawn is allowed — the same direction the air
/// system fails; a fill that runs out of budget answers "open" for everything at that level as well.</item>
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
    /// <summary>Cells the outside-in fill may visit per level: the level's own 97×97 slice, the walkable
    /// terrain within the band and a fair amount of cave on top of that.</summary>
    private const int WalledFillBudget = 60000;

    /// <summary>How far above/below the queried feet level the walking fill follows the terrain (#1347).</summary>
    private const int WalledFillBand = 12;

    /// <summary>Levels cached per base before the stalest are dropped (a hilly base is queried at a few dozen feet levels).</summary>
    private const int WalledLevelsPerBase = 24;

    /// <summary>Seconds a level's fill is trusted without a change inside its box (#1367). The air system's
    /// 1.5 s was shorter than the 1.5–4 s between spawn attempts, so nearly every attempt recomputed; a block
    /// set or a gate toggled inside the box marks the level dirty instead, so the interval only backs that up.</summary>
    private const double WalledRecomputeInterval = 8.0;

    /// <summary>One base's reachable-from-outside set at one feet level (everything else in the box is enclosed).</summary>
    private sealed class WalledLevel
    {
        public string Body = string.Empty;
        public Vector3i Center;   // the base core — the fill's box is the reach cube around it
        public int FeetY;
        public HashSet<Vector3i> Reachable = new();
        public bool FailOpen; // the fill ran out of budget — nothing at this level reads as enclosed
        public bool Dirty = true; // a block changed / a gate toggled inside the box since the last fill
        public double ComputedAt = double.NegativeInfinity;
        public int Computes; // fills run for this level (test seam)
    }

    private readonly Dictionary<(int BaseId, int FeetY), WalledLevel> _baseWalls = new();

    /// <summary>Fills computed so far (test seam for the cache behaviour, #1367).</summary>
    public int WalledFillComputesForTest { get; private set; }

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

            if (System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - level.Center.X, circ)) <= SealedRoomMaxReach
                && System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - level.Center.Z, circ)) <= SealedRoomMaxReach
                && System.Math.Abs(canonical.Y - level.FeetY) <= WalledFillBand + 1
                && System.Math.Abs(canonical.Y - level.Center.Y) <= SealedRoomMaxReach + 1)
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
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet != body
                || WrapAbs(canonical.X - b.Cell.X) > SealedRoomMaxReach
                || System.Math.Abs(canonical.Y - b.Cell.Y) > SealedRoomMaxReach
                || WrapAbsZ(canonical.Z - b.Cell.Z) > SealedRoomMaxReach)
            {
                continue;
            }

            var level = RefreshBaseWalls(b, canonical.Y);
            if (!level.FailOpen && !level.Reachable.Contains(canonical))
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

        if (level.Body != body || level.Dirty || level.Center != b.Cell || _uptime - level.ComputedAt >= WalledRecomputeInterval)
        {
            level.Body = body;
            level.Center = b.Cell;
            level.FeetY = feetY;
            level.ComputedAt = _uptime;
            level.Reachable = ComputeReachableFromOutside(b, feetY, out bool failOpen);
            level.FailOpen = failOpen;
            level.Dirty = false;
            level.Computes++;
            WalledFillComputesForTest++;
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
            if (b.Planet == _world.LocationId && WrapAbs(canonical.X - b.Cell.X) <= SealedRoomMaxReach
                && System.Math.Abs(canonical.Y - b.Cell.Y) <= SealedRoomMaxReach
                && WrapAbsZ(canonical.Z - b.Cell.Z) <= SealedRoomMaxReach)
            {
                return _baseWalls.TryGetValue((b.Id, canonical.Y), out var level) ? level.Computes : 0;
            }
        }

        return 0;
    }

    // Scratch cell classes: bits 0–1 = free (unknown / free / solid), bits 2–3 = carries feet (unknown / yes / no).
    private const byte WallFreeMask = 0x03, WallFree = 0x01, WallSolid = 0x02;
    private const byte WallSupportMask = 0x0C, WallSupports = 0x04, WallNoSupport = 0x08;

    /// <summary>
    /// The walking flood from the reach box's boundary through cells an animal could pass (#1315, #1347):
    /// a cell is <i>free</i> when it is neither a colliding block nor a fluid (walk-through props pass, as
    /// the creature body gate reads them) and not covered by a door that counts as wall; the fill may stand in
    /// a free cell that is <i>supported</i> (a colliding block or a fluid under it — so a pond is crossed on
    /// its surface rather than read as a moat, and never flooded through its volume; grass, props and tree
    /// canopies carry no feet, which also keeps the fill out of every tree crown) or that lies on the queried
    /// level itself (the original horizontal slice). From a
    /// cell it steps to the four neighbour columns at the same height, one up (onto a supported cell — a
    /// step, never a levitation) or one down (through a free cell above the landing). Seeds: every boundary
    /// column's free cells on the level, plus its supported free cells within <see cref="WalledFillBand"/>,
    /// so the fill can come down a slope from higher ground. The band also bounds the flood, so a deep cave
    /// system under the base cannot eat the budget.
    /// </summary>
    private HashSet<Vector3i> ComputeReachableFromOutside(ServerBase b, int feetY, out bool failOpen)
    {
        int circ = _world.Circumference;
        int r = SealedRoomMaxReach;
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
        var wallDoors = DoorCellsWhere(d => !d.Open || !IsHandOperated(d.Kind)); // #1358: proximity doors are walls in any state
        var reachable = new HashSet<Vector3i>();
        var frontier = new Queue<Vector3i>();
        failOpen = false;

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

        // Whether the cell UNDER (dx, y, dz) carries feet: a colliding block or a fluid — not air, grass, a prop or a canopy.
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
                bool supports = IsCollidingBlock(_world.GetBlockIfLoaded(World(dx, by, dz)), fluidsPass: false, foliagePasses: true);
                v = supports ? WallSupports : WallNoSupport;
                scratch[i] |= v;
            }

            return v == WallSupports;
        }

        // Where the fill may stand: a free cell in the band that is on the queried level or has something under it.
        bool Standable(int dx, int y, int dz)
            => y >= yMin && y <= yMax && Free(dx, y, dz) && (y == feetY || Supported(dx, y, dz));

        void Visit(int dx, int y, int dz)
        {
            if (reachable.Add(World(dx, y, dz)))
            {
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

        int budget = WalledFillBudget;
        while (frontier.Count > 0)
        {
            if (budget-- <= 0)
            {
                failOpen = true; // too much open ground to walk — nothing at this level may read as fenced in
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

        return reachable;
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

    /// <summary>Drops a removed base's cached wall levels (called when its core is mined).</summary>
    private void ForgetBaseWalls(int baseId)
    {
        foreach (var key in _baseWalls.Keys.Where(k => k.BaseId == baseId).ToList())
        {
            _baseWalls.Remove(key);
        }
    }

    /// <summary><c>/basewalls</c> (#1452): the admin's window into the enclosure fill. A yard that "should be
    /// fenced in" but still gets animals used to be undiagnosable — the fill fails open silently (budget,
    /// unloaded chunks, a one-high segment, an open hinge door) and nothing logs a spawn. The report names the
    /// nearest core, the fill at the admin's feet level (size, budget, fail-open), what the admin's own cell
    /// reads as, and the rules the texts used to skip.</summary>
    private void AdminBaseWalls(PlayerSession session)
    {
        foreach (string line in BaseWallsReport(session))
        {
            Send(session, new ServerMessage { Text = line });
        }
    }

    private List<string> BaseWallsReport(PlayerSession session)
    {
        var p = session.State;
        var cell = WorldConstants.CanonicalBlock(p.Position.ToBlock(), _world.Circumference);
        string L(string key) => Localize(session.Locale, key);
        var lines = new List<string>();

        ServerBase? nearest = null;
        int nearestDist = int.MaxValue;
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId)
            {
                continue;
            }

            int d = System.Math.Max(WrapAbs(cell.X - b.Cell.X), System.Math.Max(System.Math.Abs(cell.Y - b.Cell.Y), WrapAbsZ(cell.Z - b.Cell.Z)));
            if (d <= SealedRoomMaxReach && d < nearestDist)
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
            .Replace("{dist}", nearestDist.ToString()).Replace("{reach}", SealedRoomMaxReach.ToString()));

        var level = RefreshBaseWalls(nearest, cell.Y);
        lines.Add(L("srv.basewalls.level")
            .Replace("{y}", cell.Y.ToString())
            .Replace("{cells}", level.Reachable.Count.ToString())
            .Replace("{budget}", WalledFillBudget.ToString())
            .Replace("{verdict}", L(level.FailOpen ? "srv.basewalls.fail_open" : "srv.basewalls.complete")));

        string state = InSealedBaseRoom(cell) ? L("srv.basewalls.here_sealed")
            : !level.FailOpen && !level.Reachable.Contains(cell) ? L("srv.basewalls.here_enclosed")
            : L("srv.basewalls.here_open");
        lines.Add(L("srv.basewalls.here").Replace("{state}", state));
        lines.Add(L("srv.basewalls.rules"));
        return lines;
    }

    /// <summary>Test seam: the <c>/basewalls</c> report lines for a session (localized to its locale).</summary>
    public IReadOnlyList<string> BaseWallsReportForTest(PlayerSession session) => BaseWallsReport(session);

    /// <summary>Test seam: whether a cell reads as fenced in by a base's walls right now (cache refreshed).</summary>
    public bool InWalledBaseAreaForTest(int x, int y, int z) => InWalledBaseArea(new Vector3i(x, y, z));

    /// <summary>Test seam: the size of the outside-in fill at a cell's level for the first base in reach, and
    /// whether that fill ran out of budget (an answer of "open" that means nothing).</summary>
    public (int Reachable, bool FailOpen) WalledFillForTest(int x, int y, int z)
    {
        var canonical = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && WrapAbs(canonical.X - b.Cell.X) <= SealedRoomMaxReach
                && System.Math.Abs(canonical.Y - b.Cell.Y) <= SealedRoomMaxReach
                && WrapAbsZ(canonical.Z - b.Cell.Z) <= SealedRoomMaxReach)
            {
                var level = RefreshBaseWalls(b, canonical.Y);
                return (level.Reachable.Count, level.FailOpen);
            }
        }

        return (0, false);
    }
}
