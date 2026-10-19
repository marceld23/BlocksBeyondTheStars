// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The mixed meadows (#2833, generation 28): twelve more wild bushes and plants join the catalog, and the surface flora
/// pick mixes per column, so several species stand side by side on one planet. An older world never gains one of the new
/// plants and keeps its one-species patches.
/// </summary>
public sealed class FloraMixedMeadowsTests
{
    private const int Gen = WorldDescription.MixedFloraGeneration;

    private static readonly string[] Wave =
    {
        "flora_berrybush", "flora_blossombush", "flora_featherbush", "flora_droopbush", "flora_needlebush", "flora_frostberry",
        "flora_clover", "flora_daisy", "flora_thistle", "flora_herb", "flora_foxtail", "flora_lupine",
    };

    private static readonly HashSet<string> AccentSpecies = new()
    {
        "flora_berrybush", "flora_blossombush", "flora_frostberry", "flora_clover", "flora_daisy", "flora_thistle", "flora_lupine",
    };

    private static readonly HashSet<string> BerrySpecies = new() { "flora_berrybush", "flora_frostberry" };

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // --- Catalog + blocks ---

    [Fact]
    public void TheWave_IsGeneration28_AppendedAfterTheFifiBerries_AndBeforeTheCrops()
    {
        // A wild species' roster id is its catalog index: the wave must append, never insert, or every world's plants
        // would be renamed.
        var keys = FloraCatalog.All.Select(s => s.Key).ToList();
        int fifi = keys.IndexOf(FifiPlant.BerriesKey);
        Assert.Equal(Wave, keys.Skip(fifi + 1).Take(Wave.Length));
        Assert.All(FloraCatalog.All.Skip(fifi + 1 + Wave.Length), sp => Assert.True(sp.Cultivated, $"{sp.Key} sits among the crops"));
        Assert.True(FloraCatalog.All.Count <= BlocksBeyondTheStars.Shared.Bio.FloraForm.MaxIndices,
            "the bio lab's packed form addresses at most FloraForm.MaxIndices catalog entries");

        foreach (var key in Wave)
        {
            var sp = FloraCatalog.Find(key)!;
            Assert.Equal(Gen, sp.MinGeneration);
            Assert.False(sp.Solid, $"{key} is a side-view cross billboard, not a cube");
            Assert.False(sp.Hanging || sp.Aquatic || sp.Cultivated || sp.Authored || sp.Fruit || sp.Rainbow, key);
            Assert.Equal(FloraHabitat.Surface, sp.Habitat);
            Assert.NotEqual(FloraTag.None, sp.Tags);

            var block = _content.GetBlock(key);
            Assert.NotNull(block);
            Assert.Equal("flora", block!.Category);
            Assert.Equal(AccentSpecies.Contains(key), block.Accent);
            Assert.True(BlocksBeyondTheStars.Shared.Bio.FloraForm.Breedable(key), $"{key} is a wild plant the bio lab can clone");
            Assert.Equal(BerrySpecies.Contains(key), block.Drops.Any(d => d.Item == "berries"));
            Assert.Contains(block.Drops, d => d.Item == "plant_fiber");
            foreach (var host in sp.Hosts)
            {
                Assert.True(_content.GetBlock(host)?.FloraHost == true, $"{key}'s host '{host}' must be a flora host");
            }
        }
    }

    // --- Rosters ---

    private IEnumerable<PlanetType> FloraPlanets()
        => _content.Planets.Values.Where(p => p.FloraDensity > 0 && !p.IsAirless).OrderBy(p => p.Key, System.StringComparer.Ordinal);

    [Fact]
    public void WaveSpecies_StayInactiveBelowGeneration28_AndEachActivatesFromIt()
    {
        var seen = new HashSet<string>();
        foreach (var planet in FloraPlanets())
        {
            for (long seed = 1; seed <= 6; seed++)
            {
                foreach (var fs in FloraGenerator.GenerateRoster(planet, seed, Gen - 1))
                {
                    if (Wave.Contains(fs.BlockKey))
                    {
                        Assert.False(fs.Active, $"{planet.Key} seed {seed}: {fs.BlockKey} active on generation {Gen - 1}");
                    }
                }

                foreach (var fs in FloraGenerator.GenerateRoster(planet, seed, Gen))
                {
                    if (fs.Active && Wave.Contains(fs.BlockKey))
                    {
                        seen.Add(fs.BlockKey);
                    }
                }
            }
        }

        Assert.Equal(Wave.OrderBy(k => k, System.StringComparer.Ordinal), seen.OrderBy(k => k, System.StringComparer.Ordinal));
    }

    [Fact]
    public void WaveSpecies_KeepTheirRosterIdentity_AcrossGenerations()
    {
        // The roll is drawn on every generation (the per-species stream never moves); only the activation is gated.
        var planet = _content.GetPlanet("varied")!;
        var older = FloraGenerator.GenerateRoster(planet, 4242, Gen - 1);
        var newer = FloraGenerator.GenerateRoster(planet, 4242, Gen);
        Assert.Equal(older.Count, newer.Count);
        for (int i = 0; i < older.Count; i++)
        {
            Assert.Equal(older[i].Id, newer[i].Id);
            Assert.Equal(older[i].Name, newer[i].Name);
            Assert.Equal(older[i].Toxic, newer[i].Toxic);
            Assert.Equal("fl" + FloraCatalog.All.ToList().FindIndex(s => s.Key == older[i].BlockKey), older[i].Id);
        }
    }

    [Fact]
    public void Generation28Rosters_KeepEveryHostSurfacePlanted()
    {
        // The coverage rule (FloraGenerator.EnsureCoverage) on a generation-28 world: every land host keeps an active land
        // species, the seas an aquatic one. A strict theme (the flower fields) covers only its own surfaces — left out here.
        var landHosts = FloraCatalog.All
            .Where(s => !s.Aquatic && !s.Cultivated && !s.Hanging && s.OnSurface && !s.Rainbow)
            .SelectMany(s => s.Hosts).Distinct().ToList();
        foreach (var planet in FloraPlanets().Where(p => !FloraThemes.Resolve(p.FloraTheme).Strict))
        {
            for (long seed = 1; seed <= 4; seed++)
            {
                var roster = FloraGenerator.GenerateRoster(planet, seed, Gen);
                foreach (var host in landHosts)
                {
                    Assert.True(roster.Any(r => r.Active && !r.Aquatic && FloraCatalog.Find(r.BlockKey) is { } sp
                            && sp.OnSurface && !sp.Rainbow && !sp.Hanging && sp.Hosts.Contains(host)),
                        $"{planet.Key} seed {seed}: no active species covers '{host}'");
                }

                Assert.Contains(roster, r => r.Active && r.Aquatic);
            }
        }
    }

    // --- World generation: the mixed pick ---

    /// <summary>The surface flora species of every column of a square area (null = no plant), read from the generated
    /// chunks around each column's surface.</summary>
    private Dictionary<(int X, int Z), ushort?> SurfaceFlora(PlanetType planet, long seed, int generation, int x0, int z0, int size)
    {
        var gen = new WorldGenerator(seed, _content);
        gen.SetTerrainGeneration(generation);
        var wild = new HashSet<ushort>();
        foreach (var sp in FloraCatalog.All.Where(s => s.OnSurface && !s.Hanging && !s.Aquatic && !s.Cultivated))
        {
            if (_content.GetBlock(sp.Key) is { } b)
            {
                wild.Add(b.NumericId.Value);
            }
        }

        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        BlockId At(int x, int y, int z)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!chunks.TryGetValue(coord, out var chunk))
            {
                chunks[coord] = chunk = gen.Generate(planet, coord);
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }

        var result = new Dictionary<(int X, int Z), ushort?>();
        for (int x = x0; x < x0 + size; x++)
        {
            for (int z = z0; z < z0 + size; z++)
            {
                int surface = gen.SurfaceHeight(planet, x, z);
                ushort? found = null;
                for (int y = surface + 8; y >= surface - 6 && found is null; y--)
                {
                    ushort id = At(x, y, z).Value;
                    if (wild.Contains(id))
                    {
                        found = id;
                    }
                }

                result[(x, z)] = found;
            }
        }

        return result;
    }

    /// <summary>The share of neighbouring planted column pairs (east and south) that grow different species.</summary>
    private static double MixedNeighbourShare(Dictionary<(int X, int Z), ushort?> flora)
    {
        int pairs = 0, mixed = 0;
        foreach (var ((x, z), id) in flora)
        {
            if (id is not { } a)
            {
                continue;
            }

            foreach (var n in new[] { (x + 1, z), (x, z + 1) })
            {
                if (flora.TryGetValue(n, out var other) && other is { } b)
                {
                    pairs++;
                    if (a != b)
                    {
                        mixed++;
                    }
                }
            }
        }

        return pairs == 0 ? 0 : (double)mixed / pairs;
    }

    [Fact]
    public void Generation28_PutsDifferentSpeciesSideBySide_WhereOlderWorldsGrowOnePatch()
    {
        // A lush temperate world, the same 32 × 32 columns (away from the pad at the origin) on generation 27 and 28. The
        // terrain is the same; only the flora pick changes. Deterministic: a fixed seed and area.
        var planet = _content.GetPlanet("meadowlands")!;
        const long seed = 7;
        const int x0 = 160, z0 = 96, size = 32;
        var older = SurfaceFlora(planet, seed, Gen - 1, x0, z0, size);
        var newer = SurfaceFlora(planet, seed, Gen, x0, z0, size);

        int plantedOlder = older.Values.Count(v => v is not null);
        int plantedNewer = newer.Values.Count(v => v is not null);
        Assert.True(plantedNewer >= 60, $"the meadow must be planted to judge the mix (got {plantedNewer} plants)");

        double shareOlder = MixedNeighbourShare(older);
        double shareNewer = MixedNeighbourShare(newer);
        Assert.True(shareNewer >= 0.25 && shareNewer >= shareOlder + 0.15,
            $"generation 28 must mix neighbouring plants (mixed neighbour share {shareOlder:P0} → {shareNewer:P0}; plants {plantedOlder} → {plantedNewer})");

        // Every 16 × 16 chunk of the area grows at least two species side by side on generation 28.
        for (int cx = x0; cx < x0 + size; cx += WorldConstants.ChunkSize)
        {
            for (int cz = z0; cz < z0 + size; cz += WorldConstants.ChunkSize)
            {
                var species = newer.Where(kv => kv.Key.X >= cx && kv.Key.X < cx + WorldConstants.ChunkSize
                                                && kv.Key.Z >= cz && kv.Key.Z < cz + WorldConstants.ChunkSize && kv.Value is not null)
                    .Select(kv => kv.Value!.Value).Distinct().Count();
                Assert.True(species >= 2, $"the chunk at ({cx}, {cz}) grows {species} species on generation 28");
            }
        }
    }
}
