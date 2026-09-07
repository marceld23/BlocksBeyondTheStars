// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Phase-1 validation of <see cref="RiverField"/> — the block-resolution rasterization of the coarse
/// <see cref="RiverNetwork"/>. Still NOT wired into <see cref="WorldGenerator.Generate"/>; these prove the
/// placement field covers every channel, follows the terrain (no floating water), flags waterfalls at real
/// per-block cliffs the coarse net smooths over, and is deterministic. See
/// <c>docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md</c>.
/// </summary>
public class RiverFieldTests
{
    private readonly ITestOutputHelper _out;
    public RiverFieldTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static int SeaLevel(PlanetType p)
    {
        bool hasAir = !string.Equals(p.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);
        double waterAb = p.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return p.BaseHeight + (int)Math.Round((waterAb - 0.95) * p.Amplitude);
    }

    private static (RiverNetwork net, RiverField field) BuildReal(string planetKey, long seed, int cellSize = 16)
    {
        var content = Content();
        var planet = content.GetPlanet(planetKey)!;
        var gen = new WorldGenerator(seed, content);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int Height(int x, int z) => gen.SurfaceHeight(planet, x, z);
        var net = RiverNetwork.Build(seed, circ, period, SeaLevel(planet), Height, cellSize);
        var field = RiverField.Build(net, Height, circ);
        return (net, field);
    }

    // The field must cover every routed channel cell and be deterministic (the basis for client rebuild).
    [Fact]
    public void RealWorld_FieldCoversChannels_AndIsDeterministic()
    {
        const string key = "jungle";
        const long seed = 992211;
        var (net, field) = BuildReal(key, seed);
        var (_, field2) = BuildReal(key, seed);

        Assert.True(field.ColumnCount > 0, "no river columns rasterized on a wet world");
        Assert.Equal(field.ColumnCount, field2.ColumnCount);
        Assert.Equal(field.WaterfallColumnCount, field2.WaterfallColumnCount);

        // Every channel cell that has a downstream is covered (its world centre hits the field).
        int covered = 0, channels = 0;
        foreach (int c in net.ChannelCells)
        {
            if (net.FlowAccum[c] < 2 || net.FlowDir[c] < 0) continue;
            channels++;
            net.CellWorld(c, out int wx, out int wz);
            if (field.TryGet(wx, wz, out _)) covered++;
        }

        Assert.True(covered >= (int)(channels * 0.95), $"field covered only {covered}/{channels} channel cells");

        // No floating water at the field level: every column has its bed strictly below its water surface.
        foreach (var col in field.Columns)
        {
            Assert.True(col.BedY < col.WaterSurfaceY, "a river column has its bed at/above the water surface");
        }

        _out.WriteLine($"{key}/{seed}: columns={field.ColumnCount}, waterfallColumns={field.WaterfallColumnCount}, " +
                       $"channelCoverage={covered}/{channels}");
    }

    // Phase-4 polish: more source springs (the per-world density lever) yield more channels, and the extra
    // upstream flow widens the rivers — so the rasterized field covers strictly more ground. Also re-checks
    // determinism with the higher density.
    [Fact]
    public void MoreSources_WidenAndMultiplyRivers()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        const long seed = 992211;
        var gen = new WorldGenerator(seed, content);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int Height(int x, int z) => gen.SurfaceHeight(planet, x, z);
        int sea = gen.SeaLevel(planet);

        var netLow = RiverNetwork.Build(seed, circ, period, sea, Height, cellSize: 16, sourceCount: 20);
        var netHigh = RiverNetwork.Build(seed, circ, period, sea, Height, cellSize: 16, sourceCount: 120);
        Assert.True(netHigh.ChannelCells.Count >= netLow.ChannelCells.Count,
            "more source springs should not reduce the channel count");

        var fieldLow = RiverField.Build(netLow, Height, circ);
        var fieldHigh = RiverField.Build(netHigh, Height, circ);
        var fieldHigh2 = RiverField.Build(netHigh, Height, circ);

        Assert.True(fieldHigh.ColumnCount > fieldLow.ColumnCount,
            $"denser/wider rivers should cover more ground (low={fieldLow.ColumnCount}, high={fieldHigh.ColumnCount})");
        Assert.Equal(fieldHigh.ColumnCount, fieldHigh2.ColumnCount); // determinism at the higher density

        _out.WriteLine($"density: low(20)={fieldLow.ColumnCount} cols, high(120)={fieldHigh.ColumnCount} cols");
    }

    // Per-block detection finds waterfalls the coarse network smooths over (Phase-0 found net waterfalls=0).
    [Fact]
    public void Synthetic_ChannelOverCliff_FlagsWaterfall()
    {
        const int w = 160, period = 80, seaLevel = 5, cell = 4;
        // Terrain ramps down east→west to a sea strip; a 30-block CLIFF sits at x=100; a shallow V in z funnels
        // many sources onto the z≈0 line so flow accumulates into a real channel that crosses the cliff.
        int H(int x, int z)
        {
            int wx = ((x % w) + w) % w;
            if (wx < 3) return 0;                       // sea sink at the west edge
            int zc = WorldConstants.WrapZ(z, w);        // period derived from circ; here circ==w
            int ramp = wx < 100 ? wx : wx + 30;         // +30 cliff east of x=100
            return ramp + Math.Abs(zc) / 4;             // V-valley funnels flow to z=0
        }

        var net = RiverNetwork.Build(seed: 77, circumference: w, latitudePeriod: period,
            seaLevel: seaLevel, height: H, cellSize: cell);
        var field = RiverField.Build(net, H, circumference: w);
        var field2 = RiverField.Build(net, H, circumference: w);

        Assert.True(field.ColumnCount > 0, "no channel rasterized over the synthetic valley");
        Assert.True(field.WaterfallColumnCount > 0, "the cliff crossing was not flagged as a waterfall");
        Assert.Equal(field.ColumnCount, field2.ColumnCount); // determinism

        // The flagged waterfalls must carry a real drop (> the default min of 4).
        Assert.Contains(field.Columns, c => c.WaterfallDrop > 4);

        _out.WriteLine($"synthetic cliff: columns={field.ColumnCount}, waterfallColumns={field.WaterfallColumnCount}, " +
                       $"maxDrop={field.Columns.Max(c => c.WaterfallDrop)}");
    }

    /// <summary>Terrain generation 3: inside a soluble-rock region the reach runs under a rock roof. The terrain
    /// surface is untouched, the water sits a constant cover below it with headroom for a walkable passage,
    /// each side gets a bank ledge that seals the water laterally, and the reach dives in / comes back out
    /// through an open shaft. A null region reproduces the classic surface-only rasterisation exactly.</summary>
    [Fact]
    public void Synthetic_SunkRegion_RunsTheReachUnderARoof_WithMouthsAndBanks()
    {
        const int w = 160, period = 80, seaLevel = 5, cell = 4;
        const int cover = 12, headroom = 3, minCover = 3;

        int H(int x, int z)
        {
            int wx = ((x % w) + w) % w;
            if (wx < 3) return 0;                       // sea sink at the west edge
            int zc = WorldConstants.WrapZ(z, w);
            return wx + Math.Abs(zc) / 4;               // a clean ramp down to the sea, a V that funnels flow
        }

        // The karst belt the river has to cross on its way west.
        bool Sunk(int x, int z) => ((x % w) + w) % w is >= 40 and < 80;

        var net = RiverNetwork.Build(seed: 77, circumference: w, latitudePeriod: period,
            seaLevel: seaLevel, height: H, cellSize: cell);
        var classic = RiverField.Build(net, H, circumference: w);
        var field = RiverField.Build(net, H, circumference: w, sunkRegion: Sunk, sunkCover: cover, sunkHeadroom: headroom);
        var again = RiverField.Build(net, H, circumference: w, sunkRegion: Sunk, sunkCover: cover, sunkHeadroom: headroom);

        Assert.DoesNotContain(classic.Columns, c => c.Underground); // a null region is the classic field
        Assert.Equal(field.ColumnCount, again.ColumnCount);         // determinism

        var sunkCols = field.ColumnsByPosition.Where(kv => kv.Value.Underground).ToList();
        Assert.True(sunkCols.Count > 0, "no underground reach was rasterized across the karst belt");

        int mouths = 0, banks = 0, water = 0, centerlineCovered = 0;
        foreach (var (pos, col) in sunkCols)
        {
            int terrain = H(pos.X, pos.Z);
            Assert.Equal(0, col.WaterfallDrop);                     // no waterfalls inside a passage
            Assert.True(col.RoofY > col.WaterSurfaceY, "the passage has no air above its water");
            Assert.True(col.BedY <= col.WaterSurfaceY, "the bed sits above the waterline");
            if (!col.Mouth && terrain - col.RoofY >= minCover)
            {
                centerlineCovered++;                                // real rock over the roof at this column
            }

            if (col.Mouth)
            {
                mouths++;                                           // the swallow hole / the spring
                Assert.True(col.RoofY - col.WaterSurfaceY > headroom, "a mouth shaft is no taller than the passage");
            }
            else
            {
                // The whole cross-section carries the CENTERLINE's levels, so only the centerline's own cover
                // is guaranteed against its own ground; what every column must hold is the passage geometry.
                Assert.Equal(headroom, col.RoofY - col.WaterSurfaceY);
            }

            if (col.IsBank)
            {
                banks++;
                Assert.Equal(col.WaterSurfaceY, col.BedY);           // a ledge, not a channel
            }
            else
            {
                water++;
                Assert.True(col.WaterSurfaceY > col.BedY);
            }

            // The belt plus at most one coarse cell of ramp on either side.
            int wx = ((pos.X % w) + w) % w;
            Assert.InRange(wx, 40 - cell - 1, 80 + cell);
        }

        Assert.True(mouths > 0, "the reach never opened a shaft — it would step straight into a wall");
        Assert.True(banks > 0, "no bank ledges: the underground water would have air beside it");
        Assert.True(water > 0, "the passage carries no water at all");
        Assert.True(centerlineCovered > sunkCols.Count / 2, "most of the passage is not actually under rock");

        // Every surface column of the sunk field still matches the classic one — only the belt changed.
        foreach (var (pos, col) in field.ColumnsByPosition.Where(kv => !kv.Value.Underground))
        {
            Assert.True(classic.TryGet(pos.X, pos.Z, out var c0), $"a surface column appeared at {pos}");
            Assert.Equal(c0.WaterSurfaceY, col.WaterSurfaceY);
            Assert.Equal(c0.BedY, col.BedY);
        }

        _out.WriteLine($"sunk belt: underground={sunkCols.Count} (water={water}, banks={banks}, mouths={mouths}), " +
                       $"total={field.ColumnCount}, classic={classic.ColumnCount}");
    }
}
