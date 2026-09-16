// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1917 / #1918: kit stations get exterior detail (solar wings, antennae, domes) without touching a module, and a
/// station's flown hull keeps what can be seen from outside plus the hangar mouth the ship docks at.</summary>
public sealed class StationHullTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static List<StructureKit> ShippedStationKits()
        => Content.StructureKits.Where(k => k.Kind == StructureKit.KindStation).ToList();

    private static StructureTemplate? Module(string key) => Content.TemplateByKey(StructureKit.KindStation, key);

    /// <summary>The same kit without exterior detail — and with the extent the composer then places the modules in, so
    /// both draw the very same modules.</summary>
    private static StructureKit WithoutExterior(StructureKit kit) => new()
    {
        Key = kit.Key,
        Name = kit.Name,
        Kind = kit.Kind,
        Tier = kit.Tier,
        Pack = kit.Pack,
        Weight = kit.Weight,
        ModulesMin = kit.ModulesMin,
        ModulesMax = kit.ModulesMax,
        Start = kit.Start,
        Entries = kit.Entries,
        MaxExtent = (kit.MaxExtent > 0 ? kit.MaxExtent : StationKitComposer.DefaultMaxExtent) - 2 * StationKitExterior.MarginXZ,
    };

    /// <summary>Every module's box (inclusive) inside a composed station.</summary>
    private static List<(Vector3i Min, Vector3i Max)> Boxes(StationComposition comp)
        => comp.Modules.Select(m =>
        {
            var t = TemplateTransform.RotateY(Module(m.Key)!, m.Turns);
            return (new Vector3i(m.X, m.Y, m.Z), new Vector3i(m.X + t.Width - 1, m.Y + t.Height - 1, m.Z + t.Length - 1));
        }).ToList();

    private static bool InAny(List<(Vector3i Min, Vector3i Max)> boxes, int x, int y, int z)
        => boxes.Any(b => x >= b.Min.X && x <= b.Max.X && y >= b.Min.Y && y <= b.Max.Y && z >= b.Min.Z && z <= b.Max.Z);

    [Fact]
    public void ShippedKits_CarryExteriorDetail()
    {
        var kits = ShippedStationKits();
        Assert.Equal(5, kits.Count);
        Assert.All(kits, k => Assert.True(k.SolarWings > 0 && k.Antennas > 0 && k.Domes > 0, $"kit '{k.Key}' has no exterior detail"));
        var small = kits.First(k => k.Tier == "small");
        var colossal = kits.First(k => k.Tier == "colossal");
        Assert.True(colossal.SolarWings > small.SolarWings && colossal.Antennas > small.Antennas && colossal.Domes > small.Domes);
    }

    [Fact]
    public void ExteriorDetail_SitsOutsideTheModules_AndLeavesEveryModuleCellAndMarkerUntouched()
    {
        ushort glass = Content.GetBlock("glass")!.NumericId.Value;
        ushort light = Content.GetBlock("light_white")!.NumericId.Value;
        foreach (var kit in ShippedStationKits())
        {
            for (long seed = 1; seed <= 4; seed++)
            {
                var with = StationKitComposer.Compose(kit, Module, seed, Content, out var compWith, out var failWith);
                var without = StationKitComposer.Compose(WithoutExterior(kit), Module, seed, Content, out var compWithout, out var failWithout);
                Assert.True(with != null, failWith);
                Assert.True(without != null, failWithout);

                // Same modules, moved in by the margin on the sides only.
                Assert.Equal(compWithout.Modules.Count, compWith.Modules.Count);
                var shift = new Vector3i(StationKitExterior.MarginXZ, 0, StationKitExterior.MarginXZ);
                for (int i = 0; i < compWith.Modules.Count; i++)
                {
                    var a = compWith.Modules[i];
                    var b = compWithout.Modules[i];
                    Assert.Equal((b.Key, b.X + shift.X, b.Y, b.Z + shift.Z, b.Turns), (a.Key, a.X, a.Y, a.Z, a.Turns));
                }

                Assert.Equal(without!.Width + 2 * StationKitExterior.MarginXZ, with!.Width);
                Assert.Equal(without.Height + StationKitExterior.MarginTop, with.Height);
                Assert.Equal(without.Length + 2 * StationKitExterior.MarginXZ, with.Length);
                Assert.True(with.Width <= StationKitComposer.DefaultMaxExtent && with.Length <= StationKitComposer.DefaultMaxExtent,
                    $"kit '{kit.Key}' seed {seed}: {with.Width}×{with.Length} outgrows the extent the editor shows");

                var boxes = Boxes(compWith);
                int solarCells = 0, lights = 0, outsideCells = 0;
                for (int x = 0; x < with.Width; x++)
                    for (int y = 0; y < with.Height; y++)
                        for (int z = 0; z < with.Length; z++)
                        {
                            ushort got = with.Get(x, y, z);
                            if (InAny(boxes, x, y, z))
                            {
                                int ox = x - shift.X, oz = z - shift.Z;
                                Assert.True(got == without.Get(ox, y, oz), $"kit '{kit.Key}' seed {seed}: module cell ({x},{y},{z}) changed");
                                continue;
                            }

                            if (got == 0)
                            {
                                continue;
                            }

                            outsideCells++;
                            if (got == glass && with.GetModifier(x, y, z).Tint == StationKitExterior.SolarTint)
                            {
                                solarCells++;
                            }

                            if (got == light)
                            {
                                lights++;
                            }
                        }

                Assert.True(outsideCells > 0, $"kit '{kit.Key}' seed {seed}: no exterior detail at all");
                Assert.True(solarCells > 0, $"kit '{kit.Key}' seed {seed}: no solar wing");
                Assert.True(lights > 0, $"kit '{kit.Key}' seed {seed}: no dome or antenna beacon");

                var markersWith = with.Markers.Select(m => (m.Type, m.LocalPos)).OrderBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.LocalPos.X).ThenBy(m => m.LocalPos.Y).ThenBy(m => m.LocalPos.Z);
                var markersWithout = without.Markers.Select(m => (m.Type, m.LocalPos + shift)).OrderBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.Item2.X).ThenBy(m => m.Item2.Y).ThenBy(m => m.Item2.Z);
                Assert.Equal(markersWithout, markersWith);
            }
        }
    }

    [Fact]
    public void ZeroCounts_MeanNoDetailAndNoMargin()
    {
        var kit = WithoutExterior(ShippedStationKits()[0]);
        var s = StationKitComposer.Compose(kit, Module, 3, Content, out var comp, out var failure);
        Assert.True(s != null, failure);
        Assert.False(comp.HasExterior);
        var boxes = Boxes(comp);
        Assert.Equal(0, boxes.Min(b => b.Min.X));
        Assert.Equal(0, boxes.Min(b => b.Min.Z));
        for (int x = 0; x < s!.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    Assert.False(s.Get(x, y, z) != 0 && !InAny(boxes, x, y, z), $"a block outside the modules at ({x},{y},{z})");
                }
    }

    [Fact]
    public void ExteriorDetail_NeverSitsInFrontOfAWindowOrTheHangarMouth()
    {
        var seeThrough = StationHull.SeeThroughIds(Content);
        foreach (var kit in ShippedStationKits())
        {
            for (long seed = 1; seed <= 4; seed++)
            {
                var s = StationKitComposer.Compose(kit, Module, seed, Content, out var comp, out var failure);
                Assert.True(s != null, failure);
                var boxes = Boxes(comp);
                for (int x = 0; x < s!.Width; x++)
                    for (int y = 0; y < s.Height; y++)
                        for (int z = 0; z < s.Length; z++)
                        {
                            if (s.Get(x, y, z) == 0 || InAny(boxes, x, y, z))
                            {
                                continue;
                            }

                            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                int nx = x + dx, nz = z + dz;
                                bool window = s.InBounds(nx, y, nz) && InAny(boxes, nx, y, nz) && seeThrough.Contains(s.Get(nx, y, nz));
                                Assert.False(window, $"kit '{kit.Key}' seed {seed}: exterior piece at ({x},{y},{z}) sits against a window or mouth");
                            }
                        }

                // The dock approach: straight out of the mouth to the structure's edge, nothing in the way.
                var dock = StationHull.FindDock(s, Content);
                Assert.True(dock.IsMouth, $"kit '{kit.Key}' seed {seed}: no hangar mouth found");
                int cx = (int)Math.Floor(dock.X - (dock.OutX > 0 ? 0.5f : dock.OutX < 0 ? -0.5f : 0f));
                int cy = (int)Math.Floor(dock.Y);
                int cz = (int)Math.Floor(dock.Z - (dock.OutZ > 0 ? 0.5f : dock.OutZ < 0 ? -0.5f : 0f));
                for (int step = 1; ; step++)
                {
                    int px = cx + dock.OutX * step, pz = cz + dock.OutZ * step;
                    if (!s.InBounds(px, cy, pz))
                    {
                        break;
                    }

                    Assert.True(s.Get(px, cy, pz) == 0, $"kit '{kit.Key}' seed {seed}: the dock approach is blocked at ({px},{cy},{pz})");
                }
            }
        }
    }

    [Fact]
    public void Replay_WithExterior_BakesTheSameStation()
    {
        foreach (var kit in ShippedStationKits())
        {
            var s = StationKitComposer.Compose(kit, Module, 11, Content, out var comp, out var failure);
            Assert.True(s != null, failure);
            var again = StationKitComposer.Replay(comp, Module, Content, kit.Tier, out var replayFailure);
            Assert.True(again != null, replayFailure);
            Assert.Equal(new Vector3i(0, 0, 0), again!.ModuleShift);
            Assert.Equal((s!.Width, s.Height, s.Length), (again.Width, again.Height, again.Length));
            for (int x = 0; x < s.Width; x++)
                for (int y = 0; y < s.Height; y++)
                    for (int z = 0; z < s.Length; z++)
                    {
                        Assert.Equal(s.Get(x, y, z), again.Get(x, y, z));
                        Assert.Equal(s.GetModifier(x, y, z), again.GetModifier(x, y, z));
                    }
        }
    }

    [Fact]
    public void Replay_OfACompositionPinnedBeforeTheMargin_MovesItsModulesInByTheMargin()
    {
        var kit = ShippedStationKits().First(k => k.Tier == "medium");
        var old = StationKitComposer.Compose(WithoutExterior(kit), Module, 5, Content, out var comp, out var failure);
        Assert.True(old != null, failure);

        // The pre-#1918 record gets its kit's exterior pinned on the first replay after the update.
        comp.SolarWings = kit.SolarWings;
        comp.Antennas = kit.Antennas;
        comp.Domes = kit.Domes;
        var replay = StationKitComposer.Replay(comp, Module, Content, kit.Tier, out var replayFailure);
        Assert.True(replay != null, replayFailure);
        var shift = new Vector3i(StationKitExterior.MarginXZ, 0, StationKitExterior.MarginXZ);
        Assert.Equal(shift, replay!.ModuleShift);

        // Stamped at (origin - shift), every old cell lands exactly where it always was.
        foreach (var (min, max) in Boxes(comp))
        {
            for (int x = min.X; x <= max.X; x++)
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        Assert.Equal(old!.Get(x, y, z), replay.Get(x + shift.X, y, z + shift.Z));
                    }
        }
    }

    [Fact]
    public void VisibleCells_KeepTheOutside_AndWhatShowsThroughTheMouth_ButNotSealedRooms()
    {
        ushort field = Content.GetBlock("force_field")!.NumericId.Value;
        foreach (var kit in ShippedStationKits())
        {
            var s = StationKitComposer.Compose(kit, Module, 2, Content, out _, out var failure);
            Assert.True(s != null, failure);
            var visible = StationHull.VisibleCells(s!, Content).ToHashSet();
            int solid = 0;
            for (int x = 0; x < s!.Width; x++)
                for (int y = 0; y < s.Height; y++)
                    for (int z = 0; z < s.Length; z++)
                    {
                        if (s.Get(x, y, z) != 0)
                        {
                            solid++;
                        }
                    }

            Assert.True(visible.Count < solid, $"kit '{kit.Key}': the hull keeps every cell ({visible.Count} of {solid})");

            // Every outermost block of every column is visible (nothing in the silhouette is missing).
            for (int x = 0; x < s.Width; x++)
                for (int z = 0; z < s.Length; z++)
                {
                    for (int y = s.Height - 1; y >= 0; y--)
                    {
                        if (s.Get(x, y, z) != 0)
                        {
                            Assert.Contains(new Vector3i(x, y, z), visible);
                            break;
                        }
                    }
                }

            // The mouth is part of the hull, and the hangar floor behind it shows through.
            var dock = StationHull.FindDock(s, Content);
            Assert.Contains(visible, c => s.Get(c.X, c.Y, c.Z) == field);
            int bx = (int)Math.Floor(dock.X) - (dock.OutX > 0 ? 2 : dock.OutX < 0 ? -1 : 0);
            int bz = (int)Math.Floor(dock.Z) - (dock.OutZ > 0 ? 2 : dock.OutZ < 0 ? -1 : 0);
            var floorBehind = Enumerable.Range(0, s.Height).Select(y => new Vector3i(bx, y, bz))
                .Where(c => s.InBounds(c.X, c.Y, c.Z) && s.Get(c.X, c.Y, c.Z) != 0 && s.Get(c.X, c.Y, c.Z) != field && c.Y < (int)dock.Y)
                .OrderByDescending(c => c.Y).FirstOrDefault();
            Assert.Contains(floorBehind, visible);
        }
    }

    [Fact]
    public void VisibleCells_OfASealedBox_AreItsShellOnly()
    {
        ushort hull = Content.GetBlock("iron_wall")!.NumericId.Value;
        ushort crate = Content.GetBlock("crate")!.NumericId.Value;
        const int n = 7;
        var blocks = new ushort[n * n * n];
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    bool shell = x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == n - 1;
                    blocks[(x * n + y) * n + z] = shell ? hull : (x == 3 && y == 1 && z == 3 ? crate : (ushort)0);
                }

        var s = new StationStructure(n, n, n, "small", n, n, n, blocks, new List<StationMarker>(), new List<StationModule>());
        var visible = StationHull.VisibleCells(s, Content);
        Assert.Equal(n * n * n - (n - 2) * (n - 2) * (n - 2), visible.Count);
        Assert.DoesNotContain(new Vector3i(3, 1, 3), visible);

        // A window on the -Z wall lets the view in: the crate behind it is part of the hull now.
        blocks[(3 * n + 1) * n + 0] = Content.GetBlock("glass")!.NumericId.Value;
        var windowed = StationHull.VisibleCells(new StationStructure(n, n, n, "small", n, n, n, blocks, new List<StationMarker>(), new List<StationModule>()), Content);
        Assert.Contains(new Vector3i(3, 1, 3), windowed);

        // Without a hangar marker the dock falls back to the middle of the -Z face.
        var dock = StationHull.FindDock(s, Content);
        Assert.False(dock.IsMouth);
        Assert.Equal((0, -1), (dock.OutX, dock.OutZ));
    }
}
