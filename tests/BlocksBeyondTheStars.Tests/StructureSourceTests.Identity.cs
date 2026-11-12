// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3512: each wrapper builds exactly what its generator builds — called here directly, with the arguments the server's
/// stamper passes for a fresh instance — cell for cell (block, dye/glow, shape) and marker for marker. A wrapper that adds,
/// drops or reorders anything fails here.
/// </summary>
public sealed partial class StructureSourceTests
{
    /// <summary>The generation a request builds for by default — the generators are called with it too (#3516: the ruins,
    /// monuments, G.D.S. ruins and SPS labs of generation 38 decay through the shared ruin pass).</summary>
    private const int Generation = WorldDescription.CurrentTerrainGeneration;

    private static BuiltStructure Build(string key, long seed, params (string Key, string Value)[] values)
        => StructureSources.Get(key)!.Build(new StructureBuildRequest(Content, seed)
        {
            Params = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        });

    private static void AssertSame(BuiltStructure b, SettlementStructure s, int yShift = 0)
    {
        AssertGrid(b, s.Width, s.Height, s.Length, yShift, s.Get, s.GetModifier, s.GetShape);
        AssertMarkers(s.Markers.Select(m => new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y + yShift, m.LocalPos.Z, m.Data, m.DoorAxis)),
            b.Markers.Take(s.Markers.Count));
    }

    private static void AssertSame(BuiltStructure b, StationStructure s)
    {
        AssertGrid(b, s.Width, s.Height, s.Length, 0, s.Get, s.GetModifier, s.GetShape);
        AssertMarkers(s.Markers.Select(m => new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z, m.Data)), b.Markers);
    }

    private static void AssertGrid(BuiltStructure b, int w, int h, int l, int yShift, Func<int, int, int, ushort> get,
        Func<int, int, int, (int Tint, int Glow)>? mod = null, Func<int, int, int, int>? shape = null)
    {
        Assert.Equal(w, b.Width);
        Assert.Equal(l, b.Length);
        Assert.Equal(h + yShift, b.Height);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    if (b.Get(x, y + yShift, z) != get(x, y, z) || b.GetModifier(x, y + yShift, z) != (mod?.Invoke(x, y, z) ?? (0, 0))
                        || b.GetShape(x, y + yShift, z) != (shape?.Invoke(x, y, z) ?? 0))
                    {
                        Assert.Fail($"{b.SourceKey}: cell ({x},{y},{z}) differs from the generator's");
                    }
                }
    }

    private static void AssertMarkers(IEnumerable<BuiltMarker> expected, IEnumerable<BuiltMarker> actual)
        => Assert.Equal(expected.ToList(), actual.ToList());

    [Fact]
    public void Settlement_IsTheGeneratorsFreshSettlement()
    {
        var modules = Content.SettlementModulesFor(new List<string>(), null);
        double chance = modules.Count == 0 ? 0 : new WorldDescription().SettlementTemplateUse.Probability();
        var direct = SettlementGenerator.Generate("town", true, SeedA, "stone", Content, modules, chance, new List<string>(),
            terrainGeneration: Generation);
        var b = Build("settlement", SeedA, ("tier", "town"), ("ruined", "true"));
        AssertSame(b, direct);
        Assert.Equal(direct.Markers.Count, b.Markers.Count);
    }

    [Fact]
    public void SettlementKit_IsTheGeneratorsKitSettlement()
    {
        var kit = Content.KitsFor(StructureKit.KindSettlement, "village", null, null).First();
        var modules = Content.SettlementModulesFor(new List<string>(), null);
        double chance = modules.Count == 0 ? 0 : new WorldDescription().SettlementTemplateUse.Probability();
        long lane = SeedB ^ WorldGenerator.StableHash("kitlayout");
        var layout = SettlementLayoutSpec.FromKit(kit, kit.Tier, new Random(unchecked((int)(lane ^ (lane >> 32)))));
        var direct = SettlementGenerator.Generate(kit.Tier, false, SeedB, "stone", Content, modules, chance, new List<string>(), null,
            layout, kit, Content.SettlementTemplates.Where(t => t.IsModule).ToList());
        var b = Build("settlement", SeedB, ("kit", kit.Key));
        AssertSame(b, direct);
        Assert.Equal(direct.Markers.Count, b.Markers.Count);
    }

    [Fact]
    public void FallenRuin_City_Monument_Camp_Lab_Factory_Rail_AreTheirGenerators()
    {
        AssertSame(Build("fallen_ruin", SeedA, ("tier", "city")),
            SettlementGenerator.Generate("city", ruined: true, SeedA, "stone", Content, terrainGeneration: Generation));
        bool cache = new Random(unchecked((int)(SeedB ^ (SeedB >> 32)))).NextDouble() < 0.35;
        AssertSame(Build("monument", SeedB, ("archetype", "ziggurat")),
            MonumentGenerator.Generate("ziggurat", SeedB, "stone", Content, cache, Generation, airless: false));
        AssertSame(Build("bandit_camp", SeedA), BanditCampGenerator.Generate(SeedA, "stone", Content));
        AssertSame(Build("sps_lab", SeedB), SpsLabGenerator.Generate(SeedB, "stone", Content, Generation));
        AssertSame(Build("factory", SeedA, ("machines", "5")), FactoryGenerator.Generate(SeedA, 5, Content));
        AssertSame(Build("rail_station", SeedA, ("heading", "2")), RailStationGenerator.Generate(2, Content));
        AssertSame(Build("rail_ruin", SeedB, ("heading", "3")), RailRuinGenerator.Generate(SeedB, 3, "stone", Content));
    }

    [Fact]
    public void City_IsTheCityWorldsCity()
    {
        var modules = Content.SettlementModulesFor(new List<string>(), null);
        double chance = modules.Count == 0 ? 0 : new WorldDescription().SettlementTemplateUse.Probability();
        int size = CityLayoutSpec.Default.Footprint, c = size / 2;
        var zones = new List<CityGenerator.OpenZone>
        {
            new(c - 11, c - 11, c + 11, c + 11),                 // the pad ring (radius 8 + 3)
            new(c - 56 - 14, c + 56 - 14, c - 56 + 14, c + 56 + 14), // the wreck crash site
        };
        var direct = CityGenerator.Generate(SeedA, Content, zones, modules, chance, new List<string>());
        AssertSame(Build("city", SeedA), direct);
    }

    [Fact]
    public void Lodge_IsTheGeneratorsLodge()
    {
        var job = Content.Lodges[0];
        long lane = SeedA ^ WorldGenerator.StableHash("alien");
        bool alien = new Random(unchecked((int)(lane ^ (lane >> 32)))).NextDouble() < 0.3;
        AssertSame(Build("lodge", SeedA, ("profession", job.Key)), LodgeGenerator.Generate(SeedA, job.Yard, "stone", new List<string>(), alien, Content));
        AssertSame(Build("lodge", SeedB, ("profession", job.Key), ("alien", "yes")),
            LodgeGenerator.Generate(SeedB, job.Yard, "stone", new List<string>(), true, Content));
    }

    [Fact]
    public void PioneerOutpost_IsTheGeneratorsOutpost_WithItsCellarBelowTheGroundRow()
    {
        var outpost = PioneerOutpostGenerator.Generate(SeedA, "stone", Content);
        var b = Build("pioneer_outpost", SeedA);
        int shift = b.GroundRow;
        Assert.True(shift > 0, "the cellar lies below the floor row");
        Assert.Equal(-outpost.Below.Min(c => c.Local.Y), shift);
        AssertSame(b, outpost.Structure, shift);
        var last = new Dictionary<(int, int, int), (ushort Block, int Shape)>();
        foreach (var (local, block, shape) in outpost.Below)
        {
            last[(local.X, local.Y, local.Z)] = (block, shape);
        }

        foreach (var ((x, y, z), (block, shape)) in last)
        {
            Assert.Equal(block, b.Get(x, y + shift, z));
            Assert.Equal(shape, b.GetShape(x, y + shift, z));
            Assert.Equal(block == 0, b.IsCavity(x, y + shift, z));
        }

        var below = outpost.BelowMarkers.Select(m => new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y + shift, m.LocalPos.Z, m.Data, m.DoorAxis));
        AssertMarkers(below, b.Markers.Skip(outpost.Structure.Markers.Count));

        var forced = PioneerOutpostGenerator.Generate(SeedB, "stone", Content, "steel", hidden: true, chests: 4);
        AssertSame(Build("pioneer_outpost", SeedB, ("wall", "steel"), ("hidden", "yes"), ("chests", "4")), forced.Structure,
            -forced.Below.Min(c => c.Local.Y));
    }

    [Fact]
    public void PioneerOutpost_WithBoat_KeepsTheOutpostAndAddsTheBoatInFront()
    {
        var plain = Build("pioneer_outpost", SeedA);
        var boat = Build("pioneer_outpost", SeedA, ("boat", "true"));
        for (int x = 0; x < plain.Width; x++)
            for (int y = 0; y < plain.Height; y++)
                for (int z = 0; z < plain.Length; z++)
                {
                    Assert.Equal(plain.Get(x, y, z), boat.Get(x, y, z));
                }

        Assert.True(boat.Length > plain.Length);
        Assert.Equal(PioneerOutpostGenerator.Boat(alongX: true, Content).Count(c => c.Block != 0), boat.CountBlocks() - plain.CountBlocks());
    }
}
