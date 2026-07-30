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
///
/// One or more plots hold a <b>greenhouse</b> (#626) — a glass house of berry crops the player can walk into
/// and harvest. Villages grow theirs in soil under a timber gable, cities run two-tier hydroponics under
/// grow lights; a city keeps two or three, a hamlet rarely one.
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

        // Fallback vendor for templates without one — in a FREE cell (#480, was ST-9): the old fixed centre
        // spot could sit inside a wall, burying the vendor. Scan upward at the centre for the first air cell
        // with something solid below; a fully solid column falls back to the roof.
        // NOTE (#480, was ST-9b): templates carry no door_slide/door_hinge markers unless the author places
        // them — RegisterDoors hangs doors ONLY on markers, so authored doorways without markers stay open
        // arches. Place door markers in the editor where you want working doors.
        if (!markers.Exists(m => m.Type == "vendor"))
        {
            int vx = w / 2, vz = l / 2, vy = 1;
            for (int y = 1; y < h; y++)
            {
                if (blocks[(vx * h + y) * l + vz] == 0 && blocks[(vx * h + (y - 1)) * l + vz] != 0)
                {
                    vy = y;
                    break;
                }

                if (y == h - 1)
                {
                    vy = h - 1; // solid column all the way up → roof
                }
            }

            markers.Add(new SettlementMarker("vendor", new Vector3i(vx, vy, vz)));
        }

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

        // Greenhouse materials (#626). A village grows its berries in soil under a timber-and-glass frame; a
        // town/city runs hydroponic trays in an iron frame under grow lights. The crop is the CULTIVATED
        // species (#627) — never toxic, never re-tinted per world — so a settlement's greenhouse is always
        // food the player can safely eat, and falls back to the wild bush only if that block is missing.
        ushort frame = town ? B("iron_wall", wall) : B("wood_log", wall);
        ushort bed = town ? B("hydro_tray", B("dirt", wall)) : B("dirt", B("mud", wall));
        ushort crop = B("flora_cropberry", B("flora_bush", flora));
        ushort growLight = town ? B("strip_light_warm", lamp) : B("torch", lamp);

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

        // Which plots hold a greenhouse (#626). Never plot 0 or 1 — those carry the vendor and the mission
        // board, the two services a settlement must have. Bigger places feed more mouths, so a city runs two
        // or three glass houses while a hamlet only sometimes has room for one at all.
        var greenhousePlots = PickGreenhousePlots(tier, cols * rows, rng);

        // Plot roles: building 0 = market, building 1 = mission board, rest = dwellings.
        int buildings = 0;
        int plotIndex = 0;
        for (int cxp = 0; cxp < cols; cxp++)
            for (int czp = 0; czp < rows; czp++)
            {
                // A village occasionally leaves a plot as an open square; a town fills them densely. The
                // first plot is always built (so it carries the vendor / a guaranteed ruin loot cache), and
                // a greenhouse plot is never dropped either — it was picked as one of the few this
                // settlement gets.
                bool greenhouse = greenhousePlots.Contains(plotIndex);
                bool skip = !town && !greenhouse && plotIndex > 0 && rng.NextDouble() < 0.18;
                if (skip)
                {
                    plotIndex++;
                    continue;
                }

                // Per-building variety: footprint, storeys, roof, door side, accent band. (The draws stay
                // unconditional so the rng stream reads the same whether or not this plot is a greenhouse.)
                int fp = Building - rng.Next(0, 3);                     // 4..6
                int storeys = town ? (plotIndex == 0 ? floors : 1 + rng.Next(0, floors)) : 1;
                int doorSide = rng.Next(0, 4);
                // Desert settlements favour flat (adobe) roofs; elsewhere alien + half of houses are pitched.
                int roofStyle = (!desert && (alien || rng.NextDouble() < 0.5)) ? 1 : 0;
                if (greenhouse)
                {
                    fp = Building; // a greenhouse always takes the full footprint — its beds need the width
                }

                int off = (Building - fp) / 2;
                int ox = cxp * Plot + 1 + off;
                int oz = czp * Plot + 1 + off;

                if (greenhouse)
                {
                    StampGreenhouse(Set, ox, oz, fp, town, frame, glass, bed, crop, growLight, doorSide, rng, ruined);
                }
                else
                {
                    StampBuilding(Set, ox, oz, fp, storeys, wall, accent, glass, ladder, doorSide, roofStyle, rng, ruined);
                }

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

                    // A greenhouse also announces itself: the resident standing in the aisle is its gardener
                    // (the "npc" marker above), and this second marker is what lets anything else — tests, a
                    // map POI, a future mission — find the glass house without re-scanning the voxels.
                    if (greenhouse)
                    {
                        markers.Add(new SettlementMarker("greenhouse", centre));
                    }

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

        // A central feature (well / plaza / monument) on the middle lane. A ruin gets the BROKEN version of
        // one (#525): a fallen town used to have a landmark too, and without it the rubble field reads as
        // nothing but eroded houses.
        if (!ruined)
        {
            StampCentralFeature(Set, w, l, accent, path, flora, lamp, B("water", 0), rng);
        }
        else
        {
            StampBrokenFeature(Set, w, l, B("ancient_brick", B("stone", wall)), B("rune_stone", B("stone", wall)), rng);
        }

        // Some settlements are walled — a low perimeter fence with a gap for the entrance.
        if (!ruined && rng.NextDouble() < (town ? 0.35 : 0.5))
        {
            StampPerimeter(Set, w, l, fence, rng);
        }

        // Ruins: a decay pass turns the settlement into a proper ruin. Collapse rises with height — ground
        // walls mostly survive while roofs and upper storeys are almost all gone — and one building is spared
        // the worst of it so it reads as a half-standing tower. Rubble piles and flora then reclaim the
        // ground. Every ruin differs (the spared plot + the seeded thresholds vary per instance).
        if (ruined)
        {
            ushort rubble = B("stone", wall);

            // Spare one plot from the heaviest collapse so a tall fragment / tower keeps standing.
            int sparedCx = rng.Next(0, System.Math.Max(1, cols)) * Plot + 1 + Building / 2;
            int sparedCz = rng.Next(0, System.Math.Max(1, rows)) * Plot + 1 + Building / 2;
            const int sparedR = Building;

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    for (int z = 0; z < l; z++)
                    {
                        if (blocks[(x * h + y) * l + z] == 0)
                        {
                            continue;
                        }

                        double heightFrac = (double)y / System.Math.Max(1, h - 1);
                        double pRemove = 0.20 + 0.65 * heightFrac; // 20% at the base rising to ~85% at roof level

                        bool spared = System.Math.Abs(x - sparedCx) <= sparedR && System.Math.Abs(z - sparedCz) <= sparedR;
                        if (spared)
                        {
                            pRemove *= 0.35; // the tower decays far less
                        }

                        if (rng.NextDouble() < pRemove)
                        {
                            Set(x, y, z, 0); // collapsed / missing
                        }
                    }

            // Rubble piles + flora overgrowth on the surviving ground (only where a floor/ground cell remains).
            for (int x = 1; x < w - 1; x++)
                for (int z = 1; z < l - 1; z++)
                {
                    if (blocks[(x * h + 0) * l + z] == 0)
                    {
                        continue;
                    }

                    double r = rng.NextDouble();
                    if (rubble != 0 && r < 0.14)
                    {
                        Set(x, 1, z, rubble); // fallen debris
                    }
                    else if (flora != 0 && r < 0.24)
                    {
                        Set(x, 1, z, flora); // overgrowth reclaiming the rubble
                    }
                }
        }

        return new SettlementStructure(w, h, l, tier, ruined, inhabitant, blocks, markers, buildings);
    }

    /// <summary>Wall height of a greenhouse (the y of its ceiling row): a village garden house is low enough
    /// that its glass gable still fits the single-storey height budget, a town/city bay is roomy enough for a
    /// second growing tier above the floor beds.</summary>
    private const int GreenhouseVillageH = 4;
    private const int GreenhouseTownH = 6;

    /// <summary>The rack tier of a hydroponics bay: trays at this height, crops one above.</summary>
    private const int GreenhouseRackY = 3;

    /// <summary>Picks which plots hold a greenhouse (#626). Plots 0 and 1 are reserved for the vendor and the
    /// mission board, so the pick starts at 2 and spreads the houses across the settlement rather than
    /// clustering them. A hamlet is usually just a vendor and a board, so it only gets one when its layout
    /// rolled a spare plot — and even then only half the time.</summary>
    private static HashSet<int> PickGreenhousePlots(string tier, int totalPlots, System.Random rng)
    {
        int wanted = tier switch
        {
            "city" => 2 + rng.Next(0, 2),   // 2..3 — a city feeds a lot of people
            "town" => 1 + rng.Next(0, 2),   // 1..2
            "hamlet" => rng.Next(0, 2),     // 0..1, and only if there is a spare plot at all
            _ => 1,                          // village
        };

        var plots = new HashSet<int>();
        int free = totalPlots - 2; // plots 0 + 1 are spoken for
        if (free <= 0 || wanted <= 0)
        {
            return plots;
        }

        wanted = System.Math.Min(wanted, free);
        int stride = System.Math.Max(1, free / wanted);
        for (int n = 0; n < wanted; n++)
        {
            // Walk forward from the spread position until a free plot turns up, so two greenhouses never
            // land on the same plot however the stride rounds.
            for (int probe = 0; probe < free; probe++)
            {
                int idx = 2 + ((n * stride) + probe) % free;
                if (plots.Add(idx))
                {
                    break;
                }
            }
        }

        return plots;
    }

    /// <summary>Stamps a greenhouse: a glass house whose beds grow berry crops the player can harvest and eat.
    /// Two builds share this shape — a village grows its berries in soil under a timber-and-glass frame with a
    /// pitched glass gable, a town/city runs hydroponic trays on two tiers under grow lights behind a flat
    /// glass ceiling. The beds are laid PERPENDICULAR to the door, so walking in puts the player in the aisle
    /// rather than in the crops, and the corner posts + a sill course keep the glass box from reading as a
    /// featureless cube. A ruin gets the empty shell — the decay pass then shatters it.</summary>
    private static void StampGreenhouse(System.Action<int, int, int, ushort> set, int ox, int oz, int fp,
        bool town, ushort frame, ushort glass, ushort bed, ushort crop, ushort growLight,
        int doorSide, System.Random rng, bool ruined)
    {
        int height = town ? GreenhouseTownH : GreenhouseVillageH;

        // Shell: a glass box on a frame. The floor is decking, the corner posts and the sill course at knee
        // height are frame material, everything else you can see through — that is the whole point of it.
        for (int x = 0; x < fp; x++)
            for (int y = 0; y <= height; y++)
                for (int z = 0; z < fp; z++)
                {
                    bool sideWall = x == 0 || x == fp - 1 || z == 0 || z == fp - 1;
                    bool corner = (x == 0 || x == fp - 1) && (z == 0 || z == fp - 1);

                    if (y == 0)
                    {
                        set(ox + x, y, oz + z, frame); // deck (bed cells overwrite this below)
                    }
                    else if (y == height)
                    {
                        set(ox + x, y, oz + z, glass); // glazed ceiling
                    }
                    else if (sideWall)
                    {
                        set(ox + x, y, oz + z, corner || y == 1 ? frame : glass);
                    }
                    else
                    {
                        set(ox + x, y, oz + z, 0); // the growing room
                    }
                }

        // Door: the same 2-wide, 3-tall opening every settlement building uses, so the player fits through
        // and the server's door probe finds the gap it expects.
        int mid = fp / 2;
        int w0 = System.Math.Max(1, mid - 1), w1 = mid;
        int dy2 = System.Math.Min(height - 1, 3);
        for (int w = w0; w <= w1; w++)
            for (int y = 1; y <= dy2; y++)
            {
                switch (doorSide)
                {
                    case 0: set(ox + w, y, oz, 0); break;
                    case 1: set(ox + w, y, oz + fp - 1, 0); break;
                    case 2: set(ox, y, oz + w, 0); break;
                    default: set(ox + fp - 1, y, oz + w, 0); break;
                }
            }

        // Two bed rows hard against the side walls with an aisle between them. A door in an X wall opens along
        // X, so the beds must run along X to leave that lane clear — and the other way round for a Z door.
        bool bedsAlongX = doorSide == 2 || doorSide == 3;
        int rowA = 1, rowB = fp - 2;
        for (int i = 1; i <= fp - 2; i++)
        {
            foreach (int row in new[] { rowA, rowB })
            {
                int bx = bedsAlongX ? i : row;
                int bz = bedsAlongX ? row : i;
                set(ox + bx, 0, oz + bz, bed);

                // Not every cell carries a plant — a few gaps read as a bed being worked rather than a
                // wallpaper of identical bushes. The gaps follow the POSITION, not a die roll, so every
                // greenhouse is guaranteed a proper crop of berries instead of occasionally coming out
                // nearly bare. A ruin is left unplanted; the decay pass reclaims it instead.
                if (!ruined && crop != 0 && (i + row) % 7 != 0)
                {
                    set(ox + bx, 1, oz + bz, crop);
                }

                // The hydroponics bay stacks a second growing tier on a rack above the floor bed.
                if (town && !ruined && bed != 0)
                {
                    set(ox + bx, GreenhouseRackY, oz + bz, bed);
                    if (crop != 0 && (i * 2 + row) % 5 != 0)
                    {
                        set(ox + bx, GreenhouseRackY + 1, oz + bz, crop);
                    }
                }
            }
        }

        // Light. A city bay runs grow lights in the ceiling (they read as fixtures in the glass and give the
        // house a glow at night); a village garden house just has a torch on a corner post.
        if (!ruined && growLight != 0)
        {
            if (town)
            {
                set(ox + mid, height, oz + 1, growLight);
                set(ox + mid, height, oz + fp - 2, growLight);
            }
            else
            {
                // One cell off the aisle centre — the gardener's spawn marker stands there.
                set(ox + System.Math.Max(1, mid - 1), 1, oz + System.Math.Max(1, mid - 1), growLight);
            }
        }

        // Roof: a pitched glass gable over a village garden house, a frame parapet around the city bay's flat
        // glazed ceiling. Both reuse the settlement roof pass, just glazed.
        StampRoof(set, ox, oz, fp, height, town ? 0 : 1, glass, frame, rng);
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

    /// <summary>The ruined twin of <see cref="StampCentralFeature"/> (#525): the landmark the town once had,
    /// found the way a ruin leaves it — a snapped column pair, the springer of an arch that no longer spans
    /// anything, a toppled inscribed stone. The decay pass runs afterwards and takes a little more of it, so
    /// no two fallen towns wear the same fragment.</summary>
    private static void StampBrokenFeature(System.Action<int, int, int, ushort> set,
        int w, int l, ushort masonry, ushort rune, System.Random rng)
    {
        if (masonry == 0)
        {
            return;
        }

        int cx = w / 2, cz = l / 2;

        // A broken paving disc — the plaza that used to be here.
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx * dx + dz * dz <= 5 && rng.NextDouble() < 0.7)
                {
                    set(cx + dx, 0, cz + dz, masonry);
                }
            }

        // Two column stumps of unequal height — what is left of a gateway or a portico.
        int leftH = 1 + rng.Next(3);
        int rightH = 1 + rng.Next(4);
        for (int y = 1; y <= leftH; y++)
        {
            set(cx - 2, y, cz, masonry);
        }

        for (int y = 1; y <= rightH; y++)
        {
            set(cx + 2, y, cz, masonry);
        }

        // The taller stump still carries the first stone of its arch, jutting into nothing.
        if (rightH >= 3 && rng.NextDouble() < 0.6)
        {
            set(cx + 1, rightH, cz, masonry);
        }

        // Toppled inscribed stones in the rubble — the only writing the settlers left behind.
        if (rune != 0)
        {
            for (int i = 0; i < 1 + rng.Next(3); i++)
            {
                int rx = cx + rng.Next(-2, 3);
                int rz = cz + rng.Next(-2, 3);
                set(rx, 1, rz, rune);
            }
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
