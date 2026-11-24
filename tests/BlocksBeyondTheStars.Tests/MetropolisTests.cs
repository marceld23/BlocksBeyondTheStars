// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The metropolises (#3983), without a server: the planner lays a city on the land (the landmass of its plaza only, the sea
/// and the land out of reach left out, every landmark on a free square of its own, the services by the plaza), the composer
/// builds it without a wall or G.D.S. guards and claims only its districts, the layout pins itself, and the Earth city kit is
/// complete. The cities on a real Earth: <see cref="EarthMetropolisTests"/>.
/// </summary>
public sealed class MetropolisTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static MetropolisPlanner.CellGround Land => new(1, 0, 0);
    private static MetropolisPlanner.CellGround Sea => new(0, 0, 1);
    private static MetropolisPlanner.CellGround Coast => new(0.7, 0, 0.3);
    private static MetropolisPlanner.CellGround Mountain => new(0.1, 0.9, 0);

    /// <summary>A map of letters per row (Z index, north last) into a ground function: # land, ~ sea, c coast, ^ out of reach.</summary>
    private static Func<int, int, MetropolisPlanner.CellGround> Ground(params string[] rows) => (gx, gz) => rows[gz][gx] switch
    {
        '~' => Sea,
        'c' => Coast,
        '^' => Mountain,
        _ => Land,
    };

    private static char At(CityLayoutSpec spec, int gx, int gz) => spec.RoleMap[gz][gx];

    private static StructureTemplate? Module(string key) => Content.SettlementTemplates.FirstOrDefault(t => t.Key == key);

    [Fact]
    public void Planner_KeepsToThePlazasLandmass_AndLeavesTheSeaAndTheHeightsToTheLand()
    {
        var plan = MetropolisPlanner.Lay(new MetropolisPlanner.Request
        {
            Grid = 6,
            Plaza = (1, 1),
            Seed = 7,
            Ground = Ground(
                "###~~#", // an island in the corner, across the water
                "####c~",
                "###c~~",
                "##^~~~",
                "~~~~##", // and another landmass in the north
                "~~~~##"),
        });

        var spec = plan.Layout;
        Assert.True(spec.Metro);
        Assert.Equal('P', At(spec, 1, 1));
        Assert.Equal((1, 1), spec.PlazaCell);
        Assert.Equal('.', At(spec, 5, 0)); // the island across the strait is somebody else's
        Assert.Equal('.', At(spec, 4, 4)); // so is the northern land
        Assert.Equal('.', At(spec, 3, 0));  // the sea
        Assert.Equal('.', At(spec, 2, 3));  // the heights, out of the floor's reach
        Assert.Equal('Q', At(spec, 4, 1));  // a coast district by the sea is a quay
        foreach (var (gx, gz) in new[] { (0, 0), (1, 0), (2, 0), (0, 1), (2, 1), (3, 1), (0, 2), (1, 2), (2, 2), (0, 3), (1, 3) })
        {
            Assert.NotEqual('.', At(spec, gx, gz));
        }
    }

    [Fact]
    public void Planner_PutsEveryLandmarkOnAFreeSquareOfItsOwn_NearThePlaza_EitherWayRound()
    {
        var plan = MetropolisPlanner.Lay(new MetropolisPlanner.Request
        {
            Grid = 6,
            Plaza = (3, 2),
            Seed = 11,
            Landmarks = new[]
            {
                new MetropolisPlanner.LandmarkRequest("tower", 101, 101),   // three districts each way
                new MetropolisPlanner.LandmarkRequest("hall", 165, 62),     // five by two — turned if it fits better
                new MetropolisPlanner.LandmarkRequest("statue", 35, 35),    // two by two (with the margin)
            },
        });

        Assert.Empty(plan.Unplaced);
        Assert.Equal(3, plan.Squares.Count);
        var spec = plan.Layout;
        var cells = new HashSet<(int, int)>();
        foreach (var sq in plan.Squares)
        {
            var lm = new[] { ("tower", 101, 101), ("hall", 165, 62), ("statue", 35, 35) }.Single(l => l.Item1 == sq.Key);
            int w = sq.Turns == 0 ? lm.Item2 : lm.Item3, l = sq.Turns == 0 ? lm.Item3 : lm.Item2;
            var (x0, z0) = spec.DistrictOrigin(sq.Gx0, sq.Gz0);
            var (x1, z1) = spec.DistrictOrigin(sq.Gx1, sq.Gz1);
            Assert.True(x1 + spec.DistrictSize - x0 >= w + 2 && z1 + spec.DistrictSize - z0 >= l + 2, $"{sq.Key} fits its square");
            Assert.InRange(sq.CentreX - (w / 2), x0, x1 + spec.DistrictSize - w);
            Assert.InRange(sq.CentreZ - (l / 2), z0, z1 + spec.DistrictSize - l);
            for (int gx = sq.Gx0; gx <= sq.Gx1; gx++)
            {
                for (int gz = sq.Gz0; gz <= sq.Gz1; gz++)
                {
                    Assert.Equal('L', At(spec, gx, gz));
                    Assert.True(cells.Add((gx, gz)), $"{sq.Key} overlaps another square at {gx},{gz}");
                }
            }
        }

        Assert.Equal('P', At(spec, 3, 2)); // no square takes the plaza
    }

    [Fact]
    public void Planner_IsDeterministic_AndBringsTheServicesStationsTowersAndAPark()
    {
        MetropolisPlanner.Plan Lay() => MetropolisPlanner.Lay(new MetropolisPlanner.Request { Grid = 6, Plaza = (3, 3), Seed = 99, Stations = 2 });
        var a = Lay().Layout;
        Assert.Equal(a.Serialize(), Lay().Layout.Serialize());

        string all = string.Concat(a.RoleMap);
        Assert.Equal(1, all.Count(c => c == 'P'));
        Assert.Equal(2, all.Count(c => c == 'M'));
        Assert.Equal(3, all.Count(c => c == 'H'));
        Assert.Equal(2, all.Count(c => c == 'S'));
        Assert.True(all.Count(c => c == 'T') >= 2);
        Assert.True(all.Count(c => c == 'G') >= 1);
        Assert.DoesNotContain('.', all); // all land: nothing left out

        // The services ring the plaza; the rail halls' squares stand two districts out or more.
        for (int gx = 0; gx < 6; gx++)
        {
            for (int gz = 0; gz < 6; gz++)
            {
                int ring = Math.Max(Math.Abs(gx - 3), Math.Abs(gz - 3));
                if (At(a, gx, gz) is 'M' or 'H')
                {
                    Assert.Equal(1, ring);
                }

                if (At(a, gx, gz) == 'S')
                {
                    Assert.True(ring >= 2, "a rail hall's square stands away from the plaza");
                }
            }
        }
    }

    [Fact]
    public void Layout_PinsItselfLosslessly_AndItsPlazaHoldsThePlaceCentre()
    {
        var plan = MetropolisPlanner.Lay(new MetropolisPlanner.Request { Grid = 4, Plaza = (1, 1), Seed = 3 });
        string text = plan.Layout.Serialize();
        Assert.EndsWith(",metro", text, StringComparison.Ordinal);
        Assert.True(CityLayoutSpec.TryParse(text, out var back));
        Assert.True(back.Metro);
        Assert.Equal(text, back.Serialize());
        Assert.Equal((1, 1), back.PlazaCell);
        Assert.Equal((4 + 36 + 16, 4 + 36 + 16), back.PlazaCentre);
        Assert.Equal(4 * 32 + 5 * 4, back.Footprint); // Berlin: 148 across

        // A classic layout keeps its five fields and its centre plaza.
        Assert.Equal("7,32,4,20,", CityLayoutSpec.Default.Serialize());
        Assert.Equal((128, 128), CityLayoutSpec.Default.PlazaCentre);
    }

    [Fact]
    public void Composer_BuildsAnOpenCity_WithoutWallOrGuards_ClaimingOnlyItsDistricts_InItsThemesColours()
    {
        var plan = MetropolisPlanner.Lay(new MetropolisPlanner.Request
        {
            Grid = 4,
            Plaza = (1, 1),
            Seed = 5,
            Ground = Ground("####", "####", "###~", "##~~"),
        });
        var kit = Content.KitByKey("city_earth_1")!;
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var palette = new Dictionary<string, string> { ["wall"] = "concrete#9C4A36", ["roof"] = "metal_panel#2F5A3C" };
        var composition = new List<string>();
        var s = CityGenerator.Generate(5, Content, Array.Empty<CityGenerator.OpenZone>(), null, 0.0, composition, null, plan.Layout, kit, pool,
            theme: "london", materials: ModuleMaterials.FromPalette(Content, palette));

        Assert.Equal(plan.Layout.Footprint, s.Width);
        Assert.Equal(MetropolisPlanner.Height, s.Height);
        Assert.DoesNotContain(s.Markers, m => m.Type == "guard_post"); // no G.D.S. here
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == SettlementGenerator.TavernMarker);
        Assert.Contains(s.Markers, m => m.Type == SettlementGenerator.WorkshopMarker);
        Assert.Equal("earth_plaza_1", composition[1 * 4 + 1]); // the plaza district takes the kit's plaza (index gx * grid + gz)

        // The districts left to the land are unclaimed — columns and streets — and nothing is built there.
        Assert.True(s.HasColumnMap);
        var (sx, sz) = plan.Layout.DistrictOrigin(3, 3);
        Assert.Equal(-1, s.FloorAt(sx + 16, sz + 16));
        Assert.Equal(0, s.FloorAt(4 + 36 + 16, 4 + 36 + 16));
        for (int y = 1; y < s.Height; y++)
        {
            Assert.Equal(0, s.Get(sx + 16, y, sz + 16));
        }

        // No wall round the city: the outer street is open ground.
        Assert.Equal(0, s.Get(1, 2, s.Length / 2));

        // The theme's colours: a dyed wall cell in its brick red, a roof cell in its green.
        ushort concrete = Content.GetBlock("concrete")!.NumericId.Value;
        bool brick = false;
        for (int x = 0; x < s.Width && !brick; x++)
            for (int y = 1; y < s.Height && !brick; y++)
                for (int z = 0; z < s.Length && !brick; z++)
                    brick = s.Get(x, y, z) == concrete && s.GetModifier(x, y, z).Tint == 0x9C4A36;
        Assert.True(brick, "the walls wear the theme's colour");
    }

    [Fact]
    public void EarthCityKit_IsComplete_PinOnly_AndNamesOnlyEarthModules()
    {
        var kit = Content.KitByKey("city_earth_1");
        Assert.NotNull(kit);
        Assert.True(kit!.PinOnly);
        Assert.Equal(new[] { "sol_earth" }, kit.PlanetTypes);
        Assert.DoesNotContain(Content.KitsFor(StructureKit.KindCity, null, new[] { "default" }, "sol_earth", 99), k => k.Key == kit.Key);

        var modules = kit.Entries.Select(e => Module(e.Module)).ToList();
        Assert.All(modules, m => Assert.NotNull(m));
        var functions = modules.Select(m => m!.Function).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            StructureRoles.CityPlaza, StructureRoles.CityMarket, StructureRoles.CityHall, StructureRoles.CityHousing,
            StructureRoles.CityTower, StructureRoles.CityGarden, StructureRoles.CityQuay,
        }, functions);
        foreach (var m in modules.Select(m => m!))
        {
            Assert.True(m.PinOnly, $"{m.Key} is pin-only");
            Assert.Equal(new[] { "sol_earth" }, m.PlanetTypes);
            Assert.Equal(StructureRoles.MetropolisTier, m.Tier);
            Assert.True(m.Width <= 32 && m.Length <= 32 && m.Height <= MetropolisPlanner.Height - 1, $"{m.Key}: {m.Width}×{m.Height}×{m.Length}");
            Assert.All(m.Cells.Where(c => c.Kind != "marker"), c => Assert.True(MaterialTokens.IsToken(c.Id) || Content.GetBlock(c.Id) != null, $"{m.Key}: '{c.Id}'"));
            if (m.Function is not (StructureRoles.CityPlaza or StructureRoles.CityGarden))
            {
                Assert.Contains(m.Cells, c => c.Kind == "marker" && c.Id.StartsWith("door_", StringComparison.Ordinal));
                Assert.Contains(m.Cells, c => c.Kind == "marker" && c.Id == "room");
            }
        }

        // The services' posts: the notice house's board, the inn's keeper, the workshop's craftsman, the markets' vendors.
        string[] Posts(string key) => Module(key)!.Cells.Where(c => c.Kind == "marker").Select(c => c.Id).ToArray();
        Assert.Contains("mission_board", Posts("earth_notice_house_1"));
        Assert.Contains(SettlementGenerator.TavernMarker, Posts("earth_inn_1"));
        Assert.Contains(SettlementGenerator.WorkshopMarker, Posts("earth_workshop_1"));
        Assert.Contains("vendor", Posts("earth_market_hall_1"));

        // No other world's pool offers a module of it (on Earth only the kit names them: the cities are its only composed ones).
        Assert.All(new[] { "gds_desert", "rocky", "jungle" }, planet =>
            Assert.DoesNotContain(Content.SettlementModulesFor(new[] { "default" }, planet), m => m.Kit == "city_earth"));
    }

    [Fact]
    public void EarthCities_AreValidPlaces_WithPalettesGridsAndNames()
    {
        var cities = Content.PlacesFor("sol_earth")!.OfKind(PlaceListKinds.City).ToList();
        Assert.Equal(9, cities.Count);
        foreach (var c in cities)
        {
            Assert.InRange(c.Grid, PlaceRules.MinCityGrid, PlaceRules.MaxCityGrid);
            Assert.Equal(2, c.Plaza.Count);
            Assert.Equal(PlaceRules.PaletteTokens.Length, c.Palette.Count);
            Assert.Equal("city." + c.Key + ".name", c.NameKey);
        }

        // Europe's three are the small ones, the others up to about 216 across (London's grid reaches over its island).
        int Across(string key) => cities.Single(c => c.Key == key).Grid * 36 + 4;
        Assert.True(Across("berlin") <= 148 && Across("paris") <= 184);
        Assert.All(new[] { "cairo", "tokyo", "sydney", "toronto", "new_york", "rio" }, k => Assert.True(Across(k) <= 220));

        // A broken palette is a load problem.
        var list = new PlaceListDefinition
        {
            PlanetType = "sol_earth",
            Places = { new PlaceDefinition { Key = "x", Kind = PlaceListKinds.City, Grid = 13, Plaza = { 5 }, Palette = { ["glow"] = "nope#12" } } },
        };
        var problems = PlaceRules.Problems(list, Content.LandmarkByKey, k => Content.GetBlock(k) != null);
        Assert.Contains(problems, p => p.Contains("grid", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("plaza", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("palette token", StringComparison.Ordinal));
    }
}
