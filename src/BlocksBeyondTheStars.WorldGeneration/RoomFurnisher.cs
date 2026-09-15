// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Procedural interiors (#1828): puts furniture into a room — a bed, a table with a chair, a crate, a plant,
/// a light, a market counter, a terminal — along the walls of a floor region, leaving the centre, the NPC's
/// spot and the door lane walkable. Works on any floor region: the rectangular interior of a procedural
/// building (<see cref="SettlementGenerator.StampBuilding"/>) or the flood-filled floor of an authored room
/// (a <c>room</c> marker in a template, <see cref="FloodRoom"/>). The pieces are the existing prop blocks
/// (bed, crate, flower pot, torch, lights …) and the #805 furniture SHAPES (table, chair, slab counter) on
/// the style's material, so a village sits at a log table and a town at a steel one.
/// <para>Deterministic from the <see cref="System.Random"/> handed in — callers seed it from a hash so the
/// main generator stream stays untouched and a room always gets the same furniture.</para>
/// </summary>
public static class RoomFurnisher
{
    /// <summary>The look of a room: timber-and-stone village, iron-and-steel town/city, crystal-lit alien.</summary>
    public enum Style
    {
        Village,
        Town,
        Alien,

        /// <summary>A station deck (#1874): the town's steel pieces under ceiling lights.</summary>
        Station,
    }

    /// <summary>What a room is for — drives which pieces it gets.</summary>
    public enum RoomRole
    {
        House,
        Market,
        Board,
        Upper,

        /// <summary>A station crew cabin (#1874): a bed, a locker, a lamp, a chair at a small table.</summary>
        Cabin,

        /// <summary>A cabin whose author already placed the bed — everything but the bed.</summary>
        CabinNoBed,

        /// <summary>A canteen or bar: tables with chairs, where the crew sits in the evening.</summary>
        Lounge,

        /// <summary>A store room: crates along the walls.</summary>
        Storage,

        /// <summary>A medbay: a bed, a terminal, a locker.</summary>
        Medbay,

        /// <summary>A hall or hub: a terminal, a plant, a table — no bed.</summary>
        Hall,

        /// <summary>A settlement tavern (#1885): a counter, tables with chairs, a barrel crate, lights — no bed.</summary>
        Tavern,

        /// <summary>A settlement workshop (#1885): a workbench, a forge, crates, a light.</summary>
        Workshop,
    }

    /// <summary>Writes one cell: block id + packed shape (0 = cube) + tint + glow.</summary>
    public delegate void CellSink(int x, int y, int z, ushort block, int shape, int tint, int glow);

    /// <summary>The blocks a style furnishes with. 0 for a piece the content lacks (it is then skipped).</summary>
    public sealed class Palette
    {
        public ushort Bed;
        public int BedShape;
        public ushort TableMaterial;
        public ushort ChairMaterial;
        public ushort Storage;
        public ushort Light;
        public ushort Plant;
        public int PlantShape;
        public ushort PlantAlt;
        public double PlantAltChance;
        public ushort Counter;
        public ushort Terminal;
        public ushort TerminalAlt;
        public double TerminalAltChance;
        public ushort Workbench;
        public ushort Forge;

        /// <summary>True when the building lights its rooms itself (a lamp in the deck, #1808) — no floor light then.</summary>
        public bool CeilingLit;
    }

    /// <summary>Style of a settlement tier: town/city/metropolis build town-style rooms, alien settlements
    /// their own, everything else a village room.</summary>
    public static Style StyleFor(string tier, bool alien)
        => alien ? Style.Alien
            : StructureRoles.IsTownStyleTier(tier) || tier == StructureRoles.MetropolisTier || tier == CityGenerator.Tier ? Style.Town
            : Style.Village;

    /// <summary>The palette of a style from the loaded content — a missing block leaves its piece out.</summary>
    public static Palette PaletteFor(Style style, GameContent content)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;

        var p = new Palette
        {
            Bed = B("bed"),
            BedShape = ShapeCode.Pack(PropShapes.BedSingleCell, 0), // the one-cell fallback; a room with space gets the two-cell bed (#1846)
            Plant = B("flower_pot"),
            PlantShape = ShapeCode.Pack(PropShapes.DefaultPlaceShape("flower_pot"), 0),
            Workbench = B("workbench"),
            Forge = B("forge"),
        };

        switch (style)
        {
            case Style.Town:
            case Style.Station:
                p.TableMaterial = B("steel_floor", B("iron_wall"));
                p.ChairMaterial = B("steel_floor", B("iron_wall"));
                p.Storage = B("crate", B("wood_crate"));
                p.Light = B("light_white", B("data_cache"));
                p.PlantAlt = B("algae_tank");
                p.PlantAltChance = 0.34;
                p.Counter = B("steel_floor", B("iron_wall"));
                p.Terminal = B("data_cache");
                p.TerminalAlt = B("gaming_pc");
                p.TerminalAltChance = 0.25;
                break;
            case Style.Alien:
                p.TableMaterial = B("iron_wall", B("stone"));
                p.ChairMaterial = B("iron_wall", B("stone"));
                p.Storage = B("crate", B("wood_crate"));
                p.Light = B("data_cache", B("torch"));
                p.Counter = B("iron_wall", B("stone"));
                p.Terminal = B("data_cache");
                break;
            default:
                p.TableMaterial = B("wood_log", B("stone"));
                p.ChairMaterial = B("wood_log", B("stone"));
                p.Storage = B("wood_crate", B("crate"));
                p.Light = B("torch");
                p.Counter = B("wood_log", B("stone"));
                p.Terminal = B("data_cache");
                break;
        }

        return p;
    }

    private enum Piece
    {
        Bed,
        Light,
        Table,
        Storage,
        Plant,
        Counter,
        Terminal,
        Workbench,
        Forge,
    }

    private static readonly Piece[] HousePieces = { Piece.Bed, Piece.Light, Piece.Table, Piece.Storage, Piece.Plant };
    private static readonly Piece[] MarketPieces = { Piece.Counter, Piece.Storage, Piece.Light, Piece.Storage, Piece.Table };
    private static readonly Piece[] BoardPieces = { Piece.Table, Piece.Terminal, Piece.Light, Piece.Storage };
    private static readonly Piece[] UpperPieces = { Piece.Bed, Piece.Storage, Piece.Light, Piece.Plant };
    private static readonly Piece[] CabinPieces = { Piece.Bed, Piece.Storage, Piece.Light, Piece.Table, Piece.Plant };
    private static readonly Piece[] CabinNoBedPieces = { Piece.Storage, Piece.Light, Piece.Table, Piece.Plant };
    private static readonly Piece[] LoungePieces = { Piece.Table, Piece.Table, Piece.Plant, Piece.Table, Piece.Storage, Piece.Light };
    private static readonly Piece[] StoragePieces = { Piece.Storage, Piece.Storage, Piece.Storage, Piece.Storage, Piece.Light };
    private static readonly Piece[] MedbayPieces = { Piece.Bed, Piece.Light, Piece.Terminal, Piece.Storage };
    private static readonly Piece[] HallPieces = { Piece.Terminal, Piece.Plant, Piece.Light, Piece.Table };
    private static readonly Piece[] TavernPieces = { Piece.Counter, Piece.Table, Piece.Table, Piece.Storage, Piece.Light, Piece.Table, Piece.Plant };
    private static readonly Piece[] WorkshopPieces = { Piece.Workbench, Piece.Forge, Piece.Storage, Piece.Light, Piece.Storage, Piece.Table };

    /// <summary>
    /// Furnishes one floor region. <paramref name="region"/> = the walkable floor cells (x, z) at height
    /// <paramref name="floorY"/> (the cell the player stands IN, not the deck below), <paramref name="clearance"/>
    /// = air cells from the floor up (a room shorter than two is left alone), <paramref name="reserved"/> = cells
    /// that must stay air (the NPC marker, the door lane, the ladder). Pieces hug the walls — a wall is any
    /// side of a cell with no region cell beyond it — corners first, and never take more than a third of the
    /// room. Returns the number of cells written.
    /// </summary>
    public static int Furnish(CellSink set, IReadOnlyList<(int X, int Z)> region, int floorY, int clearance,
        Palette p, RoomRole role, ICollection<(int X, int Z)> reserved, System.Random rng)
    {
        if (region.Count == 0 || clearance < 2)
        {
            return 0;
        }

        var cells = new HashSet<(int X, int Z)>(region);
        var used = new HashSet<(int X, int Z)>();
        foreach (var r in reserved)
        {
            used.Add(r);
        }

        int WallSides((int X, int Z) c)
        {
            int n = 0;
            if (!cells.Contains((c.X + 1, c.Z))) n++;
            if (!cells.Contains((c.X - 1, c.Z))) n++;
            if (!cells.Contains((c.X, c.Z + 1))) n++;
            if (!cells.Contains((c.X, c.Z - 1))) n++;
            return n;
        }

        // Candidate cells: along the walls, not reserved. Shuffled once, then corners first (stable), so two
        // rooms of the same shape still differ in where the bed ends up.
        var candidates = new List<(int X, int Z)>();
        foreach (var c in region)
        {
            if (!used.Contains(c) && WallSides(c) > 0)
            {
                candidates.Add(c);
            }
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var corners = new List<(int X, int Z)>();
        var edges = new List<(int X, int Z)>();
        foreach (var c in candidates)
        {
            if (WallSides(c) >= 2) corners.Add(c); else edges.Add(c);
        }

        var order = new List<(int X, int Z)>(corners.Count + edges.Count);
        order.AddRange(corners);
        order.AddRange(edges);

        int budget = System.Math.Max(1, region.Count / 3);
        var pieces = role switch
        {
            RoomRole.Market => MarketPieces,
            RoomRole.Board => BoardPieces,
            RoomRole.Upper => UpperPieces,
            RoomRole.Cabin => CabinPieces,
            RoomRole.CabinNoBed => CabinNoBedPieces,
            RoomRole.Lounge => LoungePieces,
            RoomRole.Storage => StoragePieces,
            RoomRole.Medbay => MedbayPieces,
            RoomRole.Hall => HallPieces,
            RoomRole.Tavern => TavernPieces,
            RoomRole.Workshop => WorkshopPieces,
            _ => HousePieces,
        };

        int written = 0;
        int placed = 0;

        (int X, int Z)? TakeFree()
        {
            foreach (var c in order)
            {
                if (!used.Contains(c))
                {
                    return c;
                }
            }

            return null;
        }

        (int X, int Z)? FreeNeighbour((int X, int Z) c, bool preferInside)
        {
            (int X, int Z)? fallback = null;
            foreach (var d in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var n = (c.X + d.Item1, c.Z + d.Item2);
                if (!cells.Contains(n) || used.Contains(n))
                {
                    continue;
                }

                bool inside = WallSides(n) == 0;
                if (inside == preferInside)
                {
                    return n;
                }

                fallback ??= n;
            }

            return fallback;
        }

        // A one-cell bed lies along its wall; a chair faces its table. The yaw's world meaning is
        // ShapeCode.YawDirection (the client's rotation: 0 = +Z, 1 = −X, 2 = −Z, 3 = +X) — a form's local +Z
        // (the chair's backrest, the bed head's foot side) ends up on that side of the cell.
        int YawAlongWall((int X, int Z) c) => cells.Contains((c.X, c.Z + 1)) && cells.Contains((c.X, c.Z - 1)) ? 0 : 1;

        void Put((int X, int Z) c, ushort block, int shape)
        {
            set(c.X, floorY, c.Z, block, shape, 0, 0);
            used.Add(c);
            written++;
        }

        foreach (var piece in pieces)
        {
            if (placed >= budget)
            {
                break;
            }

            var cell = TakeFree();
            if (cell is null)
            {
                break;
            }

            var c = cell.Value;
            switch (piece)
            {
                case Piece.Bed:
                    if (p.Bed == 0) continue;
                    // A two-cell bed (#1846): head against the wall, foot on a free neighbour — along the wall
                    // when there is one, else out into the room. A cramped cell keeps the one-cell bed.
                    if (FreeNeighbour(c, preferInside: false) is { } footCell)
                    {
                        int bedYaw = ShapeCode.YawToward(footCell.X - c.X, footCell.Z - c.Z);
                        Put(c, p.Bed, ShapeCode.Pack(BlockShape.BedHead, bedYaw));
                        Put(footCell, p.Bed, ShapeCode.Pack(BlockShape.BedFoot, bedYaw));
                    }
                    else
                    {
                        Put(c, p.Bed, ShapeCode.Pack(ShapeCode.ShapeOf(p.BedShape), YawAlongWall(c)));
                    }

                    break;

                case Piece.Light:
                    if (p.Light == 0 || p.CeilingLit) continue;
                    Put(c, p.Light, 0);
                    break;

                case Piece.Table:
                    if (p.TableMaterial == 0) continue;
                    Put(c, p.TableMaterial, ShapeCode.Pack(BlockShape.Table, 0));
                    if (p.ChairMaterial != 0)
                    {
                        var seat = FreeNeighbour(c, preferInside: true);
                        if (seat is { } s)
                        {
                            // The backrest (the chair's local +Z) points AWAY from the table: the step from the
                            // table to the seat. (Before #1846 the yaw table was mirrored for ±X, so half the
                            // chairs sat with their backs to the table.)
                            Put(s, p.ChairMaterial, ShapeCode.Pack(BlockShape.Chair, ShapeCode.YawToward(s.X - c.X, s.Z - c.Z)));
                        }
                    }

                    break;

                case Piece.Storage:
                    if (p.Storage == 0) continue;
                    Put(c, p.Storage, 0);
                    break;

                case Piece.Plant:
                    if (p.PlantAlt != 0 && rng.NextDouble() < p.PlantAltChance)
                    {
                        Put(c, p.PlantAlt, 0);
                    }
                    else if (p.Plant != 0)
                    {
                        Put(c, p.Plant, p.PlantShape);
                    }
                    else
                    {
                        continue;
                    }

                    break;

                case Piece.Counter:
                    if (p.Counter == 0) continue;
                    Put(c, p.Counter, ShapeCode.Pack(BlockShape.Slab, 0));
                    var next = FreeNeighbour(c, preferInside: false);
                    if (next is { } n2 && WallSides(n2) > 0)
                    {
                        Put(n2, p.Counter, ShapeCode.Pack(BlockShape.Slab, 0));
                    }

                    break;

                case Piece.Workbench:
                    if (p.Workbench == 0) continue;
                    Put(c, p.Workbench, 0);
                    break;

                case Piece.Forge:
                    if (p.Forge == 0) continue;
                    Put(c, p.Forge, 0);
                    break;

                case Piece.Terminal:
                    if (p.TerminalAlt != 0 && rng.NextDouble() < p.TerminalAltChance)
                    {
                        Put(c, p.TerminalAlt, 0);
                    }
                    else if (p.Terminal != 0)
                    {
                        Put(c, p.Terminal, 0);
                    }
                    else
                    {
                        continue;
                    }

                    break;
            }

            placed++;
        }

        return written;
    }

    /// <summary>
    /// The floor region of an authored room (#1828): every air cell reachable from (<paramref name="sx"/>,
    /// <paramref name="sy"/>, <paramref name="sz"/>) on that level through 4-connected air cells that stand on
    /// something solid, capped at <paramref name="cap"/> cells so a marker on open ground never floods the whole
    /// template. <paramref name="clearance"/> = the shortest run of air above any region cell (at most 4).
    /// Empty when the start cell is not a floor cell.
    /// </summary>
    public static List<(int X, int Z)> FloodRoom(System.Func<int, int, int, ushort> get, int w, int h, int l,
        int sx, int sy, int sz, int cap, out int clearance)
    {
        var region = new List<(int X, int Z)>();
        clearance = 0;
        if (sy <= 0 || sy >= h || sx < 0 || sz < 0 || sx >= w || sz >= l)
        {
            return region;
        }

        bool Floor(int x, int z) => x >= 0 && z >= 0 && x < w && z < l && get(x, sy, z) == 0 && get(x, sy - 1, z) != 0;
        if (!Floor(sx, sz))
        {
            return region;
        }

        var seen = new HashSet<(int X, int Z)> { (sx, sz) };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue((sx, sz));
        clearance = 4;
        while (queue.Count > 0 && region.Count < cap)
        {
            var c = queue.Dequeue();
            region.Add(c);

            int run = 0;
            for (int y = sy; y < h && y < sy + 4 && get(c.X, y, c.Z) == 0; y++)
            {
                run++;
            }

            clearance = System.Math.Min(clearance, run);

            foreach (var d in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var n = (c.X + d.Item1, c.Z + d.Item2);
                if (!seen.Contains(n) && Floor(n.Item1, n.Item2))
                {
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return region;
    }

    // ------------------------------------------------------------------ door lanes (#1901)

    /// <summary>How many rows on each side of a doorway stay walkable (like the ship doorway rule, #211).</summary>
    public const int LaneDepth = 2;

    /// <summary>How far along its wall a door gap is scanned from the marker — the server's door probe reach.</summary>
    private const int GapReach = 3;

    /// <summary>
    /// The walk-through lane of one door (#1901) — the single rule every composer shares (kit stations, settlements,
    /// cities, the editor's preview and its export check), so neither furniture nor an authored block ever stands in
    /// a doorway. Coordinates are those of the grid the lane was measured on; (x, z) cells lie at <see cref="FootY"/>.
    /// </summary>
    public sealed class DoorLane
    {
        internal DoorLane(int footY, bool wallAlongX, int gapMin, int gapMax, int doorwayMin, int doorwayMax,
            List<(int X, int Z)> gap, List<(int X, int Z)> clear, List<(int X, int Z)> keep)
        {
            FootY = footY;
            WallAlongX = wallAlongX;
            GapMin = gapMin;
            GapMax = gapMax;
            DoorwayMin = doorwayMin;
            DoorwayMax = doorwayMax;
            Gap = gap;
            Clear = clear;
            Keep = keep;
        }

        /// <summary>The height a body stands at in the doorway: the air cell over the doorway's floor.</summary>
        public int FootY { get; }

        /// <summary>True when the door's wall runs along X (the passage crosses Z), false when it runs along Z.</summary>
        public bool WallAlongX { get; }

        /// <summary>The first and last along-wall coordinate of the gap (x for a wall along X, z otherwise).</summary>
        public int GapMin { get; }

        /// <inheritdoc cref="GapMin"/>
        public int GapMax { get; }

        /// <summary>The first and last across coordinate of the doorway itself — a joint between two station modules
        /// is two deep (both port walls are open).</summary>
        public int DoorwayMin { get; }

        /// <inheritdoc cref="DoorwayMin"/>
        public int DoorwayMax { get; }

        /// <summary>The doorway cells: where a room ends, so a flood fill reads them as the closed door. Empty when the
        /// gap has no jamb at one end (a door marker on open floor splits nothing).</summary>
        public IReadOnlyList<(int X, int Z)> Gap { get; }

        /// <summary>The cells that must stay walkable at foot and head height: the doorway plus up to
        /// <see cref="LaneDepth"/> rows on each side, across the full gap width. A side ends early at the grid's edge
        /// (an entrance) or at a wall (a room one row deep).</summary>
        public IReadOnlyList<(int X, int Z)> Clear { get; }

        /// <summary><see cref="Clear"/> plus the first row's two corners one step past the ends of the gap, so a body
        /// can turn into the doorway — no furniture goes on any of these.</summary>
        public IReadOnlyList<(int X, int Z)> Keep { get; }
    }

    /// <summary>True for a door marker type (<c>door_slide</c>, <c>door_hinge</c>, <c>door_energy</c> …).</summary>
    public static bool IsDoorMarker(string type) => type.StartsWith("door_", System.StringComparison.Ordinal);

    /// <summary>
    /// Measures the lane of the door marker at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) on a
    /// grid <paramref name="w"/> × <paramref name="h"/> × <paramref name="l"/> (<paramref name="get"/> is only asked
    /// inside it; 0 = air). The marker may stand up to two cells above the doorway's floor. The wall axis and the gap
    /// are probed the way the server hangs the door (the jamb beside the marker, the air run along the wall); the
    /// doorway extends across while its jambs continue (a two-deep joint). Null when the marker cell is solid or
    /// outside the grid.
    /// </summary>
    public static DoorLane? DoorLaneAt(System.Func<int, int, int, ushort> get, int w, int h, int l, int x, int y, int z)
    {
        bool InXZ(int cx, int cz) => cx >= 0 && cz >= 0 && cx < w && cz < l;
        bool Solid(int cx, int cy, int cz) => InXZ(cx, cz) && cy >= 0 && cy < h && get(cx, cy, cz) != 0;
        if (y < 0 || y >= h || !InXZ(x, z) || Solid(x, y, z))
        {
            return null;
        }

        // The foot level: an author may set the marker a cell or two above the doorway's floor.
        int foot = y;
        if (!Solid(x, y - 1, z))
        {
            for (int k = 1; k <= 2 && y - k >= 1 && !Solid(x, y - k, z); k++)
            {
                if (Solid(x, y - k - 1, z))
                {
                    foot = y - k;
                    break;
                }
            }
        }

        // The server's door probe (GameServer.MakeDoor): a jamb beside the marker along X means the wall runs along X.
        bool alongX = Solid(x - 1, foot, z) || Solid(x + 1, foot, z);
        int a0 = alongX ? x : z, b0 = alongX ? z : x;
        (int X, int Z) Cell(int a, int b) => alongX ? (a, b) : (b, a);
        bool In(int a, int b)
        {
            var c = Cell(a, b);
            return InXZ(c.X, c.Z);
        }

        bool S(int a, int b, int cy)
        {
            var c = Cell(a, b);
            return Solid(c.X, cy, c.Z);
        }

        int lo = 0, hi = 0;
        for (int s = 1; s <= GapReach && In(a0 - s, b0) && !S(a0 - s, b0, foot); s++)
        {
            lo = -s;
        }

        for (int s = 1; s <= GapReach && In(a0 + s, b0) && !S(a0 + s, b0, foot); s++)
        {
            hi = s;
        }

        int gMin = a0 + lo, gMax = a0 + hi;
        bool bounded = S(gMin - 1, b0, foot) && S(gMax + 1, b0, foot);

        bool DoorwayRow(int b)
        {
            for (int a = gMin; a <= gMax; a++)
            {
                if (!In(a, b) || S(a, b, foot))
                {
                    return false;
                }
            }

            return S(gMin - 1, b, foot) && S(gMax + 1, b, foot);
        }

        int dMin = b0, dMax = b0;
        if (bounded)
        {
            for (int k = 1; k <= LaneDepth && DoorwayRow(b0 - k); k++)
            {
                dMin = b0 - k;
            }

            for (int k = 1; k <= LaneDepth && DoorwayRow(b0 + k); k++)
            {
                dMax = b0 + k;
            }
        }

        var gap = new List<(int X, int Z)>();
        var clear = new List<(int X, int Z)>();
        var corners = new List<(int X, int Z)>();
        for (int b = dMin; b <= dMax; b++)
            for (int a = gMin; a <= gMax; a++)
            {
                if (In(a, b))
                {
                    clear.Add(Cell(a, b));
                    if (bounded)
                    {
                        gap.Add(Cell(a, b));
                    }
                }
            }

        foreach (int side in new[] { -1, 1 })
        {
            int start = side < 0 ? dMin : dMax;
            for (int k = 1; k <= LaneDepth; k++)
            {
                int b = start + side * k;
                bool any = false, wall = true;
                for (int a = gMin; a <= gMax; a++)
                {
                    if (!In(a, b))
                    {
                        continue;
                    }

                    any = true;
                    wall &= S(a, b, foot) && S(a, b, foot + 1) && (foot + 2 >= h || S(a, b, foot + 2));
                }

                if (!any || wall)
                {
                    break; // the grid's edge (an entrance leads outside) or the far wall of a shallow room
                }

                for (int a = gMin; a <= gMax; a++)
                {
                    if (In(a, b))
                    {
                        clear.Add(Cell(a, b));
                    }
                }

                if (k == 1)
                {
                    foreach (int a in new[] { gMin - 1, gMax + 1 })
                    {
                        if (In(a, b))
                        {
                            corners.Add(Cell(a, b));
                        }
                    }
                }
            }
        }

        var keep = new List<(int X, int Z)>(clear);
        keep.AddRange(corners);
        return new DoorLane(foot, alongX, gMin, gMax, dMin, dMax, gap, clear, keep);
    }

    /// <summary>The lanes of every door marker in <paramref name="doors"/> (those that measure at all).</summary>
    public static List<DoorLane> DoorLanes(System.Func<int, int, int, ushort> get, int w, int h, int l, IEnumerable<Vector3i> doors)
    {
        var lanes = new List<DoorLane>();
        foreach (var d in doors)
        {
            if (DoorLaneAt(get, w, h, l, d.X, d.Y, d.Z) is { } lane)
            {
                lanes.Add(lane);
            }
        }

        return lanes;
    }

    /// <summary>A form a body walks onto at foot height: a flight of stairs, a ramp, or a plate lying on the floor.</summary>
    public static bool WalkableAtFoot(int descriptor) => ShapeCode.ShapeOf(descriptor) switch
    {
        (int)BlockShape.Stairs or (int)BlockShape.Ramp or (int)BlockShape.LowRamp => true,
        (int)BlockShape.Sheet or (int)BlockShape.Panel => true,
        _ => false,
    };

    /// <summary>
    /// The cells that block a door lane (#1901): any block in a <see cref="DoorLane.Clear"/> cell at foot or head
    /// height — at foot height a stair, a ramp or a floor plate still lets a body through. Empty when every lane of
    /// <paramref name="doors"/> is walkable. <paramref name="shapeAt"/> gives the packed shape descriptor of a cell.
    /// </summary>
    public static List<Vector3i> BlockedDoorLanes(System.Func<int, int, int, ushort> get, System.Func<int, int, int, int> shapeAt,
        int w, int h, int l, IEnumerable<Vector3i> doors)
    {
        var blocked = new List<Vector3i>();
        var seen = new HashSet<Vector3i>();
        foreach (var lane in DoorLanes(get, w, h, l, doors))
        {
            foreach (var (x, z) in lane.Clear)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    int y = lane.FootY + dy;
                    if (y >= h || get(x, y, z) == 0 || (dy == 0 && WalkableAtFoot(shapeAt(x, y, z))))
                    {
                        continue;
                    }

                    var cell = new Vector3i(x, y, z);
                    if (seen.Add(cell))
                    {
                        blocked.Add(cell);
                    }
                }
            }
        }

        return blocked;
    }

    /// <summary>The blocked door-lane cells of an authored template (its own blocks and door markers): what the
    /// editor refuses to export and what every shipped template is tested against (#1901).</summary>
    public static List<Vector3i> BlockedDoorLanes(StructureTemplate t)
    {
        var blocks = new Dictionary<Vector3i, TemplateCell>();
        var doors = new List<Vector3i>();
        foreach (var c in t.Cells)
        {
            var p = new Vector3i(c.X, c.Y, c.Z);
            if (c.Kind == "block")
            {
                blocks[p] = c;
            }
            else if (c.Kind == "marker" && IsDoorMarker(c.Id))
            {
                doors.Add(p);
            }
        }

        ushort Get(int x, int y, int z) => blocks.ContainsKey(new Vector3i(x, y, z)) ? (ushort)1 : (ushort)0;
        int ShapeAt(int x, int y, int z) => blocks.TryGetValue(new Vector3i(x, y, z), out var c) ? c.Shape : 0;
        return BlockedDoorLanes(Get, ShapeAt, t.Width, t.Height, t.Length, doors);
    }

    /// <summary>
    /// True when <paramref name="block"/> in the form <paramref name="descriptor"/> is a piece this palette's furnisher
    /// places: a table, chair, bench or counter of its materials, a store, a plant, a terminal, a workbench or a forge.
    /// Never a bed or a light, and never a plain cube of a furniture material (that is a wall or a deck). The existing-
    /// world cleanup (#1901) only ever removes such pieces.
    /// </summary>
    public static bool IsFurnishingPiece(Palette p, ushort block, int descriptor)
    {
        if (block == 0 || block == p.Bed || block == p.Light)
        {
            return false;
        }

        if (block == p.TableMaterial || block == p.ChairMaterial || block == p.Counter)
        {
            return ShapeCode.ShapeOf(descriptor) is (int)BlockShape.Table or (int)BlockShape.Chair or (int)BlockShape.Bench or (int)BlockShape.Slab;
        }

        return block == p.Storage || block == p.Plant || block == p.PlantAlt || block == p.Terminal || block == p.TerminalAlt
            || block == p.Workbench || block == p.Forge;
    }
}
