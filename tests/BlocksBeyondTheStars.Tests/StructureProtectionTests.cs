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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Protection, settable for every template (terrain generation 39): <c>auto</c> keeps each kind's own rule (a landmark is
/// protected while intact), <c>protected</c> and <c>open</c> overrule it — for a landmark, a settlement template and a station
/// template. A protected box lives in the footprint registry and is asked through one query that refuses a player's mining and
/// placing and lets an admin pass; residents no longer depend on it. A generation-38 world reads none of it.
/// </summary>
public sealed class StructureProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_protection_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Start(GameContent content, string name, out RecordingTransport transport,
        int generation = WorldDescription.SchoolClubLandmarksGeneration, Action<ServerConfig>? configure = null)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 20261111,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            LandmarkChance = 1.0,
        };
        config.World.TerrainGeneration = generation;
        configure?.Invoke(config);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return server;
    }

    private static GameContent LandmarkContent(StructureTemplate landmark)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(new[] { landmark });
        Assert.Single(content.Landmarks);
        return content;
    }

    private static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance lm, Vector3i local)
        => WorldConstants.CanonicalBlock(lm.Cell(local), server.World.Circumference);

    private static List<string> RejectionsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason).ToList();

    // ---------------------------------------------------------------- landmarks

    [Theory]
    [InlineData(false, "", WorldDescription.SchoolClubLandmarksGeneration, true)]           // intact, auto: protected
    [InlineData(true, "", WorldDescription.SchoolClubLandmarksGeneration, false)]           // a ruin, auto: open
    [InlineData(true, "protected", WorldDescription.SchoolClubLandmarksGeneration, true)]   // a ruin the template protects
    [InlineData(false, "open", WorldDescription.SchoolClubLandmarksGeneration, false)]      // intact, but open
    [InlineData(false, "protected", WorldDescription.StructurePipelineGeneration, false)]   // generation 38 reads no word
    public void ALandmarksProtection_FollowsItsTemplate_AndAnAdminPasses(bool ruin, string protection, int generation, bool expected)
    {
        var t = LandmarkShapes.Tower("guarded", ruin: ruin);
        t.Protection = protection;
        var server = Start(LandmarkContent(t), $"guard_{ruin}_{protection}_{generation}", out _, generation);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.Equal(expected, lm.Protected);
        Assert.Equal(expected, server.FootprintsForTest.Single(f => f.Kind == StructureTypeRules.Landmark).Protected);

        var wall = At(server, lm, new Vector3i(0, lm.Layout.GroundRow + 3, 5));
        Assert.Equal(expected, server.IsProtectedStructureCellForTest(wall));
        Assert.Equal(expected, server.BreakProtectedForTest(wall, "Ada"));
        Assert.False(server.IsProtectedStructureCellForTest(wall, isAdmin: true));
        Assert.False(server.BreakProtectedForTest(wall, "Ada", isAdmin: true));
        Assert.Equal(expected, server.IsProtectedStructureCellForTest(new Vector3i(wall.X, lm.GroundY - 5, wall.Z))); // the plinth
        Assert.False(server.IsProtectedStructureCellForTest(new Vector3i(wall.X, lm.GroundY, lm.Origin.Z - 3)));     // beside it
        Assert.False(server.IsLandmarkBlockForTest(wall)); // the old rule holds nothing without the template's legacy flag
    }

    [Fact]
    public void AProtectedLandmark_RefusesAPlayersMiningAndPlacing_AnAdminMinesIt()
    {
        var server = Start(LandmarkContent(LandmarkShapes.Tower("statue", ruin: false)), "guard_hands", out var transport);
        var lm = Assert.Single(server.LandmarksForTest);
        var player = server.AddLocalPlayer("Ada");
        player.State.Role = PlayerRole.Player; // the world's first joiner is its admin (#2486) — a visitor here
        player.State.AboardShip = false;
        player.State.Inventory.Add("stone", 16, 64);
        var wall = At(server, lm, new Vector3i(0, lm.Layout.GroundRow + 3, 5));
        var inside = At(server, lm, new Vector3i(3, lm.Layout.GroundRow + 3, 5));
        Assert.NotEqual(0, server.World.GetBlock(wall).Value);
        Assert.True(server.World.GetBlock(inside).IsAir);
        player.State.Position = new Vector3f(wall.X - 2.5f, wall.Y, wall.Z + 0.5f);

        server.MineBlockOnce("Ada", wall.X, wall.Y, wall.Z);
        Assert.Contains("@srv.protect.structure", RejectionsTo(transport, player));
        Assert.False(server.World.GetBlock(wall).IsAir);

        transport.Sent.Clear();
        server.PlaceBlock("Ada", inside.X, inside.Y, inside.Z, "stone");
        Assert.Contains("@srv.protect.structure", RejectionsTo(transport, player));
        Assert.True(server.World.GetBlock(inside).IsAir);

        player.State.Role = PlayerRole.WorldAdmin;
        transport.Sent.Clear();
        server.MineBlock("Ada", wall.X, wall.Y, wall.Z);
        Assert.DoesNotContain("@srv.protect.structure", RejectionsTo(transport, player));
        Assert.True(server.World.GetBlock(wall).IsAir, "an admin could not mine a protected landmark");
    }

    [Fact]
    public void AnIntactUnprotectedLandmark_StillStaffsItsVendor()
    {
        var shop = LandmarkShapes.Tower("open_shop", ruin: false);
        shop.Protection = LandmarkRules.ProtectionOpen;
        shop.Cells.RemoveAll(c => c.Kind == "marker");
        shop.Cells.Add(LandmarkShapes.Marker(8, 1, 8, "vendor", "theme=grocer;inside=1"));
        var server = Start(LandmarkContent(shop), "guard_open_shop", out _);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.False(lm.Protected);
        Assert.False(server.IsProtectedStructureCellForTest(At(server, lm, new Vector3i(0, 3, 5))));
        var keeper = Assert.Single(server.LandmarkResidentsForTest("open_shop"));
        Assert.Equal(("vendor", "grocer", true), (keeper.Role, keeper.Theme, keeper.InsideOnly));
    }

    // ---------------------------------------------------------------- settlements

    /// <summary>A hand-made inhabited template, 9 × 6 × 9 (the one VillageBelongingsTests stamps).</summary>
    private const string Roundhouse = "stone_roundhouse_home";

    [Theory]
    [InlineData("", false, true, false, true)]          // auto, intact: the settlement rule as always, nothing in the registry
    [InlineData("open", false, false, false, true)]     // open, intact: free for players; fire and the villagers' work keep out
    [InlineData("protected", true, false, true, false)] // a ruin the template protects: the registry holds it
    [InlineData("auto", true, false, false, false)]     // auto, a ruin: open as always
    public void ASettlementTemplatesProtection_OverrulesItsKind(string protection, bool ruined, bool settlementRule, bool registry, bool worldRule)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SettlementTemplateByKey(Roundhouse)!.Protection = protection;
        using var w = new NpcLifeWorld(content, $"guard_village_{protection}_{ruined}");
        const int Ox = NpcLifeWorld.Cx - 20, Oz = NpcLifeWorld.Cz - 20;
        w.Server.StampSettlementForTest(Roundhouse, Ox, w.PadY, Oz, ruined: ruined);
        var cell = new Vector3i(Ox + 4, w.PadY + 2, Oz + 4);
        Assert.Equal(worldRule, w.Server.IsSettlementBlock(cell)); // the world's own processes (fire, NPC work)
        Assert.Equal(registry, w.Server.IsProtectedStructureCellForTest(cell));
        Assert.Equal(settlementRule || registry, w.Server.BreakProtectedForTest(cell, NpcLifeWorld.Owner));
        Assert.Equal(settlementRule, w.Server.BreakProtectedForTest(cell, NpcLifeWorld.Owner, isAdmin: true)); // the settlement rule knows no admin
    }

    // ---------------------------------------------------------------- stations

    /// <summary>Content whose every station tier is one small hand-made station with the given protection (no kits, so a fresh
    /// station always takes it).</summary>
    private static GameContent StationContent(string protection)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var shape = content.StationTemplates.Single(t => t.Key == "pocket_waystation");
        var stations = new List<StructureTemplate>();
        foreach (string tier in new[] { "small", "medium", "large", "huge", "colossal" })
        {
            stations.Add(new StructureTemplate
            {
                Key = "guarded_station_" + tier,
                Name = "Guarded Station",
                Tier = tier,
                Kind = "station",
                Width = shape.Width,
                Height = shape.Height,
                Length = shape.Length,
                Cells = shape.Cells,
                Protection = protection,
            });
        }

        content.SetStructureTemplates(stations, content.SettlementTemplates);
        content.SetStructureKits(Array.Empty<StructureKit>());
        return content;
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("open", false)]
    public void AStationTemplatesProtection_OverrulesTheHullRule(string protection, bool hullProtected)
    {
        var content = StationContent(protection);
        var server = Start(content, "guard_station_" + (protection.Length == 0 ? "auto" : protection), out var transport,
            configure: c =>
            {
                c.PlaceWrecks = false;
                c.World.SpaceStations = Frequency.Frequent;
                c.World.StationTemplateUse = Frequency.Frequent;
                c.Rules.FreeSpaceFlight = true;
            });
        var pilot = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        var contact = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove("Pilot", contact.Position.X, contact.Position.Y, contact.Position.Z - 8f);
        server.BoardStation("Pilot", contact.Id);
        Assert.True(server.InStation("Pilot"));

        var origin = server.StationStampOriginForTest(contact.Id);
        var spot = new Vector3i(origin.X + 1, origin.Y + 1, origin.Z + 1);
        pilot.State.Position = new Vector3f(spot.X + 2.5f, spot.Y, spot.Z + 0.5f);
        server.World.SetBlock(spot, content.GetBlock("stone")!.NumericId);
        transport.Sent.Clear();
        server.MineBlock("Pilot", spot.X, spot.Y, spot.Z);
        Assert.Equal(hullProtected, RejectionsTo(transport, pilot).Contains("@srv.protect.station"));
        Assert.Equal(!hullProtected, server.World.GetBlock(spot).IsAir);
    }
}
