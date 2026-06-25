// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>An interactive / spawn point inside a settlement (vendor, mission board, NPC, ...).</summary>
public readonly struct SettlementMarker
{
    public readonly string Type;       // vendor / mission_board / npc / loot
    public readonly Vector3i LocalPos;

    public SettlementMarker(string type, Vector3i localPos)
    {
        Type = type;
        LocalPos = localPos;
    }
}

/// <summary>
/// A procedurally generated planet-surface settlement: several <b>buildings assembled from blocks
/// and laid out on a plot grid</b> (with streets between), baked into one local voxel structure to
/// be stamped onto the terrain. Two tiers — <b>primitive villages</b> (single-storey huts in the
/// biome's material) and <b>modern towns</b> (multi-storey iron/glass buildings) — plus a
/// <b>ruined</b> variant (a decay pass collapses parts, removes NPCs, leaves loot). Inhabitants are
/// <b>human or alien</b> per settlement.
///
/// Buildings vary per instance — footprint, height, roof (flat parapet / pitched), door side and an
/// accent band differ from house to house; the settlement also gets a <b>central feature</b> (well /
/// plaza / monument), <b>street paths</b>, scattered <b>lamps + gardens</b>, and (sometimes) a
/// <b>perimeter fence</b>. Alien settlements are themed with alien materials + denser growth.
/// </summary>
public sealed class SettlementStructure
{
    public int Width { get; }
    public int Height { get; }
    public int Length { get; }
    public string Tier { get; }       // "village" | "town"
    public bool Ruined { get; }
    public string Inhabitant { get; } // "human" | "alien" (empty when ruined)

    private readonly ushort[] _blocks; // [x*H*L + y*L + z]
    // Sparse per-cell modifiers (only authored templates populate these; procedural settlements leave them null).
    private readonly Dictionary<int, (int Tint, int Glow)>? _mods;
    private readonly Dictionary<int, int>? _shapes;
    public IReadOnlyList<SettlementMarker> Markers { get; }
    public int BuildingCount { get; }

    internal SettlementStructure(int w, int h, int l, string tier, bool ruined, string inhabitant,
        ushort[] blocks, IReadOnlyList<SettlementMarker> markers, int buildingCount,
        Dictionary<int, (int Tint, int Glow)>? mods = null, Dictionary<int, int>? shapes = null)
    {
        Width = w;
        Height = h;
        Length = l;
        Tier = tier;
        Ruined = ruined;
        Inhabitant = inhabitant;
        _blocks = blocks;
        _mods = mods;
        _shapes = shapes;
        Markers = markers;
        BuildingCount = buildingCount;
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
/// Builds a <see cref="SettlementStructure"/> deterministically from a seed. Lays out buildings on
/// a plot grid (streets between them), each a hollow room with a door + windows. Villages are
/// single-storey in the biome's surface material; towns are multi-storey iron/glass. One building
/// hosts the <b>market vendor</b>, one the <b>mission board</b>, the rest are dwellings with an
/// <b>NPC</b> spawn (human or alien). The <b>ruined</b> variant runs a decay pass (drops blocks,
/// no NPCs, scatters loot).
/// </summary>
public static class SettlementGenerator
{
    private const int Plot = 8;      // plot stride (building + street margin)
    private const int Building = 6;  // max building footprint (Building×Building)
    private const int FloorH = 4;    // height of one storey
    private const int RoofCap = 3;   // reserved head-room above the top storey for roofs

    /// <summary>(plot columns, plot rows, floors) base per tier (size is jittered per instance). Four
    /// size tiers from tiny hamlets to sprawling cities; hamlet/village are village-style (biome material,
    /// single storey), town/city are town-style (iron/glass, multi-storey).</summary>
    public static (int Cols, int Rows, int Floors) Layout(string tier) => tier switch
    {
        "city" => (4, 4, 4),
        "town" => (3, 3, 2),
        "hamlet" => (1, 2, 1),
        _ => (2, 2, 1), // "village"
    };

    /// <summary>Town-style settlements (modern iron/glass, multi-storey) vs primitive village-style.</summary>
    private static bool IsTownStyle(string tier) => tier == "town" || tier == "city";

    /// <summary>Builds a settlement structure from a hand-designed template (the editor export) — blocks
    /// become voxels, markers become vendor/mission_board/npc points. Templates are intact (not ruined).</summary>
    public static SettlementStructure FromTemplate(StructureTemplate t, GameContent content)
    {
        int w = System.Math.Max(1, t.Width), h = System.Math.Max(1, t.Height), l = System.Math.Max(1, t.Length);
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int, int)>();
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();
        int buildings = 0;

        foreach (var cell in t.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.Z < 0 || cell.X >= w || cell.Y >= h || cell.Z >= l)
            {
                continue;
            }

            if (cell.Kind == "marker")
            {
                markers.Add(new SettlementMarker(cell.Id, new Vector3i(cell.X, cell.Y, cell.Z)));
                if (cell.Id == "npc" || cell.Id == "vendor") buildings++;
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

        if (!markers.Exists(m => m.Type == "vendor")) markers.Add(new SettlementMarker("vendor", new Vector3i(w / 2, 1, l / 2)));

        return new SettlementStructure(w, h, l, t.Tier, ruined: false, inhabitant: "human", blocks, markers, System.Math.Max(1, buildings), mods, shapes);
    }

    public static SettlementStructure Generate(string tier, bool ruined, long seed, string biomeSurfaceBlock, GameContent content)
    {
        bool town = IsTownStyle(tier);
        var (baseCols, baseRows, baseFloors) = Layout(tier);

        // Use a stable hash (not string.GetHashCode, which is randomized per process) so the build
        // is genuinely deterministic from the seed across runs.
        int tierHash = (int)WorldGenerator.StableHash(tier);
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ tierHash ^ (ruined ? 0x5111 : 0)));

        // Per-instance size jitter so two same-tier settlements differ in scale.
        int cols = baseCols + rng.Next(0, 2);
        int rows = baseRows + rng.Next(0, 2);
        int floors = town ? baseFloors + rng.Next(0, 2) : 1; // towns 2..3 storeys; villages stay single-storey

        string inhabitant = ruined ? string.Empty : (rng.NextDouble() < 0.5 ? "human" : "alien");
        bool alien = inhabitant == "alien";

        // Materials: a town is iron/glass; a village uses the biome's surface block (mud/stone/…).
        // The accent + lamp + garden materials theme the settlement (alien worlds look different).
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        bool desert = biomeSurfaceBlock == "sand";
        ushort wall = town ? B("iron_wall") : B(biomeSurfaceBlock, B("stone"));
        ushort glass = B("glass");
        ushort ladder = B("ladder");
        // Gardens use the biome's own flora species (alien settlements keep their crystal growths).
        string biomeFloraKey = biomeSurfaceBlock switch
        {
            "sand" => "flora_cactus",
            "ice" => "flora_frostflower",
            "mud" => "flora_mushroom",
            "grass" => "flora_fern",
            "basalt" => "flora_emberbloom",
            _ => "flora_plant",
        };
        ushort flora = alien ? B("flora_crystal", B("flora_plant")) : B(biomeFloraKey, B("flora_plant"));
        // Paths take on the ground material of the biome (sandy tracks, icy lanes, …).
        ushort path = town
            ? B("carbon", B("stone"))
            : desert ? B("sand", B("stone"))
            : biomeSurfaceBlock == "ice" ? B("ice", B("stone"))
            : B("stone", wall);
        ushort accent = alien ? B("crystal", B("carbon")) : (town ? B("glass") : B("carbon", B("stone")));
        ushort lamp = B("data_cache", glass);
        ushort fence = alien ? B("crystal", wall) : wall;

        int w = cols * Plot + 1;
        int l = rows * Plot + 1;
        int h = floors * FloorH + 1 + RoofCap;
        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        var markers = new List<SettlementMarker>();

        // Street paths along the plot margins (a simple grid of lanes on the ground).
        if (path != 0)
        {
            StampPaths(Set, w, l, cols, rows, path);
        }

        // Plot roles: building 0 = market, building 1 = mission board, rest = dwellings.
        int buildings = 0;
        int plotIndex = 0;
        for (int cxp = 0; cxp < cols; cxp++)
            for (int czp = 0; czp < rows; czp++)
            {
                // A village occasionally leaves a plot as an open square; a town fills them densely. The
                // first plot is always built (so it carries the vendor / a guaranteed ruin loot cache).
                bool skip = !town && plotIndex > 0 && rng.NextDouble() < 0.18;
                if (skip)
                {
                    plotIndex++;
                    continue;
                }

                // Per-building variety: footprint, storeys, roof, door side, accent band.
                int fp = Building - rng.Next(0, 3);                     // 4..6
                int storeys = town ? (plotIndex == 0 ? floors : 1 + rng.Next(0, floors)) : 1;
                int doorSide = rng.Next(0, 4);
                // Desert settlements favour flat (adobe) roofs; elsewhere alien + half of houses are pitched.
                int roofStyle = (!desert && (alien || rng.NextDouble() < 0.5)) ? 1 : 0;
                int off = (Building - fp) / 2;
                int ox = cxp * Plot + 1 + off;
                int oz = czp * Plot + 1 + off;

                StampBuilding(Set, ox, oz, fp, storeys, wall, accent, glass, ladder, doorSide, roofStyle, rng, ruined);
                buildings++;

                // A lamp post + a small garden beside the door, so streets feel inhabited.
                DecorateAround(Set, ox, oz, fp, doorSide, lamp, flora, alien, rng);

                // Interaction / spawn marker at the building's interior floor centre.
                var centre = new Vector3i(ox + fp / 2, 1, oz + fp / 2);
                if (!ruined)
                {
                    string role = plotIndex switch
                    {
                        0 => "vendor",
                        1 => "mission_board",
                        _ => "npc",
                    };
                    markers.Add(new SettlementMarker(role, centre));

                    // A real door fills this building's doorway: a sci-fi slider for towns/cities, a hinged
                    // door for villages/hamlets. Placed on the lower door column; the server probes the gap to
                    // centre + size the door. (Ruins are abandoned — their doorways stay open.)
                    int mid = fp / 2, w0 = System.Math.Max(1, mid - 1);
                    var doorCell = doorSide switch
                    {
                        0 => new Vector3i(ox + w0, 1, oz),
                        1 => new Vector3i(ox + w0, 1, oz + fp - 1),
                        2 => new Vector3i(ox, 1, oz + w0),
                        _ => new Vector3i(ox + fp - 1, 1, oz + w0),
                    };
                    markers.Add(new SettlementMarker(town ? "door_slide" : "door_hinge", doorCell));
                }
                else if (plotIndex == 0 || rng.NextDouble() < 0.6)
                {
                    markers.Add(new SettlementMarker("loot", centre)); // ruins: scavenge instead of services
                }

                plotIndex++;
            }

        // A central feature (well / plaza / monument) on the middle lane.
        if (!ruined)
        {
            StampCentralFeature(Set, w, l, accent, path, flora, lamp, B("water", 0), rng);
        }

        // Some settlements are walled — a low perimeter fence with a gap for the entrance.
        if (!ruined && rng.NextDouble() < (town ? 0.35 : 0.5))
        {
            StampPerimeter(Set, w, l, fence, rng);
        }

        // Ruins: a decay pass removes a fraction of blocks and lets flora reclaim the rubble.
        if (ruined)
        {
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    for (int z = 0; z < l; z++)
                    {
                        if (blocks[(x * h + y) * l + z] != 0 && rng.NextDouble() < 0.35)
                        {
                            Set(x, y, z, 0); // collapsed / missing
                        }
                    }

            if (flora != 0)
            {
                for (int x = 1; x < w - 1; x++)
                    for (int z = 1; z < l - 1; z++)
                    {
                        if (blocks[(x * h + 0) * l + z] != 0 && rng.NextDouble() < 0.08)
                        {
                            Set(x, 1, z, flora); // overgrowth on intact ground
                        }
                    }
            }
        }

        return new SettlementStructure(w, h, l, tier, ruined, inhabitant, blocks, markers, buildings);
    }

    /// <summary>Stamps one hollow building of N storeys with a roof, a door on a chosen side, a window
    /// band and an accent stripe; multi-storey buildings get climbable ladders between decks.</summary>
    private static void StampBuilding(System.Action<int, int, int, ushort> set, int ox, int oz, int fp, int storeys,
        ushort wall, ushort accent, ushort glass, ushort ladder, int doorSide, int roofStyle, System.Random rng, bool ruined)
    {
        int height = storeys * FloorH;
        for (int x = 0; x < fp; x++)
            for (int y = 0; y <= height; y++)
                for (int z = 0; z < fp; z++)
                {
                    bool shell = x == 0 || x == fp - 1 || z == 0 || z == fp - 1 || y == 0 || y == height;
                    bool interFloor = y > 0 && y < height && (y % FloorH == 0); // storey decks

                    if (shell)
                    {
                        bool sideWall = x == 0 || x == fp - 1 || z == 0 || z == fp - 1;
                        bool window = sideWall && (y % FloorH == 2) && x > 0 && x < fp - 1 && z > 0 && z < fp - 1;
                        bool band = sideWall && (y % FloorH == 1); // accent stripe at each storey base
                        ushort b = window ? glass : (band && accent != 0 ? accent : wall);
                        set(ox + x, y, oz + z, b);
                    }
                    else if (interFloor)
                    {
                        set(ox + x, y, oz + z, wall); // floor between storeys
                    }
                    else
                    {
                        set(ox + x, y, oz + z, 0); // hollow room
                    }
                }

        // Door: a 2-wide, 3-tall gap on the chosen wall at ground level so the player fits through
        // comfortably (a 1-wide / 2-tall opening was too tight to walk through).
        int mid = fp / 2;
        int w0 = System.Math.Max(1, mid - 1), w1 = mid; // two columns, kept inside the corners
        int dy1 = 1, dy2 = System.Math.Min(height - 1, 3); // up to 3 tall, never into the ceiling
        for (int w = w0; w <= w1; w++)
            for (int y = dy1; y <= dy2; y++)
            {
                switch (doorSide)
                {
                    case 0: set(ox + w, y, oz, 0); break;             // -Z
                    case 1: set(ox + w, y, oz + fp - 1, 0); break;    // +Z
                    case 2: set(ox, y, oz + w, 0); break;             // -X
                    default: set(ox + fp - 1, y, oz + w, 0); break;   // +X
                }
            }

        // Vertical access between storeys: a hole through each deck + a full-height ladder in a corner.
        if (storeys > 1)
        {
            int lx = ox + 1, lz = oz + 1;
            for (int f = 1; f < storeys; f++)
            {
                set(lx, f * FloorH, lz, 0);
            }

            for (int y = 1; y < height; y++)
            {
                set(lx, y, lz, ladder);
            }
        }

        StampRoof(set, ox, oz, fp, height, roofStyle, wall, accent, rng);
    }

    /// <summary>Caps a building: a flat parapet (a low accent rim) or a pitched, stepped roof.</summary>
    private static void StampRoof(System.Action<int, int, int, ushort> set, int ox, int oz, int fp, int height,
        int roofStyle, ushort wall, ushort accent, System.Random rng)
    {
        if (roofStyle == 0)
        {
            // Flat parapet: a one-block rim around the roof edge.
            ushort rim = accent != 0 ? accent : wall;
            for (int x = 0; x < fp; x++)
            {
                set(ox + x, height + 1, oz, rim);
                set(ox + x, height + 1, oz + fp - 1, rim);
            }

            for (int z = 0; z < fp; z++)
            {
                set(ox, height + 1, oz + z, rim);
                set(ox + fp - 1, height + 1, oz + z, rim);
            }

            return;
        }

        // Pitched: shrinking rings of hull up to a peak (kept within RoofCap).
        int levels = System.Math.Min(RoofCap, fp / 2);
        for (int r = 1; r <= levels; r++)
        {
            int y = height + r;
            int x0 = ox + r, x1 = ox + fp - 1 - r, z0 = oz + r, z1 = oz + fp - 1 - r;
            if (x0 > x1 || z0 > z1)
            {
                break;
            }

            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    bool edge = x == x0 || x == x1 || z == z0 || z == z1;
                    if (edge || r == levels)
                    {
                        set(x, y, z, wall);
                    }
                }
        }
    }

    /// <summary>Lays street paths along the plot margins (the grid lanes between buildings).</summary>
    private static void StampPaths(System.Action<int, int, int, ushort> set, int w, int l, int cols, int rows, ushort path)
    {
        for (int cxp = 0; cxp <= cols; cxp++)
        {
            int x = System.Math.Min(w - 1, cxp * Plot);
            for (int z = 0; z < l; z++)
            {
                set(x, 0, z, path);
            }
        }

        for (int czp = 0; czp <= rows; czp++)
        {
            int z = System.Math.Min(l - 1, czp * Plot);
            for (int x = 0; x < w; x++)
            {
                set(x, 0, z, path);
            }
        }
    }

    /// <summary>A lamp post and a little garden patch next to a building's door.</summary>
    private static void DecorateAround(System.Action<int, int, int, ushort> set, int ox, int oz, int fp, int doorSide,
        ushort lamp, ushort flora, bool alien, System.Random rng)
    {
        int mid = fp / 2;
        int px, pz;
        switch (doorSide)
        {
            case 0: px = ox + mid + 1; pz = oz - 1; break;
            case 1: px = ox + mid + 1; pz = oz + fp; break;
            case 2: px = ox - 1; pz = oz + mid + 1; break;
            default: px = ox + fp; pz = oz + mid + 1; break;
        }

        if (lamp != 0 && rng.NextDouble() < 0.7)
        {
            set(px, 1, pz, lamp);
            set(px, 2, pz, lamp); // a short post
        }

        // Gardens — denser around alien dwellings.
        int patches = alien ? 3 : 1;
        for (int i = 0; i < patches; i++)
        {
            if (flora != 0 && rng.NextDouble() < 0.6)
            {
                int gx = ox - 1 + rng.Next(0, fp + 2);
                int gz = oz - 1 + rng.Next(0, fp + 2);
                set(gx, 1, gz, flora);
            }
        }
    }

    /// <summary>A focal point on the settlement's central lane: a well, a plaza or a monument.</summary>
    private static void StampCentralFeature(System.Action<int, int, int, ushort> set,
        int w, int l, ushort accent, ushort path, ushort flora, ushort lamp, ushort water, System.Random rng)
    {
        int cx = w / 2, cz = l / 2;
        int kind = rng.Next(0, 3);
        ushort floor = path != 0 ? path : accent;

        // A 3×3 paved plaza.
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                set(cx + dx, 0, cz + dz, floor);
            }

        switch (kind)
        {
            case 0: // Well: a ring of accent with water in the middle.
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx != 0 || dz != 0)
                        {
                            set(cx + dx, 1, cz + dz, accent);
                        }
                    }

                if (water != 0)
                {
                    set(cx, 1, cz, water);
                }

                break;

            case 1: // Monument: an accent column with a lamp on top.
                set(cx, 1, cz, accent);
                set(cx, 2, cz, accent);
                if (lamp != 0)
                {
                    set(cx, 3, cz, lamp);
                }

                break;

            default: // Garden plaza: lamps at the corners, flora in the middle.
                if (lamp != 0)
                {
                    set(cx - 1, 1, cz - 1, lamp);
                    set(cx + 1, 1, cz + 1, lamp);
                }

                if (flora != 0)
                {
                    set(cx, 1, cz, flora);
                }

                break;
        }
    }

    /// <summary>A low perimeter fence around the settlement with a one-wide entrance gap per side.</summary>
    private static void StampPerimeter(System.Action<int, int, int, ushort> set, int w, int l, ushort fence, System.Random rng)
    {
        if (fence == 0)
        {
            return;
        }

        int gapX = 1 + rng.Next(System.Math.Max(1, w - 2));
        int gapZ = 1 + rng.Next(System.Math.Max(1, l - 2));
        for (int x = 0; x < w; x++)
        {
            if (x != gapX)
            {
                set(x, 1, 0, fence);
                set(x, 1, l - 1, fence);
            }
        }

        for (int z = 0; z < l; z++)
        {
            if (z != gapZ)
            {
                set(0, 1, z, fence);
                set(w - 1, 1, z, fence);
            }
        }
    }
}
