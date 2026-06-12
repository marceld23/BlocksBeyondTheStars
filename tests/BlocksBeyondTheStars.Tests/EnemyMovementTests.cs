using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Enemies MOVE now (they used to stand rooted at their spawn points forever): planet fiends hunt a
/// player in detection range and wander otherwise; space hostiles (drones/UFOs) patrol around their
/// post and chase the ship when it comes close.
/// </summary>
public sealed class EnemyMovementTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EnemyMovementTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_enemy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world, Seed = 9, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false, PlaceSettlements = false, PlaceWrecks = false, ViewDistanceChunks = 1,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE; // hostile NPCs only spawn with combat enabled
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void PlanetEnemies_HuntTheNearbyPlayer()
    {
        var server = Started("hunt", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false; // on foot on the surface — a valid enemy target

            // Tick until the first fiend spawns (the spawner is interval-driven).
            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];

            // Fiends spawn well OUTSIDE their 28-block detection range now (they roam until approached),
            // so walk the prey into range first — the hunt behaviour is what's under test here.
            pilot.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(
                enemy.Position.X + 10f, enemy.Position.Y, enemy.Position.Z);

            var start = enemy.Position;
            double d0 = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - start.X, 2) + Math.Pow(pilot.State.Position.Z - start.Z, 2));

            for (int i = 0; i < 10; i++)
            {
                server.Tick(0.2); // 2 s of hunting
            }

            var now = enemy.Position;
            Assert.False(now.Equals(start), "a fiend with a player in detection range must move");

            double d1 = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - now.X, 2) + Math.Pow(pilot.State.Position.Z - now.Z, 2));
            Assert.True(d1 < d0, $"the fiend should close in on the player (was {d0:F1}, now {d1:F1})");
        }
    }

    [Fact]
    public void PlanetEnemies_SpawnWellOutsideDetectionRange()
    {
        var server = Started("farspawn", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false;

            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];
            double d = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - enemy.Position.X, 2)
                + Math.Pow(pilot.State.Position.Z - enemy.Position.Z, 2));
            Assert.True(d >= 30, $"fiends must spawn outside detection range (28), not ambush-close (was {d:F1})");
        }
    }

    [Fact]
    public void SpaceHostiles_PatrolInsteadOfHangingStill()
    {
        var server = Started("patrol", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Drone);
            Assert.NotNull(drone); // SpaceNpcEnemies = Normal spawns drones
            var start = drone!.Position;

            for (int i = 0; i < 20; i++)
            {
                server.Tick(0.2); // 4 s — far from the ship, so this is the patrol orbit
            }

            Assert.False(drone.Position.Equals(start), "a space hostile must patrol around its post, not hang still");
        }
    }
}
