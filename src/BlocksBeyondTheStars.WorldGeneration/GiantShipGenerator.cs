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
/// #3609: the GIANT SHIP WRECKS (<see cref="GiantShipRules"/>) — a huge authored spaceship (<c>data/giant_ships/*.json</c>, a
/// <see cref="StructureTemplate"/> with <c>giantShip: true</c>) exists only as a wreck, derived from the intact design with the
/// instance's seed. Pure and deterministic, like <see cref="LandmarkGenerator"/>: the server stamps with it and re-derives the
/// markers with it on a later load, the editor previews a wreck with it, the Tools command dumps one.
/// <list type="bullet">
/// <item><see cref="BuildPlanet"/> — lying on a planet: read the compact body (the landmark reader), a quarter turn, ruin
/// revision 3 with profile <c>crash</c> (or the template's ruin zones): the rows up to the ground row are the buried keel, what
/// no longer hangs on them falls and lies on the ground as scrap (crystal shards for an alien ship), plants grow through only
/// on a body with air and water freezes on one without. The same <see cref="LandmarkLayout"/> the landmark stamp writes.</item>
/// <item><see cref="BuildSpace"/> — drifting in space: never turned, revision 3 with profile <c>hull</c>: no ground, every
/// piece of at least <see cref="MinPieceCells"/> blocks stays where it is, the crumbs go, water is ice. A
/// <see cref="GiantSpaceLayout"/> (the derelict technique turns it into a station structure).</item>
/// </list>
/// Markers: a wreck keeps its chests (and the classic loot markers), sounds and entrances — in space also the doorways
/// (<see cref="DoorMarker"/>), from which the derelict decay reads the rooms. On a planet a chest or a sound whose floor fell
/// rests on what is left (the landmark rule); in space nothing falls — a sound stays where it is, a chest while it stands. One
/// left without a floor (or with its cell filled) moves to the nearest free cell with a floor under it and a roof above it,
/// inside the wreck — the boarding arrival is always standable.
/// </summary>
public static partial class GiantShipGenerator
{
    /// <summary>The tier name a giant wreck's structure carries (<see cref="SettlementStructure.Tier"/>).</summary>
    public const string Tier = GiantShipRules.RecordKind;

    /// <summary>The ruin revision a giant wreck is built with (pin it with the record: a later revision never moves a
    /// standing wreck's markers).</summary>
    public const int RuinRevision = StructureRuiner.GiantRevision;

    /// <summary>In space a piece of at least this many touching blocks stays; smaller ones drift off.</summary>
    public const int MinPieceCells = StructureRuiner.MinPieceCells;

    /// <summary>The marker of a doorway between two rooms (<c>axis=x|z</c>), kept in space for the derelict decay.</summary>
    public const string DoorMarker = "door_slide";

    /// <summary>The marker types a wreck on a planet keeps: what a ruin keeps plus the entrances.</summary>
    public static readonly string[] PlanetMarkers =
    {
        LandmarkRules.ChestMarker, "loot", "data_terminal", "relic_cache", LandmarkRules.SoundMarker, LandmarkRules.DenMarker,
        GiantShipRules.EntranceMarker,
    };

    /// <summary>The marker types a wreck in space keeps: the planet's plus the doorways — and (#3902) a warship's bridge
    /// console and its lamps, which the ship-mode decay turns into blocks (no shipped giant wreck carries one).</summary>
    public static readonly string[] SpaceMarkers =
    {
        LandmarkRules.ChestMarker, "loot", "data_terminal", "relic_cache", LandmarkRules.SoundMarker, LandmarkRules.DenMarker,
        GiantShipRules.EntranceMarker, DoorMarker, GiantShipRules.ConsoleMarker, GiantShipRules.LampMarker,
    };

    /// <summary>The rubble of a human wreck: scrap, rusted panels, iron and carbon fragments.</summary>
    private static readonly RuinWeightedBlock[] HumanRubble =
    {
        new("scrap_metal", 4), new("scrap_pile", 3), new("rusted_panel", 2), new("carbon", 2), new("iron_block", 1),
    };

    /// <summary>The rubble of an alien (crystal) wreck: crystal shards, no rust.</summary>
    private static readonly RuinWeightedBlock[] AlienRubble = { new("crystal", 1) };

    /// <summary>Whether the template is an alien (crystal) ship: its scorch and rubble are crystal, never rust or carbon.</summary>
    public static bool IsAlien(StructureTemplate t)
        => string.Equals(t.Style, GiantShipRules.StyleAlien, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The wreck of <paramref name="t"/> lying on a planet: turned by <paramref name="turns"/> quarter turns, ruined with
    /// revision 3 (profile <c>crash</c>, or the template's ruin zones) from <paramref name="seed"/>, the markers it keeps
    /// seated. <paramref name="surfaceBlock"/> is the biome surface (overgrowth, material tokens); <paramref name="atmosphere"/>
    /// says whether the body has air — plants grow through only then, and without it water freezes. <paramref name="abandonBeds"/>
    /// (a world of terrain generation 45, <see cref="AbandonedBeds.Applies"/>): the wreck's beds are the abandoned twin, a lone
    /// one given its foot on a free floor cell beside it away from every marker (<see cref="AbandonedBeds"/>).
    /// </summary>
    public static LandmarkLayout BuildPlanet(StructureTemplate t, long seed, int turns, GameContent content, string surfaceBlock,
        bool atmosphere, bool abandonBeds = false)
    {
        turns = ((turns % 4) + 4) % 4;
        var grid = Fill(t, turns, content, surfaceBlock, out var markers);
        int groundRow = System.Math.Clamp(t.GroundRow, 0, grid.H - 1);
        bool alien = IsAlien(t);
        var options = new RuinOptions
        {
            Overgrowth = atmosphere,
            RubblePalette = alien ? AlienRubble : HumanRubble,
            Scorch = ScorchFor(alien),
            FreezeWater = !atmosphere,
        };
        Ruin(t, grid, markers, RuinSeed("planet", t, seed), LandmarkRules.ProfileCrash, groundRow, surfaceBlock, options, content,
            ZonesFor(t, turns, space: false));
        AbandonBeds(grid, markers, groundRow, content, abandonBeds);
        SettleMarkers(grid, markers, groundRow, PlanetMarkers, content);

        int cavities = 0;
        foreach (bool c in grid.Cavity)
        {
            cavities += c ? 1 : 0;
        }

        var structure = new SettlementStructure(grid.W, grid.H, grid.L, Tier, ruined: true, inhabitant: string.Empty, grid.Blocks,
            markers, buildingCount: 1, grid.Mods, grid.Shapes);
        return new LandmarkLayout(structure, groundRow, grid.Cavity, ruined: true, turns, cavities);
    }

    /// <summary>
    /// The wreck of <paramref name="t"/> drifting in space: never turned (template coordinates), ruined with revision 3
    /// (profile <c>hull</c> — every zone a hull with its own strength) from <paramref name="seed"/>; every piece of at least
    /// <see cref="MinPieceCells"/> blocks stays where it is, water is ice. The markers it keeps (doorways included) are in
    /// template coordinates; <see cref="GiantSpaceLayout.Entrance"/> is the boarding arrival. <paramref name="abandonBeds"/>: the
    /// beds are the abandoned twin, as on a planet (<see cref="BuildPlanet"/>). <paramref name="fresh"/> (#3902, the escort
    /// cruiser's wreck): the ship was shot a moment ago, not left to the centuries — the same holes and broken glass, but no
    /// scorch marks at all (no soot tint, no crystal). False (every giant wreck) builds exactly what it always built.
    /// </summary>
    public static GiantSpaceLayout BuildSpace(StructureTemplate t, long seed, GameContent content, bool abandonBeds = false,
        bool fresh = false)
    {
        var grid = Fill(t, 0, content, "stone", out var markers);
        int intact = 0;
        foreach (ushort b in grid.Blocks)
        {
            intact += b != 0 ? 1 : 0;
        }

        var options = new RuinOptions { Overgrowth = false, Scorch = fresh ? null : ScorchFor(IsAlien(t)), FreezeWater = true };
        Ruin(t, grid, markers, RuinSeed("space", t, seed), LandmarkRules.ProfileHull, groundRow: 0, "stone", options, content,
            ZonesFor(t, 0, space: true));
        AbandonBeds(grid, markers, groundRow: 0, content, abandonBeds);
        var footing = SettleMarkers(grid, markers, groundRow: 0, SpaceMarkers, content, space: true);
        return new GiantSpaceLayout(t.Key, grid, markers, EntranceOf(markers, footing), intact);
    }

    /// <summary>The soot a human hull's scorch patches are tinted with: the plating stays (its texture and shape), darkened.</summary>
    public const int SootTint = 0x3A3734;

    /// <summary>The scorch marks (revision 3 lays them as patches round the breaches and a few burn zones): soot on a human hull
    /// — the plating darkened, not replaced — and crystal shards on an alien one (thinner: crystal glows).</summary>
    private static RuinScorch ScorchFor(bool alien) => alien
        ? new RuinScorch { Block = "crystal", NearBreachChance = 0.2, ElsewhereChance = 0.005 }
        : new RuinScorch { Tint = SootTint, NearBreachChance = 0.3, ElsewhereChance = 0.02 };

    /// <summary>The ruin pass's seed for a setting: the instance seed mixed with the setting and the ship, so the planet and the
    /// space wreck of one seed never share a pattern.</summary>
    private static long RuinSeed(string setting, StructureTemplate t, long seed)
        => unchecked((long)StableHashing.Mixed("ruin:giant:" + setting + ":" + t.Key, seed));

    /// <summary>The intact body in a grid, turned (the landmark reader: compact rows and listed cells, keep bits, cavities),
    /// and its markers.</summary>
    private static VoxelGrid Fill(StructureTemplate t, int turns, GameContent content, string surfaceBlock,
        out List<SettlementMarker> markers)
    {
        bool swap = (turns & 1) == 1;
        int w = System.Math.Max(1, swap ? t.Length : t.Width);
        int h = System.Math.Max(1, t.Height);
        int l = System.Math.Max(1, swap ? t.Width : t.Length);
        var grid = new VoxelGrid(w, h, l);
        var materials = ModuleMaterials.ForSettlement("village", surfaceBlock, alien: false, content);
        LandmarkGenerator.FillBlocks(t, turns, grid, content, materials);
        markers = LandmarkGenerator.ReadMarkers(t, turns, grid, content);
        return grid;
    }

    /// <summary>Generation 45: the ruined wreck's beds become the abandoned twin — after the ruin pass (a bed it broke stays
    /// broken), before the markers settle, never reaching a marker's column with a new foot, and below the ground row only into
    /// the hull's own air (a cavity), never into the ground it lies in.</summary>
    private static void AbandonBeds(VoxelGrid grid, List<SettlementMarker> markers, int groundRow, GameContent content, bool abandonBeds)
    {
        if (abandonBeds)
        {
            var away = AbandonedBeds.AwayFromMarkers(grid, markers);
            AbandonedBeds.Apply(grid, content, (x, y, z) => away(x, y, z) && (y >= groundRow || grid.Cavity[grid.Index(x, y, z)]));
        }
    }

    private static void Ruin(StructureTemplate t, VoxelGrid grid, List<SettlementMarker> markers, long seed, string profile,
        int groundRow, string surfaceBlock, RuinOptions options, GameContent content, List<RuinZoneBox> zones)
    {
        var cells = new Vector3i[markers.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = markers[i].LocalPos;
        }

        StructureRuiner.RunZoned(grid, content, seed, profile, t.RuinStrength, groundRow, cells, surfaceBlock, RuinRevision, options,
            zones);
    }

    /// <summary>The template's ruin zones in the grid's coordinates, turned with the ship; the profile for the setting
    /// (<see cref="LandmarkRules.GiantProfileOf"/>), the strength as authored.</summary>
    private static List<RuinZoneBox> ZonesFor(StructureTemplate t, int turns, bool space)
    {
        var boxes = new List<RuinZoneBox>();
        foreach (var z in t.RuinZones ?? new List<RuinZone>())
        {
            if (z is null)
            {
                continue;
            }

            var (ax, az) = LandmarkGenerator.Turn(z.X0, z.Z0, t.Width, t.Length, turns);
            var (bx, bz) = LandmarkGenerator.Turn(z.X1, z.Z1, t.Width, t.Length, turns);
            if (turns == 0 || t.Width <= 0 || t.Length <= 0)
            {
                (ax, az, bx, bz) = (z.X0, z.Z0, z.X1, z.Z1);
            }

            boxes.Add(new RuinZoneBox(ax, z.Y0, az, bx, z.Y1, bz, LandmarkRules.GiantProfileOf(z, space), z.Strength));
        }

        return boxes; // clipped to the grid by the pass
    }
}
