// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Suit oxygen on a toxic (non-breathable) world: it drains while the player is outside on the surface, but
/// the ship's life support keeps the air breathable — standing inside the ship refills the reserve and never
/// drains it. (Default rules: Survival + Normal oxygen consumption.)
/// </summary>
public sealed class OxygenTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public OxygenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_oxy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "oxy"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "oxy",
            Seed = 7,
            StartPlanet = "rocky", // rocky = toxic atmosphere → oxygen matters
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Submerged_DrainsSuitOxygen_EvenOnABreathableWorld()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "oxyswim"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "oxyswim",
                Seed = 7,
                StartPlanet = "jungle", // breathable atmosphere
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = true, // a ship so AboardShip is tracked
                PlaceSettlements = false,
                PlaceWrecks = false,
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            var p = server.AddLocalPlayer("Diver");
            server.TickForTest(0.1);
            Assert.True(server.AtmosphereBreathable, "jungle should be breathable for this test");

            // Build a deep pool (stone floor + a tall, wide body of water) far from the ship. The body is
            // generous so an un-piloted player stays submerged through any in-tick drift.
            var water = _content.GetBlock("water")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            int cx = 240, cz = 240, floorY = 100;
            for (int x = cx - 3; x <= cx + 3; x++)
                for (int z = cz - 3; z <= cz + 3; z++)
                {
                    server.World.SetBlock(new Vector3i(x, floorY, z), stone);
                    for (int y = floorY + 1; y <= floorY + 8; y++) server.World.SetBlock(new Vector3i(x, y, z), water);
                }

            var abovePool = new Vector3f(cx + 0.5f, floorY + 14f, cz + 0.5f); // dry air above the pool
            var dive = new Vector3f(cx + 0.5f, floorY + 3f, cz + 0.5f);       // chest-deep in the pool

            // Outside the ship, dry, on a breathable world: oxygen recovers.
            float dry = p.State.Oxygen = 50f;
            for (int i = 0; i < 6; i++) { p.State.Position = abovePool; server.TickForTest(0.5); }
            Assert.True(p.State.Oxygen > dry, $"Oxygen should recover dry on a breathable world (was {p.State.Oxygen}).");

            // Submerged: the suit air now drains despite the breathable atmosphere.
            float wet = p.State.Oxygen = 80f;
            for (int i = 0; i < 6; i++) { p.State.Position = dive; server.TickForTest(0.5); }
            Assert.True(p.State.Oxygen < wet, $"Submerged, suit oxygen should drain even on a breathable world (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void AboardShip_RefillsOxygen_OutsideDrainsIt()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot"); // spawns inside the ship at the heal-tank
            var insidePos = p.State.Position;

            // Inside the ship: oxygen recovers (life support), never drains.
            p.State.Oxygen = 40f;
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.AboardShip, "Standing in the ship should read as aboard.");
            Assert.True(p.State.Oxygen > 40f, $"Oxygen should refill aboard the ship (was {p.State.Oxygen}).");

            // Step outside onto the toxic surface: oxygen now drains.
            p.State.Position = new Vector3f(insidePos.X + 60f, insidePos.Y, insidePos.Z + 60f);
            server.TickForTest(0.1); // let UpdateAboard see the move
            Assert.False(p.State.AboardShip, "Standing well away from the ship should not read as aboard.");

            float before = p.State.Oxygen = 80f;
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < before, $"Oxygen should drain on the toxic surface (was {p.State.Oxygen}).");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
