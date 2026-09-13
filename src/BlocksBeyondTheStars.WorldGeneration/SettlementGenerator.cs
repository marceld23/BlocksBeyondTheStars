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
    public const int Plot = 8;       // plot stride (building + street margin); public so the editor can show the tier envelope (#1402)
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
    private static bool IsTownStyle(string tier) => StructureRoles.IsTownStyleTier(tier);

    /// <summary>The largest plot module (#1826) that fits EVERY settlement of a tier: the building footprint by
    /// the tier's base storey height plus the roof cap (a town rolled with an extra storey has more room, but a
    /// module sized to the base height is never turned away). The editor shows this envelope.</summary>
    public static (int W, int H, int L) PlotModuleEnvelope(string tier)
    {
        var (_, _, floors) = Layout(tier);
        return (Building, floors * FloorH + RoofCap, Building);
    }

    /// <summary>The marker an author places on a floor cell to have that room furnished procedurally (#1828).</summary>
    public const string RoomMarker = "room";

    /// <summary>The most floor cells a <see cref="RoomMarker"/> flood-fills — a marker on open ground stops here.</summary>
    private const int RoomCap = 256;

    /// <summary>A cell sink over a local voxel grid + its sparse tint/glow and shape tables (the shape a
    /// structure carries per cell, <see cref="SettlementStructure.GetShape"/>).</summary>
    private static RoomFurnisher.CellSink SinkFor(ushort[] blocks, int w, int h, int l,
        Dictionary<int, (int Tint, int Glow)> mods, Dictionary<int, int> shapes)
        => (x, y, z, b, shape, tint, glow) =>
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l)
            {
                return;
            }

            int idx = (x * h + y) * l + z;
            blocks[idx] = b;
            if (b != 0 && (tint != 0 || glow != 0)) mods[idx] = (tint, glow); else mods.Remove(idx);
            if (b != 0 && shape != 0) shapes[idx] = shape; else shapes.Remove(idx);
        };

    /// <summary>Builds a settlement structure from a hand-designed template (the editor export) — blocks
    /// become voxels, markers become vendor/mission_board/npc points. Templates are intact (not ruined).
    /// Rooms the author marked with a <see cref="RoomMarker"/> are furnished (#1828).</summary>
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

        FurnishAuthoredRooms((x, y, z) => blocks[(x * h + y) * l + z], w, h, l, markers,
            RoomFurnisher.PaletteFor(RoomFurnisher.StyleFor(t.Tier, alien: false), content),
            (long)WorldGenerator.StableHash("furnish:" + t.Key), SinkFor(blocks, w, h, l, mods, shapes));

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

    /// <summary>
    /// Builds a procedural settlement. <paramref name="modules"/> (#1827) are the authored building modules the
    /// world allows (pack / planet filtered by the caller); with <paramref name="moduleChance"/> per plot, a
    /// HASH of tier + seed + plot decides whether a plot is built from a module of its role instead of the
    /// procedural building — never an rng draw, so every plot that stays procedural is byte-identical whether
    /// or not modules exist. Rooms are furnished (#1828) on the way.
    /// </summary>
    public static SettlementStructure Generate(string tier, bool ruined, long seed, string biomeSurfaceBlock, GameContent content,
        IReadOnlyList<StructureTemplate>? modules = null, double moduleChance = 0.0)
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
        ushort growLight = town ? B("strip_light_warm", lamp) : B("torch", lamp);

        // Each greenhouse grows ONE of the cultivated crops (#1204), picked per plot from a hash — never from
        // `rng`: the draws below are read unconditionally so the stream must not shift, or every existing
        // seed would re-roll its layout. A city with two or three houses gets a mixed harvest that way.
        var cropSet = new List<ushort>();
        foreach (var key in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.CultivatedKeys())
        {
            ushort id = B(key);
            if (id != 0)
            {
                cropSet.Add(id);
            }
        }

        ushort fallbackCrop = B("flora_bush", flora);
        ushort CropFor(int plot) => cropSet.Count == 0
            ? fallbackCrop
            : cropSet[(int)((WorldGenerator.StableHash($"crop:{tier}:{seed}:{plot}") & 0x7fffffff) % cropSet.Count)];

        int w = cols * Plot + 1;
        int l = rows * Plot + 1;
        int h = floors * FloorH + 1 + RoofCap;
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int Tint, int Glow)>();
        var shapes = new Dictionary<int, int>();
        var setCell = SinkFor(blocks, w, h, l, mods, shapes);
        void Set(int x, int y, int z, ushort b) => setCell(x, y, z, b, 0, 0, 0);
        ushort Get(int x, int y, int z) => blocks[(x * h + y) * l + z];

        // Interiors (#1828): the style's furniture palette; every procedural room is furnished from a hash of
        // its plot so the main stream never shifts.
        var furniture = RoomFurnisher.PaletteFor(RoomFurnisher.StyleFor(tier, alien), content);

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
                // settlement gets. Plot 1 carries the mission board and the quartermaster (#1199): before,
                // the same 18 % roll could drop it, leaving roughly one village in six with no board, no
                // build jobs and no bounty. The roll is still DRAWN for plot 1 (the rng stream every later
                // plot reads from must not shift), its result is simply ignored there.
                bool greenhouse = greenhousePlots.Contains(plotIndex);
                bool skip = !town && !greenhouse && plotIndex > 0 && rng.NextDouble() < 0.18 && plotIndex != 1;
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

                // #1827: an authored module for this plot? Its role is the plot's role; it must fit the plot
                // and match the settlement's style. Picked by hash, so the rng stream above and below is the
                // same with or without modules. A module brings its own markers (door, npc, room …).
                string plotRole = greenhouse ? StructureRoles.Greenhouse
                    : plotIndex == 0 ? StructureRoles.Market
                    : plotIndex == 1 ? StructureRoles.Board
                    : StructureRoles.House;
                long plotHash = (long)WorldGenerator.StableHash($"furnish:{tier}:{seed}:{plotIndex}");
                var module = PickModule(modules, moduleChance, $"module:{tier}:{seed}:{plotIndex}", plotRole,
                    m => StructureRoles.IsTownStyleTier(m.Tier) == town, Building, h - 1, Building);
                var moduleMarkers = new List<SettlementMarker>();
                if (module != null)
                {
                    ox = cxp * Plot + 1 + (Building - module.Width) / 2;
                    oz = czp * Plot + 1 + (Building - module.Length) / 2;
                    fp = System.Math.Max(module.Width, module.Length);
                    StampModule(module, ox, 0, oz, content, Get, setCell, moduleMarkers, furniture, plotHash);
                    int side = DoorSideOf(moduleMarkers, ox, oz, module.Width, module.Length);
                    if (side >= 0) doorSide = side;
                }
                else if (greenhouse)
                {
                    StampGreenhouse(Set, ox, oz, fp, town, frame, glass, bed, CropFor(plotIndex), growLight, doorSide, rng, ruined);
                }
                else
                {
                    var groundRole = plotIndex == 0 ? RoomFurnisher.RoomRole.Market
                        : plotIndex == 1 ? RoomFurnisher.RoomRole.Board
                        : RoomFurnisher.RoomRole.House;
                    StampBuilding(Set, ox, oz, fp, storeys, wall, accent, glass, ladder, doorSide, roofStyle, rng, ruined,
                        furnish: furniture, setCell: setCell, furnishSeed: plotHash, groundRole: groundRole);
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

                    if (module != null)
                    {
                        // The module's own markers stand in for the procedural ones. A missing role marker (a
                        // market module without a vendor …) is added over the module's centre column, in the
                        // first free cell — exactly the FromTemplate fallback.
                        markers.AddRange(moduleMarkers);
                        if (!moduleMarkers.Exists(m => m.Type == role))
                        {
                            markers.Add(new SettlementMarker(role, new Vector3i(centre.X, FreeCellAbove(Get, h, centre.X, centre.Z), centre.Z)));
                        }

                        if (greenhouse && !moduleMarkers.Exists(m => m.Type == "greenhouse"))
                        {
                            markers.Add(new SettlementMarker("greenhouse", centre));
                        }

                        plotIndex++;
                        continue;
                    }

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

        return new SettlementStructure(w, h, l, tier, ruined, inhabitant, blocks, markers, buildings, mods, shapes);
    }

    // --- authored building modules (#1827) ------------------------------------------------------------------

    /// <summary>
    /// Picks the module for one slot, or null. The decision is a HASH of <paramref name="hashKey"/> (never an
    /// rng draw): under <paramref name="chance"/> the slot takes a module, and among the candidates — the
    /// <paramref name="role"/> the slot needs, <paramref name="styleOk"/>, and fitting the envelope — a second
    /// hash picks one weighted by <see cref="StructureTemplate.Weight"/>. Pool order matters for the pick,
    /// so callers pass the content's list unsorted.
    /// </summary>
    internal static StructureTemplate? PickModule(IReadOnlyList<StructureTemplate>? modules, double chance, string hashKey,
        string role, System.Func<StructureTemplate, bool> styleOk, int maxW, int maxH, int maxL)
    {
        if (modules == null || modules.Count == 0 || chance <= 0)
        {
            return null;
        }

        double roll = (WorldGenerator.StableHash(hashKey) & 0xFFFFFF) / (double)0x1000000;
        if (roll >= chance)
        {
            return null;
        }

        var fit = new List<StructureTemplate>();
        int total = 0;
        foreach (var m in modules)
        {
            if (m.Role == role && styleOk(m) && m.Width > 0 && m.Height > 0 && m.Length > 0
                && m.Width <= maxW && m.Height <= maxH && m.Length <= maxL)
            {
                fit.Add(m);
                total += System.Math.Max(1, m.Weight);
            }
        }

        if (fit.Count == 0)
        {
            return null;
        }

        int pick = (int)((WorldGenerator.StableHash(hashKey + ":pick") & 0x7fffffff) % total);
        foreach (var m in fit)
        {
            pick -= System.Math.Max(1, m.Weight);
            if (pick < 0)
            {
                return m;
            }
        }

        return fit[fit.Count - 1];
    }

    /// <summary>Stamps a module's cells at (<paramref name="ox"/>, <paramref name="oy"/>, <paramref name="oz"/>)
    /// of the destination grid, translates its markers into <paramref name="markersOut"/> and furnishes the
    /// rooms it marks (#1828). <paramref name="get"/> reads the destination (for the room flood fill).</summary>
    internal static void StampModule(StructureTemplate module, int ox, int oy, int oz, GameContent content,
        System.Func<int, int, int, ushort> get, RoomFurnisher.CellSink setCell, List<SettlementMarker> markersOut,
        RoomFurnisher.Palette furniture, long furnishSeed)
    {
        int w = module.Width, h = module.Height, l = module.Length;
        var local = new List<SettlementMarker>();
        foreach (var cell in module.Cells)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.Z < 0 || cell.X >= w || cell.Y >= h || cell.Z >= l)
            {
                continue;
            }

            if (cell.Kind == "marker")
            {
                local.Add(new SettlementMarker(cell.Id, new Vector3i(cell.X, cell.Y, cell.Z)));
            }
            else
            {
                ushort id = content.GetBlock(cell.Id)?.NumericId.Value ?? 0;
                if (id != 0)
                {
                    setCell(ox + cell.X, oy + cell.Y, oz + cell.Z, id, cell.Shape, cell.Tint, cell.Glow);
                }
            }
        }

        FurnishAuthoredRooms((x, y, z) => get(ox + x, oy + y, oz + z), w, h, l, local, furniture, furnishSeed,
            (x, y, z, b, shape, tint, glow) => setCell(ox + x, oy + y, oz + z, b, shape, tint, glow));

        foreach (var m in local)
        {
            markersOut.Add(new SettlementMarker(m.Type, new Vector3i(ox + m.LocalPos.X, oy + m.LocalPos.Y, oz + m.LocalPos.Z)));
        }
    }

    /// <summary>Which wall of a module box its first door marker sits on (0 −Z, 1 +Z, 2 −X, 3 +X), or −1
    /// when it has none — the lamp post and garden go beside the door.</summary>
    internal static int DoorSideOf(IReadOnlyList<SettlementMarker> markers, int ox, int oz, int w, int l)
    {
        foreach (var m in markers)
        {
            if (!m.Type.StartsWith("door_", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (m.LocalPos.Z == oz) return 0;
            if (m.LocalPos.Z == oz + l - 1) return 1;
            if (m.LocalPos.X == ox) return 2;
            if (m.LocalPos.X == ox + w - 1) return 3;
        }

        return -1;
    }

    /// <summary>The first air cell with something solid under it, scanning up the column (x, z) from y = 1;
    /// the top row when the column is solid all the way (the FromTemplate vendor fallback, #480).</summary>
    internal static int FreeCellAbove(System.Func<int, int, int, ushort> get, int h, int x, int z)
    {
        for (int y = 1; y < h; y++)
        {
            if (get(x, y, z) == 0 && get(x, y - 1, z) != 0)
            {
                return y;
            }
        }

        return h - 1;
    }

    /// <summary>
    /// Furnishes every room an author marked (#1828): each <see cref="RoomMarker"/> flood-fills the floor it
    /// stands on (<see cref="RoomFurnisher.FloodRoom"/>), every other marker cell in that room and the cells
    /// around each door marker stay free, and the room's role follows the marker found inside it — a vendor
    /// makes a market, a mission board an office, anything else a home. Coordinates are those of the markers.
    /// </summary>
    internal static void FurnishAuthoredRooms(System.Func<int, int, int, ushort> get, int w, int h, int l,
        IReadOnlyList<SettlementMarker> markers, RoomFurnisher.Palette furniture, long seed, RoomFurnisher.CellSink setCell)
    {
        int n = 0;
        foreach (var room in markers)
        {
            if (room.Type != RoomMarker)
            {
                continue;
            }

            n++;
            var region = RoomFurnisher.FloodRoom(get, w, h, l, room.LocalPos.X, room.LocalPos.Y, room.LocalPos.Z, RoomCap, out int clearance);
            if (region.Count == 0)
            {
                continue;
            }

            var cells = new HashSet<(int X, int Z)>(region);
            var reserved = new HashSet<(int X, int Z)>();
            var role = RoomFurnisher.RoomRole.House;
            foreach (var m in markers)
            {
                if (m.Type == RoomMarker || m.LocalPos.Y != room.LocalPos.Y)
                {
                    continue;
                }

                var at = (m.LocalPos.X, m.LocalPos.Z);
                if (m.Type.StartsWith("door_", System.StringComparison.Ordinal))
                {
                    // The doorway is air over floor, so the flood fill reaches it: the gap itself, the second
                    // door column and the lane through it stay free.
                    reserved.Add(at);
                    foreach (var d in new[] { (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1) })
                    {
                        reserved.Add((at.Item1 + d.Item1, at.Item2 + d.Item2));
                    }

                    continue;
                }

                if (!cells.Contains(at))
                {
                    continue;
                }

                reserved.Add(at);
                if (m.Type == "vendor") role = RoomFurnisher.RoomRole.Market;
                else if (m.Type == "mission_board" && role != RoomFurnisher.RoomRole.Market) role = RoomFurnisher.RoomRole.Board;
            }

            var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ (n * 7919)));
            RoomFurnisher.Furnish(setCell, region, room.LocalPos.Y, clearance, furniture, role, reserved, rng);
        }
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

    /// <summary>Stamps a greenhouse: a glass house whose beds grow one cultivated crop the player can harvest and
    /// eat. Two builds share this shape — a village grows its crop in soil under a timber-and-glass frame with a
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
    /// band and an accent stripe; multi-storey buildings get climbable ladders between decks. With a
    /// <paramref name="furnish"/> palette and a <paramref name="setCell"/> sink every storey is furnished
    /// (#1828): the ground floor for <paramref name="groundRole"/>, the upper ones as living quarters, from
    /// <paramref name="furnishSeed"/> — the NPC's spot, the door lane and the ladder stay clear.</summary>
    internal static void StampBuilding(System.Action<int, int, int, ushort> set, int ox, int oz, int fp, int storeys,
        ushort wall, ushort accent, ushort glass, ushort ladder, int doorSide, int roofStyle, System.Random rng, bool ruined,
        ushort ceilingLight = 0, RoomFurnisher.Palette? furnish = null, RoomFurnisher.CellSink? setCell = null,
        long furnishSeed = 0, RoomFurnisher.RoomRole groundRole = RoomFurnisher.RoomRole.House)
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

        // Ceiling lights (#1808): a light block set INTO the deck above every storey — one over the middle
        // of a small room, a 2×2 grid over a wide one — so the rooms are lit and the roof stays a solid
        // cover (the cool-room check reads the ceiling as a roof, and nothing hangs into the walkway).
        if (ceilingLight != 0)
        {
            foreach (var (lx, lz) in CeilingLightCells(fp))
            {
                for (int f = 1; f <= storeys; f++)
                {
                    set(ox + lx, f * FloorH, oz + lz, ceilingLight);
                }
            }
        }

        // Interiors (#1828). The region is the hollow room of each storey (one cell in from the shell); the
        // NPC's centre cell, the two door columns' first interior row and the ladder corner stay free.
        if (furnish != null && setCell != null && fp >= 4)
        {
            int fm = fp / 2, fw0 = System.Math.Max(1, fm - 1), fw1 = fm;
            for (int f = 0; f < storeys; f++)
            {
                var region = new List<(int X, int Z)>();
                for (int x = 1; x < fp - 1; x++)
                    for (int z = 1; z < fp - 1; z++)
                    {
                        region.Add((ox + x, oz + z));
                    }

                var reserved = new HashSet<(int X, int Z)>();
                if (f == 0)
                {
                    reserved.Add((ox + fm, oz + fm)); // the resident's spot
                    for (int wv = fw0; wv <= fw1; wv++)
                    {
                        switch (doorSide)
                        {
                            case 0: reserved.Add((ox + wv, oz + 1)); break;
                            case 1: reserved.Add((ox + wv, oz + fp - 2)); break;
                            case 2: reserved.Add((ox + 1, oz + wv)); break;
                            default: reserved.Add((ox + fp - 2, oz + wv)); break;
                        }
                    }
                }

                if (storeys > 1)
                {
                    reserved.Add((ox + 1, oz + 1)); // the ladder …
                    reserved.Add((ox + 2, oz + 1)); // … and the step off it
                    reserved.Add((ox + 1, oz + 2));
                }

                var role = f == 0 ? groundRole : RoomFurnisher.RoomRole.Upper;
                var roomRng = new System.Random(unchecked((int)(furnishSeed ^ (furnishSeed >> 32)) ^ (f * 7919)));
                RoomFurnisher.Furnish(setCell, region, f * FloorH + 1, FloorH - 1, furnish, role, reserved, roomRng);
            }
        }
    }

    /// <summary>Where the ceiling lights of a room <paramref name="fp"/> wide go: the centre cell up to nine
    /// wide, a quarter-point 2×2 grid beyond that. Local (x, z) cells, always inside the shell.</summary>
    internal static (int X, int Z)[] CeilingLightCells(int fp)
    {
        if (fp <= 9)
        {
            return new[] { (fp / 2, fp / 2) };
        }

        int a = fp / 4, b = fp - 1 - fp / 4;
        return new[] { (a, a), (b, a), (a, b), (b, b) };
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
    internal static void DecorateAround(System.Action<int, int, int, ushort> set, int ox, int oz, int fp, int doorSide,
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
