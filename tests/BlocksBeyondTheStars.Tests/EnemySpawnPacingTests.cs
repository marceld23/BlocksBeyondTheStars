// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Enemy spawn pacing (#740, #741). Planet machines: no instant refill after a kill (the timer used to
/// bank time at the cap), far-from-everyone machines despawn. Space: ambient hostiles must not engage
/// the launch point on their own (aggro stays below the spawn distances), destroyed hostiles stay dead
/// across relaunches until the sector re-arms, and repeat launches vary the wave instead of replaying it.
/// </summary>
public sealed class EnemySpawnPacingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EnemySpawnPacingTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_pacing_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, Action<GameRules> configure, out SqliteWorldRepository repo)
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
        configure(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- Planet machines (#740) ----------------

    [Fact]
    public void PlanetMachineKill_IsNotRefilledInstantly()
    {
        var server = Started("norefill", r => { }, out var repo); // default rules: Survival + PlanetEnemies Normal
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Fighter");
            pilot.State.AboardShip = false;

            // Fill to the cap (Normal + solo = 2) on the quick initial cadence.
            for (int i = 0; i < 60 && server.PlanetEnemies.Count < 2; i++)
            {
                server.Tick(0.5);
            }

            Assert.Equal(2, server.PlanetEnemies.Count);

            // Sit at the cap for a while — the OLD bug banked spawn-timer time here, which made the next
            // kill refill on the very next tick.
            for (int i = 0; i < 30; i++)
            {
                server.Tick(0.5);
            }

            // Kill one machine with bare fists (teleport onto it between swings; it may be moving).
            var victim = server.PlanetEnemies[0];
            for (int i = 0; i < 15 && server.PlanetEnemies.Any(e => e.Id == victim.Id); i++)
            {
                var cur = server.PlanetEnemies.FirstOrDefault(e => e.Id == victim.Id);
                if (cur is null)
                {
                    break;
                }

                pilot.State.Position = cur.Position;
                server.AttackEntity("Fighter", victim.Id);
                server.Tick(2.0);
            }

            Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == victim.Id);

            // No instant replacement: refills wait for the post-kill grace plus the slow jittered
            // interval (>= 30 s in total), so 10 s after the kill the slot must still be free.
            for (int i = 0; i < 20; i++)
            {
                server.Tick(0.5);
            }

            Assert.True(server.PlanetEnemies.Count < 2,
                "a killed machine must not be refilled within seconds — refills wait for the slow interval");

            // …but the spawner is only slowed, not dead: the population finds its way back to the cap.
            for (int i = 0; i < 400 && server.PlanetEnemies.Count < 2; i++)
            {
                server.Tick(0.5);
            }

            Assert.Equal(2, server.PlanetEnemies.Count);
        }
    }

    [Fact]
    public void PlanetMachines_FarFromEveryPlayer_Despawn()
    {
        var server = Started("farprune", r => { }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Wanderer");
            pilot.State.AboardShip = false;

            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];

            // Walk far away (well past the 150-block leash): the machine must despawn instead of
            // trailing the player across the planet forever.
            var p = pilot.State.Position;
            pilot.State.Position = new Vector3f(p.X + 200f, p.Y, p.Z);
            server.Tick(0.5);
            server.Tick(0.5);

            Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == enemy.Id);
        }
    }

    // ---------------- Space waves (#741) ----------------

    [Fact]
    public void SpaceHostiles_DoNotEngageTheLaunchPoint()
    {
        var server = Started("optin", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Normal;
            r.AlienUfos = AlienActivity.Normal;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            float hullBefore = server.Ship.Hull;

            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Hostile); // drones + UFO are out there

            // Park at the launch point for a minute. The old aggro radii exceeded the spawn distances
            // (UFO: spawn ~227 < aggro 240), so the UFO hunted the ship the moment it launched.
            for (int i = 0; i < 120; i++)
            {
                server.Tick(0.5);
            }

            Assert.True(server.InSpace("Pilot"));
            foreach (var e in server.SpaceEntitiesFor("Pilot").Where(e => e.Hostile))
            {
                double d = Math.Sqrt((e.Position.X * e.Position.X) + (e.Position.Y * e.Position.Y)
                    + (e.Position.Z * e.Position.Z));
                Assert.True(d > 100, $"an unprovoked ambient hostile must stay away from the launch point (was {d:F0}u)");
            }

            Assert.Equal(hullBefore, server.Ship.Hull); // nobody shot at the parked ship
        }
    }

    [Fact]
    public void SpaceWave_ClearedDrone_StaysCleared_UntilTheSectorRearms()
    {
        var server = Started("cleared", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone, hull 40
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("ship_cannon_1"); // 20 dmg
            server.Ship.Modules.Remove("tractor_beam");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z);
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // 40 -> 20
            server.TickForTest(1.1); // let the cannon's server-enforced cooldown cycle (#694)
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // destroyed
            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Kind == CombatEntityKind.Drone);

            // Land and relaunch: the cleared drone must NOT be back at its post.
            server.LeaveSpace("Pilot");
            server.EnterSpace("Pilot");
            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Kind == CombatEntityKind.Drone);

            // After the replenish window the sector re-arms and the next launch faces a drone again.
            server.LeaveSpace("Pilot");
            server.Tick(250.0);
            server.Tick(250.0); // past the ~8 min replenish window
            server.EnterSpace("Pilot");
            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Kind == CombatEntityKind.Drone);
        }
    }

    [Fact]
    public void SpaceWave_VariesBetweenFlights_AndRunsQuietSometimes()
    {
        var server = Started("variety", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Normal; // 2 drones baseline
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            server.EnterSpace("Pilot"); // flight 0
            var first = server.SpaceEntitiesFor("Pilot")
                .Where(e => e.Kind == CombatEntityKind.Drone).Select(e => e.Position).ToList();
            Assert.Equal(2, first.Count);
            server.LeaveSpace("Pilot");

            server.EnterSpace("Pilot"); // flight 1 — the wave must sit somewhere else now
            var second = server.SpaceEntitiesFor("Pilot")
                .Where(e => e.Kind == CombatEntityKind.Drone).Select(e => e.Position).ToList();
            Assert.Equal(2, second.Count);
            Assert.DoesNotContain(second, p => first.Any(q => q.Equals(p)));
            server.LeaveSpace("Pilot");

            server.EnterSpace("Pilot"); // flight 2
            server.LeaveSpace("Pilot");

            server.EnterSpace("Pilot"); // flight 3 — every 4th flight runs quieter (one drone fewer)
            Assert.Equal(1, server.SpaceEntitiesFor("Pilot").Count(e => e.Kind == CombatEntityKind.Drone));
        }
    }
}
