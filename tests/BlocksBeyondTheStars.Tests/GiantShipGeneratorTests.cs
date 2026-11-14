// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The giant ship generator (#3609): a giant ship template in, a wreck out — on a planet (profile crash, turned, the landmark
/// layout) and in space (profile hull, never turned, every big piece kept) — deterministic, water frozen without air, the
/// chests, sounds and entrances kept (the doorways too in space), the boarding arrival always standable, and a stable content
/// fingerprint.
/// </summary>
public sealed class GiantShipGeneratorTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly GiantShipTestHulls.Hull Hull = GiantShipTestHulls.Build(120, 24, 32);

    private static readonly StructureTemplate Ship = GiantShipTestHulls.Template(Hull);

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static LandmarkLayout Planet(long seed, int turns = 0, bool air = true, StructureTemplate? t = null)
        => GiantShipGenerator.BuildPlanet(t ?? Ship, seed, turns, Content, "grass", air);

    private static ulong Hash(GiantSpaceLayout s)
    {
        var h = new StructureGoldenHash(Content);
        h.Add(s.Key).Add(s.IntactCells).Add(s.Entrance);
        h.Grid(s.Width, s.Height, s.Length, s.Get, s.GetModifier, s.GetShape);
        h.Add(s.Markers.Count);
        foreach (var m in s.Markers)
        {
            h.Add(m.Type).Add(m.LocalPos).Add(m.Data);
        }

        return h.Value;
    }

    private static bool Solid(ushort id) => id != 0 && Content.BlockById(new Shared.Primitives.BlockId(id))!.Solid;

    /// <summary>A visitor can stand at the cell: it and the one above free, a solid block (or the rock below the ground row)
    /// under it.</summary>
    private static bool Standable(System.Func<int, int, int, ushort> get, int h, int ground, Vector3i p)
    {
        bool free(int y) => y >= h || (get(p.X, y, p.Z) == 0 && y >= ground);
        bool floor = p.Y - 1 < 0 ? ground > 0 : Solid(get(p.X, p.Y - 1, p.Z)) || (p.Y - 1 < ground && get(p.X, p.Y - 1, p.Z) == 0);
        return free(p.Y) && free(p.Y + 1) && floor;
    }

    [Fact]
    public void BothSettings_AreDeterministic_AndDifferBySeed()
    {
        ulong a = StructureGoldens.Landmark(Content, Planet(3));
        Assert.Equal(a, StructureGoldens.Landmark(Content, Planet(3)));
        Assert.NotEqual(a, StructureGoldens.Landmark(Content, Planet(4)));

        ulong s = Hash(GiantShipGenerator.BuildSpace(Ship, 3, Content));
        Assert.Equal(s, Hash(GiantShipGenerator.BuildSpace(Ship, 3, Content)));
        Assert.NotEqual(s, Hash(GiantShipGenerator.BuildSpace(Ship, 4, Content)));
    }

    [Fact]
    public void Planet_IsTheLandmarkLayout_TurnedAndRuined()
    {
        var l = Planet(1, turns: 1);
        Assert.True(l.Ruined);
        Assert.Equal(1, l.Turns);
        Assert.Equal(Hull.Ground, l.GroundRow);
        Assert.Equal(GiantShipGenerator.Tier, l.Structure.Tier);
        Assert.Equal((Hull.L, Hull.H, Hull.W), (l.Structure.Width, l.Structure.Height, l.Structure.Length));

        var space = GiantShipGenerator.BuildSpace(Ship, 1, Content);
        Assert.Equal((Hull.W, Hull.H, Hull.L), (space.Width, space.Height, space.Length)); // never turned
        Assert.Equal(Hull.Blocks.Count, space.IntactCells);
        Assert.InRange(space.SolidCells, space.IntactCells / 2, space.IntactCells - 1);
    }

    [Fact]
    public void Water_FreezesWithoutAir_AndStaysWaterWithIt()
    {
        ushort water = Id("water"), ice = Id("ice");
        for (long seed = 1; seed <= 3; seed++)
        {
            var wet = Planet(seed, air: true);
            var dry = Planet(seed, air: false);
            var space = GiantShipGenerator.BuildSpace(Ship, seed, Content);
            bool Has(LandmarkLayout l, ushort b) => GiantShipTestHulls.Cells(l.Structure).Any(c => l.Structure.Get(c.X, c.Y, c.Z) == b);
            Assert.True(Has(wet, water) && !Has(wet, ice), $"seed {seed}: the pool on a body with air froze");
            Assert.True(Has(dry, ice) && !Has(dry, water), $"seed {seed}: the pool on an airless body did not freeze");
            Assert.Contains(ice, space.Blocks);
            Assert.DoesNotContain(water, space.Blocks);
        }
    }

    [Fact]
    public void Markers_ChestsSoundsEntrancesSurvive_DoorwaysOnlyInSpace()
    {
        int chests = Hull.Markers.Count(m => m.Id == LandmarkRules.ChestMarker);
        int doors = Hull.Markers.Count(m => m.Id == GiantShipGenerator.DoorMarker);
        for (long seed = 1; seed <= 4; seed++)
        {
            var planet = Planet(seed).Structure.Markers;
            Assert.Equal(chests, planet.Count(m => m.Type == LandmarkRules.ChestMarker));
            Assert.Single(planet, m => m.Type == LandmarkRules.SoundMarker);
            Assert.Single(planet, m => m.Type == GiantShipRules.EntranceMarker);
            Assert.DoesNotContain(planet, m => m.Type == "npc" || m.Type == GiantShipGenerator.DoorMarker);
            Assert.All(planet.Where(m => m.Type == LandmarkRules.ChestMarker), m => Assert.Equal("table=ruin_common", m.Data));

            var space = GiantShipGenerator.BuildSpace(Ship, seed, Content).Markers;
            Assert.Equal(chests, space.Count(m => m.Type == LandmarkRules.ChestMarker));
            Assert.Equal(doors, space.Count(m => m.Type == GiantShipGenerator.DoorMarker));
            Assert.DoesNotContain(space, m => m.Type == "npc");

            // A doorway stays exactly where the author put it, its settings line (axis=x|z) intact: the derelict decay reads the
            // rooms from it.
            var authored = Hull.Markers.Where(m => m.Id == GiantShipGenerator.DoorMarker).Select(m => (new Vector3i(m.X, m.Y, m.Z), m.Config));
            Assert.Equal(authored, space.Where(m => m.Type == GiantShipGenerator.DoorMarker).Select(m => (m.LocalPos, m.Data)));
        }
    }

    /// <summary>Cavity cells (the toolkit's hollow buried decks below the ground row): on a planet they stay carved air in the
    /// rock — the pass never fills them, rubble never lies in them, nothing anchors on them; in space they are plain air.</summary>
    [Fact]
    public void CavityCells_StayHollowOnAPlanet_AndAreAirInSpace()
    {
        var t = GiantShipTestHulls.Template(Hull, "test_hollow");
        var bilge = new List<Vector3i>();
        for (int x = 30; x < 90; x++)
        {
            for (int y = 2; y < Hull.Ground; y++)
            {
                for (int z = Hull.Zc - 3; z <= Hull.Zc + 3; z++)
                {
                    bilge.Add(new Vector3i(x, y, z)); // inside the keel, below the ground row: empty in the hull
                }
            }
        }

        Assert.All(bilge, c => Assert.DoesNotContain(Hull.Blocks, b => b.X == c.X && b.Y == c.Y && b.Z == c.Z));
        t.Cells.AddRange(bilge.Select(c => new TemplateCell { Kind = LandmarkRules.CavityKind, X = c.X, Y = c.Y, Z = c.Z }));
        for (long seed = 1; seed <= 3; seed++)
        {
            var planet = GiantShipGenerator.BuildPlanet(t, seed, 0, Content, "grass", atmosphere: true);
            Assert.Equal(bilge.Count, planet.CavityCount);
            Assert.All(bilge, c => Assert.True(planet.IsCavity(c.X, c.Y, c.Z) && planet.Structure.Get(c.X, c.Y, c.Z) == 0,
                $"seed {seed}: the cavity at {c} was filled"));

            var space = GiantShipGenerator.BuildSpace(t, seed, Content);
            Assert.All(bilge, c => Assert.Equal(0, space.Get(c.X, c.Y, c.Z)));
            Assert.Equal(Hash(GiantShipGenerator.BuildSpace(GiantShipTestHulls.Template(Hull, "test_hollow"), seed, Content)), Hash(space));
        }
    }

    /// <summary>The boarding arrival and every chest can be stood on — in both settings, for many seeds — and no two share a cell.</summary>
    [Fact]
    public void EntranceAndChests_AreStandable_AndApart()
    {
        for (long seed = 1; seed <= 8; seed++)
        {
            var planet = Planet(seed, turns: (int)(seed % 4));
            var s = planet.Structure;
            foreach (var m in s.Markers.Where(m => m.Type is LandmarkRules.ChestMarker or GiantShipRules.EntranceMarker))
            {
                Assert.True(Standable(s.Get, s.Height, planet.GroundRow, m.LocalPos), $"seed {seed}: the planet's {m.Type} at {m.LocalPos} floats");
            }

            var space = GiantShipGenerator.BuildSpace(Ship, seed, Content);
            Assert.Equal(space.Markers.First(m => m.Type == GiantShipRules.EntranceMarker).LocalPos, space.Entrance);
            foreach (var m in space.Markers.Where(m => m.Type is LandmarkRules.ChestMarker or GiantShipRules.EntranceMarker))
            {
                Assert.True(Standable(space.Get, space.Height, 0, m.LocalPos), $"seed {seed}: the space wreck's {m.Type} at {m.LocalPos} floats");
            }

            var cells = space.Markers.Where(m => m.Type != GiantShipGenerator.DoorMarker && m.Type != LandmarkRules.SoundMarker)
                .Select(m => m.LocalPos).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    /// <summary>In space nothing falls: over a floor that is gone (here a shaft cut through every deck and the bottom), a sound
    /// stays where it is and a chest moves to the nearest standable cell inside the hull — never down to the bottom of the grid,
    /// never outside the wreck's box. (The landmark rule used to drop both to row 0, under the hull.)</summary>
    [Fact]
    public void Space_AMarkerOverACollapsedFloor_StaysInsideTheHull()
    {
        var hull = GiantShipTestHulls.Build(120, 24, 32);
        int floor = hull.Deck(1), zc = hull.Zc;
        hull.Blocks.RemoveAll(b => b.X >= 49 && b.X <= 53 && b.Z >= zc + 4 && b.Z <= zc + 8 && b.Y <= floor);
        var sound = new Vector3i(50, floor + 1, zc + 5);
        var chest = new Vector3i(52, floor + 1, zc + 7);
        hull.Markers.Add(new TemplateCell { Kind = "marker", Id = LandmarkRules.SoundMarker, X = sound.X, Y = sound.Y, Z = sound.Z, Config = "sound=amb_engine" });
        hull.Markers.Add(new TemplateCell { Kind = "marker", Id = LandmarkRules.ChestMarker, X = chest.X, Y = chest.Y, Z = chest.Z, Config = "table=shaft" });
        var t = GiantShipTestHulls.Template(hull, "test_shaft");
        for (long seed = 1; seed <= 4; seed++)
        {
            var space = GiantShipGenerator.BuildSpace(t, seed, Content);
            Assert.Equal(sound, space.Markers.Single(m => m.Data == "sound=amb_engine").LocalPos);

            var c = space.Markers.Single(m => m.Data == "table=shaft").LocalPos;
            Assert.True(Standable(space.Get, space.Height, 0, c), $"seed {seed}: the chest at {c} floats");
            Assert.Contains(Enumerable.Range(c.Y + 2, System.Math.Max(0, space.Height - c.Y - 2)), y => space.Get(c.X, y, c.Z) != 0);

            var solid = Enumerable.Range(0, space.Blocks.Length).Where(i => space.Blocks[i] != 0)
                .Select(i => (X: i / (space.Height * space.Length), Y: i % (space.Height * space.Length) / space.Length, Z: i % space.Length)).ToList();
            foreach (var m in space.Markers)
            {
                var p = m.LocalPos;
                Assert.True(p.X >= solid.Min(s => s.X) && p.X <= solid.Max(s => s.X) && p.Y > solid.Min(s => s.Y) && p.Y <= solid.Max(s => s.Y)
                    && p.Z >= solid.Min(s => s.Z) && p.Z <= solid.Max(s => s.Z), $"seed {seed}: the {m.Type} at {p} lies outside the wreck");
            }
        }
    }

    /// <summary>An entrance with no floor — here authored in the open below the hull's tapered tail — moves to the nearest
    /// standable cell inside the hull (with a roof above it).</summary>
    [Fact]
    public void AnEntranceWithoutAFloor_MovesToTheNearestStandableCellInside()
    {
        var t = GiantShipTestHulls.Template(Hull, "test_hulk_lost");
        var lost = t.Cells.First(c => c.Kind == "marker" && c.Id == GiantShipRules.EntranceMarker);
        (lost.X, lost.Y, lost.Z) = (1, 0, Hull.Zc); // under the tapered tail: open space, nothing below

        var space = GiantShipGenerator.BuildSpace(t, 2, Content);
        Assert.NotEqual(new Vector3i(1, 0, Hull.Zc), space.Entrance);
        Assert.True(Standable(space.Get, space.Height, 0, space.Entrance), $"the arrival at {space.Entrance} floats");
        Assert.Contains(Enumerable.Range(space.Entrance.Y + 2, System.Math.Max(0, space.Height - space.Entrance.Y - 2)),
            y => space.Get(space.Entrance.X, y, space.Entrance.Z) != 0); // a roof: it is inside

        var planet = GiantShipGenerator.BuildPlanet(t, 2, 0, Content, "grass", atmosphere: false);
        var e = planet.Structure.Markers.Single(m => m.Type == GiantShipRules.EntranceMarker).LocalPos;
        Assert.True(e.Y >= planet.GroundRow && Standable(planet.Structure.Get, planet.Structure.Height, planet.GroundRow, e),
            $"the planet's entrance at {e} is in the rock or floats");
    }

    [Fact]
    public void AnAlienShip_BreaksIntoCrystal_AHumanOneIntoScrap()
    {
        var human = Planet(5);
        var alien = Planet(5, t: GiantShipTestHulls.Template(Hull, "test_squid", style: GiantShipRules.StyleAlien));
        var scrapKeys = new[] { "scrap_metal", "scrap_pile", "rusted_panel" }.Select(Id).ToHashSet();
        bool Any(LandmarkLayout l, System.Func<ushort, bool> p) => GiantShipTestHulls.Cells(l.Structure).Any(c => p(l.Structure.Get(c.X, c.Y, c.Z)));
        Assert.True(Any(human, scrapKeys.Contains), "a human wreck left no scrap");
        Assert.False(Any(alien, scrapKeys.Contains), "an alien wreck left rust or scrap");
        Assert.True(Any(alien, b => b == Id("crystal")), "an alien wreck left no crystal");
        Assert.True(GiantShipGenerator.IsAlien(GiantShipTestHulls.Template(Hull, "x", style: "Alien")));
    }

    [Fact]
    public void Fingerprint_IsStable_AndFollowsTheBodyOnly()
    {
        string f = GiantShipGenerator.Fingerprint(Ship);
        Assert.Matches("^[0-9a-f]{16}$", f);
        Assert.Equal(f, GiantShipGenerator.Fingerprint(GiantShipTestHulls.Template(Hull)));

        var renamed = GiantShipTestHulls.Template(Hull, "another_key");
        (renamed.Name, renamed.Weight, renamed.Pack) = ("Another", 7, "school");
        renamed.PlanetTypes.Add("rocky");
        Assert.Equal(f, GiantShipGenerator.Fingerprint(renamed));

        var deeper = GiantShipTestHulls.Template(Hull);
        deeper.GroundRow++;
        Assert.NotEqual(f, GiantShipGenerator.Fingerprint(deeper));

        var moved = GiantShipTestHulls.Template(Hull);
        moved.Cells.First(c => c.Kind == "marker").X++;
        Assert.NotEqual(f, GiantShipGenerator.Fingerprint(moved));

        var repainted = GiantShipTestHulls.Template(Hull);
        repainted.Palette[0].Tint = 0x123456;
        Assert.NotEqual(f, GiantShipGenerator.Fingerprint(repainted));
    }

    /// <summary>Ruin zones name their profile per setting: a hull zone on a planet decays everywhere, every zone in space is a hull.</summary>
    [Fact]
    public void RuinZones_AreHonoured_AndDeterministic()
    {
        var zoned = GiantShipTestHulls.Template(Hull, "test_zoned");
        zoned.RuinZones.Add(new RuinZone { X0 = 0, Y0 = 0, Z0 = 0, X1 = 30, Y1 = Hull.H - 1, Z1 = Hull.L - 1, Profile = "hull", Strength = 1.0 });
        ulong a = StructureGoldens.Landmark(Content, Planet(6, t: zoned));
        Assert.Equal(a, StructureGoldens.Landmark(Content, Planet(6, t: zoned)));
        Assert.NotEqual(StructureGoldens.Landmark(Content, Planet(6)), a);
        Assert.NotEqual(Hash(GiantShipGenerator.BuildSpace(Ship, 6, Content)), Hash(GiantShipGenerator.BuildSpace(zoned, 6, Content)));
    }

    /// <summary>The pins (#3609): the fingerprint of the synthetic ship and its wrecks in both settings. A change that moves a
    /// wreck pin moves every giant wreck that stands — it needs a new ruin revision.</summary>
    [Fact]
    public void Wrecks_MatchThePins()
    {
        var cases = new List<(string, ulong)>
        {
            ("fingerprint", System.Convert.ToUInt64(GiantShipGenerator.Fingerprint(Ship), 16)),
        };
        foreach (long seed in new long[] { 1, 20261114 })
        {
            cases.Add(($"planet:{seed}:air", StructureGoldens.Landmark(Content, Planet(seed, turns: 1, air: true))));
            cases.Add(($"planet:{seed}:airless", StructureGoldens.Landmark(Content, Planet(seed, turns: 2, air: false))));
            cases.Add(($"space:{seed}", Hash(GiantShipGenerator.BuildSpace(Ship, seed, Content))));
        }

        StructureGoldens.AssertPinned("giant wrecks", Pins, cases);
    }

    private static readonly Dictionary<string, string> Pins = new()
    {
        ["fingerprint"] = "e40fc6c9d03b33a6",
        ["planet:1:air"] = "7d8c6a42039789a8",
        ["planet:1:airless"] = "3494ebda51f2bd94",
        ["space:1"] = "275c63c7954ec204",
        ["planet:20261114:air"] = "bd9f3969732835a1",
        ["planet:20261114:airless"] = "410617d77e363f48",
        ["space:20261114"] = "205b6cca4b6b7185",
    };
}
