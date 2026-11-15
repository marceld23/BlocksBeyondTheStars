// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3680: the fallen guardian giants are laid down by the structures stage of a terrain-generation-44 world — the kind's rule
/// (gate, chances, margins), the seeded decisions (count, facing, body seed, who can wake), and on a real server: the record the
/// boss logic reads, the lying body cell for cell as <see cref="GuardianGiantBody.Lying"/>, dry and resting on the ground, clear
/// of everything stamped before it, written once and never again.
/// </summary>
public sealed class GuardianGiantStampTests : IDisposable
{
    private const long WorldSeed = 20261115;

    private static readonly Lazy<GameContent> LazyContent = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_guardiangiant_" + Guid.NewGuid().ToString("N"));

    private static GameContent Content => LazyContent.Value;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    // ---- the kind ------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheKind_IsGatedByGeneration44_AndShippedAsItsDefaults()
    {
        Assert.Equal(44, WorldDescription.GuardianGiantsGeneration);
        Assert.True(WorldDescription.GuardianGiantsGeneration < WorldDescription.CurrentTerrainGeneration); // 45: the abandoned beds
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, new ServerConfig().World.TerrainGeneration);

        var rule = StructureTypeRules.Defaults.Get(StructureTypeRules.GuardianGiant);
        Assert.Equal(GuardianGiantRules.StructureKind, rule.Kind);
        Assert.Equal(WorldDescription.GuardianGiantsGeneration, rule.MinGeneration);
        Assert.Equal((0.34, 2), (rule.Chance, rule.Cap));
        Assert.Equal(new[] { 0.3 }, rule.Thresholds);
        Assert.Equal(new[] { "notVoid", "notGas", "notAsteroid", "notCityWorld" }, rule.Conditions);
        Assert.Equal(40, rule.MarginFrom(StructureTypeRules.Pad, 0));
        Assert.Equal(30, rule.MarginFrom(StructureTypeRules.WreckSite, 0));
        Assert.Equal(60, rule.MarginFrom(StructureTypeRules.Settlement, 6));
        Assert.Equal(60, rule.MarginFrom(StructureTypeRules.BanditCamp, 6));
        Assert.Equal(24, rule.MarginFrom(StructureTypeRules.Landmark, 6));
        Assert.Equal(24, rule.MarginFrom(StructureTypeRules.GiantWreck, 6));
        Assert.Equal(24, rule.MarginFrom(StructureTypeRules.Civilization, 6));
        Assert.True(Content.StructureTypes.Get(StructureTypeRules.GuardianGiant).SameAs(rule));

        // A file can never switch it on in an older world.
        var lowered = StructureTypeRules.Parse("""{ "guardian_giant": { "minGeneration": 40 } }""");
        Assert.Equal(WorldDescription.GuardianGiantsGeneration, lowered.Get(StructureTypeRules.GuardianGiant).MinGeneration);
    }

    [Fact]
    public void TheCount_IsAFirstGiantOnAboutEveryThirdBody_ASecondOnAboutAThirdOfThose_NeverMore()
    {
        var rule = StructureTypeRules.Defaults.Get(StructureTypeRules.GuardianGiant);
        const int Bodies = 6000;
        int one = 0, two = 0;
        for (int i = 0; i < Bodies; i++)
        {
            long seed = 1000 + (i / 12);
            string body = "sys" + (i % 4) + "-p" + (i % 12);
            int n = GuardianGiantSites.Count(seed, body, rule.Chance, rule.Thresholds, rule.Cap);
            Assert.InRange(n, 0, 2);
            Assert.Equal(n, GuardianGiantSites.Count(seed, body, rule.Chance, rule.Thresholds, rule.Cap)); // seeded
            one += n >= 1 ? 1 : 0;
            two += n == 2 ? 1 : 0;
        }

        Assert.InRange(one / (double)Bodies, 0.31, 0.37);
        Assert.InRange(two / (double)one, 0.25, 0.35);
        Assert.Equal(0, GuardianGiantSites.Count(1, "sys0-p0", 0.0, rule.Thresholds, rule.Cap));
        Assert.Equal(2, GuardianGiantSites.Count(1, "sys0-p0", 1.0, new[] { 1.0, 1.0 }, 2)); // the cap holds
        Assert.Equal(1, GuardianGiantSites.Count(1, "sys0-p0", 1.0, Array.Empty<double>(), 0)); // no further chance: one
    }

    [Fact]
    public void AboutAQuarterCanWake_AndEveryFacingTurnsUp_SeededPerGiant()
    {
        const int Giants = 8000;
        int wake = 0;
        var facings = new int[4];
        for (int i = 0; i < Giants; i++)
        {
            long seed = 77 + (i / 20);
            string body = "sys" + (i % 5) + "-p" + (i % 10);
            int index = i % 2;
            bool canWake = GuardianGiantSites.CanWake(seed, body, index);
            Assert.Equal(canWake, GuardianGiantSites.CanWake(seed, body, index));
            wake += canWake ? 1 : 0;
            int facing = GuardianGiantSites.Facing(seed, body, index);
            Assert.InRange(facing, 0, 3);
            facings[facing]++;
        }

        Assert.InRange(wake / (double)Giants, GuardianGiantRules.WakeShare - 0.02, GuardianGiantRules.WakeShare + 0.02);
        Assert.All(facings, f => Assert.InRange(f, Giants / 5, Giants * 3 / 10));
        Assert.NotEqual(GuardianGiantSites.BodySeed(1, "sys0-p1", 0), GuardianGiantSites.BodySeed(1, "sys0-p1", 1));
        Assert.NotEqual(GuardianGiantSites.BodySeed(1, "sys0-p1", 0), GuardianGiantSites.BodySeed(2, "sys0-p1", 0));
    }

    // ---- on a server ---------------------------------------------------------------------------------------------------

    private SvGameServer Start(string name, out SqliteWorldRepository repo, int generation = WorldDescription.GuardianGiantsGeneration,
        double chance = 1.0, GameContent? content = null, Action<ServerConfig>? tweak = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = WorldSeed,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceLandmarks = false,
            GiantWreckChance = 0.0,
            GuardianGiantChance = chance,
        };
        config.World.TerrainGeneration = generation;
        tweak?.Invoke(config);
        var server = new SvGameServer(config, content ?? Content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    private static Vector3i Cell(SvGameServer server, int x, int y, int z)
        => WorldConstants.CanonicalBlock(new Vector3i(x, y, z), server.World.Circumference);

    /// <summary>Every cell of the record's lying body holds its block, every column rests on solid ground (no plant, no fluid
    /// under it) and the bed under it is dry.</summary>
    private static void AssertLiesAsItsBody(SvGameServer server, GameContent content, GuardianGiantRecord g)
    {
        foreach (var (x, y, z, key) in GuardianGiantBuilder.Cells(g.X, g.Y, g.Z, g.Seed, g.Facing, lit: false))
        {
            Assert.Equal(content.GetBlock(key)!.NumericId, server.World.GetBlock(Cell(server, x, y, z)));
        }

        foreach (var column in GuardianGiantBody.Lying(g.Seed, lit: false, g.Facing).GroupBy(c => (c.Dx, c.Dz)))
        {
            var below = server.World.GetBlock(Cell(server, g.X + column.Key.Dx, g.Y + column.Min(c => c.Dy) - 1, g.Z + column.Key.Dz));
            var def = content.BlockById(below);
            Assert.True(def is { Solid: true } && def.Key is not ("water" or "lava") && def.Category != "flora",
                $"column {column.Key} of '{g.SiteId}' rests on '{def?.Key}'");
        }

        int floor = g.Y - GuardianGiantBuilder.AnchorAboveGround;
        var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(g.Facing);
        for (int dx = minX; dx <= maxX; dx++)
        {
            for (int dz = minZ; dz <= maxZ; dz++)
            {
                for (int y = floor; y <= floor + 1; y++)
                {
                    string key = content.BlockById(server.World.GetBlock(Cell(server, g.X + dx, y, g.Z + dz)))?.Key ?? "?";
                    Assert.True(key is not ("water" or "lava" or "ice"), $"the bed of '{g.SiteId}' holds {key} at {dx},{y},{dz}");
                }
            }
        }
    }

    [Fact]
    public void AGeneration44Body_LaysItsGiantDown_WritesItsRecords_AndTheBodyLiesOnTheGround()
    {
        var server = Start("gg_lay", out var repo);
        using (repo)
        {
            string location = server.World.LocationId;
            var g = Assert.Single(server.StampedGuardianGiantsForTest);
            Assert.Equal((location, 0, location + "#0"), (g.LocationId, g.Index, g.SiteId));
            Assert.Equal(GuardianGiantSites.Facing(WorldSeed, location, 0), g.Facing);
            Assert.Equal(GuardianGiantSites.BodySeed(WorldSeed, location, 0), g.Seed);
            Assert.Equal(GuardianGiantSites.CanWake(WorldSeed, location, 0), g.CanWake);
            Assert.Equal(GuardianGiantRecord.Dormant, g.State);
            Assert.Equal((g.X, g.Y, g.Z, g.Facing), (g.CorpseX, g.CorpseY, g.CorpseZ, g.CorpseFacing));
            Assert.InRange(g.X, 0, server.World.Circumference - 1);

            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == GuardianGiantRules.StructureKind);
            Assert.True(rec.Placed);
            Assert.Equal(0, rec.Index);
            var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(g.Facing);
            Assert.Equal((maxX - minX + 1, maxZ - minZ + 1), (rec.Width, rec.Length));
            Assert.Equal(g.Y - GuardianGiantBuilder.AnchorAboveGround, rec.GroundY);
            var pin = MarkerSettings.Parse(rec.KitLayout);
            Assert.Equal((g.Facing, g.Seed, g.CanWake), (pin.GetInt("facing", -1), pin.GetInt("seed", 0), pin.GetBool("wake")));
            Assert.Equal((g.X, g.Y, g.Z), (pin.GetInt("ax", 0), pin.GetInt("ay", 0), pin.GetInt("az", 0)));
            Assert.Contains(server.StampReportForTest, r => r.Kind == GuardianGiantRules.StructureKind && r.Placed == r.Requested);
            Assert.Contains(server.SiteFlatsForTest, s => s.MinX == rec.X && s.MinZ == rec.Z && s.FloorY == rec.GroundY);

            AssertLiesAsItsBody(server, Content, g);
            server.Stop();
        }
    }

    [Fact]
    public void TheSameSeed_LaysTheSameGiants_AndASecondGiantKeepsClearOfTheFirst()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStructureTypes(StructureTypeRules.Parse("""{ "guardian_giant": { "thresholds": [1.0] } }""")); // always a second one
        var giants = new List<List<GuardianGiantRecord>>();
        foreach (string name in new[] { "gg_twice_a", "gg_twice_b" })
        {
            var server = Start(name, out var repo, content: content);
            using (repo)
            {
                var list = server.StampedGuardianGiantsForTest.ToList();
                Assert.Equal(2, list.Count);
                Assert.All(list, g => AssertLiesAsItsBody(server, content, g));
                var boxes = server.FootprintsForTest.Where(f => f.Kind == GuardianGiantRules.StructureKind).ToList();
                Assert.Equal(2, boxes.Count);
                Assert.False(Closer(server, boxes[0], boxes[1], 48), "the second giant lies within 48 blocks of the first");
                giants.Add(list);
                server.Stop();
            }
        }

        Assert.Equal(giants[0].Select(Pose), giants[1].Select(Pose));
    }

    private static (int, int, int, int, int, bool) Pose(GuardianGiantRecord g) => (g.X, g.Y, g.Z, g.Facing, g.Seed, g.CanWake);

    /// <summary>Whether two boxes come closer than <paramref name="margin"/> on both axes (the registry's overlap test).</summary>
    private static bool Closer(SvGameServer server, StructureFootprints.Footprint a, StructureFootprints.Footprint b, int margin)
    {
        int dx = Math.Abs(WorldConstants.WrapDeltaX(a.Cx - b.Cx, server.World.Circumference));
        int dz = Math.Abs(a.Cz - b.Cz);
        return dx < a.Hw + b.Hw + margin && dz < a.Hl + b.Hl + margin;
    }

    [Fact]
    public void AGiant_KeepsItsMarginsFromThePads_TheWreck_AndTheTowns()
    {
        var server = Start("gg_clear", out var repo, tweak: c => c.PlaceSettlements = true);
        using (repo)
        {
            var rule = Content.StructureTypes.Get(StructureTypeRules.GuardianGiant);
            var boxes = server.FootprintsForTest;
            var giants = boxes.Where(f => f.Kind == GuardianGiantRules.StructureKind).ToList();
            Assert.NotEmpty(giants);
            Assert.Contains(boxes, f => f.Kind == StructureTypeRules.Pad);
            foreach (var g in giants)
            {
                foreach (var other in boxes.Where(f => f.Owner != g.Owner))
                {
                    int margin = rule.MarginFrom(other.Kind, other.Margin);
                    Assert.False(Closer(server, g, other, margin), $"the giant {g.Owner} lies within {margin} of {other.Kind} {other.Owner}");
                }
            }

            Assert.Empty(server.FootprintCollisionsForTest());
            server.Stop();
        }
    }

    [Fact]
    public void ALaterLoad_WritesNoBlockAgain_AndLeavesTheRecordsStateAlone()
    {
        const string Name = "gg_replay";
        string location;
        GuardianGiantRecord first;
        Vector3i mined;
        var server = Start(Name, out var repo);
        using (repo)
        {
            location = server.World.LocationId;
            first = Assert.Single(server.StampedGuardianGiantsForTest);
            var (x, y, z, _) = GuardianGiantBuilder.Cells(first.X, first.Y, first.Z, first.Seed, first.Facing, lit: false).First();
            mined = Cell(server, x, y, z);
            server.Stop();
        }

        // A player mined a plate, and the boss logic marked the giant defeated.
        using (var edit = new SqliteWorldRepository(new SaveGamePaths(_root, Name)))
        {
            edit.Initialize();
            edit.SetBlock(location, mined, 0);
            var meta = edit.LoadMetadata()!;
            var rec = meta.GuardianGiants[first.SiteId];
            rec.State = GuardianGiantRecord.Defeated;
            rec.DefeatedAt = 1234;
            rec.Rewarded.Add("player-1");
            edit.SaveMetadata(meta);
        }

        var again = Start(Name, out var repo2);
        using (repo2)
        {
            var g = Assert.Single(again.StampedGuardianGiantsForTest);
            Assert.Equal(Pose(first), Pose(g));
            Assert.Equal((GuardianGiantRecord.Defeated, 1234L), (g.State, g.DefeatedAt));
            Assert.Equal(new[] { "player-1" }, g.Rewarded);
            Assert.True(again.World.GetBlock(mined).IsAir, "a mined plate came back");
            Assert.Single(again.PlacementRecordsForTest, r => r.Kind == GuardianGiantRules.StructureKind);
            Assert.Contains(again.FootprintsForTest, f => f.Kind == GuardianGiantRules.StructureKind); // still kept clear of
            again.Stop();
        }
    }

    [Theory]
    [InlineData(WorldDescription.CivilizationsGeneration, true)]
    [InlineData(WorldDescription.GuardianGiantsGeneration, false)]
    public void AnOlderWorld_OrTheHostsSwitchOff_LaysNoGiant_AndDecidesNothing(int generation, bool hostOn)
    {
        var server = Start("gg_none_" + generation, out var repo, generation, tweak: c => c.PlaceGuardianGiants = hostOn);
        using (repo)
        {
            Assert.Empty(server.StampedGuardianGiantsForTest);
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == GuardianGiantRules.StructureKind);
            server.Stop();
        }
    }

    [Fact]
    public void ABodyThatRollsNone_RecordsTheSkipOnce()
    {
        var server = Start("gg_skip", out var repo, chance: 0.0);
        using (repo)
        {
            Assert.Empty(server.StampedGuardianGiantsForTest);
            var skip = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == GuardianGiantRules.StructureKind);
            Assert.False(skip.Placed);
            server.Stop();
        }
    }
}
