// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ruin zones of the school club's landmarks (terrain generation 39): each zone decays with its own profile and strength,
/// the cells of no zone exactly as the template's own pass would take them, then one shared settle — and a template without
/// zones is the pass as it shipped. The grid is three stone pillars that stand apart, so each one settles on its own and can be
/// compared cell by cell.
/// </summary>
public sealed class StructureRuinerZoneTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly long[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    private const int PillarA = 0, PillarB = 10, PillarC = 20, Pillar = 6, Height = 30;

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    /// <summary>Three solid stone pillars 6 × 30 × 6, four columns apart, on the ground row 0.</summary>
    private static VoxelGrid Pillars()
    {
        var g = new VoxelGrid(PillarC + Pillar, Height, Pillar);
        foreach (int x0 in new[] { PillarA, PillarB, PillarC })
        {
            for (int x = x0; x < x0 + Pillar; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int z = 0; z < Pillar; z++)
                    {
                        g.Set(x, y, z, Id("stone"));
                    }
                }
            }
        }

        return g;
    }

    /// <summary>Zone A (the first pillar): masonry, barely decayed; zone B (the second): masonry at full strength.</summary>
    private static List<RuinZoneBox> Zones() => new()
    {
        new RuinZoneBox(PillarA, 0, 0, PillarA + Pillar - 1, Height - 1, Pillar - 1, LandmarkRules.ProfileMasonry, 0.05),
        new RuinZoneBox(PillarB, 0, 0, PillarB + Pillar - 1, Height - 1, Pillar - 1, LandmarkRules.ProfileMasonry, 1.0),
    };

    private static VoxelGrid Ruin(long seed, IReadOnlyList<RuinZoneBox>? zones)
    {
        var g = Pillars();
        if (zones is null)
        {
            StructureRuiner.Run(g, Content, seed, LandmarkRules.ProfileMasonry, 0.9, 0, Array.Empty<Vector3i>(), "grass",
                StructureRuiner.Revision, null);
        }
        else
        {
            StructureRuiner.RunZoned(g, Content, seed, LandmarkRules.ProfileMasonry, 0.9, 0, Array.Empty<Vector3i>(), "grass",
                StructureRuiner.Revision, null, zones);
        }

        return g;
    }

    /// <summary>The cells of the pillar starting at <paramref name="x0"/> above the rubble rows that lost their stone.</summary>
    private static int Lost(VoxelGrid g, int x0)
        => RuinerTestGrids.Cells(g).Count(c => c.X >= x0 && c.X < x0 + Pillar && c.Y > 3 && g.Get(c.X, c.Y, c.Z) == 0);

    private static bool SamePillar(VoxelGrid a, VoxelGrid b, int x0)
        => RuinerTestGrids.Cells(a).Where(c => c.X >= x0 && c.X < x0 + Pillar).All(c => a.Get(c.X, c.Y, c.Z) == b.Get(c.X, c.Y, c.Z));

    [Fact]
    public void TheCellsOfNoZone_FallExactlyAsTheTemplatesOwnPass_TheZonesDoNot()
    {
        bool zoneDiffers = false;
        foreach (long seed in Seeds)
        {
            var plain = Ruin(seed, null);
            var zoned = Ruin(seed, Zones());
            Assert.True(SamePillar(plain, zoned, PillarC), $"seed {seed}: the pillar outside every zone fell otherwise than in the template's pass");
            zoneDiffers |= !SamePillar(plain, zoned, PillarA) || !SamePillar(plain, zoned, PillarB);
        }

        Assert.True(zoneDiffers, "the zones decayed exactly like the template's own pass");
    }

    [Fact]
    public void EachZone_DecaysWithItsOwnStrength()
    {
        int weak = 0, strong = 0, template = 0;
        foreach (long seed in Seeds)
        {
            var zoned = Ruin(seed, Zones());
            weak += Lost(zoned, PillarA);
            strong += Lost(zoned, PillarB);
            template += Lost(Ruin(seed, null), PillarA);
        }

        Assert.True(weak * 2 < strong, $"the weak zone lost {weak} cells, the strong one {strong}");
        Assert.True(weak < template, $"the weak zone lost {weak} cells, the same pillar under the template's strength {template}");
    }

    [Fact]
    public void AZonedRuin_IsDeterministic_AndWithoutZonesItIsThePassAsItShipped()
    {
        foreach (long seed in Seeds.Take(3))
        {
            Assert.Equal(RuinerTestGrids.Hash(Content, Ruin(seed, Zones())), RuinerTestGrids.Hash(Content, Ruin(seed, Zones())));
            Assert.Equal(RuinerTestGrids.Hash(Content, Ruin(seed, null)), RuinerTestGrids.Hash(Content, Ruin(seed, new List<RuinZoneBox>())));
        }

        // A landmark that names no zone builds exactly as it did before the zones existed (the pinned landmark goldens of
        // StructureRuinerGoldenTests and StructureGeneratorGoldenTests hold the numbers).
        var tower = LandmarkShapes.Tower();
        Assert.Empty(tower.RuinZones);
    }

    [Fact]
    public void ALandmarksZones_TurnWithIt_AndChangeItsRuin()
    {
        var tower = LandmarkShapes.Tower("zoned_tower", size: 16, height: 40);
        tower.RuinZones = new List<RuinZone>
        {
            new() { X0 = 0, Y0 = 0, Z0 = 0, X1 = 15, Y1 = 19, Z1 = 15, Profile = LandmarkRules.ProfileMasonry, Strength = 0.1 },
            new() { X0 = 0, Y0 = 20, Z0 = 0, X1 = 15, Y1 = 39, Z1 = 15, Profile = LandmarkRules.ProfileLattice, Strength = 0.9 },
        };
        Assert.Empty(LandmarkRules.Problems(tower));

        ulong a = StructureGoldens.Landmark(Content, LandmarkGenerator.Build(tower, 5, 1, Content, "grass"));
        ulong b = StructureGoldens.Landmark(Content, LandmarkGenerator.Build(tower, 5, 1, Content, "grass"));
        Assert.Equal(a, b);

        var unzoned = LandmarkShapes.Tower("zoned_tower", size: 16, height: 40);
        Assert.NotEqual(a, StructureGoldens.Landmark(Content, LandmarkGenerator.Build(unzoned, 5, 1, Content, "grass")));

        // A zone's box turns with the structure: (x, z) → (L − 1 − z, x) per quarter turn, width and length swapping.
        var wide = new StructureTemplate { Width = 10, Height = 4, Length = 4 };
        wide.RuinZones.Add(new RuinZone { X0 = 0, Y0 = 1, Z0 = 0, X1 = 2, Y1 = 3, Z1 = 3 });
        var box = Assert.Single(LandmarkGenerator.ZonesFor(wide, 1));
        Assert.Equal((0, 1, 0, 3, 3, 2), (box.X0, box.Y0, box.Z0, box.X1, box.Y1, box.Z1));
        Assert.Equal(LandmarkRules.ProfileMasonry, box.Profile); // no profile of its own: the template's (masonry by default)
    }

    [Fact]
    public void TheJsonShapes_OfZonesSeatProtectionAndFixedLandmarks()
    {
        var t = System.Text.Json.JsonSerializer.Deserialize<StructureTemplate>(
            "{\"key\":\"j\",\"landmark\":true,\"seat\":\"span\",\"protection\":\"open\",\"ruinZones\":[{\"x0\":1,\"y0\":2,\"z0\":3,\"x1\":4,\"y1\":5,\"z1\":6,\"profile\":\"lattice\",\"strength\":0.4}]}",
            ContentLoader.JsonOptions)!;
        Assert.Equal((LandmarkRules.SeatSpan, LandmarkRules.ProtectionOpen), (t.Seat, t.Protection));
        var z = Assert.Single(t.RuinZones);
        Assert.Equal((1, 2, 3, 4, 5, 6, LandmarkRules.ProfileLattice, 0.4), (z.X0, z.Y0, z.Z0, z.X1, z.Y1, z.Z1, z.Profile, z.Strength));
        Assert.True(z.Contains(4, 2, 3) && !z.Contains(5, 2, 3));

        var p = System.Text.Json.JsonSerializer.Deserialize<PlanetType>("{\"key\":\"p\",\"fixedLandmarks\":[\"a\",\"b\"]}", ContentLoader.JsonOptions)!;
        Assert.Equal(new[] { "a", "b" }, p.FixedLandmarks);
    }

    [Fact]
    public void TheLoader_RefusesABrokenZone_AnUnknownSeat_AndAnUnknownProtection()
    {
        StructureTemplate With(Action<StructureTemplate> edit)
        {
            var t = LandmarkShapes.Tower("broken");
            edit(t);
            return t;
        }

        Assert.Contains(LandmarkRules.Problems(With(t => t.RuinZones.Add(new RuinZone { X1 = 16, Y1 = 3, Z1 = 3 }))),
            p => p.Contains("outside", StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(With(t => t.RuinZones.Add(new RuinZone { X1 = 3, Y1 = 3, Z1 = 3, Profile = "hull" }))),
            p => p.Contains("unknown ruin profile 'hull'", StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(With(t => t.RuinZones.Add(new RuinZone { X1 = 3, Y1 = 3, Z1 = 3, Strength = 1.5 }))),
            p => p.Contains("strength", StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(With(t => t.Seat = "hover")), p => p.Contains("unknown seat", StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(With(t => (t.Seat, t.Width, t.Length) = (LandmarkRules.SeatSpan, 8, 8))),
            p => p.Contains("long axis", StringComparison.Ordinal));
        Assert.Contains(LandmarkRules.Problems(With(t => t.Protection = "locked")), p => p.Contains("unknown protection", StringComparison.Ordinal));
        Assert.Empty(LandmarkRules.Problems(With(t => (t.Seat, t.Protection) = ("SPAN", "Open"))));

        // At load a broken landmark is dropped with a warning, a broken word of a settlement template reads as auto.
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(new[] { With(t => t.Protection = "locked"), LandmarkShapes.Tower("fine") }, warnings.Add);
        Assert.Equal(new[] { "fine" }, content.Landmarks.Select(t => t.Key));
        Assert.Contains(warnings, w => w.Contains("'broken'", StringComparison.Ordinal));

        var village = new StructureTemplate { Key = "odd_village", Tier = "small", Width = 4, Height = 4, Length = 4, Protection = "locked" };
        content.SetStructureTemplates(content.StationTemplates, content.SettlementTemplates.Append(village).ToList(), warnings.Add);
        Assert.Equal(string.Empty, content.SettlementTemplates.Single(t => t.Key == "odd_village").Protection);
        Assert.Equal(LandmarkRules.ProtectionAuto, LandmarkRules.ProtectionOf(village));
    }
}
