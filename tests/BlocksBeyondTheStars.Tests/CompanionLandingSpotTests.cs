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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3397 ("when landing my robot Latschuh is always stuck in the ship's floor"): a following companion appears beside its
/// owner on every arrival — but after a landing the owner stands INSIDE the parked hull. Every three-block spot was hull too,
/// and the fallback put the companion on the ground under the ship, inside its floor. The spot search now widens until it
/// lies beside the ship.
/// </summary>
public sealed class CompanionLandingSpotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_complanding_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string name)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        }, _content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    private static TamedCreature Robot(string name) => new()
    {
        Id = "robo",
        Kind = TamedCreature.RobotKind,
        Roams = true,
        Following = true,
        SpeciesId = SvGameServer.RobotSpeciesId,
        Species = SvGameServer.RobotSpecies(),
        Name = name,
        Bond = 100,
        SizeScale = 1f,
    };

    [Fact]
    public void AnOwnerAboardTheLandedShip_FindsTheRobotBesideTheHull_NotInItsFloor()
    {
        var server = Started("comp_aboard");
        var owner = server.AddLocalPlayer("Ada");
        var (origin, size) = server.LandedShipBoundsForTest("Ada");
        owner.State.Position = new Vector3f(origin.X + 1.5f, origin.Y + 1, origin.Z + (size.Z / 2) + 0.5f);
        owner.State.AboardShip = true;
        Assert.True(server.EntityBlockedByShipForTest(owner.State.Position)); // the owner really stands inside the hull
        owner.State.TamedCreatures.Add(Robot("Latschuh"));

        server.ReconcileCompanionsForTest(); // the arrival: companions appear beside their owner

        var robot = Assert.Single(server.CompanionEntitiesForTest("Ada"));
        Assert.False(server.EntityBlockedByShipForTest(robot.Position), $"the robot stands in the hull at {robot.Position}");
        float dx = robot.Position.X - owner.State.Position.X, dz = robot.Position.Z - owner.State.Position.Z;
        Assert.True(dx * dx + dz * dz <= 17f * 17f, "it waits beside the ship, not somewhere far off");
    }

    [Fact]
    public void AnOwnerOnOpenGround_KeepsTheRobotThreeBlocksAway()
    {
        var server = Started("comp_ground");
        var owner = server.AddLocalPlayer("Ada");
        var (origin, size) = server.LandedShipBoundsForTest("Ada");
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(origin.X + size.X + 20.5f, origin.Y + 1, origin.Z + 0.5f); // well clear of the hull
        Assert.False(server.EntityBlockedByShipForTest(owner.State.Position));
        owner.State.TamedCreatures.Add(Robot("Robo"));

        server.ReconcileCompanionsForTest();

        var robot = Assert.Single(server.CompanionEntitiesForTest("Ada"));
        float dx = robot.Position.X - owner.State.Position.X, dz = robot.Position.Z - owner.State.Position.Z;
        Assert.True(dx * dx + dz * dz <= 3.2f * 3.2f, $"the old three-block ring (or the owner's own spot) — at {robot.Position}");
    }
}
