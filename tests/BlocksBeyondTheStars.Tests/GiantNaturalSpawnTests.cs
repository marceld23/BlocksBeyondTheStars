// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The giants' NATURAL spawn path (the other giant tests place them with <c>SummonGiantForTest</c>): a player who stands on
/// foot on a sand-sea world gets a hidden sandworm under the sea within a few ticks of the spawn check — without a thumper,
/// without a command. Written for Marcel's 2026-09-26 question "does the sandworm really appear on sand-sea worlds?".
/// </summary>
public sealed class GiantNaturalSpawnTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GiantNaturalSpawnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_giantspawn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, int seed)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "giantspawn"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "giantspawn",
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(7)]
    public void ASandworm_SpawnsByItself_UnderTheSea_NearAPlayerOnFoot(int seed)
    {
        var server = Started("sand_sea", out var repo, seed);
        using (repo)
        {
            // Somewhere on the sea, walking out from the spawn.
            (int X, int Z) spot = default;
            bool found = false;
            for (int r = 0; r < 1200 && !found; r += 17)
            {
                for (int a = 0; a < 16 && !found; a++)
                {
                    int x = (int)(Math.Cos(a * 0.3927) * r), z = (int)(Math.Sin(a * 0.3927) * r);
                    if (server.IsSandSeaAtForTest(x, z))
                    {
                        spot = (x, z);
                        found = true;
                    }
                }
            }

            Assert.True(found, "no sand sea within 1200 blocks of the spawn — the sea should cover half the world");
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(spot.X + 0.5f, SurfaceY(server, spot.X, spot.Z) + 1, spot.Z + 0.5f);
            Assert.Empty(server.GiantsForTest());

            // The spawn check runs every 2 s while someone is on foot; give it a few rounds.
            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
            }

            var worms = server.GiantsForTest().Where(g => g.Kind == CreatureBodyPlan.Sandworm).ToList();
            Assert.NotEmpty(worms);
            Assert.True(worms.Count <= server.GiantSpeciesForTest().WormCount);
            foreach (var w in worms)
            {
                Assert.Equal("hidden", w.Phase); // under the sand until it hears something
                double dist = Math.Sqrt(Shared.World.WorldConstants.WrapDistanceSquared(w.Position, p.State.Position, server.World.Circumference));
                Assert.InRange(dist, 60.0, 190.0); // 70–170 from the player, plus the roam since (wrap-aware: the spawn sits near x = 0)
                Assert.True(server.IsSandSeaAtForTest((int)Math.Floor(w.Position.X), (int)Math.Floor(w.Position.Z)), "a worm lives under the sea, never under rock");
            }
        }
    }
}
