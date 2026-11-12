// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A ship design a layout wreck may be built from (#3521): the definition, its layout and its draw weight.</summary>
public readonly struct WreckDesign
{
    public WreckDesign(ShipDefinition design, ShipLayout layout, int weight)
    {
        Design = design;
        Layout = layout;
        Weight = weight;
    }

    public ShipDefinition Design { get; }

    public ShipLayout Layout { get; }

    public int Weight { get; }
}

/// <summary>
/// #3521: wrecks built from the real ship layout (terrain generation 38). The hammerhead used to crash as a 15 × 5 × 15
/// box; now every wreck is the very hull the flyable ship is built from (<see cref="ShipCellBlocks"/>), breached and
/// scorched by the shared ruin pass (<see cref="StructureRuiner"/>, profile <c>hull</c>). The result is the same
/// <see cref="WreckStructure"/>, so repairing and claiming it work unchanged.
/// </summary>
public static partial class WreckGenerator
{
    /// <summary>The revision of the layout wreck, pinned in the planet wreck's record (<c>rev=…</c>). Revision 1 runs the
    /// ruin pass in its revision 1, whatever the pass's newest revision is.</summary>
    public const int LayoutRevision = 1;

    /// <summary>The local row of a planet wreck that lies at the ground surface: the hull is sunk one block (the crash
    /// pose), so its floor (row 0) and this row are in the ground — the decay never breaches them.</summary>
    public const int PlanetGroundRow = 1;

    /// <summary>The widest hull (x or z extent, every cell counted) a wreck is built from: the crash site settlements keep
    /// clear is 2 × 14 blocks wide, and the hull is centred on it.</summary>
    public const int MaxLayoutExtent = 28;

    /// <summary>How hard the ruin pass hits a layout wreck (0..1).</summary>
    private const double LayoutDecayStrength = 0.75;

    /// <summary>The ruin pass revision of <see cref="LayoutRevision"/> 1.</summary>
    private const int LayoutRuinRevision = 1;

    /// <summary>The block the scorch marks are made of — what the box wreck's scorch has always been.</summary>
    private const string ScorchBlock = "carbon";

    /// <summary>The origin (<c>human</c> or <c>alien</c>) a wreck of this design and seed has — the first draw of both
    /// generators, so it is known even when the layout is gone.</summary>
    public static string OriginOf(string designKey, long seed) => DrawOrigin(NewRandom(designKey, seed));

    private static System.Random NewRandom(string designKey, long seed)
        => new(unchecked((int)(seed ^ (seed >> 32)) ^ (int)WorldGenerator.StableHash(designKey)));

    private static string DrawOrigin(System.Random rng) => rng.NextDouble() < 0.4 ? "alien" : "human";

    /// <summary>
    /// The wreck of <paramref name="design"/> built from <paramref name="layout"/>: the exact layout cells shifted so the
    /// lowest corner is (0, 0, 0) (<see cref="WreckStructure.LayoutOffset"/>), hatches and doors open, a block no item
    /// places as hull and an alien hull of crystal (<see cref="WreckLayoutHull"/>). The decay tears a gash at the rear
    /// doorway and runs the ruin pass (profile <c>hull</c>: breaches, scorch, smashed glass, dead lights); rows up to
    /// <paramref name="groundRow"/> are never touched (-1 = none: a wreck in space). Markers come from the station tiles —
    /// cargo a loot cache, reactor and workshop a module, cockpit and console a data terminal — topped up on free floor
    /// cells to at least two caches and one module.
    /// </summary>
    public static WreckStructure GenerateFromLayout(ShipDefinition design, ShipLayout layout, long seed, GameContent content,
        int groundRow = PlanetGroundRow)
    {
        var rng = NewRandom(design.Key, seed);
        string origin = DrawOrigin(rng);
        var hull = WreckLayoutHull.Build(layout, content, origin == "alien");
        var markers = LayoutMarkers(hull, rng);

        var grid = VoxelGrid.CopyOf(hull.W, hull.H, hull.L, hull.Intact, hull.Mods, hull.Shapes);
        TearGash(grid, hull, groundRow);
        var markerCells = new List<Vector3i>();
        foreach (var m in markers)
        {
            markerCells.Add(m.LocalPos);
        }

        long ruinSeed = unchecked((long)StableHashing.Mixed("ruin:wreck:" + design.Key, seed));
        StructureRuiner.Run(grid, content, ruinSeed, LandmarkRules.ProfileHull, LayoutDecayStrength, 0, markerCells, "stone",
            LayoutRuinRevision, HullRuinOptions(groundRow));

        for (int i = 0; i < markers.Count; i++)
        {
            var at = RuinMarkerRules.Seat(grid, markers[i].LocalPos, groundRow + 1);
            markers[i] = new WreckMarker(markers[i].Type, at);
        }

        var extras = new WreckCellExtras(hull.Offset, hull.Interior, grid.Mods, grid.Shapes, hull.Mods, hull.Shapes);
        return new WreckStructure(hull.W, hull.H, hull.L, design.Key, origin, grid.Blocks, hull.Intact, markers, extras);
    }

    /// <summary>The ruin options of a wreck: scorch marks of <see cref="ScorchBlock"/>, most of them around the breaches, and
    /// the rows in the ground protected.</summary>
    private static RuinOptions HullRuinOptions(int groundRow) => new()
    {
        Overgrowth = false,
        ProtectRowsUpTo = groundRow,
        Scorch = new RuinScorch { Block = ScorchBlock, NearBreachChance = 0.3, ElsewhereChance = 0.03 },
    };

    /// <summary>The crash impact: a gash torn around the rear doorway (the hatch or door with the lowest z, nearest the
    /// middle on a tie) — the doorway's gap and a column either side, from the door's row three rows up — or, on a hull
    /// without a door, in the middle of its lowest-z wall. Never in a row up to <paramref name="groundRow"/>.</summary>
    private static void TearGash(VoxelGrid grid, WreckLayoutHull hull, int groundRow)
    {
        if (RearOpening(hull) is not { } door)
        {
            return;
        }

        int lo = door.X, hi = door.X;
        for (int s = 1; s <= 3 && hull.In(door.X - s, door.Y, door.Z) && hull.IntactAt(door.X - s, door.Y, door.Z) == 0; s++)
        {
            lo = door.X - s;
        }

        for (int s = 1; s <= 3 && hull.In(door.X + s, door.Y, door.Z) && hull.IntactAt(door.X + s, door.Y, door.Z) == 0; s++)
        {
            hi = door.X + s;
        }

        for (int x = lo - 1; x <= hi + 1; x++)
        {
            for (int y = System.Math.Max(door.Y, groundRow + 1); y <= door.Y + 3; y++)
            {
                if (grid.In(x, y, door.Z) && y > hull.FloorRow)
                {
                    grid.Clear(x, y, door.Z);
                }
            }
        }
    }

    /// <summary>The cell the gash centres on: the rear doorway, else the middle of the lowest-z wall one row above the floor.</summary>
    private static Vector3i? RearOpening(WreckLayoutHull hull)
    {
        Vector3i? best = null;
        int mid = hull.W / 2;
        foreach (var d in hull.Doorways)
        {
            if (best is not { } b || d.Z < b.Z || (d.Z == b.Z && System.Math.Abs(d.X - mid) < System.Math.Abs(b.X - mid)))
            {
                best = d;
            }
        }

        if (best is not null)
        {
            return best;
        }

        int y = hull.FloorRow + 1;
        for (int z = 0; z < hull.L; z++)
        {
            if (hull.IntactAt(mid, y, z) != 0)
            {
                return new Vector3i(mid, y, z);
            }
        }

        return null;
    }

    /// <summary>A stable fingerprint of a layout (its size and every cell in file order), recorded with a planet wreck so a
    /// changed layout is never taken for the one its blocks were stamped from.</summary>
    public static string LayoutFingerprint(ShipLayout layout)
    {
        var sb = new StringBuilder();
        sb.Append(layout.Width).Append(',').Append(layout.Height).Append(',').Append(layout.Length);
        foreach (var c in layout.Cells)
        {
            sb.Append('|').Append(c.X).Append(',').Append(c.Y).Append(',').Append(c.Z).Append(',').Append(c.Kind).Append(',')
                .Append(c.Id).Append(',').Append(c.Tint).Append(',').Append(c.Glow).Append(',').Append(c.Shape);
        }

        return StableHashing.Mixed(sb.ToString()).ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether a layout fits a crash site: its x and z extent (every cell counted) is at most
    /// <see cref="MaxLayoutExtent"/>.</summary>
    public static bool FitsCrashSite(ShipLayout layout)
    {
        if (layout.Cells.Count == 0)
        {
            return false;
        }

        var (min, max) = WreckLayoutHull.Bounds(layout);
        return max.X - min.X + 1 <= MaxLayoutExtent && max.Z - min.Z + 1 <= MaxLayoutExtent;
    }

    /// <summary>
    /// The designs a layout wreck is drawn from: every shipped ship with a layout that fits a crash site
    /// (<see cref="FitsCrashSite"/>), in key order (weight 1), then every user ship whose uses include <c>wreck</c> and
    /// whose layout fits and whose pack is in <paramref name="enabledPacks"/> (null or empty = every pack), in load order with
    /// its own weight. <paramref name="claimable"/> (the planet wreck, which a repair turns into a ship of the fleet) also asks
    /// a user ship to fit a landing pad.
    /// </summary>
    public static List<WreckDesign> LayoutDesigns(GameContent content, bool claimable, IReadOnlyCollection<string>? enabledPacks = null)
    {
        var shipped = new List<ShipDefinition>(content.Ships.Values);
        shipped.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var pool = new List<WreckDesign>();
        foreach (var ship in shipped)
        {
            if (content.GetShipLayout(ship.Layout) is { } layout && FitsCrashSite(layout))
            {
                pool.Add(new WreckDesign(ship, layout, 1));
            }
        }

        foreach (var user in content.UserShipsFor(UserShipUses.Wreck, enabledPacks))
        {
            if (FitsCrashSite(user.Layout) && (!claimable || user.FitsPad))
            {
                pool.Add(new WreckDesign(user.Definition, user.Layout, System.Math.Max(1, user.Weight)));
            }
        }

        return pool;
    }

    /// <summary>The design a hash picks from <paramref name="pool"/>, by weight; null for an empty pool.</summary>
    public static WreckDesign? PickDesign(IReadOnlyList<WreckDesign> pool, ulong hash)
    {
        long total = 0;
        foreach (var d in pool)
        {
            total += d.Weight;
        }

        if (total <= 0)
        {
            return null;
        }

        long at = (long)(hash % (ulong)total);
        foreach (var d in pool)
        {
            at -= d.Weight;
            if (at < 0)
            {
                return d;
            }
        }

        return pool[pool.Count - 1];
    }
}
