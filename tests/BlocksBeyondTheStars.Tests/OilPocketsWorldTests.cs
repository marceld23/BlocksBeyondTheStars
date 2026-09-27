// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 18, oil (#2106, Justus' idea, Marcel's rules): a finite, still liquid in sealed tar-rimmed pockets under the
/// living worlds only — never in a cave, never flowing, harvested by the pump. The pure half: the content, the life rule,
/// the pocket's shape and seal, and the generation gate.
/// </summary>
public sealed class OilPocketsWorldTests
{
    private const int Gen = WorldDescription.OilGeneration;
    private const long Seed = 20260927;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Generator(int generation = Gen)
    {
        var gen = new WorldGenerator(Seed, Content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    /// <summary>Reads generated blocks by world position, one cached chunk per coordinate.</summary>
    private sealed class Probe
    {
        private readonly WorldGenerator _gen;
        private readonly PlanetType _planet;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Probe(WorldGenerator gen, PlanetType planet)
        {
            _gen = gen;
            _planet = planet;
        }

        public BlockId At(int x, int y, int z)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = _gen.Generate(_planet, coord);
                _chunks[coord] = chunk;
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }
    }

    /// <summary>The first column of a living world that a pocket covers with an inner (oil) span, scanning outward.</summary>
    private static (int X, int Z, int Lo, int Hi, int InLo, int InHi)? FindPocket(WorldGenerator gen, PlanetType planet)
    {
        for (int x = 0; x < 2400; x += 4)
            for (int z = -1200; z < 1200; z += 4)
            {
                if (gen.TryGetOilPocketSpanForTest(planet, x, z, out int lo, out int hi, out int inLo, out int inHi) && inHi >= inLo)
                {
                    return (x, z, lo, hi, inLo, inHi);
                }
            }

        return null;
    }

    // ---------------- the content ----------------

    [Fact]
    public void Oil_IsDataComplete_AndGatedToGenerationEighteen()
    {
        Assert.Equal(18, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);

        var oil = Content.GetBlock("oil")!;
        Assert.True(oil.Liquid, "oil is a still liquid");
        Assert.False(oil.Solid, "you sink into oil");
        Assert.False(oil.Mineable, "a drill cannot mine oil — only the pump harvests it");
        Assert.True(oil.Flammable, "oil burns");
        Assert.Contains(oil.Drops, d => d.Item == "oil");

        var item = Content.GetItem("oil")!;
        Assert.Equal("oil", item.PlacesBlock);

        var pump = Content.GetItem("fluid_pump")!;
        Assert.Equal(ToolKind.Gadget, pump.Tool!.Kind);
        Assert.True(pump.Tool.EnergyPerUse > 0f);
        Assert.Contains(Content.Recipes.Values, r => r.Outputs.Any(o => o.Item == "fluid_pump"));
        Assert.True(Content.Blueprints.ContainsKey("fluid_pump"));

        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = Content.CreateLocalizer(locale);
            foreach (var key in new[] { "block.oil.name", "item.oil.name", "item.fluid_pump.name", "blueprint.fluid_pump.name", "srv.pump.no_fluid" })
            {
                Assert.False(loc.Get(key).StartsWith("["), $"{locale} misses {key}");
            }
        }

        // Water and lava stay the automaton's fluids — neither is a "liquid" by data.
        Assert.False(Content.GetBlock("water")!.Liquid);
        Assert.False(Content.GetBlock("lava")!.Liquid);
    }

    [Fact]
    public void HasLife_MeansAirAndPlantsAndAnimals()
    {
        Assert.True(Content.GetPlanet("jungle")!.HasLife);
        Assert.True(Content.GetPlanet("meadowlands")!.HasLife);
        Assert.False(Content.GetPlanet("toxic_world")!.HasLife, "no plants, no animals");
        Assert.False(Content.GetPlanet("crystal")!.HasLife, "airless");
        Assert.False(Content.GetPlanet("orbital_station")!.HasLife, "void");
        Assert.False(Content.GetPlanet("gds_desert")!.HasLife, "no animals");
    }

    // ---------------- the pocket ----------------

    [Fact]
    public void ALivingWorld_CarriesASealedTarRimmedOilPocket_UnderItsGround()
    {
        var gen = Generator();
        var planet = Content.GetPlanet("jungle")!;
        var found = FindPocket(gen, planet);
        Assert.True(found.HasValue, "a living world of generation 18 rolls oil pockets");
        var (px, pz, lo, hi, inLo, inHi) = found!.Value;
        Assert.True(hi <= gen.SurfaceHeight(planet, px, pz) - 4, "the shell stays under the ground");
        Assert.True(lo < inLo && inHi < hi, "the oil sits inside the shell");

        var probe = new Probe(gen, planet);
        var oil = Block("oil");
        var tar = Block("tar");
        int oilCells = 0, tarCells = 0, sealedOil = 0;
        for (int x = px - 16; x <= px + 16; x++)
            for (int z = pz - 16; z <= pz + 16; z++)
                for (int y = lo - 10; y <= hi + 10; y++)
                {
                    var b = probe.At(x, y, z);
                    if (b == tar)
                    {
                        tarCells++;
                    }

                    if (b != oil)
                    {
                        continue;
                    }

                    oilCells++;
                    bool enclosed = true;
                    foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                    {
                        var n = probe.At(x + dx, y + dy, z + dz);
                        if (n != oil && n != tar)
                        {
                            enclosed = false;
                        }
                    }

                    if (enclosed)
                    {
                        sealedOil++;
                    }
                }

        Assert.True(oilCells >= 40, $"a pocket holds a real deposit (found {oilCells} oil cells)");
        Assert.True(tarCells >= oilCells / 2, $"the tar shell wraps it ({tarCells} tar around {oilCells} oil)");
        // Tunnels and blob caves never open a pocket (the branch runs before them); only a mega-cavern that ran earlier
        // may cut a face — a rare overlap, so nearly every oil cell is enclosed by oil or tar.
        Assert.True(sealedOil >= oilCells * 0.9, $"the pocket is sealed ({sealedOil} of {oilCells} enclosed)");
    }

    [Fact]
    public void TheSameWorld_OnGenerationSeventeen_HasNoOil()
    {
        var gen18 = Generator();
        var planet = Content.GetPlanet("jungle")!;
        var found = FindPocket(gen18, planet);
        Assert.True(found.HasValue);
        var (px, pz, lo, hi, _, _) = found!.Value;

        var gen17 = Generator(Gen - 1);
        Assert.False(gen17.TryGetOilPocketSpanForTest(planet, px, pz, out _, out _, out _, out _), "no pocket below generation 18");
        var probe = new Probe(gen17, planet);
        var oil = Block("oil");
        for (int x = px - 16; x <= px + 16; x += 2)
            for (int z = pz - 16; z <= pz + 16; z += 2)
                for (int y = lo - 2; y <= hi + 2; y++)
                {
                    Assert.NotEqual(oil, probe.At(x, y, z));
                }
    }

    [Fact]
    public void DeadWorlds_HaveNoOilPockets()
    {
        var gen = Generator();
        foreach (var key in new[] { "toxic_world", "crystal", "gds_desert" })
        {
            var planet = Content.GetPlanet(key)!;
            Assert.Null(FindPocket(gen, planet));
        }
    }
}
