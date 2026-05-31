using System.Collections.Generic;
using Spacecraft.Shared.Content;
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

    /// <summary>(module count, number of floors) per size tier.</summary>
    public static (int Modules, int Floors) Layout(string sizeTier) => sizeTier switch
    {
        "small" => (3, 1),
        "large" => (9, 2),
        "huge" => (14, 3),
        _ => (5, 1), // "medium"
    };

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
        int w = gw * sx + RoomW;
        int h = maxY * sy + RoomH;
        int l = gl * sz + RoomL;

        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        Vector3i OriginOf(Vector3i cell) => new((cell.X - minX) * sx, cell.Y * sy, (cell.Z - minZ) * sz);

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

        // 4) Stamp each module as a hollow room (shell of hull + a glass viewport band).
        foreach (var m in placed)
        {
            StampRoom(Set, m.Origin, hull, glass);
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
                case "hangar": markers.Add(new StationMarker("hangar", c)); break;
                case "market": markers.Add(new StationMarker("vendor", c)); break;
                case "mission": markers.Add(new StationMarker("mission_board", c)); break;
                case "medbay": markers.Add(new StationMarker("heal_tank", c)); break;
                case "quarters": markers.Add(new StationMarker("quarters", c)); break;
            }
        }

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

    /// <summary>Stamps a hollow room shell (hull walls + a glass viewport band) at a room origin.</summary>
    private static void StampRoom(System.Action<int, int, int, ushort> set, Vector3i o, ushort hull, ushort glass)
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
    }

    /// <summary>Cuts a 1-wide, 2-tall doorway in the shared wall toward a +X or +Z neighbour.</summary>
    private static void CutDoor(System.Action<int, int, int, ushort> set, Vector3i o, Vector3i dir)
    {
        if (dir.X > 0)
        {
            int x = o.X + RoomW - 1;
            for (int y = o.Y + 1; y <= o.Y + 2; y++) set(x, y, o.Z + RoomL / 2, 0);
        }
        else if (dir.Z > 0)
        {
            int z = o.Z + RoomL - 1;
            for (int y = o.Y + 1; y <= o.Y + 2; y++) set(o.X + RoomW / 2, y, z, 0);
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
