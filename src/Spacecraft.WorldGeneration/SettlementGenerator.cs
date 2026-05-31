using System.Collections.Generic;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.WorldGeneration;

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
    public IReadOnlyList<SettlementMarker> Markers { get; }
    public int BuildingCount { get; }

    internal SettlementStructure(int w, int h, int l, string tier, bool ruined, string inhabitant,
        ushort[] blocks, IReadOnlyList<SettlementMarker> markers, int buildingCount)
    {
        Width = w;
        Height = h;
        Length = l;
        Tier = tier;
        Ruined = ruined;
        Inhabitant = inhabitant;
        _blocks = blocks;
        Markers = markers;
        BuildingCount = buildingCount;
    }

    public ushort Get(int x, int y, int z) => _blocks[(x * Height + y) * Length + z];

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
    private const int Plot = 8;     // plot stride (building + street margin)
    private const int Building = 6; // building footprint (Building×Building)
    private const int FloorH = 4;   // height of one storey

    /// <summary>(plot columns, plot rows, floors) per tier.</summary>
    public static (int Cols, int Rows, int Floors) Layout(string tier) => tier switch
    {
        "town" => (3, 3, 2),
        _ => (2, 2, 1), // "village"
    };

    public static SettlementStructure Generate(string tier, bool ruined, long seed, string biomeSurfaceBlock, GameContent content)
    {
        var (cols, rows, floors) = Layout(tier);
        // Use a stable hash (not string.GetHashCode, which is randomized per process) so the build
        // is genuinely deterministic from the seed across runs.
        int tierHash = (int)WorldGenerator.StableHash(tier);
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ tierHash ^ (ruined ? 0x5111 : 0)));

        // Materials: a town is iron/glass; a village uses the biome's surface block (mud/stone/…).
        ushort wall = tier == "town"
            ? (content.GetBlock("iron_wall")?.NumericId.Value ?? 0)
            : (content.GetBlock(biomeSurfaceBlock)?.NumericId.Value ?? content.GetBlock("stone")?.NumericId.Value ?? 0);
        ushort glass = content.GetBlock("glass")?.NumericId.Value ?? 0;
        ushort floraId = content.GetBlock("flora_plant")?.NumericId.Value ?? 0;
        ushort ladder = content.GetBlock("ladder")?.NumericId.Value ?? 0;

        int w = cols * Plot + 1;
        int l = rows * Plot + 1;
        int h = floors * FloorH + 1;
        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        var markers = new List<SettlementMarker>();
        string inhabitant = ruined ? string.Empty : (rng.NextDouble() < 0.5 ? "human" : "alien");

        // Plot roles: building 0 = market, building 1 = mission board, rest = dwellings.
        int buildings = 0;
        int plotIndex = 0;
        for (int cxp = 0; cxp < cols; cxp++)
        for (int czp = 0; czp < rows; czp++)
        {
            // A village occasionally leaves a plot as an open square; a town fills them densely.
            bool skip = tier == "village" && rng.NextDouble() < 0.15;
            if (skip)
            {
                plotIndex++;
                continue;
            }

            int ox = cxp * Plot + 1;
            int oz = czp * Plot + 1;
            int storeys = tier == "town" ? floors : 1;
            StampBuilding(Set, ox, oz, storeys, wall, glass, ladder, rng, ruined);
            buildings++;

            // Interaction / spawn marker at the building's interior floor centre.
            var centre = new Vector3i(ox + Building / 2, 1, oz + Building / 2);
            if (!ruined)
            {
                string role = plotIndex switch
                {
                    0 => "vendor",
                    1 => "mission_board",
                    _ => "npc",
                };
                markers.Add(new SettlementMarker(role, centre));
            }
            else
            {
                // Ruins: loot to scavenge instead of NPCs/services.
                if (rng.NextDouble() < 0.6)
                {
                    markers.Add(new SettlementMarker("loot", centre));
                }
            }

            plotIndex++;
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

            if (floraId != 0)
            {
                for (int x = 1; x < w - 1; x++)
                for (int z = 1; z < l - 1; z++)
                {
                    if (blocks[(x * h + 0) * l + z] != 0 && rng.NextDouble() < 0.08)
                    {
                        Set(x, 1, z, floraId); // overgrowth on intact ground
                    }
                }
            }
        }

        return new SettlementStructure(w, h, l, tier, ruined, inhabitant, blocks, markers, buildings);
    }

    /// <summary>Stamps one hollow building (walls + a door + window band) of N storeys at a plot origin.</summary>
    private static void StampBuilding(System.Action<int, int, int, ushort> set, int ox, int oz, int storeys,
        ushort wall, ushort glass, ushort ladder, System.Random rng, bool ruined)
    {
        int height = storeys * FloorH;
        for (int x = 0; x < Building; x++)
        for (int y = 0; y <= height; y++)
        for (int z = 0; z < Building; z++)
        {
            bool shell = x == 0 || x == Building - 1 || z == 0 || z == Building - 1 || y == 0 || y == height;
            bool interFloor = y > 0 && y < height && (y % FloorH == 0); // storey decks

            if (shell)
            {
                bool sideWall = x == 0 || x == Building - 1 || z == 0 || z == Building - 1;
                bool window = sideWall && (y % FloorH == 2) && x > 0 && x < Building - 1 && z > 0 && z < Building - 1;
                set(ox + x, y, oz + z, window ? glass : wall);
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

        // Door: a 1-wide, 2-tall gap in the -Z wall at ground level.
        set(ox + Building / 2, 1, oz, 0);
        set(ox + Building / 2, 2, oz, 0);

        // Vertical access between storeys: a hole through each deck plus a climbable ladder running
        // the full height in the corner, so a multi-storey building is actually enterable upstairs.
        if (storeys > 1)
        {
            int lx = ox + 1, lz = oz + 1;
            for (int f = 1; f < storeys; f++)
            {
                set(lx, f * FloorH, lz, 0); // open the deck above
            }

            for (int y = 1; y < height; y++)
            {
                set(lx, y, lz, ladder); // ladder column the player climbs
            }
        }
    }
}
