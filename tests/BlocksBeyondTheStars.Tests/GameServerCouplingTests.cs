// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P5 — count-neutral machine/wreck coupling: with the rule on, planet machines cluster at a nearby wreck;
/// with it off they spawn uniformly; either way the **number** that spawn is unchanged (cap-driven).
/// </summary>
public sealed class GameServerCouplingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerCouplingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_coupling_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer StartedWithWreck(bool coupling, out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var name = $"wr_{seed}_{coupling}";
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = name,
                Seed = seed,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceWrecks = true,
            };
            config.Rules.MachineWreckCoupling = coupling;
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            if (server.HasWreck)
            {
                return server;
            }

            server.Stop();
            repo.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        throw new Xunit.Sdk.XunitException("no wreck found on 'rocky' across 60 seeds");
    }

    private static Vector3f Centroid(IReadOnlyList<(string Type, Vector3f Pos)> markers)
        => new((float)markers.Average(m => m.Pos.X), (float)markers.Average(m => m.Pos.Y), (float)markers.Average(m => m.Pos.Z));

    // Wrap-aware horizontal distance — the world wraps east–west on a PER-WORLD circumference (not the
    // 6000 default), and enemy X is normalised into [0, circ), so a naive delta is off by ~circumference.
    private static float Dist2D(Vector3f a, Vector3f b, int circ)
    {
        float dx = (float)BlocksBeyondTheStars.Shared.World.WorldConstants.WrapDeltaX(a.X - b.X, circ);
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Stand at the wreck and tick in 1 s steps until the FIRST machine spawns (so it hasn't drifted
    /// from its spawn point yet), returning the wreck centroid.</summary>
    private static Vector3f SpawnOneAtWreck(SvGameServer server)
    {
        var wreck = Centroid(server.WreckMarkers);
        var p = server.AddLocalPlayer("Scout");
        p.State.AboardShip = false;
        for (int i = 0; i < 60 && server.PlanetEnemies.Count == 0; i++)
        {
            p.State.Position = wreck; // keep the player glued to the wreck each spawn tick
            server.Tick(1.0);
            p.State.Health = 100f;
        }

        return wreck;
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Machines_cluster_at_a_wreck_when_coupling_is_on()
    {
        var server = StartedWithWreck(coupling: true, out var repo);
        using (repo)
        {
            int circ = server.World.Circumference;
            var wreck = SpawnOneAtWreck(server);
            Assert.NotEmpty(server.PlanetEnemies);
            Assert.Contains(server.PlanetEnemies, e => Dist2D(e.Position, wreck, circ) <= 22f);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Machines_do_not_cluster_at_a_wreck_when_coupling_is_off()
    {
        var server = StartedWithWreck(coupling: false, out var repo);
        using (repo)
        {
            int circ = server.World.Circumference;
            var wreck = SpawnOneAtWreck(server);
            Assert.NotEmpty(server.PlanetEnemies);
            // off → uniform golden-angle 35–50 blocks from the player (who stands at the wreck) → not clustered
            Assert.DoesNotContain(server.PlanetEnemies, e => Dist2D(e.Position, wreck, circ) <= 22f);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Coupling_does_not_change_how_many_machines_spawn()
    {
        int on = FillToCap(coupling: true);
        int off = FillToCap(coupling: false);
        Assert.Equal(off, on); // count is cap-driven; coupling changes only WHERE, not HOW MANY
    }

    private int FillToCap(bool coupling)
    {
        var server = StartedWithWreck(coupling, out var repo);
        using (repo)
        {
            var wreck = Centroid(server.WreckMarkers);
            var p = server.AddLocalPlayer("Scout");
            p.State.AboardShip = false;
            p.State.Position = wreck;
            for (int i = 0; i < 25; i++)
            {
                server.Tick(6.0);
                p.State.Health = 100f; // keep the player a valid spawn target so the cap fills in both cases
            }

            return server.PlanetEnemies.Count;
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
