// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3893: the SPS guide towers in a world — stamped from terrain generation 47 only where an inhabited settlement stands, at
/// least one per save (the first eligible body carries one whatever the roll says, the next ones roll the normal chance), as
/// ONE protected structure overlay with a placement record and a tower record that holds the console, the lamps and the tank;
/// found on the map, by the game event and by the scanner; and the admin's <c>/spstower</c> and <c>/tp tower</c>.
/// </summary>
public sealed class SpsTowerStampTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_spstower_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        _repos.ForEach(r => r.Dispose());
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

    /// <summary>A test world on the meadowlands: no starter ship, no landmark, giant wreck or guardian giant; settlements and
    /// the tower chance as asked.</summary>
    private SvGameServer Start(string name, out RecordingTransport transport, bool settlements, double? chance,
        int generation = WorldDescription.CurrentTerrainGeneration, bool cheats = false, Action<ServerConfig>? tweak = null)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 20261121,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceLandmarks = false,
            PlaceGiantWrecks = false,
            PlaceGuardianGiants = false,
            PlaceFactories = false,
            PlaceMonuments = false,
            PlaceBanditCamps = false,
            PlaceLodges = false,
            PlacePioneers = false,
            PlaceIntercityRail = false,
            PlaceRailRuins = false,
            SpsTowerChance = chance,
            Rules = cheats ? new GameRules { AdminCheats = true, AllowCheatsInSurvival = true } : new GameRules(),
        };
        config.World.TerrainGeneration = generation;
        tweak?.Invoke(config);
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        return server;
    }

    private static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance tower, Vector3i local)
        => WorldConstants.CanonicalBlock(tower.Cell(local), server.World.Circumference);

    private static ushort Id(string block) => Content.GetBlock(block)!.NumericId.Value;

    // ---------------------------------------------------------------- the stamp

    [Fact]
    public void TheFirstInhabitedWorld_CarriesTheSavesTower_ThoughTheRollSaysNo_AndItsRecordHoldsConsoleLampsAndTank()
    {
        // A chance next to nothing: no body would ever roll one — the save's guarantee places it.
        var server = Start("tower_first", out _, settlements: true, chance: 1e-9);
        Assert.True(server.InhabitedSettlementCount > 0, "the test world needs an inhabited settlement");
        var tower = Assert.Single(server.SpsTowersForTest);
        Assert.True(tower.SpsTower);
        Assert.True(tower.Protected);
        Assert.True(tower.Layout.Ruined);
        Assert.Empty(server.LandmarksForTest);
        Assert.True(server.Metadata.SpsTowerGuaranteeSpent);
        Assert.True(server.AnySpsTowerInSave());
        Assert.True(server.SpsTowersPossible);

        // Pinned like a giant wreck: one placement record, one overlay, no block edit per cell.
        var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == SpsTowerRules.RecordKind);
        Assert.True(rec.Placed);
        Assert.Equal((0, tower.Key), (rec.Index, rec.Template));
        var pin = MarkerSettings.Parse(rec.KitLayout);
        Assert.Equal(SvGameServer.LandmarkOverlayStorage, pin.Get(SvGameServer.LandmarkStoragePin));
        Assert.Equal(SpsTowerGenerator.Fingerprint(tower.Template), pin.Get(SvGameServer.GiantWreckFingerprintPin));
        Assert.Equal(SpsTowerGenerator.RuinRevision, pin.GetInt("rev", 0));
        Assert.Single(_repos[^1].ListStructureOverlays(server.World.LocationId), r => r.Key == SpsTowerRules.FeatureKey(0));
        Assert.Contains(server.StampReportForTest, r => r.Kind == SpsTowerRules.RecordKind && r.Requested == 1 && r.Placed == 1);

        // Where it stands: 60 blocks clear of every settlement, under the sky.
        var footprints = server.FootprintsForTest;
        var own = Assert.Single(footprints, f => f.Kind == StructureTypeRules.SpsTower);
        Assert.True(own.Protected);
        Assert.True(SvGameServer.SpsTowerFitsUnderSky(tower.GroundY, SpsTowerRules.HeightAboveGround(tower.Template), server.World.Planet.AtmosphereHeight));
        var (cx, cz, hw, hl) = tower.Rect;
        foreach (var town in footprints.Where(f => f.Kind == StructureTypeRules.Settlement))
        {
            int dx = Math.Abs(WorldConstants.WrapDeltaX(cx - town.Cx, server.World.Circumference)) - hw - town.Hw;
            int dz = Math.Abs(cz - town.Cz) - hl - town.Hl;
            Assert.True(Math.Max(dx, dz) >= SpsTowerRules.SettlementDistance - 2,
                $"the tower stands {Math.Max(dx, dz)} blocks from a settlement's footprint");
        }

        // The tower's record: what the later systems find again without re-deriving the ruin.
        var record = Assert.Single(server.Metadata.SpsTowers);
        Assert.Same(record, Assert.Single(server.SpsTowersOnWorld()));
        Assert.Equal((server.World.LocationId, 0, tower.Key), (record.LocationId, record.Index, record.Template));
        Assert.Equal(server.World.LocationId + "#0", record.SiteId);
        Assert.Equal((tower.Structure.Width, tower.Structure.Length, rec.GroundY), (record.Width, record.Length, record.Y));
        Assert.False(record.Lit);
        Assert.False(record.TankOpened);
        Assert.Equal(string.Empty, record.GuardSettlement);

        Assert.NotNull(record.Console);
        Assert.Equal(Id(SpsTowerRules.ConsoleBlock), server.World.GetBlock(record.Console!.ToVector()).Value);
        Assert.Same(record, server.FindSpsTowerByConsole(record.Console.ToVector()));
        Assert.Null(server.FindSpsTowerByConsole(new Vector3i(record.Console.X + 1, record.Console.Y, record.Console.Z)));

        Assert.True(record.TowerLamps.Count >= 6);
        Assert.All(record.TowerLamps, l => Assert.Equal(Id(SpsTowerRules.LampBlock), server.World.GetBlock(l.ToVector()).Value));
        Assert.True(record.HasGuardPost); // #3897: the save's guaranteed tower always keeps its guard post, whatever its seed rolls
        if (record.HasGuardPost)
        {
            var tank = record.Tank!.ToVector();
            Assert.Equal(Id(SpsTowerRules.TankBlock), server.World.GetBlock(tank).Value);
            Assert.Equal(Id(SpsTowerRules.TankBlock), server.World.GetBlock(new Vector3i(tank.X, tank.Y + 1, tank.Z)).Value);
            Assert.Same(record, server.FindSpsTowerByTank(tank));
            Assert.Same(record, server.FindSpsTowerByTank(new Vector3i(tank.X, tank.Y + 1, tank.Z)));
            Assert.Equal(2, record.TankLamps.Count);
            Assert.All(record.TankLamps, l => Assert.Equal(Id(SpsTowerRules.LampBlock), server.World.GetBlock(l.ToVector()).Value));
        }
        else
        {
            Assert.Null(record.Tank);
            Assert.Empty(record.TankLamps);
        }

        Assert.Same(record, server.NearestSpsTower(SvGameServer.SpsTowerCentre(record)));
    }

    [Fact]
    public void ATower_ComesBackFromItsRecord_AndItsStateIsNeverRewritten()
    {
        const string Name = "tower_replay";
        Vector3i origin;
        string key;
        var server = Start(Name, out _, settlements: true, chance: 1.0);
        {
            var tower = Assert.Single(server.SpsTowersForTest);
            (origin, key) = (tower.Origin, tower.Key);
            var record = Assert.Single(server.Metadata.SpsTowers);
            record.Lit = true; // what the tower's life writes
            record.GuardSettlement = "Brookhaven";
            server.SaveSpsTowers();
            server.Stop();
        }

        _repos[^1].Dispose();
        var again = Start(Name, out _, settlements: true, chance: 1.0);
        var back = Assert.Single(again.SpsTowersForTest);
        Assert.Equal((origin, key), (back.Origin, back.Key));
        Assert.True(back.Protected);
        Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == SpsTowerRules.RecordKind); // replayed, not stamped
        var kept = Assert.Single(again.Metadata.SpsTowers);
        Assert.True(kept.Lit);
        Assert.Equal("Brookhaven", kept.GuardSettlement);
        Assert.NotNull(kept.Console);
        Assert.Single(_repos[^1].ListStructureOverlays(again.World.LocationId), r => r.Key == SpsTowerRules.FeatureKey(0));
    }

    [Theory]
    [InlineData("no_settlement")]
    [InlineData("older_world")]
    [InlineData("host_off")]
    [InlineData("option_off")]
    [InlineData("chance_zero")]
    public void NoTower_WhereNobodyLives_OnAnOlderWorld_OrWithTheSwitchesOff(string which)
    {
        int generation = which == "older_world" ? WorldDescription.SkyLightsGeneration - 1 : WorldDescription.CurrentTerrainGeneration;
        var server = Start("tower_none_" + which, out _, settlements: which != "no_settlement", chance: which == "chance_zero" ? 0.0 : 1.0,
            generation: generation, tweak: c =>
            {
                c.PlaceSpsTowers = which != "host_off";
                c.World.SpsTowers = which == "option_off" ? Frequency.Off : Frequency.Normal;
            });
        Assert.Empty(server.SpsTowersForTest);
        Assert.Empty(server.Metadata.SpsTowers);
        Assert.False(server.Metadata.SpsTowerGuaranteeSpent);
        Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == SpsTowerRules.RecordKind && r.Placed);
        Assert.DoesNotContain(server.FootprintsForTest, f => f.Kind == StructureTypeRules.SpsTower);

        // A body that was asked and said no pins a skip; a world that asks nobody decides nothing (it may ask again later).
        bool decided = which is "no_settlement" or "chance_zero";
        Assert.Equal(decided, server.PlacementRecordsForTest.Any(r => r.Kind == SpsTowerRules.RecordKind));
        Assert.Equal(which is "no_settlement" or "chance_zero", server.SpsTowersPossible);
        var p = server.AddLocalPlayer("Ada");
        Assert.Equal(server.SpsTowersPossible, server.WorldPredicateForTest(p, StoryWorldPredicates.SpsTowers));
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.SpsTowerNear));
    }

    [Fact]
    public void TheGuarantee_IsSpentOnce_EveryLaterBodyRollsTheNormalChance()
    {
        // The start world has nobody home here, so the guarantee waits: the first body with an inhabited settlement gets it.
        var server = Start("tower_guarantee", out _, settlements: true, chance: 1e-9);
        string first = server.World.LocationId;
        Assert.Single(server.SpsTowersOnWorld(first));
        Assert.True(server.Metadata.SpsTowerGuaranteeSpent);

        // Every other body with air: with the same next-to-nothing chance none of them carries a tower — they roll.
        var others = server.LandableBodiesForTest()
            .Where(b => b.Id != first && Content.Planets.TryGetValue(b.PlanetType, out var type)
                        && !type.IsAirless && !type.Void && !type.IsGasWorld && !type.RuinedSettlementsOnly
                        && !b.PlanetType.StartsWith("asteroid", StringComparison.Ordinal))
            .Take(3).ToList();
        Assert.NotEmpty(others);
        int inhabited = 0;
        foreach (var (id, _) in others)
        {
            Assert.True(server.VisitBodyForTest(id));
            Assert.Equal(id, server.World.LocationId);
            inhabited += server.InhabitedSettlementCount > 0 ? 1 : 0;
            Assert.Empty(server.SpsTowersForTest);
            Assert.Empty(server.SpsTowersOnWorld(id));
        }

        Assert.True(inhabited > 0, "none of the visited bodies had an inhabited settlement — the roll was never asked");
        Assert.Single(server.Metadata.SpsTowers); // still the one of the first body
        Assert.Equal(first, server.Metadata.SpsTowers[0].LocationId);
    }

    [Fact]
    public void TheGuaranteedTower_AlwaysKeepsItsGuardPost_EveryLaterTowerRollsItBySeed()
    {
        // #3897: the mission "The Sleeper" needs a tank in reach, so the save's guaranteed tower never loses its guard post.
        // Worlds are tried until one whose guaranteed tower the seed's roll would have left without one (about six in ten).
        bool proven = false;
        for (int i = 0; i < 8 && !proven; i++)
        {
            long seed = 20261121 + i;
            var server = Start("tower_post_" + i, out _, settlements: true, chance: 1.0, tweak: c => c.Seed = seed);
            if (server.SpsTowersForTest.Count == 0)
            {
                server.Stop();
                continue; // nobody lives on this start world
            }

            string first = server.World.LocationId;
            var tower = Assert.Single(server.SpsTowersForTest);
            var record = Assert.Single(server.Metadata.SpsTowers);
            Assert.True(server.Metadata.SpsTowerGuaranteeSpent);
            Assert.True(record.HasGuardPost);
            Assert.NotNull(record.Tank);
            Assert.Equal(2, record.TankLamps.Count);
            var pinned = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == SpsTowerRules.RecordKind && r.Placed);
            Assert.Equal(1, MarkerSettings.Parse(pinned.KitLayout).GetInt(SvGameServer.SpsTowerPostPin, 0)); // pinned: a reload keeps it
            if (SpsTowerGenerator.RollsGuardPost(tower.Template, tower.Seed))
            {
                server.Stop();
                continue; // this seed would have kept the post anyway — it proves nothing
            }

            proven = true;

            // Every later tower of the save is no guaranteed one: its guard post is its seed's roll, as before.
            int later = 0;
            foreach (var (id, type) in server.LandableBodiesForTest())
            {
                if (id == first || later >= 2 || !Content.Planets.TryGetValue(type, out var planet) || planet.IsAirless || planet.Void
                    || planet.IsGasWorld || planet.RuinedSettlementsOnly || type.StartsWith("asteroid", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(server.VisitBodyForTest(id));
                foreach (var other in server.SpsTowersForTest)
                {
                    var rec = Assert.Single(server.SpsTowersOnWorld(id));
                    Assert.Equal(SpsTowerGenerator.RollsGuardPost(other.Template, other.Seed), rec.HasGuardPost);
                    later++;
                }
            }

            Assert.True(later > 0, "no later body carried a tower — the roll of a non-guaranteed tower was never seen");
            server.Stop();
        }

        Assert.True(proven, "none of the tried seeds rolled 'no guard post' for the guaranteed tower");
    }

    [Fact]
    public void TheRoll_IsAHashOfSeedAndBody_AboutEveryFifthToSixthEligibleBody()
    {
        int hits = Enumerable.Range(0, 4000).Count(i => SvGameServer.SpsTowerRolls(20261121, "body" + i, SpsTowerRules.Chance));
        Assert.InRange(hits, 620, 820); // 0.18 of 4000
        Assert.True(SvGameServer.SpsTowerRolls(1, "x", 1.0));
        Assert.False(SvGameServer.SpsTowerRolls(1, "x", 0.0));
    }

    // ---------------------------------------------------------------- the living world (an admin's hand stamp)

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Admin) AdminWorld(string name)
    {
        var server = Start(name, out var transport, settlements: false, chance: 0.0, cheats: true);
        Assert.Empty(server.SpsTowersForTest);
        var admin = server.AddLocalPlayer("Marcel");
        admin.State.Role = PlayerRole.WorldAdmin;
        admin.State.AboardShip = false;
        return (server, transport, admin);
    }

    private static List<object> Run(SvGameServer server, RecordingTransport transport, PlayerSession admin, string? arg)
    {
        int before = transport.Sent.Count;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "spstower", StringArg = arg });
        return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
    }

    [Fact]
    public void TheAdmin_StandsATowerByHand_WithOrWithoutItsGuardPost_AndGoesThereWithTp()
    {
        var (server, transport, admin) = AdminWorld("tower_admin");

        var list = Run(server, transport, admin, "list").OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t.StartsWith("/spstower sps_tower_stump · Fallen Guide Tower · ", StringComparison.Ordinal));
        Assert.Equal(4, list.Count); // the header and the three ruins
        Assert.Contains(Run(server, transport, admin, "no_such_tower").OfType<ActionRejected>(), r => r.Reason == "@srv.spstower.unknown");

        var sent = Run(server, transport, admin, "sps_tower_stump post");
        var tower = Assert.Single(server.SpsTowersForTest);
        Assert.Contains(sent.OfType<LandmarkList>(), l => l.Landmarks.Count(n => n.Kind == SpsTowerRules.MapType) == 1);
        Assert.Contains(sent.OfType<ServerMessage>(), m => m.Text.Contains("Fallen Guide Tower", StringComparison.Ordinal));
        var record = Assert.Single(server.Metadata.SpsTowers);
        Assert.True(record.HasGuardPost);
        Assert.NotNull(record.Tank);
        Assert.Equal(2, record.TankLamps.Count);
        Assert.False(server.Metadata.SpsTowerGuaranteeSpent); // a hand stamp is no roll: the save's guarantee stays

        // The world's stamped block cells equal the layout (the console, the lamps and the tank included).
        int n = 0;
        foreach (var c in GiantWreckTestWorlds.SolidCells(tower))
        {
            Assert.Equal(tower.Structure.Get(c.X, c.Y, c.Z), server.World.GetBlock(At(server, tower, c)).Value);
            n++;
        }

        Assert.True(n > 1000);

        // A second one, well away from the first (a hand stamp clears its own columns).
        admin.State.Position = new Vector3f(admin.State.Position.X + 200, admin.State.Position.Y, admin.State.Position.Z);
        Run(server, transport, admin, "sps_tower_broken nopost");
        Assert.Equal(2, server.SpsTowersForTest.Count);
        // The body decided "none" at the boot (index 0, a skip — nobody lives here), so the hand stamps take the next indices.
        Assert.Equal(new[] { 1, 2 }, server.Metadata.SpsTowers.Select(t => t.Index));
        Assert.False(server.Metadata.SpsTowers[1].HasGuardPost);
        Assert.Null(server.Metadata.SpsTowers[1].Tank);
        Assert.Equal(new[] { 1, 2 }, server.PlacementRecordsForTest.Where(r => r.Kind == SpsTowerRules.RecordKind && r.Placed).Select(r => r.Index));

        // /tp tower [n]
        var targets = server.TeleportTargetsForTest("Marcel").Where(t => t.Kind == "tower").ToList();
        Assert.Equal(new[] { 1, 2 }, targets.Select(t => t.Number));
        Assert.Contains("Fallen Guide Tower", targets[0].Label, StringComparison.Ordinal);
        int before = transport.Sent.Count;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "teleport_to_named", StringArg = "leitturm2" });
        Assert.Empty(transport.Sent.Skip(before).Select(s => s.Msg).OfType<ActionRejected>());
        Assert.True(Math.Abs(admin.State.Position.Z - targets[1].Position.Z) < 2f);
    }

    [Fact]
    public void ATower_IsProtected_ItsChestsAreLootable()
    {
        var (server, transport, admin) = AdminWorld("tower_guard");
        Run(server, transport, admin, "sps_tower_broken post");
        var tower = Assert.Single(server.SpsTowersForTest);
        var player = server.AddLocalPlayer("Ada");
        player.State.Role = PlayerRole.Player;
        player.State.AboardShip = false;

        // Nothing of it can be mined by a player — a chord, the console, a lamp, the tank; an admin passes.
        var record = Assert.Single(server.Metadata.SpsTowers);
        var chord = At(server, tower, GiantWreckTestWorlds.SolidCells(tower).First(c => c.Y > tower.Layout.GroundRow + 8));
        foreach (var cell in new[] { chord, record.Console!.ToVector(), record.TowerLamps[0].ToVector(), record.Tank!.ToVector() })
        {
            Assert.True(server.IsProtectedStructureCellForTest(cell), $"cell {cell.X},{cell.Y},{cell.Z} is not protected");
            Assert.False(server.IsProtectedStructureCellForTest(cell, isAdmin: true));
            ushort was = server.World.GetBlock(cell).Value;
            player.State.Position = new Vector3f(cell.X - 1.5f, cell.Y, cell.Z + 0.5f);
            transport.Sent.Clear();
            server.MineBlockOnce("Ada", cell.X, cell.Y, cell.Z);
            Assert.Contains(transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>(),
                r => r.Reason == "@srv.protect.structure");
            Assert.Equal(was, server.World.GetBlock(cell).Value);
        }

        // Its chests are one-time containers a visitor empties.
        var chests = tower.Structure.Markers.Where(m => m.Type == LandmarkRules.ChestMarker).Select(m => At(server, tower, m.LocalPos)).ToList();
        Assert.True(chests.Count >= 4);
        var containers = server.Containers.Where(c => chests.Contains(c.Position)).ToList();
        Assert.Equal(chests.Count, containers.Count);
        Assert.All(containers, c => Assert.NotEmpty(c.Items));
        var chest = containers[0];
        int carried = player.State.Inventory.Slots.Count(s => s is not null);
        player.State.Position = new Vector3f(chest.Position.X + 0.5f, chest.Position.Y, chest.Position.Z - 1.5f);
        server.LootContainer(player.State.PlayerId, chest.Id);
        Assert.True(player.State.Inventory.Slots.Count(s => s is not null) > carried, "the chest of a protected tower gave nothing");
    }

    [Fact]
    public void ATower_GoesOnTheMap_RaisesFoundWhileAStepListens_AndAScanReadsIt()
    {
        var (server, transport, admin) = AdminWorld("tower_found");
        Run(server, transport, admin, "sps_tower_leaning");
        var tower = Assert.Single(server.SpsTowersForTest);
        var record = Assert.Single(server.Metadata.SpsTowers);
        var p = server.AddLocalPlayer("Ada");
        p.State.AboardShip = false;
        admin.State.Position = new Vector3f(tower.Origin.X - 400, tower.GroundY + 1, tower.Origin.Z); // the admin walks off

        void StandBeside(int gap) => p.State.Position = new Vector3f(
            WorldConstants.WrapX(tower.Origin.X - gap, server.World.Circumference) + 0.5f, tower.GroundY + 1, tower.Origin.Z + (tower.Structure.Length / 2) + 0.5f);

        // The client's list: the silhouette, kind sps_tower, the ruin's key and name.
        var net = Assert.Single(server.LandmarkListForTest(p).Landmarks, l => l.Kind == SpsTowerRules.MapType);
        Assert.Equal(("sps_tower_leaning", "Leaning Guide Tower"), (net.Key, net.Name));
        Assert.True(net.Ruined);
        Assert.Contains(net.Cells, c => c != 0);
        var corner = WorldConstants.CanonicalBlock(new Vector3i(tower.Origin.X + 12, 0, tower.Origin.Z + 12), server.World.Circumference);
        Assert.NotEqual(int.MinValue, server.LandmarkTopChunkForTest(WorldConstants.WorldToChunk(corner.X), WorldConstants.WorldToChunk(corner.Z)));

        var heard = new Listener();
        StandBeside(160);
        server.TickForTest(1.1);
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == SpsTowerRules.MapType);
        Assert.False(server.SpsTowerRevealed(record));
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.SpsTowerNear));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.SpsTowers)); // a tower stands in this save

        StandBeside(90); // in sight: on the map — not yet "found"
        server.TickForTest(1.1);
        var entry = Assert.Single(server.PlanetPoisForTest("Ada"), poi => poi.Type == SpsTowerRules.MapType);
        Assert.Equal("Leaning Guide Tower", entry.Name);
        Assert.Equal(tower.Rect.Cx, entry.X);
        Assert.True(server.SpsTowerRevealed(record));
        Assert.False(server.RevealSpsTower(record)); // already there
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.LandmarkPoiType);

        // Nobody listens: coming near raises nothing.
        StandBeside(30);
        server.TickSpsTowerFoundForTest(p);
        Assert.Empty(heard.Events);
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.SpsTowerNear));

        // A step listens: once per approach.
        server.ListenForSpsTowerFoundForTest(heard);
        server.TickSpsTowerFoundForTest(p);
        server.TickSpsTowerFoundForTest(p);
        Assert.Equal(new[] { (GameEventKeys.SpsTowerFound, record.SiteId) }, heard.Events);
        StandBeside(SpsTowerRules.FoundRange + 30);
        server.TickSpsTowerFoundForTest(p);
        StandBeside(10);
        server.TickSpsTowerFoundForTest(p);
        Assert.Equal(2, heard.Events.Count);

        // Any block of it points the scanner at the tower: a relic's knowledge once, the Codex's landmark ledger.
        var local = GiantWreckTestWorlds.SolidCells(tower).First(c => c.Y > tower.Layout.GroundRow + 10);
        var cell = At(server, tower, local);
        string block = Content.BlockById(new Shared.Primitives.BlockId(tower.Structure.Get(local.X, local.Y, local.Z)))!.Key;
        int knowledge = p.State.KnowledgePoints;
        var first = server.ScanSubject("Ada", "block", block, cell: cell);
        Assert.Equal((SpsTowerRules.MapType, "landmark_sps_tower_leaning", "Leaning Guide Tower", SvGameServer.SpsTowerScanInfo),
            (first.Kind, first.SubjectKey, first.Subject, first.InfoKey));
        Assert.True(first.FirstTime);
        Assert.True(p.State.KnowledgePoints > knowledge);
        Assert.Contains(SvGameServer.SpsTowerLedgerKey(server.World.LocationId, "sps_tower_leaning"), p.State.Scanned);
        Assert.False(server.ScanSubject("Ada", "block", block, cell: cell).FirstTime);
        Assert.Equal("block", server.ScanSubject("Ada", "block", "stone", cell: new Vector3i(cell.X + 300, cell.Y, cell.Z)).Kind);
    }

    private sealed class Listener : IGameEventListener
    {
        public List<(string Key, string Arg)> Events { get; } = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }
}
