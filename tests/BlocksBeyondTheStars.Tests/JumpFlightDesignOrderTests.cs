// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3409 (Justus, v2026.11.9: "after a jump the stations have the OLD model", ghost asteroids, the wreck as a brown box):
/// the client carries the arrived system's voxel designs over the flight view's in-flight rebuild by the instance they
/// arrived for — which works only because the server sends the new <see cref="SpaceState"/> BEFORE those designs, all
/// after the old flight's <see cref="SpaceClosed"/>. This pins that order.
/// </summary>
public sealed class JumpFlightDesignOrderTests : IDisposable
{
    private static readonly HashSet<string> SceneryKinds = new(StringComparer.Ordinal) { "asteroid", "station", "wreck", "debris" };

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_jumporder_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // best effort — a locked SQLite file must not fail the test
        }
    }

    [Fact]
    public void InFlightJump_SendsTheNewSpaceState_BeforeTheNewSystemsDesigns()
    {
        var transport = new RecordingTransport();
        var server = NewServer("jump_order", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        server.Ship.Modules.Add("jump_generator");
        server.EnterSpace("Pilot");
        string originSystem = server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId;
        var target = server.Galaxy.Systems.First(s => s.Id != originSystem
            && s.Bodies.Any(b => !string.IsNullOrEmpty(b.PlanetType) && _content.GetPlanet(b.PlanetType!) is not null));
        transport.Sent.Clear();

        server.HyperjumpToSystem("Pilot", target.Id);

        var msgs = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).ToList();
        int closed = msgs.FindIndex(m => m is SpaceClosed);
        int state = msgs.FindIndex(m => m is SpaceState s && s.Hyperjump);
        var designs = msgs.Select((m, i) => (m, i))
            .Where(x => x.m is SpaceShipDesign d && SceneryKinds.Contains(d.Kind ?? string.Empty))
            .Select(x => x.i)
            .ToList();

        Assert.True(closed >= 0, "the jump closes the departure flight");
        Assert.True(state > closed, "…then opens the new one");
        Assert.NotEmpty(designs); // the arrived system has scenery (its station and the launch-ring rocks at least)
        Assert.All(designs, i => Assert.True(i > state, $"a scenery design (message {i}) came before the new SpaceState ({state})"));
    }

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }
}
