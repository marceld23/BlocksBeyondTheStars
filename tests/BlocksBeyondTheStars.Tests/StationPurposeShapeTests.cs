// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3871: a station with a purpose shows it from outside and in its floor plan. Its own hull parts — cargo racks of
/// freight crates on a trade station, ore hoppers on a mining station, radio dishes on a research station — and its own
/// passages: a trade station's concourse around a crossroads, a mining station's spine without a junction, a research
/// station's extra deck. A station composed without a purpose gets none of it.
/// </summary>
public sealed class StationPurposeShapeTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static GameContent Content => Shipped.Value;

    private static StructureTemplate? ByKey(string key) => Content.TemplateByKey(StructureKit.KindStation, key);

    private static StructureKit Kit(string key) => Content.StructureKits.Single(k => k.Key == key);

    private static ushort Id(string block) => Content.GetBlock(block)!.NumericId.Value;

    /// <summary>One closed module (a hull shell) in an empty grid, the purpose's parts placed on it; the blocks they wrote.</summary>
    private static Dictionary<ushort, int> PartsOnOneModule(string purpose, out int placed)
    {
        const int W = 24, H = 16, L = 24;
        var grid = new ushort[W, H, L];
        var box = (Min: new Vector3i(4, 2, 4), Max: new Vector3i(16, 8, 16));
        ushort hull = Id("iron_wall");
        for (int x = box.Min.X; x <= box.Max.X; x++)
        {
            for (int y = box.Min.Y; y <= box.Max.Y; y++)
            {
                for (int z = box.Min.Z; z <= box.Max.Z; z++)
                {
                    bool shell = x == box.Min.X || x == box.Max.X || y == box.Min.Y || y == box.Max.Y || z == box.Min.Z || z == box.Max.Z;
                    grid[x, y, z] = shell ? hull : (ushort)0;
                }
            }
        }

        bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L;
        bool InBox(int x, int y, int z) => x >= box.Min.X && x <= box.Max.X && y >= box.Min.Y && y <= box.Max.Y && z >= box.Min.Z && z <= box.Max.Z;
        var written = new Dictionary<ushort, int>();
        placed = StationKitPurposeParts.Place(purpose, new[] { box },
            (x, y, z) => In(x, y, z) && !InBox(x, y, z) && grid[x, y, z] == 0,
            (x, y, z) => In(x, y, z) && grid[x, y, z] != 0,
            (x, y, z) => In(x, y, z) ? grid[x, y, z] : (ushort)0,
            (x, y, z, b, _, _, _) =>
            {
                Assert.False(InBox(x, y, z), $"{purpose}: a part cell inside the module at ({x},{y},{z})");
                grid[x, y, z] = b;
                written[b] = written.TryGetValue(b, out int n) ? n + 1 : 1;
            },
            Content, new Random(7));
        return written;
    }

    [Fact]
    public void EachPurpose_BuildsItsOwnPart_OutsideTheModule()
    {
        ushort crate = Id(StationKitPurposeParts.CrateBlock), ore = Id(StationKitPurposeParts.OreBlock), light = Id("light_white");

        var trade = PartsOnOneModule(StationPurposes.Trade, out int racks);
        Assert.Equal(1, racks);
        Assert.Equal(6, trade[crate]); // three wide, two high
        Assert.Equal(1, trade[light]); // the beacon
        Assert.False(trade.ContainsKey(ore));

        var mining = PartsOnOneModule(StationPurposes.Mining, out int hoppers);
        Assert.Equal(1, hoppers);
        Assert.Equal(2, mining[ore]); // heaped in the funnel
        Assert.False(mining.ContainsKey(crate));

        var research = PartsOnOneModule(StationPurposes.Research, out int dishes);
        Assert.Equal(1, dishes);
        Assert.Equal(1, research[light]); // the feed
        Assert.Equal(9 + 12, research[Id("iron_wall")]); // the plate and the round rim
        Assert.False(research.ContainsKey(crate) || research.ContainsKey(ore));

        Assert.Equal(0, StationKitPurposeParts.Place(string.Empty, new[] { (new Vector3i(0, 0, 0), new Vector3i(8, 8, 8)) },
            (_, _, _) => true, (_, _, _) => true, (_, _, _) => 1, (_, _, _, _, _, _, _) => Assert.Fail("no purpose, no part"), Content, new Random(1)));
    }

    [Theory]
    [InlineData("large")]
    [InlineData("huge")]
    public void APurposeStation_CarriesItsParts_AndAClassicOneNone(string tier)
    {
        for (long seed = 1; seed <= 4; seed++)
        {
            foreach (string purpose in StationPurposes.All)
            {
                var s = StationKitComposer.Compose(Kit($"station_{tier}_{purpose}_1"), ByKey, seed, Content, out var composition, out var failure, purpose, 0);
                Assert.True(s != null, $"{tier} {purpose} seed {seed}: {failure}");
                Assert.Equal(purpose, composition.Purpose);
                Assert.True(composition.PurposeParts >= 1, $"{tier} {purpose} seed {seed}: no part found room");
                Assert.True(composition.PurposeParts <= StationKitPurposeParts.CountFor(composition.Modules.Count));
            }

            var classic = StationKitComposer.Compose(Kit($"station_{tier}_1"), ByKey, seed, Content, out var plain, out var why);
            Assert.True(classic != null, why);
            Assert.Equal(0, plain.PurposeParts);
        }
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("huge")]
    public void EachPurpose_HasItsOwnFloorPlan(string tier)
    {
        for (long seed = 1; seed <= 4; seed++)
        {
            var trade = Modules(tier, StationPurposes.Trade, seed, out _);
            Assert.True(trade.Count(k => k == $"st_{tier}_corridor") >= 2, $"trade seed {seed}: a concourse has two corridors");
            Assert.Contains($"st_{tier}_junction", trade);

            var mining = Modules(tier, StationPurposes.Mining, seed, out _);
            Assert.DoesNotContain($"st_{tier}_junction", mining); // a spine does not branch
            Assert.True(mining.Count(k => k == $"st_{tier}_corridor") >= (tier == "medium" ? 2 : 3), $"mining seed {seed}: a long spine");

            var research = Modules(tier, StationPurposes.Research, seed, out var composition);
            if (tier == "medium")
            {
                Assert.Contains($"st_{tier}_junction", research);
                continue;
            }

            var classic = Kit($"station_{tier}_1").Entries.Single(e => e.Module == $"st_{tier}_ladder_junction");
            Assert.True(research.Count(k => k == $"st_{tier}_ladder_junction") > classic.MinOrRequired, $"research seed {seed}: one deck more");
            Assert.True(composition.Modules.Select(m => m.Y).Distinct().Count() > 1, $"research seed {seed}: the labs go upstairs");
        }
    }

    private static List<string> Modules(string tier, string purpose, long seed, out StationComposition composition)
    {
        var s = StationKitComposer.Compose(Kit($"station_{tier}_{purpose}_1"), ByKey, seed, Content, out composition, out var failure, purpose, 0);
        Assert.True(s != null, $"{tier} {purpose} seed {seed}: {failure}");
        Assert.Equal(purpose, composition.Purpose); // the purpose kit composed, not the classic fallback
        return composition.Modules.Select(m => m.Key).ToList();
    }
}
