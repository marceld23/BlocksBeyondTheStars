// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The guaranteed start data cube (singleplayer convenience, <see cref="ServerConfig.GuaranteeStartDataCube"/>)
/// must sit a short walk OFF the landing pad — near enough to discover, far enough that it doesn't hijack a
/// new player's first minutes into the minigame right beside the ship (#296, Severin playtest).
/// </summary>
public sealed class StartDataCubeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StartDataCubeTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_cube_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void GuaranteedStartCube_SitsOffThePad_ButWithinDiscoveryRange()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cube"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "cube",
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            GuaranteeStartDataCube = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        using (repo)
        {
            server.AddLocalPlayer("Pilot"); // loads the home body → stamps pads + data cubes

            Assert.True(server.LandingPadCount > 0, "the home body must have landing pads");
            var pad0 = server.LandingPadCenters[0];
            Assert.NotEmpty(server.DataCubeSnapshots); // the guarantee must hold even when the world rolls 0 random cubes

            // The guaranteed cube is the one closest to pad 0 (scattered cubes spawn 60+ blocks out).
            int circumference = server.World.Circumference;
            double nearest = server.DataCubeSnapshots.Min(c =>
            {
                double dx = Math.Abs(c.Pos.X - pad0.X);
                dx = Math.Min(dx, circumference - dx); // X wraps around the planet
                double dz = c.Pos.Z - pad0.Z;
                return Math.Sqrt(dx * dx + dz * dz);
            });

            // Off the pad (radius 8) with margin — not glowing right beside the ship — but still a short,
            // discoverable walk from the spawn.
            Assert.InRange(nearest, 12.0, 40.0);
        }
    }
}
