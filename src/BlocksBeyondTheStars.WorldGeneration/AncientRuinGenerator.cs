// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A composed underground structure (#3334) in structure-local coordinates: x 0 … <see cref="Width"/> − 1,
/// z 0 … <see cref="Length"/> − 1, y = 0 the floor row and y 1 … <see cref="Height"/> − 1 the cavity above it. Everything in
/// that box is written by the stamper (air included); the stone shell sits around it. Either a buried G.D.S. ruin
/// (<see cref="IsGdsRuin"/>: the flooded basins (<see cref="BasinColumns"/>) reach <see cref="AncientRuinGenerator.BasinDepth"/>
/// rows under the floor, the Ressaw crystal ore lies in the stone under them (<see cref="OreCells"/>, y &lt; 0), the crates
/// and the remains are placed) or an authored structure enclosed as it was built (<see cref="Source"/> at
/// <see cref="SourceOffset"/>, its own markers for the stamper to honour).
/// </summary>
public sealed class AncientRuinLayout
{
    private readonly ushort[] _blocks;
    private readonly Dictionary<int, (int Tint, int Glow)> _mods;
    private readonly Dictionary<int, int> _shapes;

    internal AncientRuinLayout(int width, int height, int length, ushort[] blocks, Dictionary<int, (int Tint, int Glow)> mods,
        Dictionary<int, int> shapes, AncientRuinFeatures features)
    {
        Width = width;
        Height = height;
        Length = length;
        _blocks = blocks;
        _mods = mods;
        _shapes = shapes;
        Shaft = features.Shaft;
        DoorFace = features.DoorFace;
        BasinColumns = features.Basins;
        OreCells = features.Ore;
        Caches = features.Caches;
        Props = features.Props;
        RessawBlocks = features.RessawBlocks;
        Composition = features.Composition;
        KitKey = features.KitKey;
        Layout = features.Layout;
        IsGdsRuin = features.IsGdsRuin;
        Source = features.Source;
        SourceOffset = features.SourceOffset;
    }

    /// <summary>True for a G.D.S. ruin (the ruin pass ran), false for an authored structure enclosed faithfully.</summary>
    public bool IsGdsRuin { get; }

    /// <summary>An enclosed structure as it was composed (its markers: doors, containers, circuits); null for a G.D.S. ruin.</summary>
    public SettlementStructure? Source { get; }

    /// <summary>Where <see cref="Source"/>'s (0, 0, 0) sits in this box: (offset, 0, offset) — the walkway around it.</summary>
    public int SourceOffset { get; }

    public int Width { get; }

    /// <summary>Rows written from the floor up (the floor row included); the ceiling shell is row <c>Height</c>.</summary>
    public int Height { get; }

    public int Length { get; }

    /// <summary>The ladder column of the entrance tube — the shaft rises straight up from it to the surface.</summary>
    public (int X, int Z) Shaft { get; }

    /// <summary>The face (2 = +X, 4 = +Z) of the tube's doorway onto the street.</summary>
    public int DoorFace { get; }

    /// <summary>Floor columns that sank into a flooded basin: Ressaw from y = 0 down to y = −<see cref="AncientRuinGenerator.BasinDepth"/>.</summary>
    public IReadOnlyList<(int X, int Z)> BasinColumns { get; }

    /// <summary>The Ressaw crystal ore cells, in the stone under the basins (y = −BasinDepth − 1).</summary>
    public IReadOnlyList<Vector3i> OreCells { get; }

    /// <summary>The crate cells in crate order: crate n is <c>gds_cache:n</c>; the first four stand on dry ground nearest
    /// the entrance (they hold the diving suit).</summary>
    public IReadOnlyList<Vector3i> Caches { get; }

    /// <summary>The remains placed in corners: cell and block (a diving helmet, a guardian head or, rarely, a skull).</summary>
    public IReadOnlyList<(Vector3i Cell, ushort Block)> Props { get; }

    /// <summary>How many glowing Ressaw blocks stand in the ruin's walls (authored + set by the stamper).</summary>
    public int RessawBlocks { get; }

    /// <summary>The module per district (index = gx · grid + gz), pinned in the placement record.</summary>
    public IReadOnlyList<string> Composition { get; }

    public string KitKey { get; }

    public CityLayoutSpec Layout { get; }

    public ushort Get(int x, int y, int z) => _blocks[(x * Height + y) * Length + z];

    public (int Tint, int Glow) GetModifier(int x, int y, int z)
        => _mods.TryGetValue((x * Height + y) * Length + z, out var m) ? m : (0, 0);

    public int GetShape(int x, int y, int z) => _shapes.TryGetValue((x * Height + y) * Length + z, out int s) ? s : 0;
}

/// <summary>What the composer decided besides the voxels (internal hand-over to <see cref="AncientRuinLayout"/>).</summary>
internal sealed class AncientRuinFeatures
{
    public (int X, int Z) Shaft;
    public int DoorFace;
    public List<(int X, int Z)> Basins = new();
    public List<Vector3i> Ore = new();
    public List<Vector3i> Caches = new();
    public List<(Vector3i Cell, ushort Block)> Props = new();
    public int RessawBlocks;
    public List<string> Composition = new();
    public string KitKey = string.Empty;
    public CityLayoutSpec Layout;
    public bool IsGdsRuin;
    public SettlementStructure? Source;
    public int SourceOffset;
}

/// <summary>
/// The buried G.D.S. ruins of terrain generation 35 (#3334, Justus' "Alte Städte"): an underground city kit
/// (<see cref="StructureKit.Underground"/>) is composed by the ordinary city composer (<see cref="CityGenerator"/> with the
/// kit's grid, district map and modules) and then made a RUIN, hash-deterministic from the seed:
/// <list type="bullet">
/// <item>abandoned — no marker of a living thing survives (residents, vendors, boards, guards, doors, rooms); only the
/// crate markers (<c>loot</c> / <c>chest</c>) become the ruin's crates, at least <see cref="MinCaches"/>;</item>
/// <item>broken — wall tops crumble in 2 × 2 column patches, windows are smashed, most lights are dead, a few roofs fell in
/// (rubble), and two spots of the floor gave way into the flooded undercroft (sinkholes);</item>
/// <item>flooded — every Ressaw cell at floor level (a module's pool, a sinkhole) sinks into a basin of still Ressaw
/// <see cref="BasinDepth"/> deep, with a handful of Ressaw crystal ore cells in the stone under it;</item>
/// <item>glowing Ressaw blocks are set into a few exposed walls (they crack when a player lingers, #3336), and the remains —
/// empty diving helmets, broken guardian heads and, rarely, a skull — lie in corners (#3342);</item>
/// <item>the entrance: a purple tube with a ladder at one street crossing, a red light over its doorway; the server runs
/// the shaft from its top to the surface.</item>
/// </list>
/// Nothing here touches the world: the server seats the layout, carves, seals and stamps it.
/// </summary>
public static class AncientRuinGenerator
{
    /// <summary>Free rows above the composed structure, under the ceiling shell.</summary>
    public const int Headroom = 1;

    /// <summary>Ressaw rows under the floor in a flooded basin (y = −1 … −BasinDepth; the floor row itself is Ressaw too).</summary>
    public const int BasinDepth = 3;

    /// <summary>The fewest crates a ruin carries — the contract with the loot code: crate n &lt; 4 holds diving-suit piece n.</summary>
    public const int MinCaches = 4;

    /// <summary>Ressaw crystal ore cells per ruin: a handful (at least this many when there is any basin, at most <see cref="MaxOre"/>).</summary>
    public const int MinOre = 3;

    public const int MaxOre = 8;

    /// <summary>The most remains (props) per ruin.</summary>
    public const int MaxProps = 24;

    /// <summary>The share of corner cells that get a prop.</summary>
    public const double PropChance = 0.05;

    /// <summary>Of the props: skulls (rare, Marcel 2026-11-07) and empty diving helmets; the rest are guardian heads.</summary>
    public const double SkullShare = 0.14;

    public const double HelmetShare = 0.43;

    /// <summary>The fewest glowing Ressaw blocks in a ruin's walls.</summary>
    public const int MinRessawBlocks = 6;

    /// <summary>Chebyshev radius around the shaft column that no collapse, prop or rubble reaches — the landing stays clear.</summary>
    public const int EntranceClearance = 4;

    /// <summary>The crate marker kind (the loot code reads <c>gds_cache:&lt;n&gt;</c>).</summary>
    public const string CacheMarker = "gds_cache";

    /// <summary>The walkway around an enclosed authored structure (<see cref="Enclose"/>): the entrance tube stands in its
    /// corner, its doorway opens onto the walkway, and the structure itself is never cut.</summary>
    public const int EnclosureMargin = 4;

    /// <summary>The key prefix of the shipped G.D.S. ruin kits — the rule that decides whether the ruin pass runs.</summary>
    public const string GdsRuinKitPrefix = "ruin_gds";

    /// <summary>Whether a kit is a G.D.S. ruin kit: a CITY kit whose key starts with <see cref="GdsRuinKitPrefix"/>. Only these
    /// go through the ruin pass (abandoned, broken, flooded, crates, remains); every other underground kit or template — an
    /// author's own, a copy of the shipped kit under a new key included — is enclosed exactly as it was built.</summary>
    public static bool IsGdsRuinKit(StructureKit? kit)
        => kit != null && kit.KindOrDefault == StructureKit.KindCity && kit.Key.StartsWith(GdsRuinKitPrefix, System.StringComparison.Ordinal);

    /// <summary>The interior street crossings (lane i, lane j with 1 ≤ i, j &lt; grid) an entrance tube may stand on.</summary>
    public static IReadOnlyList<(int I, int J)> EntranceCrossings(CityLayoutSpec spec)
    {
        var list = new List<(int, int)>();
        for (int i = 1; i < spec.Grid; i++)
        {
            for (int j = 1; j < spec.Grid; j++)
            {
                list.Add((i, j));
            }
        }

        return list;
    }

    /// <summary>The ladder column of an entrance on crossing (<paramref name="i"/>, <paramref name="j"/>): one cell into the
    /// crossing, so the 3 × 3 tube stands on street and the crossing's fourth lane stays free for its doorway.</summary>
    public static (int X, int Z) ShaftAt(CityLayoutSpec spec, int i, int j)
        => (i * (spec.DistrictSize + spec.Street) + 1, j * (spec.DistrictSize + spec.Street) + 1);

    /// <summary>Composes a ruin. <paramref name="composition"/> follows the composers' pin rule (#1872): a non-empty list
    /// replays, an empty one records the picks, null does neither. The entrance stands on crossing
    /// (<paramref name="crossI"/>, <paramref name="crossJ"/>) — see <see cref="EntranceCrossings"/>. From terrain generation 38
    /// (<paramref name="terrainGeneration"/>, #3516) the walls break and the roofs fall through the shared ruin pass
    /// (<see cref="AncientRuinBuilder.RuinPass"/>); every older world (and the default 0) keeps the ruin's own decay.</summary>
    public static AncientRuinLayout Compose(long seed, GameContent content, CityLayoutSpec spec, StructureKit? kit,
        IReadOnlyList<StructureTemplate>? pool, IList<string>? composition, int crossI, int crossJ, System.Action<string>? warn = null,
        int terrainGeneration = 0)
    {
        var city = CityGenerator.Generate(seed, content, System.Array.Empty<CityGenerator.OpenZone>(), null, 0.0, composition, warn,
            spec, kit, pool);
        var ruin = new AncientRuinBuilder(seed, content, city, spec.Height + Headroom, ShaftAt(spec, crossI, crossJ));
        IReadOnlyList<SettlementMarker> crates = city.Markers;
        if (StructureDecay.Applies(terrainGeneration))
        {
            crates = ruin.RuinPass(city.Markers);
            ruin.Collapse(sinkholesOnly: true);
        }
        else
        {
            ruin.Decay();
            ruin.Collapse();
        }

        ruin.SetRessawIntoWalls();
        int door = ruin.Tube(gds: true);
        var basins = ruin.Basins();
        var caches = ruin.Caches(crates);
        var features = new AncientRuinFeatures
        {
            Shaft = ruin.Shaft,
            DoorFace = door,
            Basins = basins,
            Ore = ruin.Ore(basins),
            Caches = caches,
            Props = ruin.Props(caches),
            Composition = composition != null ? new List<string>(composition) : new List<string>(),
            KitKey = kit?.Key ?? string.Empty,
            Layout = spec,
            IsGdsRuin = true,
        };
        return ruin.Build(features);
    }

    /// <summary>Encloses an authored structure as it was built (an author's underground kit or whole template): the structure
    /// at (<see cref="EnclosureMargin"/>, 0, <see cref="EnclosureMargin"/>) of a box one walkway wider on every side and
    /// <see cref="Headroom"/> rows taller, a stone floor wherever it has none, and a plain entrance tube with a ladder in the
    /// walkway's corner (shaft (1, 1)). No ruin pass: its blocks, tints, shapes and markers stay exactly as authored.</summary>
    public static AncientRuinLayout Enclose(long seed, GameContent content, SettlementStructure structure, string kitKey,
        IList<string>? composition)
    {
        var box = new AncientRuinBuilder(seed, content, structure, structure.Height + Headroom, (1, 1), EnclosureMargin);
        box.FillFloor();
        int door = box.Tube(gds: false);
        var features = new AncientRuinFeatures
        {
            Shaft = box.Shaft,
            DoorFace = door,
            Composition = composition != null ? new List<string>(composition) : new List<string>(),
            KitKey = kitKey,
            Source = structure,
            SourceOffset = EnclosureMargin,
        };
        return box.Build(features);
    }
}
