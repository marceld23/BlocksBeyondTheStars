// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bodies' place lists (#3981): the longitude/latitude conversion onto the map's projection, the load checks, the
/// place-only guard (#3982), Earth's places and pads on land on the real map, and the rolled things that never stand on Earth
/// at chunk level (rune shrines and lone rune stones, on every generation).
/// </summary>
public sealed class PlaceListTests
{
    private const string Earth = "sol_earth";
    private const int EarthCircumference = 16000;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlaceListDefinition EarthPlaces => Content.PlacesFor(Earth) ?? throw new InvalidOperationException("no Earth list");

    [Fact]
    public void ALongitudeAndLatitude_LandOnTheMapsProjection()
    {
        Assert.Equal((0, 0), PlaceRules.BlockOf(0.0, 0.0, EarthCircumference));
        Assert.Equal((15994, 2289), PlaceRules.BlockOf(-0.13, 51.51, EarthCircumference)); // London: west wraps, north is +Z
        Assert.Equal((4000, -2000), PlaceRules.BlockOf(90.0, -45.0, EarthCircumference));
        Assert.Equal(PlaceRules.BlockOf(-90.0, 10.0, EarthCircumference), PlaceRules.BlockOf(270.0, 10.0, EarthCircumference));

        // The map agrees: row 0 is the south edge, so the Himalaya (north) is high mountains and the Amazon rain forest.
        var terrain = MapTerrain.For(Content.GetPlanetMap("earth")!);
        int period = WorldConstants.LatitudePeriodFor(EarthCircumference);
        int ClassAt(double lat, double lon)
        {
            var (x, z) = PlaceRules.BlockOf(lon, lat, EarthCircumference);
            return terrain.BiomeClassAt(x, z, EarthCircumference, period);
        }

        Assert.Equal((int)PlanetMapBiome.HighMountains, ClassAt(30.0, 85.0));
        Assert.Equal((int)PlanetMapBiome.RainForest, ClassAt(-5.0, -65.0));
        Assert.Equal((int)PlanetMapBiome.BorealForest, ClassAt(62.0, 100.0));
    }

    [Fact]
    public void EarthsList_Loads_WithItsPadsCitiesAndThePyramids()
    {
        var list = EarthPlaces;
        var pads = list.OfKind(PlaceListKinds.Pad).ToList();
        Assert.Equal(10, pads.Count);
        Assert.Equal("pad_london", pads[0].Key); // the home touchdown
        Assert.Equal(9, list.OfKind(PlaceListKinds.City).Count());
        Assert.Equal(new[] { "earth_pyramid_great", "earth_pyramid_second", "earth_pyramid_third" },
            list.OfKind(PlaceListKinds.Landmark).Select(p => p.Template));
        Assert.Equal(new[] { "earth_brandenburg_gate", "earth_berlin_tv_tower" }, list.Places.Single(p => p.Key == "berlin").Landmarks);
        Assert.Empty(list.Places.Single(p => p.Key == "cairo").Landmarks);
        Assert.Equal("new_york", list.Places.Single(p => p.Key == "new_york").ThemeOrKey);

        // #3986: the SPS headquarters are one complex of five parts; its members follow the pyramids in record order.
        Assert.Equal(new[] { "sps_hq" }, list.OfKind(PlaceListKinds.Complex).Select(p => p.Key));
        Assert.Equal(new[]
            {
                "earth_pyramid_great", "earth_pyramid_second", "earth_pyramid_third",
                "earth_sps_hq_main", "earth_sps_hq_hangar", "earth_sps_hq_servers", "earth_sps_hq_core", "earth_sps_hq_hall",
            },
            PlaceRules.ListedLandmarks(list).Select(l => l.Template));
    }

    /// <summary>#3986: a complex's members stand at the anchor plus their offsets — the low corner — so the centre a given seat
    /// takes is the corner plus half the footprint, odd sizes rounded down like <see cref="GivenSeat.OriginFor"/>.</summary>
    [Fact]
    public void AComplexMember_StandsAtTheAnchorPlusItsOffset()
    {
        var complex = new PlaceDefinition
        {
            Key = "c",
            Kind = PlaceListKinds.Complex,
            Longitude = 0.0,
            Latitude = 0.0,
            Members = { new PlaceMember { Template = "a", East = -30, North = 5 } },
        };
        var listed = Assert.Single(PlaceRules.ListedLandmarks(new PlaceListDefinition { Places = { complex } }));
        var (x, z) = listed.CentreOn(EarthCircumference, 41, 37);
        Assert.Equal((EarthCircumference - 30 + 20, 5 + 18), (x, z)); // west of longitude 0 wraps
        Assert.Equal((EarthCircumference - 30, 5), new GivenSeat(x, z).OriginFor(41, 37));
    }

    [Fact]
    public void TheLoadChecks_RefuseUnknownAndPooledTemplates_KindsAndCoordinates()
    {
        string pooled = Content.Landmarks.First(t => !t.PinOnly).Key;
        var list = new PlaceListDefinition
        {
            PlanetType = Earth,
            Places =
            {
                new PlaceDefinition { Key = "ok", Kind = PlaceListKinds.Landmark, Template = "earth_pyramid_third", Latitude = 10, Longitude = 10 },
                new PlaceDefinition { Key = "nowhere", Kind = PlaceListKinds.Landmark, Template = "no_such_template" },
                new PlaceDefinition { Key = "pooled", Kind = PlaceListKinds.City, Landmarks = { pooled } },
                new PlaceDefinition { Key = "north", Kind = PlaceListKinds.Pad, Latitude = 90.0 },
                new PlaceDefinition { Key = "east", Kind = PlaceListKinds.Pad, Longitude = 360.0 },
                new PlaceDefinition { Key = "tram", Kind = "tram" }, // #3985: "rail" is a kind now
                new PlaceDefinition { Key = "ok", Kind = PlaceListKinds.Pad },
                new PlaceDefinition
                {
                    Key = "hq", Kind = PlaceListKinds.Complex, Turn = 1, Template = "earth_pyramid_third",
                    Members =
                    {
                        new PlaceMember { Template = "earth_sps_hq_core" },                            // 41 × 37
                        new PlaceMember { Template = "earth_sps_hq_hall", East = 40, North = 36 },     // overlaps the core
                        new PlaceMember { Template = "earth_sps_hq_main", East = -160 },               // meets it edge to edge
                    },
                },
                new PlaceDefinition { Key = "empty_hq", Kind = PlaceListKinds.Complex },
                new PlaceDefinition { Key = "padded", Kind = PlaceListKinds.Pad, Members = { new PlaceMember { Template = "earth_sps_hq_core" } } },
            },
        };

        var problems = PlaceRules.Problems(list, Content.LandmarkByKey);
        Assert.DoesNotContain(problems, p => p.StartsWith("place 'ok' names", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown landmark template 'no_such_template'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains($"'{pooled}', which is not pinOnly", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("place 'north' lies outside", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("place 'east' lies outside", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown kind 'tram'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unique in its list", StringComparison.Ordinal)); // the second "ok"
        Assert.Contains("place 'hq' is a complex and names a template (its members name theirs)", problems);
        Assert.Contains("place 'hq' is a complex with a turn (its members stand unturned, in their shared frame)", problems);
        Assert.Contains("place 'hq' has the members 'earth_sps_hq_core' and 'earth_sps_hq_hall' overlapping", problems);
        Assert.DoesNotContain(problems, p => p.Contains("earth_sps_hq_main", StringComparison.Ordinal));
        Assert.Contains("place 'empty_hq' is a complex without members", problems);
        Assert.Contains("place 'padded' names members, which only a complex has", problems);
        Assert.Empty(PlaceRules.Problems(EarthPlaces, Content.LandmarkByKey));
    }

    /// <summary>#3982: a template a place list names stands in no pool (pinOnly), and every Earth landmark is named by a
    /// place — a landmark place or a city's landmark key.</summary>
    [Fact]
    public void PlaceOnlyGuard_EveryListedTemplateIsPinOnly_AndEveryEarthLandmarkIsNamed()
    {
        var named = Content.PlaceLists.Values.SelectMany(PlaceRules.NamedTemplates).ToHashSet(StringComparer.Ordinal);
        Assert.All(named, key => Assert.True(Content.LandmarkByKey(key) is { PinOnly: true }, $"'{key}' is unknown or in a pool"));

        var earthTemplates = Content.Landmarks.Where(t => t.Key.StartsWith("earth_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(earthTemplates);
        Assert.All(earthTemplates, t => Assert.True(named.Contains(t.Key), $"no place names the Earth landmark '{t.Key}'"));
    }

    /// <summary>A generator for Earth at its real size, at <paramref name="generation"/>.</summary>
    private static WorldGenerator EarthGenerator(int generation = WorldDescription.CurrentTerrainGeneration)
    {
        var gen = new WorldGenerator(20261124, Content);
        gen.SetTerrainGeneration(generation);
        gen.SetWorldMode(EarthCircumference, cratered: false, landingPads: null, locationId: "sol-earth");
        return gen;
    }

    [Fact]
    public void EveryPlaceAndPad_StandsOnLand_OnTheRealMap()
    {
        var gen = EarthGenerator();
        var earth = Content.GetPlanet(Earth)!;
        int sea = gen.SeaLevel(earth);
        var wet = new List<string>();
        foreach (var (key, cx, cz, hw, hl) in EarthAreas())
        {
            int step = Math.Max(1, Math.Max(hw, hl) / 6);
            for (int dx = -hw; dx <= hw; dx += Math.Max(1, step))
                for (int dz = -hl; dz <= hl; dz += Math.Max(1, step))
                {
                    if (gen.SurfaceHeight(earth, cx + dx, cz + dz) <= sea)
                    {
                        wet.Add($"{key} at {cx + dx},{cz + dz}");
                    }
                }
        }

        Assert.True(wet.Count == 0, "in the sea: " + string.Join("; ", wet.Take(12)));
    }

    /// <summary>Earth's places as (key, centre X, centre Z, half width, half length): a pad's whole square, a city's centre
    /// column, and every listed landmark's footprint — a landmark place's and each complex member's.</summary>
    private static List<(string Key, int Cx, int Cz, int Hw, int Hl)> EarthAreas()
    {
        var areas = new List<(string, int, int, int, int)>();
        foreach (var place in EarthPlaces.Places.Where(p => p.Kind is PlaceListKinds.Pad or PlaceListKinds.City))
        {
            var (cx, cz) = place.BlockOn(EarthCircumference);
            int reach = place.Kind == PlaceListKinds.Pad ? 8 : 0;
            areas.Add((place.Key, cx, cz, reach, reach));
        }

        foreach (var listed in PlaceRules.ListedLandmarks(EarthPlaces))
        {
            var t = Content.LandmarkByKey(listed.Template)!;
            var (cx, cz) = listed.CentreOn(EarthCircumference, t.Width, t.Length);
            areas.Add((listed.Template, cx, cz, t.Width / 2, t.Length / 2));
        }

        return areas;
    }

    /// <summary>#3986: no pad's footprint (radius 16, the levelled square) reaches into a listed landmark's footprint — above
    /// all the rain-forest pad beside the SPS headquarters.</summary>
    [Fact]
    public void EveryPad_KeepsClearOfEveryListedLandmark()
    {
        const int padReach = 16;
        foreach (var pad in EarthPlaces.OfKind(PlaceListKinds.Pad))
        {
            var (px, pz) = pad.BlockOn(EarthCircumference);
            foreach (var listed in PlaceRules.ListedLandmarks(EarthPlaces))
            {
                var t = Content.LandmarkByKey(listed.Template)!;
                var (cx, cz) = listed.CentreOn(EarthCircumference, t.Width, t.Length);
                var (ox, oz) = new GivenSeat(cx, cz).OriginFor(t.Width, t.Length);
                bool apart = px + padReach < ox || px - padReach >= ox + t.Width || pz + padReach < oz || pz - padReach >= oz + t.Length;
                Assert.True(apart, $"{pad.Key} reaches into {listed.Template}");
            }
        }

        // The rain-forest pad stands east of the headquarters, 70 blocks beyond the frame's east edge.
        var (hx, _) = EarthPlaces.Places.Single(p => p.Key == "sps_hq").BlockOn(EarthCircumference);
        var (rx, _) = EarthPlaces.Places.Single(p => p.Key == "pad_sps_hq").BlockOn(EarthCircumference);
        Assert.InRange(rx - hx, 240, 260);
    }

    [Fact]
    public void TheGizaPyramids_StandApartFromEachOther_AndOutsideCairo()
    {
        var cairo = EarthPlaces.Places.Single(p => p.Key == "cairo").BlockOn(EarthCircumference);
        var boxes = EarthPlaces.OfKind(PlaceListKinds.Landmark).Select(p =>
        {
            var t = Content.LandmarkByKey(p.Template)!;
            var (x, z) = p.BlockOn(EarthCircumference);
            return (Key: p.Key, MinX: x - (t.Width / 2), MinZ: z - (t.Length / 2), MaxX: x - (t.Width / 2) + t.Width, MaxZ: z - (t.Length / 2) + t.Length);
        }).ToList();
        foreach (var b in boxes)
        {
            double ex = Math.Max(0, Math.Max(b.MinX - cairo.X, cairo.X - b.MaxX));
            double ez = Math.Max(0, Math.Max(b.MinZ - cairo.Z, cairo.Z - b.MaxZ));
            Assert.True(Math.Sqrt((ex * ex) + (ez * ez)) >= 120, $"{b.Key} reaches into Cairo's 120-block circle");
            Assert.True(b.MaxX < cairo.X, $"{b.Key} is not west of Cairo");
        }

        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (a, c) = (boxes[i], boxes[j]);
                bool apart = a.MaxX <= c.MinX || c.MaxX <= a.MinX || a.MaxZ <= c.MinZ || c.MaxZ <= a.MinZ;
                Assert.True(apart, $"{a.Key} touches {c.Key}");
            }

        // The real arrangement: the great one north-east, each next one further south-west.
        Assert.True(boxes[0].MinX > boxes[1].MinX && boxes[0].MinZ > boxes[1].MinZ && boxes[1].MinX > boxes[2].MinX && boxes[1].MinZ > boxes[2].MinZ);
    }

    [Theory]
    [InlineData(WorldDescription.CurrentTerrainGeneration)]
    [InlineData(WorldDescription.RuneShrineGeneration - 1)]
    [InlineData(WorldDescription.ExtremePlanetsGeneration - 1)]
    public void NoAlienRunes_StandOnEarth_OnAnyGeneration(int generation)
    {
        var gen = EarthGenerator(generation);
        var earth = Content.GetPlanet(Earth)!;
        Assert.DoesNotContain("rune-stone", gen.PropActiveForTest(earth, crystalWorld: false, dryWorld: false));
        for (int x = 0; x < EarthCircumference; x += 997)
            for (int z = -3000; z <= 3000; z += 499)
            {
                Assert.Empty(gen.RuneShrineStoneBasesForTest(earth, x, z));
            }

        // A whitelisted type that names the rune stones keeps them, as it always did.
        Assert.Contains("rune-stone", gen.PropActiveForTest(Content.GetPlanet("valuma")!, crystalWorld: false, dryWorld: false));
    }
}
