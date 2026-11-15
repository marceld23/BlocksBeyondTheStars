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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The <c>/tp</c> targets of the lost civilizations (#3654) and of the giant wrecks in space (#3655): <c>/tp civ</c> stands the
/// admin in front of a site — on the ground beside a maze on land, on the seabed beside a sunken city, never on the sea —
/// and names it; <c>/tp giantwreck</c> also offers the giant wrecks in space of the system (said "in space" instead of a
/// distance) and BOARDS one, from a planet and from flight alike; in flight only those are offered.
/// </summary>
public sealed class AdminTeleportSitesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_tp_sites_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Start(GameContent content, ServerConfig config, RecordingTransport transport, string? design = null)
    {
        config.Rules.AdminCheats = true;
        config.Rules.AllowCheatsInSurvival = true;
        config.Rules.AllowCheatsInCreative = true;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, config.WorldName));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo) { GiantWreckDesignForTest = design };
        server.Start();
        return server;
    }

    private static PlayerSession Admin(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Marcel", "en");
        p.State.Role = PlayerRole.WorldAdmin;
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Runs <c>/tp &lt;argument&gt;</c> and returns what it sent to the admin.</summary>
    private static List<object> Tp(SvGameServer server, RecordingTransport transport, PlayerSession admin, string argument)
    {
        int before = transport.Sent.Count;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "teleport_to_named", StringArg = argument });
        return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg).ToList();
    }

    private static bool Blocks(SvGameServer server, GameContent content, Vector3i cell)
    {
        var id = server.World.GetBlock(WorldConstants.CanonicalBlock(cell, server.World.Circumference));
        if (id.IsAir)
        {
            return false;
        }

        // The body-blocking rule of the server (a liquid and the flora are passable, so is every non-solid block).
        var def = content.BlockById(id);
        return def is null || (def.Solid && !def.Liquid && def.Key is not ("water" or "lava") && def.Category != "flora");
    }

    private static Vector3i Feet(Vector3f p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z));

    [Fact]
    public void TpCiv_StandsOnTheGroundInFrontOfAMaze_ByItsName_AndEveryAliasMeansIt()
    {
        var content = CivilizationTestWorlds.Content();
        var transport = new RecordingTransport();
        var server = Start(content, CivilizationTestWorlds.Config("tp_civ_land", "meadowlands"), transport);
        var site = Assert.Single(server.CivilizationSitesForTest);
        Assert.Null(site.WaterTop);
        var admin = Admin(server);

        var target = Assert.Single(server.TeleportTargetsForTest(admin.State.PlayerId), t => t.Kind == "civ");
        Assert.StartsWith("civ1 '", target.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("civ.", target.Label, StringComparison.Ordinal); // a localized name, never the key
        Assert.Null(target.StationId);

        var list = Tp(server, transport, admin, string.Empty).OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t.StartsWith("/tp civ1 · " + target.Label + " · ", StringComparison.Ordinal) && t.EndsWith(" m", StringComparison.Ordinal));

        foreach (string word in new[] { "civ", "civ1", "labyrinth", "Zivilisation", "volk 1", "maze" })
        {
            admin.State.Position = new Vector3f(0, 500, 0);
            Tp(server, transport, admin, word);
            Assert.Equal(target.Position, admin.State.Position);
        }

        // In front of the footprint (its south side), standing on solid ground with two free cells for the body.
        var feet = Feet(admin.State.Position);
        Assert.Equal(site.Origin.Z - 3, feet.Z);
        Assert.False(Blocks(server, content, feet));
        Assert.False(Blocks(server, content, new Vector3i(feet.X, feet.Y + 1, feet.Z)));
        Assert.True(Blocks(server, content, new Vector3i(feet.X, feet.Y - 1, feet.Z)), "the target stands on the ground");
        Assert.True(feet.Y <= site.GroundY + 2, $"on the ground beside the maze, not on top of something (y {feet.Y}, ground {site.GroundY})");
    }

    [Fact]
    public void TpCiv_BesideASunkenCity_LandsOnTheSeabed_NotOnTheSea()
    {
        var content = CivilizationTestWorlds.SeaContent("meadowlands");
        var transport = new RecordingTransport();
        var server = Start(content, CivilizationTestWorlds.Config("tp_civ_sea", "meadowlands"), transport);
        var site = Assert.Single(server.CivilizationSitesForTest);
        Assert.NotNull(site.WaterTop);
        var admin = Admin(server);

        Tp(server, transport, admin, "grosstechniker");
        var feet = Feet(admin.State.Position);
        Assert.True(feet.Y < site.WaterTop!.Value, $"under the sea surface (y {feet.Y}, sea {site.WaterTop})");
        Assert.False(Blocks(server, content, feet));
        Assert.False(Blocks(server, content, new Vector3i(feet.X, feet.Y + 1, feet.Z)));
        Assert.True(Blocks(server, content, new Vector3i(feet.X, feet.Y - 1, feet.Z)), "the target stands on the seabed");
    }

    [Fact]
    public void TpCiv_ToASunkenRuin_LandsInItsBowl_BesideThePillar_NotOnTheRim()
    {
        // #3702: the target of a sunken site lies in the ruin on its floor, in the open water — here the bowl around the pillar,
        // never the flooded channel under it (the giant dwelling has one under its eye).
        var content = CivilizationTestWorlds.SeaBowlContent("meadowlands");
        var transport = new RecordingTransport();
        var server = Start(content, CivilizationTestWorlds.Config("tp_civ_bowl", "meadowlands"), transport);
        var site = Assert.Single(server.CivilizationSitesForTest);
        Assert.NotNull(site.WaterTop);
        var admin = Admin(server);

        Tp(server, transport, admin, "civ1");
        var feet = Feet(admin.State.Position);
        var water = content.GetBlock("water")!.NumericId;
        var (cx, cz, _, _) = site.Rect;
        Assert.Equal(WorldConstants.WrapX(cx, server.World.Circumference), WorldConstants.WrapX(feet.X, server.World.Circumference));
        Assert.InRange(cz - feet.Z, 2, 5); // in the ring of the bowl, beside the pillar
        Assert.Equal(site.GroundY - 2, feet.Y); // on the slab, two under the ground: in the bowl, not on its rim
        Assert.Equal(water, server.World.GetBlock(WorldConstants.CanonicalBlock(feet, server.World.Circumference)));
        Assert.True(Blocks(server, content, new Vector3i(feet.X, feet.Y - 1, feet.Z)), "the target stands on the floor");
    }

    // ---------------- giant wrecks in space ----------------

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Admin, string WreckId) SpaceWreckWorld(string name)
    {
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ChunkGenWorkers = 0,
            GiantWreckChance = 0.0, // no wreck on the planet: the only giant wreck is the start system's one in space
        };
        config.Rules.FreeSpaceFlight = true;
        var transport = new RecordingTransport();
        var server = Start(ContentLoader.LoadFromDirectory(TestPaths.DataDir()), config, transport, design: "sample_hulk");
        Assert.Empty(server.GiantWrecksForTest);
        string system = server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;
        return (server, transport, Admin(server), GiantWreckPlacer.BodyId(system, 0));
    }

    [Fact]
    public void TpGiantWreck_OffersTheSystemsWreckInSpace_AndBoardsItFromThePlanet()
    {
        var (server, transport, admin, id) = SpaceWreckWorld("tp_gw_planet");
        var target = Assert.Single(server.TeleportTargetsForTest(admin.State.PlayerId), t => t.Kind == "giantwreck");
        Assert.Equal(id, target.StationId);
        Assert.Contains(server.Galaxy.FindBody(id)!.Name, target.Label, StringComparison.Ordinal);
        Assert.Contains("Sample Hulk", target.Label, StringComparison.Ordinal); // the ship it is

        var list = Tp(server, transport, admin, "list").OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t == "/tp giantwreck1 · " + target.Label + " · in space");

        var sent = Tp(server, transport, admin, "riesenwrack");
        Assert.Empty(sent.OfType<ActionRejected>());
        Assert.True(server.InStation(admin.State.PlayerId), "the teleport boards the wreck in space");
        Assert.EndsWith(id, admin.CurrentLocationId, StringComparison.Ordinal);
        Assert.Contains(sent.OfType<ServerMessage>(), m => m.Text == "@srv.tp.to:" + target.Label);
    }

    [Fact]
    public void InFlight_TpKnowsOnlyTheGiantWrecks_AndBoardsOne()
    {
        var (server, transport, admin, id) = SpaceWreckWorld("tp_gw_flight");
        string pid = admin.State.PlayerId;
        admin.State.AboardShip = true;
        server.EnterSpace(pid);
        Assert.True(server.InSpace(pid));

        Assert.Contains(Tp(server, transport, admin, "pad").OfType<ActionRejected>(), r => r.Reason == "@srv.tp.no_surface_targets");
        Assert.True(server.InSpace(pid), "a surface target is refused in flight");

        var list = Tp(server, transport, admin, string.Empty).OfType<ServerMessage>().Select(m => m.Text).ToList();
        Assert.Contains(list, t => t.StartsWith("/tp giantwreck1 · ", StringComparison.Ordinal) && t.EndsWith(" · in space", StringComparison.Ordinal));
        Assert.DoesNotContain(list, t => t.StartsWith("/tp pad", StringComparison.Ordinal));

        Assert.Empty(Tp(server, transport, admin, "giantwreck1").OfType<ActionRejected>());
        Assert.False(server.InSpace(pid));
        Assert.EndsWith(id, admin.CurrentLocationId, StringComparison.Ordinal);
    }

    [Fact]
    public void InFlight_WithoutAGiantWreck_TpStillSaysLandFirst()
    {
        var config = CivilizationTestWorlds.Config("tp_gw_none", "meadowlands");
        config.Rules.FreeSpaceFlight = true;
        config.World.GiantWrecks = Frequency.Off;
        var transport = new RecordingTransport();
        var server = Start(CivilizationTestWorlds.Content(), config, transport);
        var admin = Admin(server);
        admin.State.AboardShip = true;
        server.EnterSpace(admin.State.PlayerId);
        Assert.True(server.InSpace(admin.State.PlayerId));

        Assert.Contains(Tp(server, transport, admin, string.Empty).OfType<ActionRejected>(), r => r.Reason == "@srv.tp.no_surface_targets");
        Assert.Contains(Tp(server, transport, admin, "giantwreck").OfType<ActionRejected>(), r => r.Reason == "@srv.tp.none_of_kind:giantwreck");
    }
}
