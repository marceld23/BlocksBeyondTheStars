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
/// Generation 22, the oil you can find (#2377): the pockets reach their designed half-height (#2370), about a third of them
/// seep to the surface through a tar chimney (#2371), the pocket grid answers the scanner's oil echo (#2372), and a gas
/// giant no longer claims oil it never holds (#2375). The pure half — the generator alone.
/// </summary>
public sealed class OilSeepsWorldTests
{
    private const int Gen = WorldDescription.OilSeepGeneration;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly long[] Seeds = { 20261007, 7, 4242, 98765, 31337, 555 };

    private static WorldGenerator Generator(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static PlanetType Planet(string key) => Content.GetPlanet(key)!;

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    /// <summary>Every pocket of the world: a radius wider than the planet asks every hotspot cell once.</summary>
    private static List<OilPocketSite> AllPockets(WorldGenerator gen, PlanetType planet)
        => gen.FindOilPocketsNear(planet, 0, 0, 100_000);

    private static BlockId At(WorldGenerator gen, PlanetType planet, Dictionary<ChunkCoord, ChunkData> cache, int x, int y, int z)
    {
        var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
        if (!cache.TryGetValue(coord, out var chunk))
        {
            chunk = gen.Generate(planet, coord);
            cache[coord] = chunk;
        }

        var origin = WorldConstants.ChunkOrigin(coord);
        return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
    }

    /// <summary>The column's top cell that is not air (water counts), looked for from just above the heightfield down.</summary>
    private static int TopCell(WorldGenerator gen, PlanetType planet, Dictionary<ChunkCoord, ChunkData> cache, int x, int z)
    {
        int y = gen.SurfaceHeight(planet, x, z) + 2;
        while (At(gen, planet, cache, x, y, z).IsAir)
        {
            y--;
        }

        return y;
    }

    [Fact]
    public void OilSeepGeneration_IsTheCurrentGeneration_AndComesAfterOil()
    {
        Assert.Equal(22, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        Assert.True(Gen > WorldDescription.OilGeneration);
    }

    [Fact]
    public void Generation22_PocketsReachTheirDesignedHalfHeight_OlderOnesStayFlat()
    {
        var planet = Planet("jungle");
        int tallestNew = 0, tallestOld = 0;
        foreach (long seed in Seeds)
        {
            foreach (var site in AllPockets(Generator(seed, Gen), planet))
            {
                tallestNew = System.Math.Max(tallestNew, site.OilHi - site.OilLo + 1);
            }

            foreach (var site in AllPockets(Generator(seed, Gen - 1), planet))
            {
                tallestOld = System.Math.Max(tallestOld, site.OilHi - site.OilLo + 1);
            }
        }

        // #2370: below generation 22 the half-height read hash bits the chance roll had zeroed — never more than four cells
        // of oil. From generation 22 the half-height spans 3–7 as designed: inner columns up to ten cells.
        Assert.InRange(tallestOld, 1, 4);
        Assert.InRange(tallestNew, 6, 11);
    }

    [Fact]
    public void Generation21_KeepsEveryPocketOfGeneration18_BitForBit()
    {
        var planet = Planet("jungle");
        var gen18 = Generator(Seeds[0], WorldDescription.OilGeneration);
        var gen21 = Generator(Seeds[0], Gen - 1);
        var sites = AllPockets(gen18, planet);
        Assert.NotEmpty(sites);
        foreach (var site in sites)
        {
            for (int dx = -15; dx <= 15; dx += 3)
                for (int dz = -15; dz <= 15; dz += 3)
                {
                    bool a = gen18.TryGetOilPocketSpanForTest(planet, site.X + dx, site.Z + dz, out int lo1, out int hi1, out int il1, out int ih1);
                    bool b = gen21.TryGetOilPocketSpanForTest(planet, site.X + dx, site.Z + dz, out int lo2, out int hi2, out int il2, out int ih2);
                    Assert.Equal(a, b);
                    Assert.Equal((lo1, hi1, il1, ih1), (lo2, hi2, il2, ih2));
                }

            Assert.False(gen21.OilSeepNear(planet, site.X, site.Z), "no seep below generation 22");
        }
    }

    [Fact]
    public void AboutAThirdOfThePockets_Seep()
    {
        int pockets = 0, seeps = 0;
        foreach (var key in new[] { "jungle", "meadowlands", "desert", "tundra", "varied" })
        {
            foreach (long seed in Seeds)
            {
                foreach (var site in AllPockets(Generator(seed, Gen), Planet(key)))
                {
                    pockets++;
                    seeps += site.Seeps ? 1 : 0;
                }
            }
        }

        Assert.True(pockets > 100, $"only {pockets} pockets sampled");
        double share = seeps / (double)pockets;
        Assert.InRange(share, 0.25, 0.50); // 3 of 8 by design
    }

    [Fact]
    public void ASeep_IsAnUnbrokenTarChimney_WithAnOilPuddle_AndNothingGrowsOnIt()
    {
        var tar = Block("tar");
        var oil = Block("oil");
        // Water over the ground — or the seabed flora that grows in it (a one-cell-deep sea's coral replaces the water).
        var wet = new HashSet<BlockId> { Block("water"), Block("flora_kelp"), Block("flora_lily"), Block("flora_coral"), Block("flora_seagrass") };
        int checkedSeeps = 0;
        foreach (var key in new[] { "jungle", "meadowlands", "savanna" })
        {
            var planet = Planet(key);
            foreach (long seed in Seeds)
            {
                var gen = Generator(seed, Gen);
                foreach (var site in AllPockets(gen, planet).Where(s => s.Seeps).Take(2))
                {
                    var cache = new Dictionary<ChunkCoord, ChunkData>();
                    int ground = TopCell(gen, planet, cache, site.X, site.Z);
                    if (wet.Contains(At(gen, planet, cache, site.X, ground, site.Z)))
                    {
                        continue; // a seep under a lake or the sea: a tar floor without a puddle — only dry ones are checked
                    }

                    // The puddle on top, the chimney under it: tar from the ground down to the pocket's oil (a cavern, an
                    // underground river or a geode may cut through it — they win by design, so allow a little).
                    Assert.Equal(oil, At(gen, planet, cache, site.X, ground, site.Z));
                    int cells = 0, tarCells = 0;
                    for (int y = ground - 1; y > site.OilHi; y--)
                    {
                        cells++;
                        tarCells += At(gen, planet, cache, site.X, y, site.Z) == tar ? 1 : 0;
                    }

                    Assert.True(cells > 10, $"{key}/{seed}: the chimney is only {cells} cells long");
                    Assert.True(tarCells >= cells * 0.85, $"{key}/{seed}: only {tarCells} of {cells} chimney cells are tar");
                    Assert.Equal(oil, At(gen, planet, cache, site.X, site.OilHi, site.Z));

                    // The chimney and its ring: tar, oil on top in the middle, and air over it — no plant, trunk or prop.
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int g = TopCell(gen, planet, cache, site.X + dx, site.Z + dz);
                            var top = At(gen, planet, cache, site.X + dx, g, site.Z + dz);
                            if (wet.Contains(top))
                            {
                                continue; // the ring's edge dips into a pond: a tar floor under the water
                            }

                            Assert.True(top == tar || top == oil, $"{key}/{seed}: ({dx},{dz}) wears {top.Value}, not the seep");
                            Assert.True(At(gen, planet, cache, site.X + dx, g + 1, site.Z + dz).IsAir,
                                $"{key}/{seed}: something stands on the seep at ({dx},{dz})");
                        }

                    checkedSeeps++;
                }
            }
        }

        Assert.True(checkedSeeps >= 5, $"only {checkedSeeps} dry seeps found to check");
    }

    [Fact]
    public void BelowGeneration22_TheSameWorldHasNoSeep()
    {
        var planet = Planet("jungle");
        var tar = Block("tar");
        var oil = Block("oil");
        var gen22 = Generator(Seeds[0], Gen);
        var gen21 = Generator(Seeds[0], Gen - 1);
        var seep = AllPockets(gen22, planet).First(s => s.Seeps);
        Assert.True(gen22.OilSeepNear(planet, seep.X, seep.Z));
        Assert.False(gen21.OilSeepNear(planet, seep.X, seep.Z));

        var cache = new Dictionary<ChunkCoord, ChunkData>();
        int ground = gen21.SurfaceHeight(planet, seep.X, seep.Z);
        for (int y = ground; y > ground - 20; y--)
        {
            var b = At(gen21, planet, cache, seep.X, y, seep.Z);
            Assert.True(b != tar && b != oil, $"generation 21 has a seep cell at depth {ground - y}");
        }
    }

    [Fact]
    public void TheEchoQuery_FindsThePocketsThePocketFunctionPlaced_NearestFirst()
    {
        var planet = Planet("jungle");
        var gen = Generator(Seeds[0], Gen);
        var all = AllPockets(gen, planet);
        Assert.InRange(all.Count, 5, 25); // ~13 on a default world (50 cells × 0.25)

        var near = gen.FindOilPocketsNear(planet, all[0].X + 30, all[0].Z - 20, 800);
        Assert.NotEmpty(near);
        Assert.Equal((all[0].X, all[0].Z), (near[0].X, near[0].Z));
        Assert.All(near, s => Assert.InRange(s.Distance, 0.0, 800.0));
        Assert.Equal(near.Select(s => s.Distance).OrderBy(d => d), near.Select(s => s.Distance));

        foreach (var site in all)
        {
            Assert.True(gen.TryGetOilPocketSpanForTest(planet, site.X, site.Z, out _, out _, out int inLo, out int inHi));
            Assert.Equal((site.OilLo, site.OilHi), (inLo, inHi));
        }
    }

    [Fact]
    public void AGasGiant_HoldsNoOil_AndItsSurveySaysSo()
    {
        var giant = Planet("gas_giant");
        Assert.True(giant.HasLife, "the gas giant counts as living (air, flora, fauna) — the reason this needed a fix");
        var gen = Generator(Seeds[0], Gen);
        Assert.False(gen.CarriesOilPockets(giant));
        Assert.Empty(gen.FindOilPocketsNear(giant, 0, 0, 100_000));
        Assert.False(gen.SurveyResources(giant, "gas_giant", 1.0, false).OilPockets);
        Assert.True(gen.SurveyResources(Planet("jungle"), "jungle", 1.0, false).OilPockets);
    }

    [Fact]
    public void DeadWorlds_AndOldGenerations_HearNoEcho()
    {
        Assert.Empty(Generator(Seeds[0], Gen).FindOilPocketsNear(Planet("crystal"), 0, 0, 100_000));
        Assert.Empty(Generator(Seeds[0], WorldDescription.OilGeneration - 1).FindOilPocketsNear(Planet("jungle"), 0, 0, 100_000));
        Assert.NotEmpty(Generator(Seeds[0], WorldDescription.OilGeneration).FindOilPocketsNear(Planet("jungle"), 0, 0, 100_000));
    }
}
