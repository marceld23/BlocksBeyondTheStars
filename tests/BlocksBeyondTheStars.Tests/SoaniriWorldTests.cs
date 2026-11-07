// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Soaniri, the Sounder home world (#3358, the Sounders story pack): the <c>sounder_world</c> type (a fixed name, never
/// rolled by the galaxy — the pack appends it), its blocks and singing plants (authored catalog species without the
/// <c>flora_</c> prefix that still draw as cross billboards), and the world generator's singing meadows (plants on chime moss
/// and in the reed marsh, obsidian outcrops with crystal caps, harp arches with vines under their lintels) — deterministic,
/// and on no other type.
/// </summary>
public sealed class SoaniriWorldTests
{
    private const string Key = SoundersIds.PlanetType;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] Plants =
    {
        FloraCatalog.ChimeReedKey, FloraCatalog.BellBloomKey, FloraCatalog.BellBloomOpenKey, FloraCatalog.HarpVineKey,
        FloraCatalog.EchoFlowerKey,
    };

    [Fact]
    public void TheType_IsTheFixedNamedSounderWorld_ThatNoRollPicks()
    {
        var p = Content.GetPlanet(Key);
        Assert.NotNull(p);
        Assert.Equal("Soaniri", p!.FixedName);
        Assert.False(p.Selectable);
        Assert.Equal(0, p.SpawnWeight);
        Assert.False(p.GuaranteedOnce);
        Assert.False(WorldConstants.IsAsteroidType(Key));
        Assert.Equal("breathable", p.Atmosphere);
        Assert.Equal("chime_moss", p.SurfaceBlock);
        Assert.True(p.SingingMeadows);
        Assert.True(p.RestrictStructures);
        Assert.Equal(new[] { "monuments" }, p.AllowedStructures);
        Assert.Equal(new[] { 4, 6 }, p.MonumentCount);
        Assert.Equal(0.0, p.EnemyDensity);
        Assert.Contains("mud", p.Biomes.Select(b => b.SurfaceBlock));
        Assert.Equal(new[] { SoundersIds.Soundi, SoundersIds.Soundrak, SoundersIds.Quali, SoundersIds.Soundeling }, p.AuthoredCreatures);
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var text = Content.CreateLocalizer(locale);
            Assert.Equal("Soaniri", text.Get("planet.sounder_world.name"));
        }
    }

    [Fact]
    public void NoGalaxy_RollsTheSounderWorld()
    {
        foreach (var exotic in new[] { Frequency.Normal, Frequency.Frequent })
        {
            for (long seed = 1; seed <= 30; seed++)
            {
                var desc = new WorldDescription
                {
                    StarSystemCount = 32,
                    SystemVariance = true,
                    AsteroidBelts = true,
                    TerrainGeneration = WorldDescription.CurrentTerrainGeneration,
                    ExoticWorlds = exotic,
                };
                var bodies = new UniverseGenerator(seed, desc, Content, "varied").Generate().AllBodies();
                Assert.DoesNotContain(bodies, b => b.PlanetType == Key);
            }
        }
    }

    [Fact]
    public void TheBlocks_AndTheSingingPlants_AreDefined()
    {
        foreach (var key in new[] { "chime_moss", "carved_obsidian", "resonance_crystal" }.Concat(Plants))
        {
            Assert.NotNull(Content.GetBlock(key));
        }

        Assert.NotNull(Content.GetBlock("resonance_crystal")!.LightColor);
        foreach (var key in Plants)
        {
            var block = Content.GetBlock(key)!;
            Assert.Equal("flora", block.Category);
            Assert.False(block.Solid); // walked through, no collider
            var species = FloraCatalog.Find(key);
            Assert.NotNull(species);
            Assert.True(species!.Authored); // in no roster: no world moves
            Assert.True(FloraCatalog.IsPlantKey(key));
            Assert.True(FloraCatalog.KeepsOwnColour(key));
            Assert.False(FloraCatalog.IsSolid(key)); // a cross billboard
            Assert.Equal(TextureAlphaMode.Cutout, TextureTiles.AlphaModeOf(key));
            Assert.True(WetCell.IsNonFullKey(key));
        }

        Assert.True(FloraCatalog.IsHanging(FloraCatalog.HarpVineKey));
        Assert.True(FloraCatalog.IsTall(FloraCatalog.ChimeReedKey));
        Assert.False(FloraCatalog.IsPlantKey("chime_moss"));
        Assert.True(FloraCatalog.IsPlantKey("flora_fern")); // the classic prefix rule still holds
        Assert.False(FloraCatalog.IsPlantKey(null));

        var en = Content.CreateLocalizer(GameLocale.English);
        var de = Content.CreateLocalizer(GameLocale.German);
        Assert.Equal("Chime Moss", en.Get("block.chime_moss.name"));
        Assert.Equal("Klangmoos", de.Get("block.chime_moss.name"));
        Assert.Equal("Resonanzkristall", de.Get("block.resonance_crystal.name"));
        Assert.Equal("Gemeißelter Obsidian", de.Get("block.carved_obsidian.name"));
        Assert.Equal("Klanghalm", de.Get("block.chime_reed.name"));
        Assert.Equal("Glockenblüte", de.Get("block.bell_bloom.name"));
        Assert.Equal("Harfenranke", de.Get("block.harp_vine.name"));
        Assert.Equal("Echoblume", de.Get("block.echo_flower.name"));
    }

    [Fact]
    public void TheSingingMeadows_GrowEveryPlant_OnItsHost_AndRaiseOutcropsAndArches()
    {
        var (gen, planet) = Generator(3358);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chunk in SurfaceChunks(gen, planet, 0, 0, 10))
        {
            CountAndCheckHosts(chunk, counts);
        }

        foreach (var key in new[] { FloraCatalog.BellBloomKey, FloraCatalog.ChimeReedKey, FloraCatalog.EchoFlowerKey, FloraCatalog.HarpVineKey,
                     "obsidian", "resonance_crystal" })
        {
            Assert.True(counts.GetValueOrDefault(key) > 0, $"no {key} in 20×20 chunks of Soaniri");
        }

        Assert.True(counts[FloraCatalog.BellBloomKey] > counts[FloraCatalog.EchoFlowerKey], "echo flowers are the rare ones");
        Assert.True(counts.GetValueOrDefault("vine_under_obsidian") > 0, "no harp vine under an arch's obsidian lintel");
        Assert.False(counts.ContainsKey(FloraCatalog.BellBloomOpenKey)); // only a song opens a bloom
    }

    [Fact]
    public void TheSingingMeadows_AreDeterministic_InAnyChunkOrder()
    {
        var (a, planet) = Generator(77);
        var (b, _) = Generator(77);
        var coords = SurfaceCoords(a, planet, 0, 0, 3).ToList();
        var forward = coords.Select(c => a.Generate(planet, c)).ToList();
        var backward = Enumerable.Reverse(coords).Select(c => b.Generate(planet, c)).Reverse().ToList();
        for (int i = 0; i < coords.Count; i++)
        {
            for (int x = 0; x < WorldConstants.ChunkSize; x++)
                for (int y = 0; y < WorldConstants.ChunkSize; y++)
                    for (int z = 0; z < WorldConstants.ChunkSize; z++)
                    {
                        Assert.Equal(forward[i].Get(x, y, z), backward[i].Get(x, y, z));
                    }
        }
    }

    [Fact]
    public void NoOtherType_GrowsASingingPlant()
    {
        var singing = Plants.Select(k => Content.GetBlock(k)!.NumericId).ToHashSet();
        foreach (var other in new[] { "varied", "jungle", "meadowlands", "crystal", "swamp" })
        {
            var planet = Content.GetPlanet(other)!;
            Assert.False(planet.SingingMeadows);
            var gen = new WorldGenerator(5, Content);
            gen.SetWorldMode(5472, false, null, "golden:" + other);
            gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
            foreach (var chunk in SurfaceChunks(gen, planet, 0, 0, 3))
            {
                for (int x = 0; x < WorldConstants.ChunkSize; x++)
                    for (int y = 0; y < WorldConstants.ChunkSize; y++)
                        for (int z = 0; z < WorldConstants.ChunkSize; z++)
                        {
                            Assert.DoesNotContain(chunk.Get(x, y, z), singing);
                        }
            }
        }
    }

    [Fact]
    public void TheZones_FollowTheBiomes()
    {
        var (gen, planet) = Generator(3358);
        var zones = new HashSet<string>(StringComparer.Ordinal);
        for (int x = -600; x <= 600; x += 40)
        {
            for (int z = -600; z <= 600; z += 40)
            {
                zones.Add(gen.SingingZoneAt(planet, x, z));
            }
        }

        Assert.Contains(zones, z => z is "meadow" or "slope");
        Assert.Equal("none", new WorldGenerator(1, Content).SingingZoneAt(Content.GetPlanet("varied")!, 0, 0));
    }

    // ---------------- helpers ----------------

    internal static (WorldGenerator Gen, PlanetType Planet) Generator(long seed)
    {
        var planet = Content.GetPlanet(Key)!;
        var gen = new WorldGenerator(seed, Content);
        gen.SetWorldMode(6400, false, null, StoryPackSystem.BodyIdOf(SoundersIds.SystemId));
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        return (gen, planet);
    }

    private static IEnumerable<ChunkCoord> SurfaceCoords(WorldGenerator gen, PlanetType planet, int cx0, int cz0, int half)
    {
        for (int cx = cx0 - half; cx < cx0 + half; cx++)
        {
            for (int cz = cz0 - half; cz < cz0 + half; cz++)
            {
                int wx = (cx * WorldConstants.ChunkSize) + 8, wz = (cz * WorldConstants.ChunkSize) + 8;
                int cy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, wx, wz));
                yield return new ChunkCoord(cx, cy, cz);
                yield return new ChunkCoord(cx, cy + 1, cz);
            }
        }
    }

    private static IEnumerable<ChunkData> SurfaceChunks(WorldGenerator gen, PlanetType planet, int cx0, int cz0, int half)
        => SurfaceCoords(gen, planet, cx0, cz0, half).Select(c => gen.Generate(planet, c));

    /// <summary>Counts the singing blocks of a chunk and checks every plant's host inside it: a standing plant on chime moss
    /// (the reed on mud too), a harp vine under obsidian.</summary>
    private static void CountAndCheckHosts(ChunkData chunk, Dictionary<string, int> counts)
    {
        var wanted = new HashSet<string>(Plants.Concat(new[] { "obsidian", "resonance_crystal" }), StringComparer.Ordinal);
        int n = WorldConstants.ChunkSize;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    string? key = Content.BlockById(chunk.Get(x, y, z))?.Key;
                    if (key is null || !wanted.Contains(key))
                    {
                        continue;
                    }

                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    if (key == FloraCatalog.HarpVineKey && y + 1 < n)
                    {
                        // it hangs from the lintel — obsidian, or where an arch met a hill or a crystal, that
                        string? above = Content.BlockById(chunk.Get(x, y + 1, z))?.Key;
                        Assert.True(above is not null && above != "air" && !Plants.Contains(above), $"a harp vine under {above}");
                        counts["vine_under_obsidian"] = counts.GetValueOrDefault("vine_under_obsidian") + (above == "obsidian" ? 1 : 0);
                    }
                    else if (Plants.Contains(key) && key != FloraCatalog.HarpVineKey && y >= 1)
                    {
                        string? host = Content.BlockById(chunk.Get(x, y - 1, z))?.Key;
                        Assert.True(host == "chime_moss" || (key == FloraCatalog.ChimeReedKey && host == "mud"), $"{key} on {host}");
                    }
                }
    }
}
