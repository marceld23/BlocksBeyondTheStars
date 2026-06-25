// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>An interactive point inside a station (vendor, mission board, heal-tank, hangar).</summary>
public readonly struct StationMarker
{
    public readonly string Type;
    public readonly Vector3i LocalPos;

    public StationMarker(string type, Vector3i localPos)
    {
        Type = type;
        LocalPos = localPos;
    }
}

/// <summary>One placed module room of a station (grid cell + assigned function).</summary>
public readonly struct StationModule
{
    public readonly string Type;          // hub / hangar / market / mission / medbay / quarters / corridor (+ _hall partners)
    public readonly Vector3i Grid;        // module-grid coordinate
    public readonly Vector3i Origin;      // world (local-to-station) min corner of the room

    public StationModule(string type, Vector3i grid, Vector3i origin)
    {
        Type = type;
        Grid = grid;
        Origin = origin;
    }
}

/// <summary>
/// A procedurally generated space station: a set of <b>module rooms assembled from building blocks
/// and joined together</b>, baked into one voxel structure. Because adjacent modules share walls in
/// a single solid hull enclosing hollow rooms, the <b>exterior matches the interior</b> by
/// construction. Stations stream/stamp like the ship — the blocks seen outside form the interior
/// walls.
/// </summary>
public sealed class StationStructure
{
    public int Width { get; }
    public int Height { get; }
    public int Length { get; }
    public string SizeTier { get; }

    // The tier's room dimensions (bigger tiers use bigger rooms) — server/tests read these instead
    // of assuming the old fixed 7×6×7 footprint.
    public int RoomW { get; }
    public int RoomH { get; }
    public int RoomL { get; }

    private readonly ushort[] _blocks; // [x*H*L + y*L + z]
    // Sparse per-cell modifiers (only authored templates populate these; procedural stations leave them null).
    private readonly Dictionary<int, (int Tint, int Glow)>? _mods;
    private readonly Dictionary<int, int>? _shapes;
    public IReadOnlyList<StationMarker> Markers { get; }
    public IReadOnlyList<StationModule> Modules { get; }

    internal StationStructure(int w, int h, int l, string tier, int roomW, int roomH, int roomL,
        ushort[] blocks, IReadOnlyList<StationMarker> markers, IReadOnlyList<StationModule> modules,
        Dictionary<int, (int Tint, int Glow)>? mods = null, Dictionary<int, int>? shapes = null)
    {
        Width = w;
        Height = h;
        Length = l;
        SizeTier = tier;
        RoomW = roomW;
        RoomH = roomH;
        RoomL = roomL;
        _blocks = blocks;
        _mods = mods;
        _shapes = shapes;
        Markers = markers;
        Modules = modules;
    }

    public ushort Get(int x, int y, int z) => _blocks[(x * Height + y) * Length + z];

    /// <summary>Per-cell dye/glow (0xRRGGBB each; 0 = none). Authored templates may set these.</summary>
    public (int Tint, int Glow) GetModifier(int x, int y, int z)
        => _mods != null && _mods.TryGetValue((x * Height + y) * Length + z, out var m) ? m : (0, 0);

    /// <summary>Per-cell packed shape+orientation (0 = plain cube). Authored templates may set this.</summary>
    public int GetShape(int x, int y, int z)
        => _shapes != null && _shapes.TryGetValue((x * Height + y) * Length + z, out var s) ? s : 0;

    public bool InBounds(int x, int y, int z)
        => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;
}

/// <summary>
/// Builds a <see cref="StationStructure"/> deterministically from a seed + size tier by laying out
/// <b>modules on a grid and joining them</b>: a central hub, then rooms grown outward (and stacked
/// for big stations) via a seeded random walk. Each module is a hollow room of <c>iron_wall</c>
/// with <c>glass</c> viewports; adjacent modules share a wall with a cut doorway, and stacked
/// modules get a floor shaft. Module count, room SIZE and floor count all scale with the tier
/// (small keeps the classic 7×6×7 rooms; large/huge/colossal grow them), and big tiers merge the
/// hangar (and at colossal the market) with a neighbour room into one double-size hall.
/// </summary>
public static class StationGenerator
{
    private const int MarginXZ = 3; // free border around the modules for exterior detail (panels/arms)
    private const int MarginTop = 3; // head-room above the top modules for antennae + domes

    /// <summary>(module count, floors, room W/H/L) per size tier. Small/medium keep today's compact
    /// rooms; the big tiers get more AND larger rooms (interior W-2 × H-2 × L-2).</summary>
    public static (int Modules, int Floors, int RoomW, int RoomH, int RoomL) Layout(string sizeTier) => sizeTier switch
    {
        "small" => (3, 1, 7, 6, 7),
        "large" => (10, 2, 9, 7, 9),
        "huge" => (16, 3, 11, 8, 11),
        "colossal" => (24, 4, 11, 8, 11),
        _ => (5, 1, 7, 6, 7), // "medium"
    };

    /// <summary>Builds a station structure from a hand-designed template (the editor export) instead of
    /// generating one — blocks become voxels, markers become interaction points. Unknown block keys are
    /// skipped (air). Guarantees a vendor + mission board so the station stays functional.</summary>
    public static StationStructure FromTemplate(StructureTemplate t, GameContent content)
    {
        int w = System.Math.Max(1, t.Width), h = System.Math.Max(1, t.Height), l = System.Math.Max(1, t.Length);
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int, int)>();
        var shapes = new Dictionary<int, int>();
        var markers = new List<StationMarker>();

        foreach (var cell in t.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.Z < 0 || cell.X >= w || cell.Y >= h || cell.Z >= l)
            {
                continue;
            }

            if (cell.Kind == "marker")
            {
                markers.Add(new StationMarker(cell.Id, new Vector3i(cell.X, cell.Y, cell.Z)));
            }
            else
            {
                ushort id = content.GetBlock(cell.Id)?.NumericId.Value ?? 0;
                if (id != 0)
                {
                    int idx = (cell.X * h + cell.Y) * l + cell.Z;
                    blocks[idx] = id;
                    if (cell.Tint != 0 || cell.Glow != 0) mods[idx] = (cell.Tint, cell.Glow);
                    if (cell.Shape != 0) shapes[idx] = cell.Shape;
                }
            }
        }

        var centre = new Vector3i(w / 2, 1, l / 2);
        if (!markers.Exists(m => m.Type == "vendor")) markers.Add(new StationMarker("vendor", centre));
        if (!markers.Exists(m => m.Type == "mission_board")) markers.Add(new StationMarker("mission_board", centre));
        if (!markers.Exists(m => m.Type == "hangar")) markers.Add(new StationMarker("hangar", centre));

        return new StationStructure(w, h, l, t.Tier, 7, 6, 7, blocks, markers, new List<StationModule>(), mods, shapes);
    }

    public static StationStructure Generate(string sizeTier, long seed, GameContent content)
    {
        var (moduleCount, floors, rw, rh, rl) = Layout(sizeTier);
        // Stable hash (string.GetHashCode is randomized per process) → deterministic across runs.
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash(sizeTier)));

        ushort hull = content.GetBlock("iron_wall")?.NumericId.Value ?? 0;
        ushort glass = content.GetBlock("glass")?.NumericId.Value ?? 0;

        // 1) Choose module grid cells: a central hub, then grow outward by a random walk; allow
        //    stacked floors for bigger stations. Each new cell attaches to an existing one.
        var cells = new List<Vector3i> { new(0, 0, 0) };
        var occupied = new HashSet<Vector3i> { new(0, 0, 0) };
        var dirs = new[]
        {
            new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0),
            new Vector3i(0, 0, 1), new Vector3i(0, 0, -1),
        };

        while (cells.Count < moduleCount)
        {
            var from = cells[rng.Next(cells.Count)];
            Vector3i next;
            if (floors > 1 && rng.NextDouble() < 0.25)
            {
                int gy = rng.Next(floors);
                next = new Vector3i(from.X, gy, from.Z);
            }
            else
            {
                var d = dirs[rng.Next(dirs.Length)];
                next = new Vector3i(from.X + d.X, from.Y, from.Z + d.Z);
            }

            if (next.Y < 0 || next.Y >= floors || !occupied.Add(next))
            {
                continue; // off-range or already placed → retry
            }

            cells.Add(next);
        }

        // 2) Normalise grid to non-negative, compute world dimensions (modules share 1-cell walls).
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var c in cells)
        {
            minX = System.Math.Min(minX, c.X); maxX = System.Math.Max(maxX, c.X);
            minZ = System.Math.Min(minZ, c.Z); maxZ = System.Math.Max(maxZ, c.Z);
            maxY = System.Math.Max(maxY, c.Y);
        }

        int sx = rw - 1, sy = rh - 1, sz = rl - 1;
        int gw = maxX - minX, gl = maxZ - minZ;
        // Reserve a border (MarginXZ) around the module footprint and head-room (MarginTop) above it, so
        // exterior greebles — solar panels, antennae, docking arms, domes — have somewhere to sit.
        int w = gw * sx + rw + 2 * MarginXZ;
        int h = maxY * sy + rh + MarginTop;
        int l = gl * sz + rl + 2 * MarginXZ;

        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        Vector3i OriginOf(Vector3i cell) => new((cell.X - minX) * sx + MarginXZ, cell.Y * sy, (cell.Z - minZ) * sz + MarginXZ);

        // 3) Assign module types: hub at the origin cell; one hangar on an outer ground cell; the
        //    rest market/mission/medbay/quarters/corridor.
        var placed = new List<StationModule>(cells.Count);
        var typeByCell = new Dictionary<Vector3i, string>();
        var palette = BuildTypePalette(cells.Count, rng);
        int paletteIdx = 0;
        // Hangar: a ground-floor cell other than the hub, with the smallest Z (outer edge → hull
        // mouth). Falls back to the last cell so it never collides with the hub at (0,0,0).
        var origin = new Vector3i(0, 0, 0);
        Vector3i hangarCell = cells[cells.Count - 1];
        bool hangarFound = false;
        bool hangarMouthFree = false;
        foreach (var c in cells)
        {
            if (c == origin || c.Y != 0)
            {
                continue;
            }

            // Prefer the outermost (smallest-Z) ground cell whose -Z face is actually open to space,
            // so the docking mouth normally looks out instead of into a neighbouring room.
            bool mouthFree = !occupied.Contains(new Vector3i(c.X, 0, c.Z - 1));
            bool better = !hangarFound
                || (mouthFree && !hangarMouthFree)
                || (mouthFree == hangarMouthFree && c.Z < hangarCell.Z);
            if (better)
            {
                hangarCell = c;
                hangarMouthFree = mouthFree;
            }

            hangarFound = true;
        }

        foreach (var c in cells)
        {
            string type;
            if (c == new Vector3i(0, 0, 0)) type = "hub";
            else if (c == hangarCell) type = "hangar";
            else type = palette[paletteIdx++ % palette.Count];
            typeByCell[c] = type;
        }

        // 3a) Double halls on the big tiers: merge the hangar (huge+) — and at colossal also the
        //     market — with a horizontal neighbour room into ONE hall (the shared wall vanishes).
        //     The partner keeps its place on the grid but is retyped "<type>_hall".
        bool bigTier = sizeTier is "huge" or "colossal";
        var mergedWalls = new HashSet<(Vector3i A, Vector3i B)>();
        if (bigTier)
        {
            TryMergeHall(typeByCell, occupied, dirs, hangarCell, "hangar", mergedWalls);
        }

        if (sizeTier == "colossal")
        {
            foreach (var kv in typeByCell)
            {
                if (kv.Value == "market")
                {
                    TryMergeHall(typeByCell, occupied, dirs, kv.Key, "market", mergedWalls);
                    break;
                }
            }
        }

        foreach (var c in cells)
        {
            placed.Add(new StationModule(typeByCell[c], c, OriginOf(c)));
        }

        // 3b) Per-module shape: the hub is a round command core; some rooms are octagonal "round"
        //     modules (chamfered corners) — the rest are plain boxes. Hall halves stay boxes so the
        //     merged space reads as one clean hall.
        var shapeByCell = new Dictionary<Vector3i, string>();
        foreach (var m in placed)
        {
            shapeByCell[m.Grid] = m.Type == "hub" ? "dome"
                : (m.Type is not ("hangar" or "hangar_hall" or "market_hall") && rng.NextDouble() < 0.35) ? "round"
                : "box";
        }

        // 4) Stamp each module as a hollow room (shell of hull + a glass viewport band); round/dome
        //    modules get chamfered corners so their interior + silhouette read as rounded.
        foreach (var m in placed)
        {
            StampRoom(Set, m.Origin, rw, rh, rl, hull, glass, shapeByCell[m.Grid] != "box");
        }

        // 5) Join modules: cut a doorway in every shared wall between adjacent placed cells (the
        //    standard 2-wide/3-tall airlock cut, so the sliding-door entities fit on every tier);
        //    merged hall pairs lose the whole wall instead. Stacked cells get a floor shaft.
        var doorCells = new List<Vector3i>();
        foreach (var m in placed)
        {
            foreach (var d in dirs)
            {
                var nb = new Vector3i(m.Grid.X + d.X, m.Grid.Y, m.Grid.Z + d.Z);
                if (!occupied.Contains(nb) || (d.X <= 0 && d.Z <= 0)) // cut each shared wall once
                {
                    continue;
                }

                if (mergedWalls.Contains((m.Grid, nb)) || mergedWalls.Contains((nb, m.Grid)))
                {
                    CutFullWall(Set, m.Origin, d, rw, rh, rl); // hall: the shared wall vanishes entirely
                    continue;
                }

                CutDoor(Set, m.Origin, d, rw, rh, rl);
                doorCells.Add(d.X > 0
                    ? new Vector3i(m.Origin.X + rw - 1, m.Origin.Y + 1, m.Origin.Z + rl / 2 - 1)
                    : new Vector3i(m.Origin.X + rw / 2 - 1, m.Origin.Y + 1, m.Origin.Z + rl - 1));
            }

            var up = new Vector3i(m.Grid.X, m.Grid.Y + 1, m.Grid.Z);
            if (occupied.Contains(up))
            {
                CutShaft(Set, m.Origin, rw, rh, rl);
            }
        }

        // 6) Hangar mouth: glaze the outer -Z wall of the hangar (hall) with an energy field — you see
        //    space through it, but it seals the hull so nobody can walk out into the void. The primary
        //    hangar always glazes its -Z wall (classic behaviour); a merged hall partner only joins in
        //    when its own -Z face is open space (else it would re-seal the merged wall opening).
        ushort field = content.GetBlock("force_field")?.NumericId.Value ?? glass;
        foreach (var m in placed)
        {
            bool mouth = m.Type == "hangar"
                || (m.Type == "hangar_hall" && !occupied.Contains(new Vector3i(m.Grid.X, m.Grid.Y, m.Grid.Z - 1)));
            if (mouth)
            {
                OpenHangar(Set, m.Origin, rw, rh, field);
            }
        }

        // 7) Markers per module type, at the room's interior floor centre.
        var markers = new List<StationMarker>();
        foreach (var m in placed)
        {
            var c = new Vector3i(m.Origin.X + rw / 2, m.Origin.Y + 1, m.Origin.Z + rl / 2);
            switch (m.Type)
            {
                case "hub": markers.Add(new StationMarker("spawn", c)); break; // enclosed centre = safe arrival point
                case "hangar": markers.Add(new StationMarker("hangar", c)); break;
                case "market": markers.Add(new StationMarker("vendor", c)); break;
                case "market_hall": markers.Add(new StationMarker("vendor", c)); break; // a second stall in the hall
                case "mission": markers.Add(new StationMarker("mission_board", c)); break;
                case "medbay": markers.Add(new StationMarker("heal_tank", c)); break;
                case "quarters": markers.Add(new StationMarker("quarters", c)); break;
            }
        }

        // Sci-fi sliding doors fill each cut doorway between modules.
        foreach (var dc in doorCells)
        {
            markers.Add(new StationMarker("door_slide", dc));
        }

        // 8) Furnish each module by type — consoles, counters, heal tanks, bunks, crates + lights — so
        //    rooms read as functional spaces instead of empty shells. All props stay relative to the
        //    room size and avoid the centre lines (door/shaft columns).
        ushort light = content.GetBlock("data_cache")?.NumericId.Value ?? glass;
        ushort tank = content.GetBlock("ice")?.NumericId.Value ?? glass;
        ushort dark = content.GetBlock("carbon")?.NumericId.Value ?? hull;
        ushort plant = content.GetBlock("flora_plant")?.NumericId.Value ?? 0;
        ushort Get(int x, int y, int z) =>
            (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l) ? blocks[(x * h + y) * l + z] : (ushort)0;
        foreach (var m in placed)
        {
            FurnishModule(Set, Get, m.Origin, m.Type, rw, rh, rl, hull, light, tank, dark, plant);
        }

        // 9) Exterior detail on exposed module faces — solar-panel wings, antennae, docking arms, and a
        //    command dome on the hub — so the hull reads as a real station, not stacked boxes.
        StampExterior(Set, placed, occupied, shapeByCell, rw, rh, rl, hull, glass, dark, light, rng);

        // Guarantee the essentials exist even on a tiny station (place them in the hub).
        var hub = placed[0];
        var hubFloor = new Vector3i(hub.Origin.X + 2, hub.Origin.Y + 1, hub.Origin.Z + 2);
        if (!markers.Exists(mk => mk.Type == "vendor")) markers.Add(new StationMarker("vendor", hubFloor));
        if (!markers.Exists(mk => mk.Type == "mission_board"))
            markers.Add(new StationMarker("mission_board", new Vector3i(hub.Origin.X + rw - 3, hub.Origin.Y + 1, hub.Origin.Z + 2)));

        return new StationStructure(w, h, l, sizeTier, rw, rh, rl, blocks, markers, placed);
    }

    /// <summary>Merges a room with one horizontal neighbour into a double hall: the partner cell is
    /// retyped "<paramref name="baseType"/>_hall" and the shared wall is recorded for full removal.
    /// Prefers an ±X partner (so a hangar pair shares the -Z outer face); falls back to ±Z. Skips
    /// the hub and already-special rooms; a room with no eligible neighbour just stays single.</summary>
    private static void TryMergeHall(Dictionary<Vector3i, string> typeByCell, HashSet<Vector3i> occupied,
        Vector3i[] dirs, Vector3i cell, string baseType, HashSet<(Vector3i A, Vector3i B)> mergedWalls)
    {
        Vector3i best = default;
        bool found = false;
        foreach (var d in dirs)
        {
            var nb = new Vector3i(cell.X + d.X, cell.Y, cell.Z + d.Z);
            if (!occupied.Contains(nb) || !typeByCell.TryGetValue(nb, out var t))
            {
                continue;
            }

            if (t is "hub" or "hangar" or "hangar_hall" or "market_hall")
            {
                continue; // keep the command core + other halls intact
            }

            bool sideways = d.X != 0; // an ±X partner keeps the pair's -Z faces aligned (hangar mouth)
            if (!found || (sideways && best.X == cell.X))
            {
                best = nb;
                found = true;
            }
        }

        if (found)
        {
            typeByCell[best] = baseType + "_hall";
            mergedWalls.Add((cell, best));
        }
    }

    /// <summary>Builds the pool of non-hub, non-hangar module types (always includes market + mission).</summary>
    private static List<string> BuildTypePalette(int cellCount, System.Random rng)
    {
        var pool = new List<string> { "market", "mission" };
        string[] extra = { "medbay", "quarters", "corridor", "market" };
        for (int i = pool.Count; i < cellCount; i++)
        {
            pool.Add(extra[rng.Next(extra.Length)]);
        }

        return pool;
    }

    /// <summary>Stamps a hollow room shell (hull walls + a glass viewport band) at a room origin. When
    /// <paramref name="round"/>, the corner columns are filled so the room is octagonal (a rounded
    /// silhouette + interior) instead of a plain box; the face mid-lines (doorways) stay clear. The
    /// viewport band grows with the room height (2 rows on classic rooms, 3 on tall ones).</summary>
    private static void StampRoom(System.Action<int, int, int, ushort> set, Vector3i o, int rw, int rh, int rl,
        ushort hull, ushort glass, bool round)
    {
        int viewTop = rh >= 8 ? 4 : 3; // a taller glazed band on the big rooms
        for (int x = 0; x < rw; x++)
            for (int y = 0; y < rh; y++)
                for (int z = 0; z < rl; z++)
                {
                    bool shell = x == 0 || x == rw - 1 || y == 0 || y == rh - 1 || z == 0 || z == rl - 1;
                    if (!shell)
                    {
                        set(o.X + x, o.Y + y, o.Z + z, 0); // hollow interior
                        continue;
                    }

                    bool sideWall = x == 0 || x == rw - 1 || z == 0 || z == rl - 1;
                    bool viewport = sideWall && y >= 2 && y <= viewTop && x > 0 && x < rw - 1 && z > 0 && z < rl - 1;
                    set(o.X + x, o.Y + y, o.Z + z, viewport ? glass : hull);
                }

        if (round)
        {
            // Fill interior corner columns → an octagonal room. Big rooms chamfer two cells deep so the
            // rounding still reads at scale; the diagonal fill (i + j ≤ depth + 1) leaves no dead pockets.
            int depth = rw >= 10 ? 2 : 1;
            for (int i = 1; i <= depth; i++)
                for (int j = 1; j <= depth + 1 - i; j++)
                    for (int y = 1; y <= rh - 2; y++)
                    {
                        set(o.X + i, o.Y + y, o.Z + j, hull);
                        set(o.X + i, o.Y + y, o.Z + rl - 1 - j, hull);
                        set(o.X + rw - 1 - i, o.Y + y, o.Z + j, hull);
                        set(o.X + rw - 1 - i, o.Y + y, o.Z + rl - 1 - j, hull);
                    }
        }
    }

    /// <summary>Places one decorative planter on a guaranteed-INTERIOR floor cell of the room — never in a
    /// wall corner. The cell must be at least two cells in from every outer wall, so all four of its horizontal
    /// neighbours are themselves interior floor (never the hull edge or open space); the plant therefore can
    /// never be seen through (into the void) or walked through out into space — the enclosure holds by
    /// construction, not by a per-neighbour check. The cell must also be empty (no furniture/light already there)
    /// and stand on a <paramref name="hull"/> floor block. Scans deterministically and places the first match;
    /// a room with no free interior cell simply gets no planter.</summary>
    private static void PlantInterior(System.Action<int, int, int, ushort> set, System.Func<int, int, int, ushort> get,
        Vector3i o, int rw, int rl, ushort hull, ushort plant)
    {
        if (plant == 0 || hull == 0)
        {
            return; // nothing to plant, or no hull to stand on
        }

        int cx = rw / 2, cz = rl / 2; // door/shaft centre columns to keep clear
        for (int lx = 2; lx <= rw - 3; lx++)
            for (int lz = 2; lz <= rl - 3; lz++)
            {
                if (lx == cx || lz == cz)
                {
                    continue; // never the door/shaft line
                }

                int wx = o.X + lx, wy = o.Y + 1, wz = o.Z + lz;
                // Empty cell (no furniture) standing on a hull floor block → a clean interior spot for a plant.
                if (get(wx, wy, wz) == 0 && get(wx, wy - 1, wz) == hull)
                {
                    set(wx, wy, wz, plant);
                    return;
                }
            }
    }

    /// <summary>
    /// Furnishes a module's interior by type with floor props + ceiling lights, all placed RELATIVE to
    /// the room size. Built-in fixtures (consoles, counters, bunks, lights) hug the walls/corners, never the
    /// centre lines (door/shaft columns); decorative planters go the opposite way — set well inside the room
    /// via <see cref="PlantInterior"/> so they never sit against the hull edge. Big rooms get denser furnishing
    /// (longer counters, more bunks, extra consoles, a centre ceiling light).
    /// </summary>
    private static void FurnishModule(System.Action<int, int, int, ushort> set, System.Func<int, int, int, ushort> get,
        Vector3i o, string type, int rw, int rh, int rl, ushort hull, ushort light, ushort tank, ushort dark, ushort plant)
    {
        int x0 = 1, x1 = rw - 2, z0 = 1, z1 = rl - 2; // interior bounds
        int cx = rw / 2, cz = rl / 2;                  // centre lines to keep clear
        int ceil = rh - 2;
        bool big = rw >= 9;

        // Corner ceiling lights in every room; big rooms add a centre light so halls stay bright.
        if (light != 0)
        {
            set(o.X + x0, o.Y + ceil, o.Z + z0, light);
            set(o.X + x1, o.Y + ceil, o.Z + z1, light);
            set(o.X + x0, o.Y + ceil, o.Z + z1, light);
            set(o.X + x1, o.Y + ceil, o.Z + z0, light);
            if (big)
            {
                set(o.X + cx, o.Y + ceil, o.Z + cz, light);
            }
        }

        switch (type)
        {
            case "hub": // a control console bank along the -X wall + status panels + an interior planter
                for (int z = z0 + 1; z <= z1 - 1; z += 2)
                {
                    if (z == cz || z == cz - 1) continue; // doorway line stays clear
                    set(o.X + x0, o.Y + 1, o.Z + z, dark);
                    set(o.X + x0, o.Y + 2, o.Z + z, light);
                }

                PlantInterior(set, get, o, rw, rl, hull, plant);
                break;

            case "market":
            case "market_hall": // a vendor counter along the +Z wall (+ a second row in big rooms)
                for (int x = x0 + 1; x <= x1 - 1; x += 2)
                {
                    if (x == cx || x == cx - 1) continue;
                    set(o.X + x, o.Y + 1, o.Z + z1, dark);
                }

                set(o.X + x0 + 1, o.Y + 2, o.Z + z1, light); // register / display
                if (big)
                {
                    for (int z = z0 + 1; z <= z1 - 2; z += 2)
                    {
                        if (z == cz || z == cz - 1) continue;
                        set(o.X + x1, o.Y + 1, o.Z + z, dark); // second stall row along +X
                    }
                }

                PlantInterior(set, get, o, rw, rl, hull, plant);
                break;

            case "medbay": // glowing heal tanks in the corners (two on big rooms)
                set(o.X + x0, o.Y + 1, o.Z + z0, tank);
                set(o.X + x0, o.Y + 2, o.Z + z0, tank);
                set(o.X + x0 + 1, o.Y + 1, o.Z + z0, light);
                if (big)
                {
                    set(o.X + x0, o.Y + 1, o.Z + z1, tank);
                    set(o.X + x0, o.Y + 2, o.Z + z1, tank);
                }

                break;

            case "quarters": // double bunks along the +X wall, one pair per 3 cells of depth + an interior planter
                for (int z = z0; z + 1 <= z1; z += 3)
                {
                    if (z <= cz && cz <= z + 1) continue; // shaft/door line
                    set(o.X + x1 - 1, o.Y + 1, o.Z + z, dark);
                    set(o.X + x1, o.Y + 1, o.Z + z, dark);
                    set(o.X + x1 - 1, o.Y + 1, o.Z + z + 1, dark);
                    set(o.X + x1, o.Y + 1, o.Z + z + 1, dark);
                }

                PlantInterior(set, get, o, rw, rl, hull, plant);
                break;

            case "hangar":
            case "hangar_hall": // supply crates stacked away from the mouth
                set(o.X + x0, o.Y + 1, o.Z + z1, dark);
                set(o.X + x0, o.Y + 2, o.Z + z1, dark);
                set(o.X + x1, o.Y + 1, o.Z + z1, dark);
                if (big)
                {
                    set(o.X + x1, o.Y + 2, o.Z + z1, dark);
                }

                break;

            default: // corridor / mission / misc — floor guide lights
                set(o.X + x0, o.Y + 1, o.Z + z1, light);
                set(o.X + x1, o.Y + 1, o.Z + z0, light);
                break;
        }
    }

    /// <summary>
    /// Adds exterior detail to the station hull: solar-panel wings on exposed side faces, antennae on the
    /// roofs of top modules, and a stepped command dome on the hub. Greebles sit in the reserved margin /
    /// the empty notches of the layout; hangar halls are skipped so the docking mouth stays clear.
    /// </summary>
    private static void StampExterior(System.Action<int, int, int, ushort> set, List<StationModule> placed,
        HashSet<Vector3i> occupied, Dictionary<Vector3i, string> shapeByCell, int rw, int rh, int rl,
        ushort hull, ushort glass, ushort dark, ushort light, System.Random rng)
    {
        var originByGrid = new Dictionary<Vector3i, Vector3i>();
        foreach (var m in placed)
        {
            originByGrid[m.Grid] = m.Origin;
        }

        foreach (var m in placed)
        {
            if (m.Type is "hangar" or "hangar_hall")
            {
                continue;
            }

            var o = m.Origin;
            var g = m.Grid;

            if (!occupied.Contains(new Vector3i(g.X + 1, g.Y, g.Z)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 1, 0, rw, rl, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X - 1, g.Y, g.Z)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, -1, 0, rw, rl, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X, g.Y, g.Z + 1)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 0, 1, rw, rl, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X, g.Y, g.Z - 1)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 0, -1, rw, rl, glass, dark);
            }

            bool top = !occupied.Contains(new Vector3i(g.X, g.Y + 1, g.Z));
            string shape = shapeByCell.TryGetValue(g, out var s) ? s : "box";
            if (top && m.Type == "hub")
            {
                Dome(set, o, rw, rh, rl, hull, light, glass: 0);  // solid command cupola
            }
            else if (top && shape == "round")
            {
                Dome(set, o, rw, rh, rl, hull, light, glass);     // glass observation dome
            }
            else if (top && rng.NextDouble() < 0.6)
            {
                Antenna(set, o.X + 1 + rng.Next(rw - 2), o.Y + rh, o.Z + 1 + rng.Next(rl - 2), dark, light);
            }

            // Connector conduits: a pipe along the roof to a +X / +Z neighbour, when both are top modules
            // (so the pipe runs over open roofs, not through a stacked module's floor).
            if (top && originByGrid.TryGetValue(new Vector3i(g.X + 1, g.Y, g.Z), out var nx)
                && !occupied.Contains(new Vector3i(g.X + 1, g.Y + 1, g.Z)))
            {
                for (int x = o.X + rw / 2; x <= nx.X + rw / 2; x++)
                {
                    set(x, o.Y + rh - 1, o.Z + 1, dark);
                }
            }

            if (top && originByGrid.TryGetValue(new Vector3i(g.X, g.Y, g.Z + 1), out var nz)
                && !occupied.Contains(new Vector3i(g.X, g.Y + 1, g.Z + 1)))
            {
                for (int z = o.Z + rl / 2; z <= nz.Z + rl / 2; z++)
                {
                    set(o.X + 1, o.Y + rh - 1, z, dark);
                }
            }
        }
    }

    /// <summary>A flat solar-panel wing (carbon frame + glass cells) jutting 2 blocks from a wall face.</summary>
    private static void SolarPanel(System.Action<int, int, int, ushort> set, Vector3i o, int dirX, int dirZ,
        int rw, int rl, ushort glass, ushort dark)
    {
        int span = (dirX != 0 ? rl : rw) - 2;
        for (int step = 1; step <= 2; step++)
            for (int s = 1; s <= span; s++)
                for (int y = o.Y + 2; y <= o.Y + 3; y++)
                {
                    int x, z;
                    if (dirX != 0)
                    {
                        x = (dirX > 0 ? o.X + rw - 1 : o.X) + dirX * step;
                        z = o.Z + s;
                    }
                    else
                    {
                        z = (dirZ > 0 ? o.Z + rl - 1 : o.Z) + dirZ * step;
                        x = o.X + s;
                    }

                    set(x, y, z, (s + step) % 2 == 0 ? glass : dark);
                }
    }

    /// <summary>A short antenna mast with a beacon tip on a module roof.</summary>
    private static void Antenna(System.Action<int, int, int, ushort> set, int x, int baseY, int z, ushort dark, ushort light)
    {
        for (int y = baseY; y < baseY + MarginTop; y++)
        {
            set(x, y, z, dark);
        }

        set(x, baseY + MarginTop - 1, z, light); // beacon tip
    }

    /// <summary>A stepped dome above a module's ceiling. <paramref name="glass"/> = 0 → a solid hull
    /// command cupola; otherwise a see-through glass observation dome. The apex is a glowing block.</summary>
    private static void Dome(System.Action<int, int, int, ushort> set, Vector3i o, int rw, int rh, int rl,
        ushort hull, ushort light, ushort glass)
    {
        ushort shell = glass != 0 ? glass : hull;
        for (int r = 0; r < MarginTop; r++)
        {
            int y = o.Y + rh + r;
            int x0 = o.X + 1 + r, x1 = o.X + rw - 2 - r, z0 = o.Z + 1 + r, z1 = o.Z + rl - 2 - r;
            if (x0 > x1 || z0 > z1)
            {
                break;
            }

            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool edge = x == x0 || x == x1 || z == z0 || z == z1;
                    if (edge || r == MarginTop - 1)
                    {
                        set(x, y, z, r == MarginTop - 1 ? light : shell); // glowing apex
                    }
                }
        }
    }

    /// <summary>Cuts the standard 2-wide, 3-tall doorway in the shared wall toward a +X or +Z neighbour
    /// — the same airlock cut on every tier, so the sliding-door entities fit big rooms too.</summary>
    private static void CutDoor(System.Action<int, int, int, ushort> set, Vector3i o, Vector3i dir, int rw, int rh, int rl)
    {
        int top = System.Math.Min(o.Y + 3, o.Y + rh - 2); // up to 3 tall, never the ceiling
        if (dir.X > 0)
        {
            int x = o.X + rw - 1, zc = o.Z + rl / 2;
            for (int y = o.Y + 1; y <= top; y++)
                for (int dz = -1; dz <= 0; dz++) set(x, y, zc + dz, 0);
        }
        else if (dir.Z > 0)
        {
            int z = o.Z + rl - 1, xc = o.X + rw / 2;
            for (int y = o.Y + 1; y <= top; y++)
                for (int dx = -1; dx <= 0; dx++) set(xc + dx, y, z, 0);
        }
    }

    /// <summary>Removes a merged hall pair's ENTIRE shared wall (interior span, frame kept), so the two
    /// rooms read as one double-size hall.</summary>
    private static void CutFullWall(System.Action<int, int, int, ushort> set, Vector3i o, Vector3i dir, int rw, int rh, int rl)
    {
        if (dir.X > 0)
        {
            int x = o.X + rw - 1;
            for (int y = o.Y + 1; y <= o.Y + rh - 2; y++)
                for (int z = o.Z + 1; z <= o.Z + rl - 2; z++)
                {
                    set(x, y, z, 0);
                }
        }
        else if (dir.Z > 0)
        {
            int z = o.Z + rl - 1;
            for (int y = o.Y + 1; y <= o.Y + rh - 2; y++)
                for (int x = o.X + 1; x <= o.X + rw - 2; x++)
                {
                    set(x, y, z, 0);
                }
        }
    }

    /// <summary>Cuts a floor shaft connecting to the module stacked above (2×2; 3×3 on big rooms).</summary>
    private static void CutShaft(System.Action<int, int, int, ushort> set, Vector3i o, int rw, int rh, int rl)
    {
        int size = rw >= 9 ? 3 : 2;
        int y = o.Y + rh - 1;
        for (int dx = 0; dx < size; dx++)
            for (int dz = 0; dz < size; dz++)
            {
                set(o.X + rw / 2 + dx - (size - 2), y, o.Z + rl / 2 + dz - (size - 2), 0);
            }
    }

    /// <summary>Glazes the outer -Z wall of a hangar room with an energy field (the docking mouth):
    /// transparent so space shows through, but solid so the player can't fall out into the void. Tall
    /// rooms get a taller mouth.</summary>
    private static void OpenHangar(System.Action<int, int, int, ushort> set, Vector3i o, int rw, int rh, ushort field)
    {
        int top = System.Math.Min(o.Y + (rh >= 8 ? 5 : 3), o.Y + rh - 2);
        for (int x = o.X + 1; x < o.X + rw - 1; x++)
            for (int y = o.Y + 1; y <= top; y++)
            {
                set(x, y, o.Z, field);
            }
    }
}
