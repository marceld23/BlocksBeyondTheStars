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
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A landmark in the living world (#3494–#3497, #3499, #3500): the sound emitters and the far silhouette a client is
/// sent, the dens, the map entry, VEGA's hint and the scan, an inhabited landmark's protection and keeper, the admin's
/// stamp, and Soaniri's own landmarks.
/// </summary>
public sealed class LandmarkWorldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landmark_world_" + Guid.NewGuid().ToString("N"));
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

    private static GameContent ContentWith(params StructureTemplate[] landmarks)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(landmarks);
        Assert.Equal(landmarks.Length, content.Landmarks.Count);
        return content;
    }

    private SvGameServer Start(GameContent content, string name, out RecordingTransport transport, string planet = "meadowlands",
        long seed = 20261111, bool cheats = false, double chance = 1.0)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            LandmarkChance = chance,
            Rules = cheats ? new GameRules { AdminCheats = true, AllowCheatsInSurvival = true } : new GameRules(),
        };
        config.World.TerrainGeneration = WorldDescription.CurrentTerrainGeneration;
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Walker(SvGameServer server, string name = "Ada")
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Stands the player <paramref name="gap"/> blocks west of the landmark's footprint, at its ground level.</summary>
    private static void StandBeside(SvGameServer server, PlayerSession p, SvGameServer.LandmarkInstance lm, int gap)
        => p.State.Position = new Vector3f(
            WorldConstants.WrapX(lm.Origin.X - gap, server.World.Circumference) + 0.5f, lm.GroundY + 1, lm.Origin.Z + (lm.Structure.Length / 2) + 0.5f);

    private static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance lm, Vector3i local)
        => WorldConstants.CanonicalBlock(lm.Cell(local), server.World.Circumference);

    [Fact]
    public void AClient_IsSent_TheSoundEmitters_AndTheFarSilhouette()
    {
        var content = ContentWith(LandmarkShapes.Tower(ruin: false));
        var server = Start(content, "lm_list", out _);
        var lm = Assert.Single(server.LandmarksForTest);
        var list = server.LandmarkListForTest(Walker(server));

        var emitter = Assert.Single(list.Emitters);
        Assert.Equal("amb_sounder", emitter.Sound);
        Assert.Equal(60f, emitter.Radius);
        Assert.Equal(0.8f, emitter.Volume, 3);
        var marker = lm.Structure.Markers.Single(m => m.Type == LandmarkRules.SoundMarker);
        var cell = At(server, lm, marker.LocalPos);
        Assert.Equal((cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), (emitter.X, emitter.Y, emitter.Z));

        var net = Assert.Single(list.Landmarks);
        Assert.Equal("test_tower", net.Key);
        Assert.Equal("Test Tower", net.Name);
        Assert.Equal(LandmarkSilhouette.DefaultStep, net.Step);
        Assert.Equal((4, 10, 4), (net.Width, net.Height, net.Length)); // 16 x 40 x 16 blocks, four to a cell
        Assert.Equal(net.Width * net.Height * net.Length, net.Cells.Length);
        Assert.Equal(lm.GroundY, net.Y);
        Assert.Contains(content.GetBlock("stone")!.NumericId.Value, net.Blocks);
        Assert.Equal(net.Blocks.Length, net.Tints.Length);

        // The tower is a hollow shaft with a floor every eight rows: its ring of walls is filled all the way up, its
        // inside only on the floors' levels (every second model row).
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    bool ring = x == 0 || z == 0 || x == 3 || z == 3;
                    byte c = net.Cells[(((x * net.Height) + y) * net.Length) + z];
                    Assert.Equal(ring || y % 2 == 0, c != 0);
                    Assert.InRange(c, (byte)0, (byte)net.Blocks.Length);
                }
            }
        }

        // A world without a landmark sends an empty list (it clears what the client held).
        var none = Start(ContentWith(), "lm_list_none", out _);
        var empty = none.LandmarkListForTest(Walker(none));
        Assert.Empty(empty.Landmarks);
        Assert.Empty(empty.Emitters);
    }

    [Fact]
    public void TheSilhouette_KeepsTheShape_ThinWallsCount_AStrayStoneDoesNot()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var t = new StructureTemplate { Key = "gate", Landmark = true, Width = 24, Height = 16, Length = 4 };
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                bool pier = x < 4 || x >= 20, lintel = y >= 12;
                if (pier || lintel)
                {
                    t.Cells.Add(LandmarkShapes.Block(x, y, 1, "stone")); // one block thick
                }
            }
        }

        t.Cells.Add(LandmarkShapes.Block(10, 0, 3, "gold_block")); // a stray block in the opening
        var model = LandmarkSilhouette.Build(LandmarkGenerator.Build(t, 1, 0, content, "grass"));
        Assert.Equal((6, 4, 1), (model.Width, model.Height, model.Length));
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                bool solid = x == 0 || x == 5 || y == 3;
                Assert.Equal(solid, model.At(x, y, 0) != 0);
            }
        }

        Assert.Equal(new[] { content.GetBlock("stone")!.NumericId.Value }, model.Blocks);

        // Underground rows never show: a mine's model starts at its ground row.
        var mine = LandmarkGenerator.Build(LandmarkShapes.Mine(), 1, 0, content, "grass", ruin: false);
        var mineModel = LandmarkSilhouette.Build(mine);
        Assert.Equal((mine.Structure.Height - mine.GroundRow + 3) / 4, mineModel.Height);
    }

    [Fact]
    public void ADen_BringsItsPackWhenSomebodyComes_KeepsIt_AndWaitsBeforeItReplacesALostAnimal()
    {
        var tower = LandmarkShapes.Tower(ruin: false);
        tower.Cells.RemoveAll(c => c.Id == LandmarkRules.DenMarker);
        tower.Cells.Add(LandmarkShapes.Marker(8, 1, 8, LandmarkRules.DenMarker, "species=native;count=3;radius=10"));
        var server = Start(ContentWith(tower), "lm_den", out _);
        var lm = Assert.Single(server.LandmarksForTest);
        var den = Assert.Single(lm.Dens);
        Assert.Equal((3, 10), (den.Count, den.Radius));
        var p = Walker(server);
        var creatures = (List<CombatEntity>)server.Creatures;
        int Pack() => creatures.Count(c => c.DenId == den.Id);

        // Nobody near: the den sleeps and costs nothing.
        StandBeside(server, p, lm, 300);
        server.TickLandmarksForTest();
        Assert.False(den.Awake);
        Assert.Equal(0, Pack());

        // Somebody comes: one animal a second, up to the count, each beside the den.
        StandBeside(server, p, lm, 12);
        for (int i = 0; i < 6; i++)
        {
            server.TickLandmarksForTest();
        }

        Assert.True(den.Awake);
        Assert.Equal(3, Pack());
        var centre = new Vector3f(den.Cell.X + 0.5f, den.Cell.Y, den.Cell.Z + 0.5f);
        Assert.All(creatures.Where(c => c.DenId == den.Id), c => Assert.True(c.Position.DistanceSquared(centre) <= 11 * 11));

        // A stray is put back; an animal that is gone while the den is awake is not replaced before the cool-down.
        var stray = creatures.First(c => c.DenId == den.Id);
        stray.Position = new Vector3f(centre.X + 40, centre.Y, centre.Z);
        creatures.Remove(creatures.Last(c => c.DenId == den.Id));
        server.TickLandmarksForTest();
        server.TickLandmarksForTest();
        Assert.True(stray.Position.DistanceSquared(centre) <= 11 * 11);
        Assert.Equal(2, Pack());
        Assert.Single(den.ReturnAt);

        den.ReturnAt[0] = 0; // the cool-down ran out
        server.TickLandmarksForTest();
        Assert.Equal(3, Pack());

        // Everybody leaves: the den sleeps, and what wanders off then is not counted as lost.
        StandBeside(server, p, lm, 300);
        server.TickLandmarksForTest();
        Assert.False(den.Awake);
        creatures.RemoveAll(c => c.DenId == den.Id);
        StandBeside(server, p, lm, 12);
        server.TickLandmarksForTest();
        Assert.Empty(den.ReturnAt);
        Assert.Equal(1, Pack());
    }

    [Fact]
    public void ADen_NeverAddsASpeciesToAWorld()
    {
        var server = Start(ContentWith(LandmarkShapes.Tower(ruin: false)), "lm_den_foreign", out _); // species=soundi, on a meadow world
        var lm = Assert.Single(server.LandmarksForTest);
        var den = Assert.Single(lm.Dens);
        var p = Walker(server);
        StandBeside(server, p, lm, 12);
        int species = server.Creatures.Select(c => c.SpeciesId).Distinct().Count();
        for (int i = 0; i < 4; i++)
        {
            server.TickLandmarksForTest();
        }

        Assert.True(den.Awake);
        Assert.Empty(den.Members);
        Assert.DoesNotContain(server.Creatures, c => c.SpeciesId == SoundersFauna.SpeciesIdOf("soundi"));
        Assert.Equal(species, server.Creatures.Select(c => c.SpeciesId).Distinct().Count());
    }

    [Fact]
    public void ALandmark_GoesOnTheMapWhenFound_VegaSpeaksOnce_AndAScanReadsIt()
    {
        var content = ContentWith(LandmarkShapes.Tower());
        var server = Start(content, "lm_found", out _);
        var lm = Assert.Single(server.LandmarksForTest);
        var p = Walker(server);

        StandBeside(server, p, lm, 200);
        server.TickForTest(1.1);
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.LandmarkPoiType);
        Assert.DoesNotContain("vega:hint:landmark_near", p.State.Milestones);

        StandBeside(server, p, lm, 60); // in sight: on the map, VEGA still silent
        server.TickForTest(1.1);
        var entry = Assert.Single(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.LandmarkPoiType);
        Assert.Equal("Test Tower", entry.Name);
        Assert.Equal(lm.Rect.Cx, entry.X);
        Assert.DoesNotContain("vega:hint:landmark_near", p.State.Milestones);

        StandBeside(server, p, lm, 20);
        server.TickForTest(1.1);
        Assert.Contains("vega:hint:landmark_near", p.State.Milestones);

        // Any block of it points the scanner at the landmark: a relic's knowledge, once.
        var wall = At(server, lm, new Vector3i(0, lm.Layout.GroundRow + 1, 5));
        Assert.NotEqual(0, lm.Structure.Get(0, lm.Layout.GroundRow + 1, 5));
        int before = p.State.KnowledgePoints;
        var first = server.ScanSubject("Ada", "block", "stone", cell: wall);
        Assert.Equal("landmark", first.Kind);
        Assert.Equal("landmark_test_tower", first.SubjectKey);
        Assert.Equal("Test Tower", first.Subject);
        Assert.True(first.FirstTime);
        Assert.Equal(8, p.State.KnowledgePoints - before);
        var again = server.ScanSubject("Ada", "block", "stone", cell: wall);
        Assert.False(again.FirstTime);
        Assert.Equal(8, p.State.KnowledgePoints - before);

        // A stone somewhere else is just a stone.
        var elsewhere = server.ScanSubject("Ada", "block", "stone", cell: new Vector3i(wall.X + 200, wall.Y, wall.Z));
        Assert.Equal("block", elsewhere.Kind);
    }

    [Fact]
    public void AnInhabitedLandmark_IsProtected_AndItsKeeperSellsItsOwnTrade_ARuinIsNeither()
    {
        var shop = LandmarkShapes.Tower("test_shop", ruin: false);
        shop.Protected = true;
        shop.Cells.RemoveAll(c => c.Kind == "marker");
        shop.Cells.Add(LandmarkShapes.Marker(8, 1, 8, "vendor", "theme=grocer;inside=1;name=npc.role.grocer"));
        shop.Cells.Add(LandmarkShapes.Marker(4, 1, 8, "npc"));
        var server = Start(ContentWith(shop), "lm_shop", out _);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.True(lm.Protected);
        Walker(server);

        var wall = At(server, lm, new Vector3i(0, 3, 5));
        Assert.True(server.IsLandmarkBlockForTest(wall));
        Assert.True(server.IsSettlementBlock(wall));
        Assert.True(server.BreakProtectedForTest(wall, "Ada"));
        Assert.True(server.IsLandmarkBlockForTest(new Vector3i(wall.X, lm.GroundY - 5, wall.Z)), "the plinth is part of the house");
        Assert.False(server.IsLandmarkBlockForTest(new Vector3i(wall.X, lm.GroundY, lm.Origin.Z - 3)));

        var residents = server.LandmarkResidentsForTest("test_shop");
        Assert.Equal(2, residents.Count);
        var keeper = Assert.Single(residents, r => r.Role == "vendor");
        Assert.Equal(("grocer", true, "npc.role.grocer"), (keeper.Theme, keeper.InsideOnly, keeper.NameKey));
        var post = At(server, lm, lm.Structure.Markers.Single(m => m.Type == "vendor").LocalPos);
        Assert.Equal((post.X + 0.5f, (float)post.Y, post.Z + 0.5f), (keeper.Post.X, keeper.Post.Y, keeper.Post.Z));

        // The ruin of the very same building: free to mine, nobody home.
        var ruin = LandmarkShapes.Tower("test_shop", ruin: true);
        ruin.Protected = true;
        var other = Start(ContentWith(ruin), "lm_shop_ruin", out _);
        var fallen = Assert.Single(other.LandmarksForTest);
        Assert.False(fallen.Protected);
        Assert.False(other.IsLandmarkBlockForTest(At(other, fallen, new Vector3i(0, 1, 5))));
        Assert.Empty(other.LandmarkResidentsForTest("test_shop"));
    }

    [Fact]
    public void TheAdmin_StampsALandmarkByHand_PinnedLikeTheRolledOne()
    {
        var content = ContentWith(LandmarkShapes.Tower());
        var server = Start(content, "lm_admin", out var transport, cheats: true, chance: 0.0);
        Assert.Empty(server.LandmarksForTest);
        var admin = server.AddLocalPlayer("Marcel");
        admin.State.Role = PlayerRole.WorldAdmin;
        admin.State.AboardShip = false;

        List<object> Run(string? arg)
        {
            int before = transport.Sent.Count;
            server.HandleForTest(admin, new AdminCommandIntent { Command = "landmark", StringArg = arg });
            return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
        }

        Assert.Contains(Run("list").OfType<ServerMessage>(), m => m.Text.StartsWith("/landmark test_tower · 16×40×16", StringComparison.Ordinal));
        Assert.Contains(Run("no_such_thing").OfType<ActionRejected>(), r => r.Reason == "@srv.landmark.unknown");

        var sent = Run("test_tower");
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.Contains(sent.OfType<LandmarkList>(), l => l.Landmarks.Length == 1);
        var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind && r.Placed);
        Assert.Equal(1, rec.Index); // index 0 is the body's own roll, which said no
        Assert.Equal(lm.Origin.Z, (int)Math.Floor(admin.State.Position.Z) + 6);

        Run("test_tower");
        Assert.Equal(2, server.LandmarksForTest.Count);
        Assert.Equal(new[] { 1, 2 }, server.PlacementRecordsForTest.Where(r => r.Kind == LandmarkRules.RecordKind && r.Placed).Select(r => r.Index));
        Assert.Equal(2, server.TeleportTargetsForTest("Marcel").Count(t => t.Kind == "landmark"));
    }

    [Fact]
    public void Soaniri_CarriesOnlyTheLandmarksThatNameIt()
    {
        var everywhere = LandmarkShapes.Tower("lm_everywhere");
        var castle = LandmarkShapes.Tower("lm_castle");
        castle.PlanetTypes.Add(SoundersIds.PlanetType);
        castle.Weight = 1;
        everywhere.Weight = 1000; // it would win every draw it took part in
        var server = Start(ContentWith(everywhere, castle), "lm_soaniri", out _, planet: SoundersIds.PlanetType, seed: 3358);
        Assert.True(server.World.Planet.RestrictStructures);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.Equal("lm_castle", lm.Key);

        // Its Soundis live there: the den's species is one Soaniri's roster has.
        var den = Assert.Single(lm.Dens);
        var p = Walker(server);
        StandBeside(server, p, lm, 12);
        for (int i = 0; i < 5; i++)
        {
            server.TickLandmarksForTest();
        }

        Assert.Equal(3, den.Members.Count);
        Assert.All(server.Creatures.Where(c => c.DenId == den.Id), c => Assert.Equal(SoundersFauna.SpeciesIdOf("soundi"), c.SpeciesId));

        // Without a landmark that names it, Soaniri carries none — "every world" does not reach a whitelisted type.
        var none = Start(ContentWith(LandmarkShapes.Tower("lm_everywhere")), "lm_soaniri_none", out _, planet: SoundersIds.PlanetType, seed: 3358);
        Assert.Empty(none.LandmarksForTest);
    }
}
