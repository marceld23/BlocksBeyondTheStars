// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The abandoned monorail stations (#2166, terrain generation 20; Justus' "Verlassene Bahnhöfe!"): on some worlds — the
/// chance the tests pin to 1 — the ruin of an old station hall: the working station's ground plan with a caved-in roof,
/// dark lamps and edges, a derelict wagon on the track bed, the dead line beyond, two salvage caches and station notices.
/// The same ruin for the same seed, pinned in the placement records, stamped once, unprotected like every ruin, back after
/// a restart; older generations, a "no" from the chance roll, a server that switched it off, airless, gas and restricted
/// worlds never get one.
/// </summary>
public sealed class RailRuinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_railruin_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const string Planet = "meadowlands";
    private const long Seed = 5;

    private static readonly string[] Locales = { "en", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt", "ru", "tr", "uk", "zh" };

    private SvGameServer Start(string save, long seed, out SqliteWorldRepository repo, string planet = Planet, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            RailRuinChance = 1.0, // a ruin wherever one fits
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    /// <summary>A structure cell of the ruin in station-local coordinates.</summary>
    private static ushort At(SettlementStructure s, int heading, int u, int y, int v)
    {
        var (x, z) = RailRuinGenerator.ToStructure(heading, u, v);
        return s.Get(x, y, z);
    }

    [Fact]
    public void TheGenerator_IsDeterministic_AndBuildsTheFallenHallTheWagonAndTheDeadLine()
    {
        var dark = new HashSet<ushort> { Id("light_white"), Id("strip_light_warm"), Id(RailRules.PylonBlockKey), Id(RailRules.StopBlockKey) };
        int tv = RailStationGenerator.TrackV;
        int hall = RailStationGenerator.Length;
        int roofY = RailRuinGenerator.Height - 1;
        var fingerprints = new HashSet<string>();
        foreach (long seed in new[] { 1L, 42L, 20261002L })
        {
            for (int heading = 0; heading < 4; heading++)
            {
                var s = RailRuinGenerator.Generate(seed, heading, "grass", _content);
                var again = RailRuinGenerator.Generate(seed, heading, "grass", _content);
                Assert.Equal(RailRuinGenerator.Footprint(heading), (s.Width, s.Length));
                Assert.Equal(RailRuinGenerator.Height, s.Height);
                Assert.Equal(RailRuinGenerator.Tier, s.Tier);
                Assert.True(s.Ruined);

                var cells = new System.Text.StringBuilder();
                int seats = 0;
                for (int x = 0; x < s.Width; x++)
                {
                    for (int y = 0; y < s.Height; y++)
                    {
                        for (int z = 0; z < s.Length; z++)
                        {
                            Assert.Equal(s.Get(x, y, z), again.Get(x, y, z));
                            Assert.Equal(s.GetShape(x, y, z), again.GetShape(x, y, z));
                            Assert.DoesNotContain(s.Get(x, y, z), dark); // nothing glows, nothing is a working rail part
                            cells.Append(s.Get(x, y, z)).Append(',');
                            if (FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s.GetShape(x, y, z))))
                            {
                                seats++;
                            }
                        }
                    }
                }

                fingerprints.Add(heading + ":" + cells);

                // Two salvage caches, each on an air cell over something to stand on.
                var caches = s.Markers.Where(m => m.Type == RailRuinGenerator.CacheMarker).ToList();
                Assert.Equal(2, caches.Count);
                foreach (var m in caches)
                {
                    Assert.Equal(0, s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z));
                    Assert.NotEqual(0, s.Get(m.LocalPos.X, m.LocalPos.Y - 1, m.LocalPos.Z));
                }

                // The hall's floor has no holes, but a whole stretch of its roof has caved in, and more of it has holes.
                int roofCells = 0;
                bool cavedStretch = false;
                for (int u = 0; u < hall; u++)
                {
                    int row = 0;
                    for (int v = 0; v < RailRuinGenerator.Width; v++)
                    {
                        Assert.NotEqual(0, At(s, heading, u, 0, v));
                        if (At(s, heading, u, roofY, v) != 0)
                        {
                            row++;
                        }
                    }

                    roofCells += row;
                    cavedStretch |= row == 0;
                }

                Assert.True(cavedStretch, "a stretch of the roof has caved in across the whole hall");
                Assert.True(roofCells < hall * RailRuinGenerator.Width * 0.85, $"the roof keeps {roofCells} cells");

                // The derelict wagon: a whole floor on the track bed, its benches by the windows, an open aisle.
                for (int u = RailRuinGenerator.WagonU; u < RailRuinGenerator.WagonU + RailRuinGenerator.WagonLength; u++)
                {
                    for (int v = tv - 1; v <= tv + 1; v++)
                    {
                        Assert.NotEqual(0, At(s, heading, u, 1, v));
                    }

                    if (u > RailRuinGenerator.WagonU)
                    {
                        Assert.Equal(0, At(s, heading, u, 3, tv)); // head room in the aisle
                    }
                }

                Assert.True(seats >= 4, $"only {seats} seats — the wagon alone has four");

                // The dead line: a standing pylon stump with a dead head, the old embankment under it.
                for (int y = 1; y <= 3; y++)
                {
                    Assert.Equal(Id("rusted_panel"), At(s, heading, RailRuinGenerator.StandingStumpU, y, tv));
                }

                Assert.Equal(Id("broken_machine"), At(s, heading, RailRuinGenerator.StandingStumpU, 4, tv));
                Assert.Equal(Id("broken_machine"), At(s, heading, RailStationGenerator.EndPylonU, 0, tv)); // the dead pylon heads
                Assert.Equal(Id("broken_machine"), At(s, heading, RailStationGenerator.ExitPylonU, 0, tv));
            }
        }

        Assert.True(fingerprints.Count > 4, "different seeds fall differently");
    }

    [Fact]
    public void AGenerationTwentyWorld_GetsOneStation_StampedLootedAndMineable_AndAllOfItSurvivesARestart()
    {
        Vector3i origin;
        int heading;
        Vector3i floorCell;
        var server = Start("ruin", Seed, out var repo);
        using (repo)
        {
            var r = Assert.Single(server.RailRuinsForTest());
            origin = r.Origin;
            heading = r.Heading;
            Assert.Equal(RailRuinGenerator.Footprint(heading), (r.Width, r.Length));
            var rec = Assert.Single(server.PlacementRecordsForTest, x => x.Kind == "rail_ruin");
            Assert.True(rec.Placed);
            Assert.Equal("heading=" + heading, rec.Template);
            Assert.Contains(("rail_ruin", 1, 1), server.StampReportForTest);

            // The blocks are in the world: the wagon's floor on the track bed, the standing stump's dead head.
            (int X, int Z) Cell(int u, int v) => RailRuinGenerator.ToStructure(heading, u, v);
            var wagon = Cell(RailRuinGenerator.WagonU + 1, RailStationGenerator.TrackV);
            floorCell = new Vector3i(origin.X + wagon.X, origin.Y + 1, origin.Z + wagon.Z);
            Assert.False(server.World.GetBlock(floorCell).IsAir);
            var stump = Cell(RailRuinGenerator.StandingStumpU, RailStationGenerator.TrackV);
            Assert.Equal(Id("broken_machine"), server.World.GetBlock(new Vector3i(origin.X + stump.X, origin.Y + 4, origin.Z + stump.Z)).Value);

            // Two salvage caches, each a one-time container whose notices are the station's own lore voice.
            var caches = server.Containers.Where(c => c.Id.StartsWith("loot_rail_ruin_", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, caches.Count);
            foreach (var c in caches)
            {
                Assert.Equal("salvage", c.Kind);
                Assert.NotEmpty(c.Items);
                Assert.Equal("rail_ruin", SvGameServer.LoreSiteOfContainer(c.Id));
                Assert.InRange(c.Position.X - origin.X, 0, r.Width - 1);
                Assert.InRange(c.Position.Z - origin.Z, 0, r.Length - 1);
            }

            // Nothing else stands on it: the settlements and the intercity line keep clear.
            int hw = r.Width / 2 + 1, hl = r.Length / 2 + 1, cx = origin.X + r.Width / 2, cz = origin.Z + r.Length / 2;
            foreach (var s in server.SettlementsForTest)
            {
                bool overlaps = Math.Abs((s.MinX + s.MaxX) / 2 - cx) < (s.MaxX - s.MinX) / 2 + 1 + hw
                                && Math.Abs((s.MinZ + s.MaxZ) / 2 - cz) < (s.MaxZ - s.MinZ) / 2 + 1 + hl;
                Assert.False(overlaps, "a settlement stands on the abandoned station");
            }

            // An admin finds it with /tp railruin: a standing spot inside its footprint.
            var admin = server.AddLocalPlayer("Admin");
            var tp = Assert.Single(server.TeleportTargetsForTest(admin.State.PlayerId), t => t.Kind == "railruin");
            Assert.Equal(1, tp.Number);
            Assert.InRange(tp.Position.X - origin.X, 0f, r.Width);
            Assert.InRange(tp.Position.Z - origin.Z, 0f, r.Length);
            var feet = new Vector3i((int)Math.Floor(tp.Position.X), (int)Math.Floor(tp.Position.Y), (int)Math.Floor(tp.Position.Z));
            Assert.True(server.World.GetBlock(feet).IsAir && server.World.GetBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)).IsAir);

            // Not protected: like every ruin, a drill takes it apart.
            var p = server.AddLocalPlayer("Scavenger");
            p.State.AboardShip = false;
            p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
            p.State.Position = new Vector3f(floorCell.X + 0.5f, floorCell.Y + 1f, floorCell.Z + 0.5f);
            server.MineBlock(p.State.PlayerId, floorCell.X, floorCell.Y, floorCell.Z);
            Assert.True(server.World.GetBlock(floorCell).IsAir, "the wagon's floor can be mined");
            server.Stop();
        }

        // The same seed, a fresh save: the same station in the same place.
        var twin = Start("twin", Seed, out var repoB);
        using (repoB)
        {
            var r = Assert.Single(twin.RailRuinsForTest());
            Assert.Equal((origin, heading), (r.Origin, r.Heading));
            twin.Stop();
        }

        // The first save again: replayed from its record, never stamped twice (the mined cell stays mined), no new caches.
        var reloaded = Start("ruin", Seed, out var repoC);
        using (repoC)
        {
            var r = Assert.Single(reloaded.RailRuinsForTest());
            Assert.Equal((origin, heading), (r.Origin, r.Heading));
            Assert.True(reloaded.World.GetBlock(floorCell).IsAir, "a mined block of the ruin stays mined");
            Assert.Equal(2, reloaded.Containers.Count(c => c.Id.StartsWith("loot_rail_ruin_", StringComparison.Ordinal)));
            reloaded.Stop();
        }
    }

    [Fact]
    public void NoStation_OnAnOlderGeneration_WhenTheChanceSaysNo_OrWhenSwitchedOff()
    {
        // Generation 19: the world never grows one, and nothing is pinned.
        var older = Start("gen19", Seed, out var repo19, configure: c => c.World.TerrainGeneration = 19);
        using (repo19)
        {
            Assert.Empty(older.RailRuinsForTest());
            Assert.DoesNotContain(older.PlacementRecordsForTest, r => r.Kind == "rail_ruin");
            older.Stop();
        }

        // The chance on its own lane: at 0 the world decides "no ruin" and pins it.
        var unlucky = Start("nochance", Seed, out var repo0, configure: c => c.RailRuinChance = 0.0);
        using (repo0)
        {
            Assert.Empty(unlucky.RailRuinsForTest());
            Assert.Contains(unlucky.PlacementRecordsForTest, r => r.Kind == "rail_ruin" && !r.Placed);
            Assert.DoesNotContain(unlucky.Containers, c => c.Id.StartsWith("loot_rail_ruin_", StringComparison.Ordinal));
            unlucky.Stop();
        }

        // Switched off: no decision at all.
        var off = Start("off", Seed, out var repoOff, configure: c => c.PlaceRailRuins = false);
        using (repoOff)
        {
            Assert.Empty(off.RailRuinsForTest());
            Assert.DoesNotContain(off.PlacementRecordsForTest, r => r.Kind == "rail_ruin");
            off.Stop();
        }
    }

    [Fact]
    public void NoStation_OnAirlessGasOrRestrictedWorlds()
    {
        foreach (var (planet, seed) in new[] { ("asteroid", 3L), ("gas_giant", 3L), ("titas", 77L) })
        {
            var server = Start("none_" + planet, seed, out var repo, planet);
            using (repo)
            {
                Assert.Empty(server.RailRuinsForTest());
                Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == "rail_ruin");
                server.Stop();
            }
        }
    }

    [Fact]
    public void TheChanceRoll_IsUniformAcrossSmallWorldSeeds_AndALaneOfItsOwn()
    {
        foreach (string body in new[] { "sys0-p0", "sys3-p1" })
        {
            int below = Enumerable.Range(1, 4000).Count(s => SvGameServer.RailRuinRollForTest(s, body) < 0.35);
            Assert.InRange(below / 4000.0, 0.31, 0.39);
            Assert.Equal(SvGameServer.RailRuinRollForTest(7, body), SvGameServer.RailRuinRollForTest(7, body));

            // Not the intercity line's lane: the two decisions are independent.
            int same = Enumerable.Range(1, 200).Count(s => SvGameServer.RailRuinRollForTest(s, body) == SvGameServer.IntercityRollForTest(s, body));
            Assert.Equal(0, same);
        }
    }

    [Fact]
    public void TheStationNotices_AreItsOwnLoreVoice_InEveryLanguage()
    {
        Assert.Equal("rail_ruin", SvGameServer.LoreSiteOfContainer("loot_rail_ruin_rail_cache_1_2_3"));
        Assert.Equal("ruin", SvGameServer.LoreSiteOfContainer("loot_ruin_loot_4_5_6")); // the fallen towns keep theirs

        var pack = _content.Stories["vega_protocol"];
        var texts = pack.LoreSites.Where(l => l.Site == "rail_ruin").ToList();
        Assert.Equal(3, texts.Count);
        Assert.Contains(texts, l => l.MinKnowledge == 0);

        string data = TestPaths.DataDir();
        foreach (string code in Locales)
        {
            var game = TestLocales.Load(code);
            Assert.False(string.IsNullOrWhiteSpace(game.GetValueOrDefault("poi.rail_ruin")), $"poi.rail_ruin in {code}");
            Assert.False(string.IsNullOrWhiteSpace(game.GetValueOrDefault("ui.lore.site.rail_ruin")), $"ui.lore.site.rail_ruin in {code}");
            var story = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(data, "stories", "vega_protocol", "locales", code + ".json")))!;
            foreach (var l in texts)
            {
                Assert.False(string.IsNullOrWhiteSpace(story.GetValueOrDefault(l.TextKey)), $"{l.TextKey} in {code}");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
