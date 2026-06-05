using System.Collections.Generic;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.WorldGeneration;

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
    public readonly string Type;          // hub / hangar / market / mission / medbay / quarters / corridor
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

    private readonly ushort[] _blocks; // [x*H*L + y*L + z]
    public IReadOnlyList<StationMarker> Markers { get; }
    public IReadOnlyList<StationModule> Modules { get; }

    internal StationStructure(int w, int h, int l, string tier, ushort[] blocks,
        IReadOnlyList<StationMarker> markers, IReadOnlyList<StationModule> modules)
    {
        Width = w;
        Height = h;
        Length = l;
        SizeTier = tier;
        _blocks = blocks;
        Markers = markers;
        Modules = modules;
    }

    public ushort Get(int x, int y, int z) => _blocks[(x * Height + y) * Length + z];

    public bool InBounds(int x, int y, int z)
        => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;
}

/// <summary>
/// Builds a <see cref="StationStructure"/> deterministically from a seed + size tier by laying out
/// <b>modules on a grid and joining them</b>: a central hub, then rooms grown outward (and stacked
/// for big stations) via a seeded random walk. Each module is a hollow room of <c>iron_wall</c>
/// with <c>glass</c> viewports; adjacent modules share a wall with a cut doorway, and stacked
/// modules get a floor shaft. Module count + types scale with size (small → huge).
/// </summary>
public static class StationGenerator
{
    private const int RoomW = 7;
    private const int RoomH = 6;
    private const int RoomL = 7;
    private const int MarginXZ = 3; // free border around the modules for exterior detail (panels/arms)
    private const int MarginTop = 3; // head-room above the top modules for antennae + domes

    /// <summary>(module count, number of floors) per size tier.</summary>
    public static (int Modules, int Floors) Layout(string sizeTier) => sizeTier switch
    {
        "small" => (3, 1),
        "large" => (9, 2),
        "huge" => (14, 3),
        _ => (5, 1), // "medium"
    };

    /// <summary>Builds a station structure from a hand-designed template (the editor export) instead of
    /// generating one — blocks become voxels, markers become interaction points. Unknown block keys are
    /// skipped (air). Guarantees a vendor + mission board so the station stays functional.</summary>
    public static StationStructure FromTemplate(StructureTemplate t, GameContent content)
    {
        int w = System.Math.Max(1, t.Width), h = System.Math.Max(1, t.Height), l = System.Math.Max(1, t.Length);
        var blocks = new ushort[w * h * l];
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
                    blocks[(cell.X * h + cell.Y) * l + cell.Z] = id;
                }
            }
        }

        var centre = new Vector3i(w / 2, 1, l / 2);
        if (!markers.Exists(m => m.Type == "vendor")) markers.Add(new StationMarker("vendor", centre));
        if (!markers.Exists(m => m.Type == "mission_board")) markers.Add(new StationMarker("mission_board", centre));
        if (!markers.Exists(m => m.Type == "hangar")) markers.Add(new StationMarker("hangar", centre));

        return new StationStructure(w, h, l, t.Tier, blocks, markers, new List<StationModule>());
    }

    public static StationStructure Generate(string sizeTier, long seed, GameContent content)
    {
        var (moduleCount, floors) = Layout(sizeTier);
        // Stable hash (string.GetHashCode is randomized per process) → deterministic across runs.
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash(sizeTier)));

        ushort hull = content.GetBlock("iron_wall")?.NumericId.Value ?? 0;
        ushort glass = content.GetBlock("glass")?.NumericId.Value ?? 0;

        // 1) Choose module grid cells: a central hub, then grow outward by a random walk; allow a
        //    second/third floor for bigger stations. Each new cell attaches to an existing one.
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

        int sx = RoomW - 1, sy = RoomH - 1, sz = RoomL - 1;
        int gw = maxX - minX, gl = maxZ - minZ;
        // Reserve a border (MarginXZ) around the module footprint and head-room (MarginTop) above it, so
        // exterior greebles — solar panels, antennae, docking arms, domes — have somewhere to sit.
        int w = gw * sx + RoomW + 2 * MarginXZ;
        int h = maxY * sy + RoomH + MarginTop;
        int l = gl * sz + RoomL + 2 * MarginXZ;

        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        Vector3i OriginOf(Vector3i cell) => new((cell.X - minX) * sx + MarginXZ, cell.Y * sy, (cell.Z - minZ) * sz + MarginXZ);

        // 3) Assign module types: hub at the most-connected cell; one hangar on an outer ground
        //    cell; the rest market/mission/medbay/quarters/corridor.
        var placed = new List<StationModule>(cells.Count);
        var typeByCell = new Dictionary<Vector3i, string>();
        var palette = BuildTypePalette(cells.Count, rng);
        int paletteIdx = 0;
        // Hangar: a ground-floor cell other than the hub, with the smallest Z (outer edge → hull
        // mouth). Falls back to the last cell so it never collides with the hub at (0,0,0).
        var origin = new Vector3i(0, 0, 0);
        Vector3i hangarCell = cells[cells.Count - 1];
        bool hangarFound = false;
        foreach (var c in cells)
        {
            if (c == origin || c.Y != 0)
            {
                continue;
            }

            if (!hangarFound || c.Z < hangarCell.Z)
            {
                hangarCell = c;
                hangarFound = true;
            }
        }

        foreach (var c in cells)
        {
            string type;
            if (c == new Vector3i(0, 0, 0)) type = "hub";
            else if (c == hangarCell) type = "hangar";
            else type = palette[paletteIdx++ % palette.Count];
            typeByCell[c] = type;
            placed.Add(new StationModule(type, c, OriginOf(c)));
        }

        // 3b) Per-module shape: the hub is a round command core; some rooms are octagonal "round"
        //     modules (chamfered corners) — the rest are plain boxes. Drives both the room stamp and
        //     the roof cap (solid command dome / glass observation cupola / antenna).
        var shapeByCell = new Dictionary<Vector3i, string>();
        foreach (var m in placed)
        {
            shapeByCell[m.Grid] = m.Type == "hub" ? "dome"
                : (m.Type != "hangar" && rng.NextDouble() < 0.35) ? "round"
                : "box";
        }

        // 4) Stamp each module as a hollow room (shell of hull + a glass viewport band); round/dome
        //    modules get chamfered corners so their interior + silhouette read as rounded.
        foreach (var m in placed)
        {
            StampRoom(Set, m.Origin, hull, glass, shapeByCell[m.Grid] != "box");
        }

        // 5) Join modules: cut a doorway in every shared wall between adjacent placed cells; cut a
        //    floor shaft between vertically stacked cells.
        foreach (var m in placed)
        {
            foreach (var d in dirs)
            {
                var nb = new Vector3i(m.Grid.X + d.X, m.Grid.Y, m.Grid.Z + d.Z);
                if (occupied.Contains(nb) && (d.X > 0 || d.Z > 0)) // cut each shared wall once
                {
                    CutDoor(Set, m.Origin, d);
                }
            }

            var up = new Vector3i(m.Grid.X, m.Grid.Y + 1, m.Grid.Z);
            if (occupied.Contains(up))
            {
                CutShaft(Set, m.Origin);
            }
        }

        // 6) Hangar mouth: open the outer -Z wall of the hangar module to space.
        var hangar = placed.Find(p => p.Type == "hangar");
        OpenHangar(Set, hangar.Origin);

        // 7) Markers per module type, at the room's interior floor centre.
        var markers = new List<StationMarker>();
        foreach (var m in placed)
        {
            var c = new Vector3i(m.Origin.X + RoomW / 2, m.Origin.Y + 1, m.Origin.Z + RoomL / 2);
            switch (m.Type)
            {
                case "hub": markers.Add(new StationMarker("spawn", c)); break; // enclosed centre = safe arrival point
                case "hangar": markers.Add(new StationMarker("hangar", c)); break;
                case "market": markers.Add(new StationMarker("vendor", c)); break;
                case "mission": markers.Add(new StationMarker("mission_board", c)); break;
                case "medbay": markers.Add(new StationMarker("heal_tank", c)); break;
                case "quarters": markers.Add(new StationMarker("quarters", c)); break;
            }
        }

        // 8) Furnish each module by type — consoles, counters, heal tanks, bunks, crates + lights — so
        //    rooms read as functional spaces instead of empty shells. All props avoid the centre column
        //    (x=3,z=3): the door/shaft lines and the hollow-room invariant.
        ushort light = content.GetBlock("data_cache")?.NumericId.Value ?? glass;
        ushort tank = content.GetBlock("ice")?.NumericId.Value ?? glass;
        ushort dark = content.GetBlock("carbon")?.NumericId.Value ?? hull;
        ushort plant = content.GetBlock("flora_plant")?.NumericId.Value ?? 0;
        foreach (var m in placed)
        {
            FurnishModule(Set, m.Origin, m.Type, hull, light, tank, dark, plant);
        }

        // 9) Exterior detail on exposed module faces — solar-panel wings, antennae, docking arms, and a
        //    command dome on the hub — so the hull reads as a real station, not stacked boxes.
        StampExterior(Set, placed, occupied, shapeByCell, hull, glass, dark, light, rng);

        // Guarantee the essentials exist even on a tiny station (place them in the hub).
        var hub = placed[0];
        var hubFloor = new Vector3i(hub.Origin.X + 2, hub.Origin.Y + 1, hub.Origin.Z + 2);
        if (!markers.Exists(mk => mk.Type == "vendor")) markers.Add(new StationMarker("vendor", hubFloor));
        if (!markers.Exists(mk => mk.Type == "mission_board"))
            markers.Add(new StationMarker("mission_board", new Vector3i(hub.Origin.X + RoomW - 3, hub.Origin.Y + 1, hub.Origin.Z + 2)));

        return new StationStructure(w, h, l, sizeTier, blocks, markers, placed);
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
    /// <paramref name="round"/>, the four corner columns are filled so the room is octagonal (a rounded
    /// silhouette + interior) instead of a plain box; the face mid-lines (doorways) stay clear.</summary>
    private static void StampRoom(System.Action<int, int, int, ushort> set, Vector3i o, ushort hull, ushort glass, bool round)
    {
        for (int x = 0; x < RoomW; x++)
        for (int y = 0; y < RoomH; y++)
        for (int z = 0; z < RoomL; z++)
        {
            bool shell = x == 0 || x == RoomW - 1 || y == 0 || y == RoomH - 1 || z == 0 || z == RoomL - 1;
            if (!shell)
            {
                set(o.X + x, o.Y + y, o.Z + z, 0); // hollow interior
                continue;
            }

            bool sideWall = x == 0 || x == RoomW - 1 || z == 0 || z == RoomL - 1;
            bool viewport = sideWall && y == 3 && x > 0 && x < RoomW - 1 && z > 0 && z < RoomL - 1;
            set(o.X + x, o.Y + y, o.Z + z, viewport ? glass : hull);
        }

        if (round)
        {
            // Fill the interior corner columns → an octagonal room (corners are 1 in from the shell).
            int[,] corners = { { 1, 1 }, { 1, RoomL - 2 }, { RoomW - 2, 1 }, { RoomW - 2, RoomL - 2 } };
            for (int i = 0; i < 4; i++)
            for (int y = 1; y <= RoomH - 2; y++)
            {
                set(o.X + corners[i, 0], o.Y + y, o.Z + corners[i, 1], hull);
            }
        }
    }

    /// <summary>
    /// Furnishes a module's interior by type with floor props + ceiling lights. Everything is placed at
    /// the corners/edges (columns x∈{1,2,4,5}, z∈{1,2,4,5} — never the centre x=3/z=3), so it never blocks
    /// a doorway, the vertical shaft, or the hollow-centre cell the room relies on.
    /// </summary>
    private static void FurnishModule(System.Action<int, int, int, ushort> set, Vector3i o, string type,
        ushort hull, ushort light, ushort tank, ushort dark, ushort plant)
    {
        // Four corner ceiling lights in every room (brighter, livelier interiors).
        if (light != 0)
        {
            set(o.X + 1, o.Y + RoomH - 2, o.Z + 1, light);
            set(o.X + RoomW - 2, o.Y + RoomH - 2, o.Z + RoomL - 2, light);
            set(o.X + 1, o.Y + RoomH - 2, o.Z + RoomL - 2, light);
            set(o.X + RoomW - 2, o.Y + RoomH - 2, o.Z + 1, light);
        }

        switch (type)
        {
            case "hub": // a control console bank along the -X wall + a status panel + a planter
                set(o.X + 1, o.Y + 1, o.Z + 2, dark);
                set(o.X + 1, o.Y + 1, o.Z + 4, dark);
                set(o.X + 1, o.Y + 2, o.Z + 2, light);
                set(o.X + 1, o.Y + 2, o.Z + 4, light);
                if (plant != 0) set(o.X + 5, o.Y + 1, o.Z + 5, plant);
                break;

            case "market": // a vendor counter along the +Z wall + a planter
                set(o.X + 2, o.Y + 1, o.Z + 5, dark);
                set(o.X + 4, o.Y + 1, o.Z + 5, dark);
                set(o.X + 2, o.Y + 2, o.Z + 5, light); // register / display
                if (plant != 0) set(o.X + 5, o.Y + 1, o.Z + 1, plant);
                break;

            case "medbay": // a glowing heal tank in a corner
                set(o.X + 1, o.Y + 1, o.Z + 1, tank);
                set(o.X + 1, o.Y + 2, o.Z + 1, tank);
                set(o.X + 2, o.Y + 1, o.Z + 1, light);
                break;

            case "quarters": // two double bunks against the +X wall (2 wide x 2 long each) + a planter
                set(o.X + 4, o.Y + 1, o.Z + 1, dark);
                set(o.X + 5, o.Y + 1, o.Z + 1, dark);
                set(o.X + 4, o.Y + 1, o.Z + 2, dark);
                set(o.X + 5, o.Y + 1, o.Z + 2, dark);
                set(o.X + 4, o.Y + 1, o.Z + 4, dark);
                set(o.X + 5, o.Y + 1, o.Z + 4, dark);
                set(o.X + 4, o.Y + 1, o.Z + 5, dark);
                set(o.X + 5, o.Y + 1, o.Z + 5, dark);
                if (plant != 0) set(o.X + 1, o.Y + 1, o.Z + 5, plant);
                break;

            case "hangar": // supply crates stacked in the corners by the mouth
                set(o.X + 1, o.Y + 1, o.Z + 1, dark);
                set(o.X + 1, o.Y + 2, o.Z + 1, dark);
                set(o.X + 5, o.Y + 1, o.Z + 1, dark);
                break;

            default: // corridor / misc — floor guide lights
                set(o.X + 1, o.Y + 1, o.Z + 5, light);
                set(o.X + 5, o.Y + 1, o.Z + 1, light);
                break;
        }
    }

    /// <summary>
    /// Adds exterior detail to the station hull: solar-panel wings on exposed side faces, antennae on the
    /// roofs of top modules, and a stepped command dome on the hub. Greebles sit in the reserved margin /
    /// the empty notches of the layout; the hangar is skipped so its docking mouth stays clear.
    /// </summary>
    private static void StampExterior(System.Action<int, int, int, ushort> set, List<StationModule> placed,
        HashSet<Vector3i> occupied, Dictionary<Vector3i, string> shapeByCell, ushort hull, ushort glass,
        ushort dark, ushort light, System.Random rng)
    {
        var originByGrid = new Dictionary<Vector3i, Vector3i>();
        foreach (var m in placed)
        {
            originByGrid[m.Grid] = m.Origin;
        }

        foreach (var m in placed)
        {
            if (m.Type == "hangar")
            {
                continue;
            }

            var o = m.Origin;
            var g = m.Grid;

            if (!occupied.Contains(new Vector3i(g.X + 1, g.Y, g.Z)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 1, 0, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X - 1, g.Y, g.Z)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, -1, 0, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X, g.Y, g.Z + 1)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 0, 1, glass, dark);
            }

            if (!occupied.Contains(new Vector3i(g.X, g.Y, g.Z - 1)) && rng.NextDouble() < 0.55)
            {
                SolarPanel(set, o, 0, -1, glass, dark);
            }

            bool top = !occupied.Contains(new Vector3i(g.X, g.Y + 1, g.Z));
            string shape = shapeByCell.TryGetValue(g, out var s) ? s : "box";
            if (top && m.Type == "hub")
            {
                Dome(set, o, hull, light, glass: 0);              // solid command cupola
            }
            else if (top && shape == "round")
            {
                Dome(set, o, hull, light, glass);                // glass observation dome
            }
            else if (top && rng.NextDouble() < 0.6)
            {
                Antenna(set, o.X + 1 + rng.Next(RoomW - 2), o.Y + RoomH, o.Z + 1 + rng.Next(RoomL - 2), dark, light);
            }

            // Connector conduits: a pipe along the roof to a +X / +Z neighbour, when both are top modules
            // (so the pipe runs over open roofs, not through a stacked module's floor).
            if (top && originByGrid.TryGetValue(new Vector3i(g.X + 1, g.Y, g.Z), out var nx)
                && !occupied.Contains(new Vector3i(g.X + 1, g.Y + 1, g.Z)))
            {
                for (int x = o.X + RoomW / 2; x <= nx.X + RoomW / 2; x++)
                {
                    set(x, o.Y + RoomH - 1, o.Z + 1, dark);
                }
            }

            if (top && originByGrid.TryGetValue(new Vector3i(g.X, g.Y, g.Z + 1), out var nz)
                && !occupied.Contains(new Vector3i(g.X, g.Y + 1, g.Z + 1)))
            {
                for (int z = o.Z + RoomL / 2; z <= nz.Z + RoomL / 2; z++)
                {
                    set(o.X + 1, o.Y + RoomH - 1, z, dark);
                }
            }
        }
    }

    /// <summary>A flat solar-panel wing (carbon frame + glass cells) jutting 2 blocks from a wall face.</summary>
    private static void SolarPanel(System.Action<int, int, int, ushort> set, Vector3i o, int dirX, int dirZ, ushort glass, ushort dark)
    {
        for (int step = 1; step <= 2; step++)
        for (int s = 1; s <= RoomL - 2; s++)
        for (int y = o.Y + 2; y <= o.Y + 3; y++)
        {
            int x, z;
            if (dirX != 0)
            {
                x = (dirX > 0 ? o.X + RoomW - 1 : o.X) + dirX * step;
                z = o.Z + s;
            }
            else
            {
                z = (dirZ > 0 ? o.Z + RoomL - 1 : o.Z) + dirZ * step;
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
    private static void Dome(System.Action<int, int, int, ushort> set, Vector3i o, ushort hull, ushort light, ushort glass)
    {
        ushort shell = glass != 0 ? glass : hull;
        for (int r = 0; r < MarginTop; r++)
        {
            int y = o.Y + RoomH + r;
            int x0 = o.X + 1 + r, x1 = o.X + RoomW - 2 - r, z0 = o.Z + 1 + r, z1 = o.Z + RoomL - 2 - r;
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

    /// <summary>Cuts a 2-wide, 3-tall doorway in the shared wall toward a +X or +Z neighbour (a 1-wide /
    /// 2-tall opening was too tight for the player to pass through).</summary>
    private static void CutDoor(System.Action<int, int, int, ushort> set, Vector3i o, Vector3i dir)
    {
        int top = System.Math.Min(o.Y + 3, o.Y + RoomH - 2); // up to 3 tall, never the ceiling
        if (dir.X > 0)
        {
            int x = o.X + RoomW - 1, zc = o.Z + RoomL / 2;
            for (int y = o.Y + 1; y <= top; y++)
            for (int dz = -1; dz <= 0; dz++) set(x, y, zc + dz, 0);
        }
        else if (dir.Z > 0)
        {
            int z = o.Z + RoomL - 1, xc = o.X + RoomW / 2;
            for (int y = o.Y + 1; y <= top; y++)
            for (int dx = -1; dx <= 0; dx++) set(xc + dx, y, z, 0);
        }
    }

    /// <summary>Cuts a 2×2 shaft in a room's ceiling to connect to the module stacked above.</summary>
    private static void CutShaft(System.Action<int, int, int, ushort> set, Vector3i o)
    {
        int y = o.Y + RoomH - 1;
        for (int dx = 0; dx <= 1; dx++)
        for (int dz = 0; dz <= 1; dz++)
        {
            set(o.X + RoomW / 2 + dx, y, o.Z + RoomL / 2 + dz, 0);
        }
    }

    /// <summary>Opens the outer -Z wall of the hangar module to space (the docking mouth).</summary>
    private static void OpenHangar(System.Action<int, int, int, ushort> set, Vector3i o)
    {
        for (int x = o.X + 1; x < o.X + RoomW - 1; x++)
        for (int y = o.Y + 1; y <= o.Y + 3 && y < o.Y + RoomH - 1; y++)
        {
            set(x, y, o.Z, 0);
        }
    }
}
