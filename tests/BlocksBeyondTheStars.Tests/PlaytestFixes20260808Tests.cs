// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.GameServer;
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
/// Regressions for the 2026-08-08 player-feedback batch (issues #833–#841). Each test pins a defect a
/// player actually hit, in the shape they hit it.
/// </summary>
public sealed class PlaytestFixes20260808Tests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlaytestFixes20260808Tests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_pt0808_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo, Action<ServerConfig>? tweak = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        tweak?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>
    /// #833 — a hovering scan-drone must actually hurt the player from the standoff ring it deliberately
    /// keeps. Its AI orbits at 4–10 blocks plus 4 blocks of hover, so the closest it ever comes is ≈5.7
    /// blocks: against the old 4-block melee aura it could never land its damage once, while the client drew
    /// it firing a laser. "Diese Drohne macht mir keinen Schaden."
    /// </summary>
    [Fact]
    public void ScanDrone_DamagesThePlayerFromItsStandoffRing()
    {
        var server = Started("droneDmg", out var repo, c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Normal;
            c.Rules.PlanetDrones = true;
        });

        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false;
            pilot.State.Health = 100f;

            // Flatten a clearing so terrain can't muddy the result: stone floor, open air above. The drone
            // ground-snaps to its hover height each tick, so it needs a real floor under it.
            var at = pilot.State.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y), pz = (int)Math.Floor(at.Z);
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int dx = -16; dx <= 16; dx++)
                for (int dz = -16; dz <= 16; dz++)
                {
                    server.World.SetBlock(new Vector3i(px + dx, py - 1, pz + dz), stone);
                    for (int dy = 0; dy <= 12; dy++)
                    {
                        server.World.SetBlock(new Vector3i(px + dx, py + dy, pz + dz), BlockId.Air);
                    }
                }

            pilot.State.Position = new Vector3f(px + 0.5f, py, pz + 0.5f);

            // Hand-place a drone at its own standoff geometry: 7 blocks out, 4 blocks up — never closer than
            // ≈5.7 blocks in 3D, which is what the old 4-block melee aura could never reach.
            server.SpawnPlanetEnemyAtForTest(
                new Vector3f(px + 7.5f, py + 4f, pz + 0.5f), CombatEntityKind.ScanDrone, damagePerSecond: 2f);

            float before = pilot.State.Health;
            for (int i = 0; i < 20; i++)
            {
                server.TickEnemiesForTest(0.5); // 10 s in range
            }

            Assert.True(pilot.State.Health < before,
                $"a drone at its own standoff ring must deal damage (health stayed {pilot.State.Health})");
        }
    }

    /// <summary>
    /// #834 — a player restored inside solid rock must be dug out. The void guards never caught this: they
    /// ask "is there ground below me", and someone entombed at the world origin has stone in every direction.
    /// The reporter sat at (0.5, −85.5, 0.5) for his whole session, 7550 stone blocks around him.
    /// </summary>
    [Fact]
    public void EntombedPlayer_IsFreedOnJoin()
    {
        var server = Started("entombed", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Buried");
            pilot.State.AboardShip = false;

            // Deep under the terrain of the origin column, then packed solid — stone in every direction, so
            // the void guards see "plenty of ground below me" and wave it through. That is the whole bug.
            var buried = new Vector3f(0.5f, pilot.State.Position.Y - 40f, 0.5f);
            var stone = _content.GetBlock("stone")!.NumericId;
            int bx = (int)Math.Floor(buried.X), by = (int)Math.Floor(buried.Y), bz = (int)Math.Floor(buried.Z);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -2; dy <= 3; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        server.World.SetBlock(new Vector3i(bx + dx, by + dy, bz + dz), stone);
                    }

            pilot.State.Position = buried;
            Assert.True(server.IsEntombedForTest(buried), "the test position must actually be inside solid blocks");
            Assert.False(server.IsInVoidForTest(buried), "…and must NOT be void — that is exactly why it slipped through");

            server.EnsureSafeSpawnForTest(pilot);

            Assert.False(server.IsEntombedForTest(pilot.State.Position),
                $"the player must not still be sealed in blocks (ended at {pilot.State.Position})");
        }
    }

    /// <summary>
    /// Reported alongside the batch: you can hit through walls, and so can enemies. The enemy→player half was
    /// already sightline-gated everywhere; the player→enemy half only checked walls for weapons with reach
    /// over 6 blocks, so every melee weapon swung straight through cover — and a client sending no aim vector
    /// skipped the check altogether.
    /// </summary>
    [Fact]
    public void Attacks_AreBlockedByAWall()
    {
        var server = Started("wallshot", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Shooter");
            pilot.State.AboardShip = false;

            var at = pilot.State.Position;
            var open = new Vector3f(at.X, at.Y + 40f, at.Z); // clear air, so only the wall we build can occlude
            pilot.State.Position = open;

            var target = new Vector3f(open.X + 4f, open.Y, open.Z);
            Assert.True(server.HasLineOfSightForTest(open, target), "baseline: nothing between them yet");

            // A wall slab across the line, tall and wide enough to cover the eye-height segment the check
            // samples (it lifts both ends by 1.5 blocks).
            var stone = _content.GetBlock("stone")!.NumericId;
            int wallX = (int)Math.Floor(open.X) + 2;
            for (int dy = 0; dy <= 4; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(wallX, (int)Math.Floor(open.Y) + dy, (int)Math.Floor(open.Z) + dz), stone);
                }

            Assert.False(server.HasLineOfSightForTest(open, target), "a solid wall must break the sightline");
        }
    }
}
