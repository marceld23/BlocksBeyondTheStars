// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The flora groups (generation 48, #3931 — <c>WorldGenerator.FloraGroupsGen48</c>): the small surface
/// plants stand in clumps with open ground between them, mostly one species per clump, while the number of plants per area
/// stays what it was. Generation 48 changed nothing else in the blocks (the colour harmony is colours only), so generation 47
/// and 48 of the same seed share their terrain and differ only in where the plants stand.
/// </summary>
public sealed class FloraGroupsTests : IClassFixture<FloraGroupsTests.Samples>
{
    private const int Gen = WorldDescription.GroupedFloraGeneration;
    private const long Seed = 7;
    private const int AreaSize = 16;

    /// <summary>A lattice of one-chunk areas spread over the whole world (20 × 12, the pad at the origin left out). The plants
    /// of one small area swing by about half with the clearing mask and its few group cells — by design — so the count only
    /// averages out over many independent areas: 238 of them hold the plant count of a generation-48 world within a few
    /// percent of generation 47, at about one chunk per area.</summary>
    private static readonly (int X, int Z)[] Areas = Enumerable.Range(0, 240)
        .Select(k => (X: -2864 + 288 * (k % 20), Z: -1424 + 240 * (k / 20)))
        .Where(a => System.Math.Abs(a.X) > 64 || System.Math.Abs(a.Z) > 64)
        .ToArray();

    private readonly Samples _samples;
    private readonly ITestOutputHelper _output;

    public FloraGroupsTests(Samples samples, ITestOutputHelper output)
    {
        _samples = samples;
        _output = output;
    }

    /// <summary>The generated plant maps, shared by the tests of the class (each world is generated once).</summary>
    public sealed class Samples
    {
        public readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        private readonly ConcurrentDictionary<(string, int), Sample> _cache = new();
        private HashSet<ushort>? _wild;

        /// <summary>The block ids of the small wild surface plants (no water, hanging, crop, fruit, rainbow or authored plant).</summary>
        public HashSet<ushort> Wild => _wild ??= FloraCatalog.All
            .Where(s => s.OnSurface && !s.Hanging && !s.Aquatic && !s.Cultivated && !s.Rainbow && !s.Authored && !s.Fruit)
            .Select(s => Content.GetBlock(s.Key)).Where(b => b is not null).Select(b => b!.NumericId.Value).ToHashSet();

        public Sample Get(string planet, int generation) => _cache.GetOrAdd((planet, generation), k => Sample.Build(this, k.Item1, k.Item2));
    }

    /// <summary>One world's plant maps over <see cref="Areas"/> (0 = no plant), whether each plant stands on one of its own
    /// species' hosts, and the probe that generated them.</summary>
    public sealed class Sample
    {
        public readonly List<ushort[,]> Maps = new();
        public readonly List<bool[,]> OnHost = new();
        public WorldProbe Probe = null!;

        public static Sample Build(Samples s, string planetKey, int generation)
        {
            var planet = s.Content.GetPlanet(planetKey)!;
            var gen = new WorldGenerator(Seed, s.Content);
            gen.SetTerrainGeneration(generation);
            var sample = new Sample { Probe = new WorldProbe(gen, planet) };
            foreach (var (x0, z0) in Areas)
            {
                sample.Scan(s, x0, z0);
            }

            return sample;
        }

        private void Scan(Samples s, int x0, int z0)
        {
            var map = new ushort[AreaSize, AreaSize];
            var onHost = new bool[AreaSize, AreaSize];
            for (int i = 0; i < AreaSize; i++)
            {
                for (int j = 0; j < AreaSize; j++)
                {
                    int x = x0 + i, z = z0 + j;
                    // The plant's cell: the air cell right above the ground (only that one, so an area costs about one chunk).
                    int surface = Probe.Surface(x, z);
                    ushort id = surface < 0 ? (ushort)0 : Probe.At(x, surface + 1, z).Value;
                    if (s.Wild.Contains(id))
                    {
                        map[i, j] = id;
                        onHost[i, j] = GrowsOn(s, id, Probe.At(x, surface, z).Value);
                    }
                }
            }

            Maps.Add(map);
            OnHost.Add(onHost);
        }

        private static bool GrowsOn(Samples s, ushort flora, ushort ground)
        {
            var sp = FloraCatalog.Find(s.Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(flora))!.Key)!;
            string host = s.Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(ground))?.Key ?? "";
            return sp.Hosts.Contains(host) || sp.LateHosts.Contains(host);
        }

        public int Count => Maps.Sum(m => m.Cast<ushort>().Count(v => v != 0));
    }

    // --- Statistics over the plant maps ---

    /// <summary>The plants within two blocks of (i, j) on the same map: how many, and how many of the same species.</summary>
    private static (int Near, int Same) Neighbours(ushort[,] map, int i, int j)
    {
        int near = 0, same = 0;
        for (int di = -2; di <= 2; di++)
        {
            for (int dj = -2; dj <= 2; dj++)
            {
                int a = i + di, b = j + dj;
                if ((di | dj) == 0 || di * di + dj * dj > 4 || a < 0 || b < 0 || a >= AreaSize || b >= AreaSize || map[a, b] == 0)
                {
                    continue;
                }

                near++;
                same += map[a, b] == map[i, j] ? 1 : 0;
            }
        }

        return (near, same);
    }

    /// <summary>The share of plants with at least two other plants within two blocks, and — over those clumped plants — the
    /// share of their near plants that are the same species.</summary>
    private static (double Clumped, double SameSpecies) Clumping(Sample sample)
    {
        int plants = 0, clumped = 0;
        double same = 0;
        foreach (var map in sample.Maps)
        {
            for (int i = 0; i < AreaSize; i++)
            {
                for (int j = 0; j < AreaSize; j++)
                {
                    if (map[i, j] == 0)
                    {
                        continue;
                    }

                    plants++;
                    var (near, sameNear) = Neighbours(map, i, j);
                    if (near >= 2)
                    {
                        clumped++;
                        same += (double)sameNear / near;
                    }
                }
            }
        }

        return (plants == 0 ? 0 : (double)clumped / plants, clumped == 0 ? 0 : same / clumped);
    }

    /// <summary>The share of the non-overlapping 6 × 6 windows that hold no plant.</summary>
    private static double EmptyWindowShare(Sample sample)
    {
        int windows = 0, empty = 0;
        foreach (var map in sample.Maps)
        {
            for (int i = 0; i + 6 <= AreaSize; i += 6)
            {
                for (int j = 0; j + 6 <= AreaSize; j += 6)
                {
                    windows++;
                    bool any = false;
                    for (int a = i; a < i + 6 && !any; a++)
                    {
                        for (int b = j; b < j + 6 && !any; b++)
                        {
                            any = map[a, b] != 0;
                        }
                    }

                    empty += any ? 0 : 1;
                }
            }
        }

        return (double)empty / windows;
    }

    /// <summary>The mean size of the connected plant patches (plants joined through their four side neighbours, within one
    /// area) — larger clumps make larger patches.</summary>
    private static double MeanPatchSize(Sample sample)
    {
        int patches = 0, plants = 0;
        var stack = new Stack<(int, int)>();
        foreach (var map in sample.Maps)
        {
            var seen = new bool[AreaSize, AreaSize];
            for (int i = 0; i < AreaSize; i++)
            {
                for (int j = 0; j < AreaSize; j++)
                {
                    if (map[i, j] == 0 || seen[i, j])
                    {
                        continue;
                    }

                    patches++;
                    seen[i, j] = true;
                    stack.Push((i, j));
                    while (stack.Count > 0)
                    {
                        var (a, b) = stack.Pop();
                        plants++;
                        foreach (var (na, nb) in new[] { (a + 1, b), (a - 1, b), (a, b + 1), (a, b - 1) })
                        {
                            if (na >= 0 && nb >= 0 && na < AreaSize && nb < AreaSize && map[na, nb] != 0 && !seen[na, nb])
                            {
                                seen[na, nb] = true;
                                stack.Push((na, nb));
                            }
                        }
                    }
                }
            }
        }

        return patches == 0 ? 0 : (double)plants / patches;
    }

    // --- The rule itself ---

    [Theory]
    [InlineData(WorldConstants.Circumference)]
    [InlineData(5472)]
    [InlineData(5476)] // not a multiple of the cell: the grid's cells widen a little so it still closes at the seam
    public void GroupFactor_AveragesOne_AndTheZoneSharesMatchTheirConstants(int circumference)
    {
        // The whole world on a stride lattice (7 × 5 blocks, coprime with the 8-block cells): the clearing mask varies over
        // about 36 blocks, so only a world-sized sample measures the shares the density factors are balanced on.
        var content = _samples.Content;
        int lat = WorldConstants.LatitudePeriodFor(circumference);
        double sum = 0;
        int columns = 0, inside = 0, ring = 0;
        for (long seed = 1; seed <= 2; seed++)
        {
            var gen = new WorldGenerator(seed, content);
            gen.SetWorldMode(circumference, false, null, "flora-groups-test");
            for (int x = 0; x < circumference; x += 7)
            {
                for (int z = -lat / 2; z < lat / 2; z += 5)
                {
                    int zone = gen.FloraZoneForTest(seed * 31, x, z);
                    sum += zone == WorldGenerator.FloraZoneInside ? WorldGenerator.FloraGroupInsideFactor : WorldGenerator.FloraGroupOutsideFactor;
                    inside += zone == WorldGenerator.FloraZoneInside ? 1 : 0;
                    ring += zone == WorldGenerator.FloraZoneRing ? 1 : 0;
                    columns++;
                }
            }
        }

        double mean = sum / columns, coverage = (double)inside / columns, ringCoverage = (double)ring / columns;
        _output.WriteLine($"circumference {circumference}: inside {coverage:F4}, ring {ringCoverage:F4}, mean group factor {mean:F4}");
        Assert.InRange(coverage, WorldGenerator.FloraGroupCoverage - 0.004, WorldGenerator.FloraGroupCoverage + 0.004);
        Assert.InRange(ringCoverage, WorldGenerator.FloraGroupRingCoverage - 0.004, WorldGenerator.FloraGroupRingCoverage + 0.004);
        Assert.InRange(mean, 0.98, 1.02);
    }

    [Theory]
    [InlineData(0.02)] // no cap bites
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.3)] // the cap bites inside the groups only: the excess goes into the rings
    [InlineData(0.4)]
    [InlineData(0.6)] // the cap bites inside and on the rings: the rest goes to the open ground
    [InlineData(0.8)]
    [InlineData(1.5)] // above the cap itself: every zone at 0.95
    public void GroupedDensity_KeepsTheAreaMean_BelowBetweenAndAboveTheCaps(double raw)
    {
        // Over a large area the three zones hold their measured shares: the mean planting chance must be the ungrouped (capped)
        // density, whether no cap bites, the cap bites inside the groups, or inside and on the rings.
        double c = WorldGenerator.FloraGroupCoverage, cr = WorldGenerator.FloraGroupRingCoverage;
        double inside = WorldGenerator.GroupedFloraDensity(raw, WorldGenerator.FloraZoneInside);
        double ring = WorldGenerator.GroupedFloraDensity(raw, WorldGenerator.FloraZoneRing);
        double open = WorldGenerator.GroupedFloraDensity(raw, WorldGenerator.FloraZoneOpen);
        Assert.Equal(System.Math.Min(raw, 0.95), c * inside + cr * ring + (1 - c - cr) * open, 6);
        Assert.True(inside >= ring && ring >= open, $"raw {raw}: inside {inside}, ring {ring}, open {open}");
        Assert.True(inside <= 0.95 && ring <= 0.95 && open <= 0.95);
        if (raw * WorldGenerator.FloraGroupInsideFactor <= 0.95)
        {
            Assert.Equal(raw * WorldGenerator.FloraGroupOutsideFactor, ring, 12); // no excess: the ring is open ground
            Assert.Equal(ring, open, 12);
        }
    }

    [Theory]
    [InlineData(5472)]
    [InlineData(5476)]
    public void Groups_WrapSeamlessly_AtBothSeams(int circumference)
    {
        var gen = new WorldGenerator(Seed, _samples.Content);
        gen.SetWorldMode(circumference, false, null, "flora-groups-seam");
        int lat = WorldConstants.LatitudePeriodFor(circumference);
        int seamIn = 0, seamCols = 0, inlandIn = 0, inlandCols = 0;
        for (int x = -24; x < 24; x++)
        {
            for (int z = -300; z < 300; z++)
            {
                var here = gen.FloraGroupAtForTest(Seed, x, z);
                Assert.Equal(here, gen.FloraGroupAtForTest(Seed, x + circumference, z));
                Assert.Equal(here, gen.FloraGroupAtForTest(Seed, x - circumference, z));
                Assert.Equal(here, gen.FloraGroupAtForTest(Seed, x, z + lat));
                int zone = gen.FloraZoneForTest(Seed, x, z); // the rings wrap too
                Assert.Equal(zone, gen.FloraZoneForTest(Seed, x + circumference, z));
                Assert.Equal(zone, gen.FloraZoneForTest(Seed, x, z + lat));
                seamIn += here is null ? 0 : 1;
                seamCols++;
                inlandIn += gen.FloraGroupAtForTest(Seed, x + 1000, z) is null ? 0 : 1;
                inlandCols++;
            }
        }

        // No gap and no doubled groups along the seam: about as many grouped columns there as inland.
        double seam = (double)seamIn / seamCols, inland = (double)inlandIn / inlandCols;
        Assert.InRange(seam, inland * 0.7, inland * 1.3);

        // A group that straddles the X seam is one group: its columns on both sides name the same cell.
        bool straddles = false;
        for (int z = -300; z < 300 && !straddles; z++)
        {
            var east = gen.FloraGroupAtForTest(Seed, circumference - 1, z);
            straddles = east is not null && east == gen.FloraGroupAtForTest(Seed, 0, z);
        }

        Assert.True(straddles, "some group must straddle the X seam (centres near the last cell's edge reach over it)");
    }

    // --- Generated worlds ---

    [Fact]
    public void Generation48_MovesOnlyTheSurfacePlants_AgainstGeneration47()
    {
        // The colour harmony of generation 48 changes no block, so every cell that differs between generation 47 and 48 of
        // the same seed has a small surface plant on at least one side: a plant appeared, vanished or changed species. The
        // later passes (tree crowns, fruit, fallen logs, scrap and boulder props) fill air cells only, so a cell a plant took
        // or freed can carry one of their blocks instead — a handful of single cells, never a whole moved prop. (Generation
        // 47 and older keep their chunks bit for bit — WorldGenerationGoldenTests pins those.)
        var older = _samples.Get("varied", Gen - 1);
        var newer = _samples.Get("varied", Gen);
        var wild = _samples.Wild;
        int changed = 0, besidePlants = 0;
        foreach (var chunkA in older.Probe.Chunks)
        {
            var coord = chunkA.Coord;
            var a = chunkA.ToArray();
            var b = newer.Probe.Chunks.First(c => c.Coord == coord).ToArray();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i])
                {
                    continue;
                }

                changed++;
                bool plantA = wild.Contains(a[i]), plantB = wild.Contains(b[i]);
                Assert.True(plantA || plantB, $"chunk {coord} cell {i}: {Key(a[i])} -> {Key(b[i])} — no surface plant on either side");
                besidePlants += (plantA || a[i] == 0) && (plantB || b[i] == 0) ? 0 : 1;
            }
        }

        _output.WriteLine($"{changed} cells changed, {besidePlants} of them a plant against another block than air");
        Assert.True(changed > 100, $"generation 48 must move the plants ({changed} cells changed)");
        Assert.True(besidePlants * 20 <= changed, $"{besidePlants} of {changed} changed cells trade a plant for another block");

        string Key(ushort id) => _samples.Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(id))?.Key ?? id.ToString();
    }


    [Theory]
    [InlineData("varied")] // floraDensity 0.10
    [InlineData("meadowlands")] // 0.22: lush — the cap bites inside the groups, the excess goes into the rings
    [InlineData("desert")] // 0.05: sparse
    [InlineData("savanna")] // 0.08
    [InlineData("flower_fields")] // 0.50: the lushest type — the cap bites inside and on many rings
    public void Generation48_KeepsThePlantsPerArea(string planet)
    {
        int older = _samples.Get(planet, Gen - 1).Count;
        int newer = _samples.Get(planet, Gen).Count;
        _output.WriteLine($"{planet}: {older} plants on generation {Gen - 1}, {newer} on generation {Gen}");
        Assert.True(older >= 1000, $"{planet}: too few plants to judge ({older})");
        Assert.InRange(newer, older * 0.88, older * 1.12);
    }

    [Theory]
    [InlineData("varied")]
    [InlineData("desert")]
    [InlineData("savanna")]
    public void Generation48_StandsInClumps_WithOpenGroundBetween(string planet)
    {
        var older = _samples.Get(planet, Gen - 1);
        var newer = _samples.Get(planet, Gen);
        var (clumpedOld, _) = Clumping(older);
        var (clumpedNew, _) = Clumping(newer);
        double emptyOld = EmptyWindowShare(older), emptyNew = EmptyWindowShare(newer);
        double patchOld = MeanPatchSize(older), patchNew = MeanPatchSize(newer);
        _output.WriteLine($"{planet}: clumped {clumpedOld:P1} -> {clumpedNew:P1}, empty 6x6 windows {emptyOld:P1} -> {emptyNew:P1}, "
            + $"mean patch {patchOld:F2} -> {patchNew:F2}");
        Assert.True(clumpedNew >= clumpedOld + 0.2, $"{planet}: clumped share {clumpedOld:P1} -> {clumpedNew:P1}");
        Assert.True(emptyNew >= emptyOld + 0.04, $"{planet}: empty windows {emptyOld:P1} -> {emptyNew:P1}");
        Assert.True(patchNew >= patchOld * 1.2, $"{planet}: mean patch size {patchOld:F2} -> {patchNew:F2}");
    }

    [Fact]
    public void Generation48_LushGround_GrowsLargerClumps_NotAFullerMeadow()
    {
        // On lush ground (meadowlands) the groups sit at the cap; the cut-off growth fills each group's ring, so the clumps grow
        // and the ground between them stays open, instead of the excess spreading over all the open ground. (flower_fields is
        // left out on purpose: its land is planted so densely that at the same amount of plants nearly every zone is at the cap —
        // there is no open ground left to keep, and its statistics barely move; Generation48_KeepsThePlantsPerArea covers it.)
        var older = _samples.Get("meadowlands", Gen - 1);
        var newer = _samples.Get("meadowlands", Gen);
        var (clumpedOld, _) = Clumping(older);
        var (clumpedNew, _) = Clumping(newer);
        double emptyOld = EmptyWindowShare(older), emptyNew = EmptyWindowShare(newer);
        double patchOld = MeanPatchSize(older), patchNew = MeanPatchSize(newer);
        _output.WriteLine($"meadowlands: plants {older.Count} -> {newer.Count}, clumped {clumpedOld:P1} -> {clumpedNew:P1}, "
            + $"empty 6x6 windows {emptyOld:P1} -> {emptyNew:P1}, mean patch {patchOld:F2} -> {patchNew:F2}");
        Assert.True(emptyNew >= emptyOld + 0.02, $"meadowlands: empty windows {emptyOld:P1} -> {emptyNew:P1}");
        Assert.True(patchNew >= patchOld * 1.4, $"meadowlands: mean patch size {patchOld:F2} -> {patchNew:F2}");
    }

    [Theory]
    [InlineData("varied")]
    [InlineData("desert")]
    [InlineData("savanna")]
    [InlineData("meadowlands")]
    public void Generation48_Clumps_AreMostlyOneSpecies(string planet)
    {
        var (_, sameOld) = Clumping(_samples.Get(planet, Gen - 1));
        var (_, sameNew) = Clumping(_samples.Get(planet, Gen));
        _output.WriteLine($"{planet}: same species among near plants {sameOld:P1} -> {sameNew:P1}");
        Assert.True(sameNew >= sameOld + 0.08, $"{planet}: same-species share {sameOld:P1} -> {sameNew:P1}");
    }

    [Theory]
    [InlineData("varied")]
    [InlineData("desert")]
    [InlineData("savanna")]
    [InlineData("meadowlands")] // ring columns join their group's species here
    public void Generation48_GroupSpecies_ComeFromTheColumnsOwnHostPool(string planet)
    {
        // Both generations pick a column's species from the pool of the column's host (a beach, painted ground or a frozen
        // host replaces the biome surface): so where both plant a column, the two species stand on their host alike. A group
        // species drawn from the wrong pool — a grass plant in beach sand — would stand off its host where generation 47 did not.
        var older = _samples.Get(planet, Gen - 1);
        var newer = _samples.Get(planet, Gen);
        int both = 0, differ = 0;
        for (int a = 0; a < older.Maps.Count; a++)
        {
            for (int i = 0; i < AreaSize; i++)
            {
                for (int j = 0; j < AreaSize; j++)
                {
                    if (older.Maps[a][i, j] != 0 && newer.Maps[a][i, j] != 0)
                    {
                        both++;
                        differ += older.OnHost[a][i, j] != newer.OnHost[a][i, j] ? 1 : 0;
                    }
                }
            }
        }

        _output.WriteLine($"{planet}: {differ} of {both} columns planted on both generations differ in standing on their host");
        Assert.True(both >= 200, $"{planet}: too few columns planted on both generations ({both})");
        Assert.True(differ <= both / 100, $"{planet}: {differ} of {both} columns changed between on and off their host");
    }


    [Fact]
    public void Generation48_IsDeterministic_AndRepeatsAcrossTheXSeam()
    {
        const int circumference = 5472;
        var planet = _samples.Content.GetPlanet("meadowlands")!;
        WorldGenerator Make()
        {
            var g = new WorldGenerator(Seed, _samples.Content);
            g.SetWorldMode(circumference, false, null, "flora-groups-seam-body");
            g.SetTerrainGeneration(Gen);
            return g;
        }

        var a = Make();
        var b = Make();
        for (int cx = -2; cx < 2; cx++)
        {
            for (int cz = 3; cz < 5; cz++)
            {
                int sy = a.SurfaceHeight(planet, cx * 16 + 8, cz * 16 + 8);
                var coord = new ChunkCoord(cx, sy / 16, cz);
                var wrapped = new ChunkCoord(cx + circumference / 16, sy / 16, cz);
                var first = a.Generate(planet, coord).ToArray();
                Assert.Equal(first, b.Generate(planet, coord).ToArray());
                Assert.Equal(first, b.Generate(planet, wrapped).ToArray());
            }
        }
    }
}
