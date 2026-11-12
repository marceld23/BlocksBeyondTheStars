// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3521: wrecks built from the real ship layout — the flyable hull's cells (the shared <see cref="ShipCellBlocks"/>
/// mapping), a repair mask a player can finish, the shared ruin pass as the decay, markers from the station tiles.
/// </summary>
public sealed class WreckLayoutGenerationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly long[] Seeds = { 1, 2, 3, 4, 5, 6 };
    private readonly ITestOutputHelper _output;

    public WreckLayoutGenerationTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> LayoutShips()
        => Content.Ships.Values.Where(s => Content.GetShipLayout(s.Layout) is not null).OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new object[] { s.Key });

    private static (ShipDefinition Design, ShipLayout Layout) Ship(string key)
    {
        var design = Content.GetShip(key)!;
        return (design, Content.GetShipLayout(design.Layout)!);
    }

    private static string KeyOf(ushort id) => Content.BlockById(new BlockId(id))?.Key ?? "?";

    private static bool Placeable(string blockKey) => Content.Items.Values.Any(i => i.PlacesBlock == blockKey);

    /// <summary>What the flyable ship builds at each layout cell (the floor guarantee included), and its doorways.</summary>
    private static (Dictionary<Vector3i, ushort> Cells, HashSet<Vector3i> Doorways) FlyingHull(ShipLayout layout)
    {
        var palette = ShipCellPalette.From(Content);
        var cells = new Dictionary<Vector3i, ushort>();
        var doorways = new HashSet<Vector3i>();
        foreach (var c in layout.Cells)
        {
            var mapped = ShipCellBlocks.Map(c, palette, Content);
            var p = new Vector3i(c.X, c.Y, c.Z);
            if (mapped.Role is ShipCellRole.Door or ShipCellRole.Opening)
            {
                doorways.Add(p);
            }
            else
            {
                cells[p] = mapped.Block.Value;
            }
        }

        foreach (var (x, z) in ShipCellBlocks.FloorFootprint(layout))
        {
            cells.TryAdd(new Vector3i(x, 0, z), palette.Wall.Value);
        }

        return (cells, doorways);
    }

    [Theory]
    [MemberData(nameof(LayoutShips))]
    public void IntactHull_IsTheFlyingShipsCells_AndEveryCellCanBeRepaired(string ship)
    {
        var (design, layout) = Ship(ship);
        var (flying, doorways) = FlyingHull(layout);
        string wall = "iron_wall";
        foreach (long seed in Seeds)
        {
            var w = WreckGenerator.GenerateFromLayout(design, layout, seed, Content);
            Assert.True(w.FromLayout);
            var o = w.LayoutOffset;
            string hull = w.Origin == "alien" ? "crystal" : wall;
            foreach (var (p, block) in flying)
            {
                string expected = KeyOf(block);
                expected = !Placeable(expected) || expected == wall ? hull : expected; // carbon, the data_cache console → hull
                Assert.Equal(expected, KeyOf(w.IntactAt(p.X - o.X, p.Y - o.Y, p.Z - o.Z)));
            }

            foreach (var d in doorways.Where(d => !flying.ContainsKey(d)))
            {
                Assert.Equal((ushort)0, w.IntactAt(d.X - o.X, d.Y - o.Y, d.Z - o.Z)); // the way in stays open
            }

            Assert.Equal(flying.Count, w.IntactHullCount()); // nothing else: no box, no extra cell
            for (int x = 0; x < w.Width; x++)
                for (int y = 0; y < w.Height; y++)
                    for (int z = 0; z < w.Length; z++)
                    {
                        if (w.IntactAt(x, y, z) is var b && b != 0)
                        {
                            Assert.True(Placeable(KeyOf(b)), $"{ship}: no item places '{KeyOf(b)}', so the wreck could never be repaired");
                        }
                    }
        }
    }

    [Theory]
    [MemberData(nameof(LayoutShips))]
    public void Decay_OnlyBreachesAndScorches_AndNeverTouchesTheRowsInTheGround(string ship)
    {
        var (design, layout) = Ship(ship);
        foreach (long seed in Seeds)
        {
            var w = WreckGenerator.GenerateFromLayout(design, layout, seed, Content);
            Assert.True(w.BreachCount() > 0, $"{ship}/{seed}: a wreck has breaches");
            Assert.True(w.BreachCount() < w.IntactHullCount() / 2, $"{ship}/{seed}: a wreck is still a ship ({w.BreachCount()} of {w.IntactHullCount()} gone)");
            for (int x = 0; x < w.Width; x++)
                for (int y = 0; y < w.Height; y++)
                    for (int z = 0; z < w.Length; z++)
                    {
                        ushort now = w.Get(x, y, z), intact = w.IntactAt(x, y, z);
                        if (y <= WreckGenerator.PlanetGroundRow)
                        {
                            Assert.Equal(intact, now); // the floor and the course in the ground are never breached
                        }
                        else if (now != 0 && now != intact)
                        {
                            string key = KeyOf(now);
                            Assert.True(key == "carbon" || key.EndsWith("_off", StringComparison.Ordinal),
                                $"{ship}/{seed}: the decay put '{key}' where the hull has '{KeyOf(intact)}'");
                        }

                        Assert.False(intact == 0 && now != 0, $"{ship}/{seed}: the decay added a block at ({x},{y},{z})");
                    }

            _output.WriteLine($"{ship}/{seed}: {w.Origin}, {w.BreachCount()} of {w.IntactHullCount()} hull cells breached");
        }
    }

    [Theory]
    [MemberData(nameof(LayoutShips))]
    public void Markers_ComeFromTheStations_WithAtLeastTwoCachesAndAModule(string ship)
    {
        var (design, layout) = Ship(ship);
        bool hasCargo = layout.Cells.Any(c => c.Kind == "station" && c.Id == "cargo");
        bool hasTerminal = layout.Cells.Any(c => c.Kind == "station" && c.Id is "cockpit" or "console");
        foreach (long seed in Seeds)
        {
            var w = WreckGenerator.GenerateFromLayout(design, layout, seed, Content);
            Assert.True(w.Markers.Count(m => m.Type == "loot") >= 2, $"{ship}/{seed}: two loot caches");
            Assert.True(w.Markers.Count(m => m.Type == "module") >= 1, $"{ship}/{seed}: a module");
            Assert.Equal(hasTerminal, w.Markers.Any(m => m.Type == "data_terminal"));
            foreach (var m in w.Markers)
            {
                var p = m.LocalPos;
                Assert.True(w.InBounds(p.X, p.Y, p.Z), $"{ship}/{seed}: {m.Type} at {p} lies outside the hull");
                Assert.Equal((ushort)0, w.Get(p.X, p.Y, p.Z));
                Assert.True(w.IsInterior(p.X, p.Y, p.Z), $"{ship}/{seed}: {m.Type} at {p} is not inside the hull");
                Assert.NotEqual((ushort)0, w.Get(p.X, p.Y - 1, p.Z)); // it lies on the floor (the rows in the ground stand)
            }

            if (hasCargo)
            {
                var o = w.LayoutOffset;
                var cargo = layout.Cells.Where(c => c.Kind == "station" && c.Id == "cargo")
                    .Select(c => new Vector3i(c.X - o.X, c.Y - o.Y, c.Z - o.Z)).ToList();
                Assert.Contains(w.Markers, m => m.Type == "loot" && cargo.Any(c =>
                    Math.Abs(c.X - m.LocalPos.X) + Math.Abs(c.Z - m.LocalPos.Z) <= 1 && m.LocalPos.Y - c.Y is 0 or 1));
            }
        }
    }

    [Fact]
    public void Generation_IsDeterministic_AndTheSeedMatters()
    {
        var (design, layout) = Ship("hammerhead");
        var a = WreckGenerator.GenerateFromLayout(design, layout, 4242, Content);
        var b = WreckGenerator.GenerateFromLayout(design, layout, 4242, Content);
        var c = WreckGenerator.GenerateFromLayout(design, layout, 4243, Content);
        Assert.Equal(Cells(a), Cells(b));
        Assert.Equal(a.Markers.Select(m => (m.Type, m.LocalPos)), b.Markers.Select(m => (m.Type, m.LocalPos)));
        Assert.NotEqual(Cells(a), Cells(c));
        Assert.Equal(WreckGenerator.OriginOf(design.Key, 4242), a.Origin);
        Assert.Equal(WreckGenerator.Generate(design, 4242, Content).Origin, a.Origin); // the same first draw as the box
    }

    [Fact]
    public void AlienWrecks_AreCrystal_HumanWrecks_AreIron()
    {
        var (design, layout) = Ship("corvette");
        var wrecks = Enumerable.Range(1, 40).Select(s => WreckGenerator.GenerateFromLayout(design, layout, s, Content)).ToList();
        var alien = wrecks.First(w => w.Origin == "alien");
        var human = wrecks.First(w => w.Origin == "human");
        Assert.Contains("crystal", Keys(alien));
        Assert.DoesNotContain("iron_wall", Keys(alien));
        Assert.Contains("iron_wall", Keys(human));
        Assert.DoesNotContain("crystal", Keys(human));
    }

    [Fact]
    public void ASpaceWreck_HasNoGround_SoItsFloorMayBreak()
    {
        int floorBreaches = 0;
        foreach (var row in LayoutShips())
        {
            var (design, layout) = Ship((string)row[0]);
            foreach (long seed in Seeds)
            {
                var w = WreckGenerator.GenerateFromLayout(design, layout, seed, Content, groundRow: -1);
                for (int x = 0; x < w.Width; x++)
                    for (int z = 0; z < w.Length; z++)
                    {
                        floorBreaches += w.IsBreach(x, 0, z) || w.IsBreach(x, 1, z) ? 1 : 0;
                    }
            }
        }

        Assert.True(floorBreaches > 0, "without ground the decay may breach the lowest rows too");
    }

    [Fact]
    public void LayoutFingerprint_IsStable_AndSeesEveryChange()
    {
        var (_, layout) = Ship("scout");
        string fp = WreckGenerator.LayoutFingerprint(layout);
        Assert.Equal(fp, WreckGenerator.LayoutFingerprint(Ship("scout").Layout));
        Assert.Matches("^[0-9a-f]{16}$", fp);
        Assert.NotEqual(fp, WreckGenerator.LayoutFingerprint(Ship("courier").Layout));

        var copy = new ShipLayout
        {
            Key = layout.Key,
            Width = layout.Width,
            Height = layout.Height,
            Length = layout.Length,
            Cells = layout.Cells.Select(c => new ShipLayoutCell { X = c.X, Y = c.Y, Z = c.Z, Kind = c.Kind, Id = c.Id }).ToList(),
        };
        Assert.Equal(fp, WreckGenerator.LayoutFingerprint(copy));
        copy.Cells[^1].Tint = 0x123456;
        Assert.NotEqual(fp, WreckGenerator.LayoutFingerprint(copy));
    }

    [Fact]
    public void LayoutDesigns_AreTheShippedLayouts_InKeyOrder_ThenTheUserShipsThatAllowWrecks()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var shipped = content.Ships.Values.Where(s => content.GetShipLayout(s.Layout) is not null).Select(s => s.Key)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(shipped, WreckGenerator.LayoutDesigns(content, claimable: true).Select(d => d.Design.Key));
        Assert.DoesNotContain("starter", shipped); // the starter has no layout: it only ever crashes as a box

        var wreck = new List<string> { "npc", "wreck" };
        var extra = UserShip("zz_extra", length: 9, uses: wreck, weight: 1);
        extra.Pack = "extra_pack";
        content.SetUserShips(new[]
        {
            UserShip("zz_small", length: 9, uses: wreck, weight: 3),
            UserShip("zz_long", length: 24, uses: wreck, weight: 1),      // too long for a landing pad
            UserShip("zz_npc_only", length: 9, uses: new List<string> { "npc" }, weight: 1),
            UserShip("zz_default", length: 9, uses: null, weight: 1),     // no uses named = npc only: a wreck is opted in
            extra,
        });
        var defaultPacks = new List<string> { "default" }; // a world that enables only the default pack
        var claimable = WreckGenerator.LayoutDesigns(content, claimable: true, defaultPacks);
        Assert.Equal(shipped.Append("zz_small"), claimable.Select(d => d.Design.Key));
        Assert.Equal(3, claimable[^1].Weight);
        Assert.Equal(shipped.Append("zz_small").Append("zz_long"),
            WreckGenerator.LayoutDesigns(content, claimable: false, defaultPacks).Select(d => d.Design.Key));

        // #3520: a user ship of a pack the world does not enable stays out; no pack filter takes every pack.
        Assert.Equal(shipped.Append("zz_small").Append("zz_extra"), WreckGenerator.LayoutDesigns(content, claimable: true).Select(d => d.Design.Key));

        // The weights decide how often a hash lands on a design.
        long total = claimable.Sum(d => (long)d.Weight);
        var hits = Enumerable.Range(0, (int)total).Select(h => WreckGenerator.PickDesign(claimable, (ulong)h)!.Value.Design.Key).ToList();
        Assert.Equal(3, hits.Count(k => k == "zz_small"));
        Assert.Null(WreckGenerator.PickDesign(new List<WreckDesign>(), 7));
    }

    private static UserShipFile UserShip(string key, int length, List<string>? uses, int weight)
    {
        var cells = new List<ShipLayoutCell>();
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < length; z++)
            {
                cells.Add(new ShipLayoutCell { X = x, Y = 0, Z = z, Id = "iron_wall" });
                cells.Add(new ShipLayoutCell { X = x, Y = 4, Z = z, Id = "iron_wall" });
            }

        cells.Add(new ShipLayoutCell { X = 2, Y = 1, Z = 2, Kind = "station", Id = "cargo" });
        return new UserShipFile
        {
            Key = key,
            Uses = uses,
            Weight = weight,
            Layout = new ShipLayout { Width = 5, Height = 4, Length = length, Cells = cells },
        };
    }

    private static List<(int, int, int, ushort)> Cells(WreckStructure w)
    {
        var list = new List<(int, int, int, ushort)>();
        for (int x = 0; x < w.Width; x++)
            for (int y = 0; y < w.Height; y++)
                for (int z = 0; z < w.Length; z++)
                {
                    list.Add((x, y, z, w.Get(x, y, z)));
                }

        return list;
    }

    private static HashSet<string> Keys(WreckStructure w)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int x = 0; x < w.Width; x++)
            for (int y = 0; y < w.Height; y++)
                for (int z = 0; z < w.Length; z++)
                {
                    if (w.IntactAt(x, y, z) is var b && b != 0)
                    {
                        keys.Add(KeyOf(b));
                    }
                }

        return keys;
    }
}
