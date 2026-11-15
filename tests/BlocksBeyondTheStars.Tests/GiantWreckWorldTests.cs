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
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3611 and the admin's tools of #3610: a giant ship wreck in the living world — its silhouette and kind in the client's
/// landmark list, the map entry once a player on foot comes near, VEGA's first-wreck line once per player, the scan that reads
/// it (a relic's knowledge once per body and ship, its own Codex group), the streaming lift; <c>/giantwreck</c> and
/// <c>/tp giantwreck</c>.
/// </summary>
public sealed class GiantWreckWorldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_giantwreck_world_" + Guid.NewGuid().ToString("N"));
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
        int generation = WorldDescription.CurrentTerrainGeneration, double chance = 1.0, bool cheats = false)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(GiantWreckTestWorlds.Config(name, "meadowlands", generation, chance, cheats), content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Walker(SvGameServer server, string name = "Ada")
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Stands the player <paramref name="gap"/> blocks west of the wreck's footprint, at its ground level.</summary>
    private static void StandBeside(SvGameServer server, PlayerSession p, SvGameServer.LandmarkInstance wreck, int gap)
        => p.State.Position = new Vector3f(
            WorldConstants.WrapX(wreck.Origin.X - gap, server.World.Circumference) + 0.5f, wreck.GroundY + 1, wreck.Origin.Z + (wreck.Structure.Length / 2) + 0.5f);

    [Fact]
    public void AWreck_RidesInTheLandmarkList_GoesOnTheMap_VegaSpeaksOnce_AndAScanReadsIt()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        var server = Start(content, "gw_found", out var transport);
        var wreck = Assert.Single(server.GiantWrecksForTest);
        var p = Walker(server);

        // The client's list: the silhouette, kind giant_wreck, the ship's key and name.
        var net = Assert.Single(server.LandmarkListForTest(p).Landmarks);
        Assert.Equal((GiantShipRules.MapType, "sample_hulk", "Sample Hulk"), (net.Kind, net.Key, net.Name));
        Assert.True(net.Ruined);
        Assert.NotEmpty(net.Cells);
        Assert.Contains(net.Cells, c => c != 0);

        // The streaming lift reaches its columns.
        var corner = WorldConstants.CanonicalBlock(new Vector3i(wreck.Origin.X + 4, 0, wreck.Origin.Z + 4), server.World.Circumference);
        Assert.NotEqual(int.MinValue, server.LandmarkTopChunkForTest(WorldConstants.WorldToChunk(corner.X), WorldConstants.WorldToChunk(corner.Z)));

        StandBeside(server, p, wreck, 160); // far away: nothing
        server.TickForTest(1.1);
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == GiantShipRules.MapType);
        Assert.DoesNotContain(SvGameServer.GiantWreckVegaMilestone, p.State.Milestones);

        StandBeside(server, p, wreck, 90); // in sight: on the map, VEGA still silent
        server.TickForTest(1.1);
        var entry = Assert.Single(server.PlanetPoisForTest("Ada"), poi => poi.Type == GiantShipRules.MapType);
        Assert.Equal("Sample Hulk", entry.Name);
        Assert.Equal(wreck.Rect.Cx, entry.X);
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.LandmarkPoiType);
        Assert.DoesNotContain(SvGameServer.GiantWreckVegaMilestone, p.State.Milestones);

        int before = transport.Sent.Count;
        StandBeside(server, p, wreck, 30);
        server.TickForTest(1.1);
        Assert.Contains(SvGameServer.GiantWreckVegaMilestone, p.State.Milestones);
        var line = Assert.Single(transport.Sent.Skip(before).Select(s => s.Msg).OfType<ShipAiLine>(), l => l.LineKey == SvGameServer.GiantWreckVegaLine);
        Assert.Equal("Sample Hulk", line.LineArg);
        Assert.DoesNotContain("vega:hint:landmark_near", p.State.Milestones); // a wreck is no landmark to VEGA

        // Once per player: the next wreck (or the same one again) is no news.
        before = transport.Sent.Count;
        StandBeside(server, p, wreck, 200);
        server.TickForTest(1.1);
        StandBeside(server, p, wreck, 20);
        server.TickForTest(1.1);
        Assert.DoesNotContain(transport.Sent.Skip(before).Select(s => s.Msg).OfType<ShipAiLine>(), l => l.LineKey == SvGameServer.GiantWreckVegaLine);

        // Any block of it points the scanner at the wreck: a relic's knowledge, once per body and ship.
        var local = GiantWreckTestWorlds.SolidCells(wreck).First();
        var cell = GiantWreckTestWorlds.At(server, wreck, local);
        string block = content.BlockById(new Shared.Primitives.BlockId(wreck.Structure.Get(local.X, local.Y, local.Z)))!.Key;
        int knowledge = p.State.KnowledgePoints;
        var first = server.ScanSubject("Ada", "block", block, cell: cell);
        Assert.Equal((GiantShipRules.MapType, "giantwreck_sample_hulk", "Sample Hulk", SvGameServer.GiantWreckScanInfo),
            (first.Kind, first.SubjectKey, first.Subject, first.InfoKey));
        Assert.True(first.FirstTime);
        Assert.Equal(8, p.State.KnowledgePoints - knowledge);
        Assert.Contains(SvGameServer.GiantWreckLedgerKey(server.World.LocationId, "sample_hulk"), p.State.Scanned);
        var again = server.ScanSubject("Ada", "block", block, cell: cell);
        Assert.False(again.FirstTime);
        Assert.Equal(8, p.State.KnowledgePoints - knowledge);

        // A block somewhere else is just a block.
        var elsewhere = server.ScanSubject("Ada", "block", "stone", cell: new Vector3i(cell.X + 300, cell.Y, cell.Z));
        Assert.Equal("block", elsewhere.Kind);
    }

    [Fact]
    public void TheAdmin_LaysAWreckByHand_AsAnOverlay_OnAnyWorld_AndGoesThereWithTp()
    {
        var content = GiantWreckTestWorlds.ContentWithHulk(out _);
        const int Older = WorldDescription.GrownSettlementsGeneration; // no wreck ever rolls here — the admin's stamp still works
        var server = Start(content, "gw_admin", out var transport, generation: Older, chance: 0.0, cheats: true);
        Assert.Empty(server.GiantWrecksForTest);
        var admin = server.AddLocalPlayer("Marcel");
        admin.State.Role = PlayerRole.WorldAdmin;
        admin.State.AboardShip = false;

        List<object> Run(string? arg)
        {
            int before = transport.Sent.Count;
            server.HandleForTest(admin, new AdminCommandIntent { Command = "giantwreck", StringArg = arg });
            return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
        }

        var list = Run("list").OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t.StartsWith("/giantwreck sample_hulk · Sample Hulk · 40×14×16", StringComparison.Ordinal));
        Assert.Contains(Run("no_such_ship").OfType<ActionRejected>(), r => r.Reason == "@srv.giantwreck.unknown");

        var sent = Run("sample_hulk");
        var wreck = Assert.Single(server.GiantWrecksForTest);
        Assert.Contains(sent.OfType<LandmarkList>(), l => l.Landmarks.Count(n => n.Kind == GiantShipRules.MapType) == 1);
        Assert.Equal(wreck.Origin.Z, (int)Math.Floor(admin.State.Position.Z) + 6);
        var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == GiantShipRules.RecordKind && r.Placed);
        Assert.Equal(0, rec.Index); // an older world decided nothing for this body — the hand stamp takes the first index
        Assert.Equal(SvGameServer.LandmarkOverlayStorage, MarkerSettings.Parse(rec.KitLayout).Get(SvGameServer.LandmarkStoragePin));
        var repo = _repos[^1];
        var overlay = GiantWreckTestWorlds.Decode(Assert.Single(repo.ListStructureOverlays(server.World.LocationId), r => r.Key == GiantShipRules.FeatureKey(0)).Data);
        Assert.Empty(GiantWreckTestWorlds.EditsOn(repo, server.World.LocationId, overlay)); // an overlay on any world, never a block edit per cell
        GiantWreckTestWorlds.AssertStandsAsBuilt(server, wreck);

        Run("sample_hulk");
        Assert.Equal(2, server.GiantWrecksForTest.Count);
        Assert.Equal(new[] { 0, 1 }, server.PlacementRecordsForTest.Where(r => r.Kind == GiantShipRules.RecordKind && r.Placed).Select(r => r.Index));
        Assert.Equal(2, server.TeleportTargetsForTest("Marcel").Count(t => t.Kind == "giantwreck" && t.StationId is null));
        Assert.Equal(("giantwreck", 2), SvGameServer.ParseTeleportTargetForTest("riesenwrack 2"));
        Assert.Empty(server.LandmarksForTest);
    }
}
