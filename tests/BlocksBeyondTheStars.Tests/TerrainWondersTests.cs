// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Terrain wonders (#698–#709): mega-rift, complex craters + chains, oriented terrain, exotic
/// accents, calderas/escarpments, style hybrid, continents, overhang bands, cenotes, caverns, tunnels.</summary>
public class TerrainWondersTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static object Invoke(WorldGenerator gen, string method, params object[] args)
        => typeof(WorldGenerator).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(gen, args)!;

    private static long SeedOf(WorldGenerator gen, PlanetType planet)
        => (long)Invoke(gen, "PlanetSeed", planet);

    // ---------- Wave 1 ----------

    [Theory]
    [InlineData("desert")]     // #700 oriented dunes + #706 arches
    [InlineData("highland")]   // #700 oriented mountain chains
    [InlineData("ice")]        // #701 penitentes + #709 crevasses (blend world)
    [InlineData("salt_flats")] // #701 salt polygons
    [InlineData("lava")]       // #701 basalt column fields, lava sea
    [InlineData("skylands")]   // #707 multi-tier islands
    public void SurfaceHeight_WrapsSeamFree_OnWonderWorlds(string key)
    {
        var content = Content();
        var planet = content.GetPlanet(key)!;
        var gen = new WorldGenerator(1234, content);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);

        for (int z = -period / 2; z < period / 2; z += 97)
            for (int x = 0; x < circ; x += 191)
            {
                Assert.Equal(gen.SurfaceHeight(planet, x, z), gen.SurfaceHeight(planet, x + circ, z));
                Assert.Equal(gen.SurfaceHeight(planet, x, z), gen.SurfaceHeight(planet, x, z + period));
            }
    }

    [Fact]
    public void MegaRift_GirdlesTheWholePlanet()
    {
        // #698: find a seed whose savanna world carries the mega-rift, then verify the canyon exists at
        // EVERY longitude (it wraps) and reaches serious depth.
        var content = Content();
        var planet = content.GetPlanet("savanna")!;
        WorldGenerator? gen = null;
        long seed = 0;
        for (long s = 1; s < 400 && gen is null; s++)
        {
            var g = new WorldGenerator(s * 7919, content);
            long ps = SeedOf(g, planet);
            if ((bool)Invoke(g, "HasMegaRift", planet, ps))
            {
                gen = g;
                seed = ps;
            }
        }

        Assert.NotNull(gen);
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        for (int x = 0; x < WorldConstants.Circumference; x += 500)
        {
            double deepest = 0;
            for (int z = -period / 2; z < period / 2; z += 4)
            {
                double o = (double)Invoke(gen!, "MegaRiftOffset", seed, x, z);
                if (o < deepest)
                {
                    deepest = o;
                }
            }

            Assert.True(deepest < -50.0, $"mega-rift missing/shallow at x={x} (deepest {deepest})");
        }
    }

    [Fact]
    public void Escarpment_SplitsTheWorldIntoTwoStoreys()
    {
        // #702: on an escarpment world the far-north and far-south storeys differ by the rolled step.
        var content = Content();
        var planet = content.GetPlanet("savanna")!;
        WorldGenerator? gen = null;
        long seed = 0;
        for (long s = 1; s < 400 && gen is null; s++)
        {
            var g = new WorldGenerator(s * 6151 + 1, content);
            long ps = SeedOf(g, planet);
            if ((bool)Invoke(g, "HasEscarpment", planet, ps))
            {
                gen = g;
                seed = ps;
            }
        }

        Assert.NotNull(gen);
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        // Somewhere the offset is clearly positive AND somewhere clearly negative (two storeys exist).
        double lo = double.MaxValue, hi = double.MinValue;
        for (int z = -period / 2; z < period / 2; z += 64)
        {
            double o = (double)Invoke(gen!, "EscarpmentOffset", seed, 1000, z);
            lo = System.Math.Min(lo, o);
            hi = System.Math.Max(hi, o);
        }

        Assert.True(hi - lo >= 50.0, $"escarpment step too small: {hi - lo}");
        Assert.True(hi > 20.0 && lo < -20.0, $"storeys not centred: hi {hi}, lo {lo}");
    }

    [Fact]
    public void RingCaldera_HasARimWallAroundASunkenFloor()
    {
        // #702: find a caldera via its offset field, then check ring-positive / interior-negative.
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        for (long s = 1; s < 200; s++)
        {
            var gen = new WorldGenerator(s * 104729, content);
            long seed = SeedOf(gen, planet);
            for (int z = -period / 2; z < period / 2; z += 64)
                for (int x = 0; x < WorldConstants.Circumference; x += 64)
                {
                    double o = (double)Invoke(gen, "CalderaOffset", seed, x, z);
                    if (o < -15.0)
                    {
                        // Inside a sunken floor: a rim (positive offset) must exist within the max radius.
                        bool rimFound = false;
                        for (int d = 8; d <= 420 && !rimFound; d += 8)
                        {
                            rimFound = (double)Invoke(gen, "CalderaOffset", seed, x + d, z) > 15.0;
                        }

                        Assert.True(rimFound, "caldera floor without a rim wall");
                        return;
                    }
                }
        }

        Assert.Fail("no ring caldera found in the scanned seeds");
    }

    [Fact]
    public void CraterProfiles_RollComplexPeaksAndTerraces_PerBody()
    {
        // #699: both complex-crater traits appear on some bodies and stay absent on others.
        var t = typeof(WorldGenerator).GetMethod("CraterProfileFor", BindingFlags.NonPublic | BindingFlags.Static)!;
        int peaks = 0, terraced = 0, plain = 0;
        for (long s = 1; s <= 400; s++)
        {
            object p = t.Invoke(null, new object[] { s * 2654435761L })!;
            bool cp = (bool)p.GetType().GetField("ComplexPeaks")!.GetValue(p)!;
            bool tr = (bool)p.GetType().GetField("Terraced")!.GetValue(p)!;
            if (cp) peaks++;
            if (tr) terraced++;
            if (!cp && !tr) plain++;
        }

        Assert.InRange(peaks, 100, 300);
        Assert.InRange(terraced, 80, 280);
        Assert.True(plain > 40, "every body rolled complex traits — simple craters must survive");
    }

    [Fact]
    public void CraterChains_CarveAlignedBowlStrings()
    {
        // #699: on a cratered body, the chain carve is nonzero somewhere and never positive beyond its lip.
        var content = Content();
        var planet = content.GetPlanet("asteroid")!;
        int found = 0;
        for (long s = 1; s <= 40 && found == 0; s++)
        {
            var gen = new WorldGenerator(s * 15485863, content);
            gen.SetWorldMode(1600, cratered: true, landingPads: null, "belt0-a1");
            long seed = SeedOf(gen, planet);
            int period = WorldConstants.LatitudePeriodFor(1600);
            for (int z = -period / 2; z < period / 2; z += 8)
                for (int x = 0; x < 1600; x += 8)
                {
                    double o = (double)Invoke(gen, "CraterChainCarve", seed, x, z);
                    Assert.True(o <= 1.01, $"chain carve rose above its rim lip: {o}");
                    if (o < -3.0)
                    {
                        found++;
                    }
                }
        }

        Assert.True(found > 0, "no crater chain found on any scanned asteroid");
    }

    [Fact]
    public void SaltPolygons_RidgeTheSaltPan()
    {
        // #701: salt flats carry a sparse +1 ridge network — present, but far from covering the pan.
        var content = Content();
        var planet = content.GetPlanet("salt_flats")!;
        var gen = new WorldGenerator(4242, content);
        long seed = SeedOf(gen, planet);
        int ridged = 0, total = 0;
        for (int z = -400; z < 400; z += 3)
            for (int x = 0; x < 800; x += 3)
            {
                double r = (double)Invoke(gen, "SaltPolygonRidge", seed, x, z);
                Assert.True(r is 0.0 or 1.0);
                if (r > 0)
                {
                    ridged++;
                }

                total++;
            }

        double frac = ridged / (double)total;
        Assert.InRange(frac, 0.02, 0.35);
    }

    // ---------- Continents (#704) ----------

    [Fact]
    public void Continents_NeverRoll_WithoutTheFlag_OrOnSmallWorlds()
    {
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        var m = typeof(WorldGenerator).GetMethod("ContinentProfileFor", BindingFlags.NonPublic | BindingFlags.Instance)!;
        bool Active(WorldGenerator g)
        {
            object prof = m.Invoke(g, new object[] { planet, SeedOf(g, planet) })!;
            return (bool)prof.GetType().GetField("Active")!.GetValue(prof)!;
        }

        for (long s = 1; s <= 40; s++)
        {
            // Flag off → never, regardless of size.
            var off = new WorldGenerator(s * 31, content);
            off.SetWorldMode(9600, cratered: false, landingPads: null, $"sys{s}-p1");
            Assert.False(Active(off), "continents rolled with the flag off");

            // Flag on but a moon-sized world → never (the gate now sits at 6000, so the start world
            // qualifies while moons at 2500–4000 stay out).
            var small = new WorldGenerator(s * 31, content);
            small.SetContinentsEnabled(true);
            small.SetWorldMode(4000, cratered: false, landingPads: null, $"sys{s}-p1");
            Assert.False(Active(small), "continents rolled below the size gate");
        }
    }

    [Fact]
    public void Continents_MakeLargeWorldsBimodal_AndOceanKeepsItsIdentity()
    {
        var content = Content();
        var varied = content.GetPlanet("varied")!;
        var ocean = content.GetPlanet("ocean")!;
        var m = typeof(WorldGenerator).GetMethod("ContinentProfileFor", BindingFlags.NonPublic | BindingFlags.Instance)!;

        WorldGenerator? contGen = null;
        for (long s = 1; s <= 80 && contGen is null; s++)
        {
            var g = new WorldGenerator(s * 6700417, content);
            g.SetContinentsEnabled(true);
            g.SetWorldMode(9600, cratered: false, landingPads: null, $"sys{s}-p2");
            object prof = m.Invoke(g, new object[] { varied, SeedOf(g, varied) })!;
            if ((bool)prof.GetType().GetField("Active")!.GetValue(prof)!)
            {
                contGen = g;
            }

            // The ocean type must NEVER roll continents (its identity is the 78–97 % flood).
            object oceanProf = m.Invoke(g, new object[] { ocean, SeedOf(g, ocean) })!;
            Assert.False((bool)oceanProf.GetType().GetField("Active")!.GetValue(oceanProf)!,
                "ocean type rolled continents");
        }

        Assert.NotNull(contGen);

        // Bimodal: platform columns sit clearly above basin columns — the spread far exceeds a classic
        // varied world's, and both regimes are well represented.
        int period = WorldConstants.LatitudePeriodFor(9600);
        int lows = 0, highs = 0;
        for (int z = -period / 2; z < period / 2; z += 160)
            for (int x = 0; x < 9600; x += 160)
            {
                int h = contGen!.SurfaceHeight(varied, x, z);
                if (h < varied.BaseHeight - 22)
                {
                    lows++;
                }
                else if (h >= varied.BaseHeight)
                {
                    highs++;
                }
            }

        Assert.True(lows > 100, $"no ocean basins found ({lows} low columns)");
        Assert.True(highs > 100, $"no continental platforms found ({highs} high columns)");
    }

    [Fact]
    public void Continents_FloodBasins_WithARealSea()
    {
        // The sea percentile targets the basin share, so the waterline must sit far above the basin
        // floors and below the platforms.
        var content = Content();
        var varied = content.GetPlanet("varied")!;
        var m = typeof(WorldGenerator).GetMethod("ContinentProfileFor", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (long s = 1; s <= 80; s++)
        {
            var g = new WorldGenerator(s * 6700417, content);
            g.SetContinentsEnabled(true);
            g.SetWorldMode(9600, cratered: false, landingPads: null, $"sys{s}-p2");
            object prof = m.Invoke(g, new object[] { varied, SeedOf(g, varied) })!;
            if (!(bool)prof.GetType().GetField("Active")!.GetValue(prof)!)
            {
                continue;
            }

            int sea = g.SeaLevel(varied);
            Assert.True(sea != int.MinValue, "continental world has no sea");
            Assert.InRange(sea, varied.BaseHeight - 40, varied.BaseHeight + 12);
            return;
        }

        Assert.Fail("no continental varied world found in the scanned seeds");
    }

    // ---------- Overhangs (#705/#706/#707) ----------

    [Fact]
    public void ExtraBands_AreEmpty_OnAirlessSpireWorlds()
    {
        // crystal: airless (no stacks/cenotes), spires style (no arches/hoodoos), no floating islands —
        // the band machinery must report nothing and cost nothing.
        var content = Content();
        var planet = content.GetPlanet("crystal")!;
        var gen = new WorldGenerator(99, content);
        Assert.False(gen.HasExtraBands(planet));
        System.Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        for (int x = 0; x < 2000; x += 61)
        {
            Assert.Equal(0, gen.GetExtraBands(planet, x, 17, bands));
        }
    }

    [Fact]
    public void Skylands_KeepTierZero_IdenticalToTheClassicBandQuery()
    {
        // #707 compat: FloatingIslandBand (the settlement-placement contract) must agree with tier 0 of
        // GetExtraBands wherever an island exists.
        var content = Content();
        var planet = content.GetPlanet("skylands")!;
        var gen = new WorldGenerator(7, content);
        System.Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int checkedCols = 0;
        for (int z = -600; z < 600 && checkedCols < 200; z += 13)
            for (int x = 0; x < 3000 && checkedCols < 200; x += 29)
            {
                if (!gen.FloatingIslandBand(planet, x, z, out int top, out int bottom))
                {
                    continue;
                }

                int n = gen.GetExtraBands(planet, x, z, bands);
                bool found = false;
                for (int i = 0; i < n; i++)
                {
                    if (bands[i].Top == top && bands[i].Bottom == bottom
                        && bands[i].Kind is WorldGenerator.BandKind.Island or WorldGenerator.BandKind.IslandPond)
                    {
                        found = true;
                    }
                }

                Assert.True(found, $"tier-0 island at ({x},{z}) missing from GetExtraBands");
                checkedCols++;
            }

        Assert.True(checkedCols > 50, "scan found too few island columns to be meaningful");
    }

    [Fact]
    public void Cenotes_DropSheerShafts_IntoTheGround()
    {
        // #707: find a cenote and verify the shaft is deep and its wall near-vertical.
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        for (long s = 1; s <= 30; s++)
        {
            var gen = new WorldGenerator(s * 32452843, content);
            long seed = SeedOf(gen, planet);
            for (int z = -period / 2; z < period / 2; z += 12)
                for (int x = 0; x < WorldConstants.Circumference; x += 12)
                {
                    double o = (double)Invoke(gen, "CenoteOffset", planet, seed, x, z);
                    if (o < -28.0)
                    {
                        // Sheer: walking +x from ANY floor column must cross the rim within one full
                        // diameter (radius reaches ~20 since the visibility tuning → scan 44).
                        bool rim = false;
                        for (int d = 2; d <= 44 && !rim; d += 2)
                        {
                            rim = (double)Invoke(gen, "CenoteOffset", planet, seed, x + d, z) > -2.0;
                        }

                        Assert.True(rim, "cenote wall is not sheer");
                        return;
                    }
                }
        }

        Assert.Fail("no cenote found in the scanned seeds");
    }

    // ---------- Tunnels (#708/#709) ----------

    [Fact]
    public void Tunnels_AreDeterministic_AndReachTheSurfaceSomewhere()
    {
        var content = Content();
        var planet = content.GetPlanet("rocky")!;
        var a = new WorldGenerator(7, content);
        var b = new WorldGenerator(7, content);
        System.Span<(int, int)> sa = stackalloc (int, int)[8];
        System.Span<(int, int)> sb = stackalloc (int, int)[8];

        bool anySpan = false, mouth = false;
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        for (int z = -period / 2; z < period / 2 && !(anySpan && mouth); z += 10)
            for (int x = 0; x < WorldConstants.Circumference && !(anySpan && mouth); x += 10)
            {
                int na = a.TunnelSpans(planet, x, z, sa);
                int nb = b.TunnelSpans(planet, x, z, sb);
                Assert.Equal(na, nb);
                for (int i = 0; i < na; i++)
                {
                    Assert.Equal(sa[i], sb[i]);
                    anySpan = true;
                    if (sa[i].Item2 >= a.SurfaceHeight(planet, x, z))
                    {
                        mouth = true; // a skylight/mouth breaks the surface (#709)
                    }
                }
            }

        Assert.True(anySpan, "no tunnel found anywhere on the world");
        Assert.True(mouth, "no tunnel mouth/skylight reaches the surface");
    }

    [Fact]
    public void WonderWorlds_GenerateChunks_WithoutErrors()
    {
        // Smoke: the full Generate path (bands, pools, repaints, caverns, tunnels) holds together.
        var content = Content();
        foreach (var key in new[] { "desert", "ice", "salt_flats", "skylands", "lava", "jungle", "badlands" })
        {
            var planet = content.GetPlanet(key)!;
            var gen = new WorldGenerator(7, content);
            for (int cy = -2; cy <= 6; cy += 2)
            {
                Assert.NotNull(gen.Generate(planet, new ChunkCoord(3, cy, 1)));
            }
        }
    }

    [Fact]
    public void ContinentalChunks_Generate_OnLargeWorlds()
    {
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        var gen = new WorldGenerator(6700417, content);
        gen.SetContinentsEnabled(true);
        gen.SetWorldMode(9600, cratered: false, landingPads: null, "sys1-p2");
        for (int cx = 0; cx < 8; cx += 2)
        {
            Assert.NotNull(gen.Generate(planet, new ChunkCoord(cx, 3, 0)));
        }
    }
}
