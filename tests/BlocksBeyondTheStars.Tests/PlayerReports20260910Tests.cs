// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player reports of 2026-09-10 (a builder on her own server, the first round on 2026.9.5). The server-side
/// piece: a creature slept in a walled, torch-lit hall she had carved out of a natural cave — a legitimate
/// cave spawn by every gate the spawner had (#1747). Her hall lies seventy blocks from the base core, past the
/// spawn exclusion and the sealed-room fill, and the walled-yard gate lets cave dwellers through on purpose.
/// </summary>
public sealed class PlayerReports20260910Tests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlayerReports20260910Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_reports0910_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "reports0910"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "reports0910",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- #1747: a room the player built is not a cave ----------------

    /// <summary>A natural pocket under the surface takes a cave dweller; the same pocket with the player's own
    /// floor block under the spot is her room, and the spawner leaves it alone.</summary>
    [Fact]
    public void CaveSpawn_SkipsAPocketThePlayerBuilt()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Lyxette");
            int x = (int)Math.Floor(p.State.Position.X) + 24;
            int z = (int)Math.Floor(p.State.Position.Z) + 24;
            int surface = (int)Math.Floor(p.State.Position.Y);

            var stone = _content.GetBlock("stone")!.NumericId;
            int caveFloor = surface - 20;

            // A solid 5×5 column from well below the pocket up to the surface, with a 3×3×3 hollow inside it.
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                    for (int y = caveFloor - 2; y <= surface; y++)
                    {
                        server.World.SetBlock(new Vector3i(x + dx, y, z + dz), stone);
                    }

            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int y = caveFloor; y <= caveFloor + 2; y++)
                    {
                        server.World.SetBlock(new Vector3i(x + dx, y, z + dz), BlockId.Air);
                    }

            var at = new Vector3f(x + 0.5f, caveFloor, z + 0.5f);
            Assert.True(server.CaveSpawnSpotClearForTest(at), "an untouched pocket on solid ground is a cave");

            // She laid the floor herself: the pocket is a room now.
            repo.SetBlock(server.World.LocationId, new Vector3i(x, caveFloor - 1, z), stone.Value, owner: p.State.PlayerId);
            Assert.False(server.CaveSpawnSpotClearForTest(at), "a pocket with the player's blocks around it is not a cave");
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
            // A leftover temp dir is not worth failing a test run over.
        }
    }
}
