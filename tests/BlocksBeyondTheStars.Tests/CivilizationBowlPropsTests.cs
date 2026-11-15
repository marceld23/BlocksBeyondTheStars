// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3701: a civilization's prop fills cavity cells when its marker stands in a cavity — the flooded bowl around the Grand
/// Technicians' eye, where the remains now lie beside the tank — but never an air pocket, and a marker on plain ground still
/// never reaches into a cavity beside it.
/// </summary>
public sealed class CivilizationBowlPropsTests
{
    private static readonly GameContent Shipped = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void TheGiantsBowlMarkers_EachPlaceTheirRemains_OnEveryTurn()
    {
        var civ = Shipped.GetCivilization("grosstechniker")!;
        var t = Shipped.CivilizationTemplateByKey("gt_giant_dwelling")!;
        int rev = LandmarkGenerator.RuinRevisionFor(WorldDescription.CivilizationsGeneration);
        var materials = CivilizationLayouts.MaterialsOf(civ, "sand", Shipped);
        var options = CivilizationLayouts.RuinOptionsOf(civ, airless: false, underwater: true);
        for (int seed = 1; seed <= 4; seed++)
        {
            for (int turns = 0; turns < 4; turns++)
            {
                var bare = LandmarkGenerator.Build(t, seed, turns, Shipped, "sand", ruin: null, rev, materials, options, dress: null);
                var full = CivilizationLayouts.Build(t, civ, seed, turns, Shipped, "sand", rev, airless: false, underwater: true);
                var bowl = full.Structure.Markers.Where(m => m.Type == CivilizationRules.PropMarker && m.LocalPos.Y <= full.GroundRow).ToList();
                Assert.Equal(8, bowl.Count);
                foreach (var m in bowl)
                {
                    int changed = 0;
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        for (int dz = -1; dz <= 0; dz++)
                        {
                            for (int dy = 0; dy <= 1; dy++)
                            {
                                int x = m.LocalPos.X + dx, y = m.LocalPos.Y + dy, z = m.LocalPos.Z + dz;
                                if (full.Structure.InBounds(x, y, z) && full.Structure.Get(x, y, z) != bare.Structure.Get(x, y, z))
                                {
                                    changed++;
                                    Assert.False(full.IsCavity(x, y, z), "a filled cavity cell holds the prop's block, it is no cavity any more");
                                }
                            }
                        }
                    }

                    Assert.True(changed > 0, $"seed {seed} turn {turns}: the bowl marker at {m.LocalPos} placed nothing");
                }

                AssertNoBlockInAnAirPocket(full);
            }
        }
    }

    [Fact]
    public void APropFillsACavity_OnlyFromAMarkerInOne_AndNeverAnAirPocket()
    {
        var (content, civ, t) = TestCivilization();
        var layout = CivilizationLayouts.Build(t, civ, seed: 5, turns: 0, content, "sand", revision: 2, airless: false, underwater: true);
        var s = layout.Structure;
        ushort stone = content.GetBlock("stone")!.NumericId.Value;

        // Marker A stands in the cavity room (row 1): its plate fills the room's cells …
        Assert.Equal(stone, s.Get(1, 1, 4));
        Assert.Equal(stone, s.Get(0, 1, 3));
        Assert.False(layout.IsCavity(1, 1, 4));
        // … but not the air pocket at (2, 1, 5).
        Assert.Equal(0, s.Get(2, 1, 5));
        Assert.True(layout.IsAirPocket(2, 1, 5));

        // Marker B stands on plain ground (row 2): its plate fills the plain cells, never the cavity column at x = 4.
        Assert.Equal(stone, s.Get(5, 2, 4));
        Assert.Equal(stone, s.Get(6, 2, 4));
        Assert.Equal(0, s.Get(4, 2, 4));
        Assert.True(layout.IsCavity(4, 2, 4));
    }

    private static void AssertNoBlockInAnAirPocket(LandmarkLayout layout)
    {
        var s = layout.Structure;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    if (layout.IsAirPocket(x, y, z))
                    {
                        Assert.Equal(0, s.Get(x, y, z));
                    }
                }
            }
        }
    }

    /// <summary>A 9 × 4 × 9 site, ground row 2: a stone slab (row 0); in row 1 a cavity room (x 0–3) with an air pocket at
    /// (2, 1, 5) and stone beyond it; a cavity column at x = 4 in row 2; marker A (prop pool "plates") in the room at
    /// (1, 1, 4), marker B on the stone at (5, 2, 4). The prop is a 3 × 1 × 3 stone plate (the same on every turn).</summary>
    private static (GameContent Content, CivilizationDefinition Civ, StructureTemplate Site) TestCivilization()
    {
        var civ = CivilizationFrameworkTests.Civ("bowl_test", types: new[] { "meadowlands" }, min: 1, max: 1, perSystem: 0,
            start: CivilizationRules.StartOnly, underwater: true);
        var site = new StructureTemplate
        {
            Key = "bowl_test_site",
            Name = "Bowl",
            Civilization = civ.Key,
            CivPool = CivilizationRules.PoolGiant,
            Width = 9,
            Height = 4,
            Length = 9,
            GroundRow = 2,
            Ruin = LandmarkRules.RuinNever,
        };
        for (int x = 0; x < 9; x++)
        {
            for (int z = 0; z < 9; z++)
            {
                site.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = "stone" });
                string kind = x <= 3 ? (x == 2 && z == 5 ? CivilizationRules.AirKind : LandmarkRules.CavityKind) : "block";
                site.Cells.Add(new TemplateCell { X = x, Y = 1, Z = z, Kind = kind, Id = kind == "block" ? "stone" : string.Empty });
            }

            site.Cells.Add(new TemplateCell { X = 4, Y = 2, Z = x, Kind = LandmarkRules.CavityKind });
        }

        site.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 4, Kind = "marker", Id = CivilizationRules.PropMarker, Config = "pool=plates" });
        site.Cells.Add(new TemplateCell { X = 5, Y = 2, Z = 4, Kind = "marker", Id = CivilizationRules.PropMarker, Config = "pool=plates" });
        var plate = new StructureTemplate
        {
            Key = "bowl_test_plate",
            Name = "Plate",
            Civilization = civ.Key,
            CivPool = CivilizationRules.PoolProp,
            PropPool = "plates",
            Width = 3,
            Height = 1,
            Length = 3,
            Ruin = LandmarkRules.RuinNever,
        };
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
            {
                plate.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = "stone" });
            }
        }

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var warnings = new List<string>();
        content.SetCivilizations(content.Civilizations.Where(c => !c.IsFramework).Append(civ).ToArray(), new[] { site, plate }, warnings.Add);
        return (content, content.GetCivilization(civ.Key)!, content.CivilizationTemplateByKey(site.Key)!);
    }
}
