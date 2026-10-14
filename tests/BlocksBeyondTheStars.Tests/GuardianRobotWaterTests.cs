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
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2554: walking Guardian robots wade one block of water and treat anything deeper as a wall — a chaser stops at the
/// shore, a robot already standing in deep water walks out, and no robot spawns with water on its feet. They used to
/// walk lake beds under water ("a column of water is no wall"). All arenas are synthetic (an iron floor with a pond cut
/// into it), so the tests do not depend on where a seed happens to put its lakes.
/// </summary>
public sealed class GuardianRobotWaterTests : IDisposable
{
    private const int ArenaX = 300; // the player's column, well away from the world's x = 0 seam
    private const int ArenaZ = 40;

    // The pond: four columns wide (x = ArenaX-8 … ArenaX-5), seven long, between the robot (west) and the player (east).
    private const int PondWest = ArenaX - 8;
    private const int PondEast = ArenaX - 5;

    private readonly string _root;
    private readonly GameContent _content;

    public GuardianRobotWaterTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_robotwater_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo, out PlayerSession pilot)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
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
        config.Rules.CreatureAbundance = AlienActivity.Off; // no wildlife sharing the entity ids
        config.Rules.PlanetDrones = false;                  // walking robots only
        config.Rules.PlanetEnemies = AlienActivity.Rare;    // cap 1: the test's own robot fills it, the spawner stays quiet
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        pilot = server.AddLocalPlayer("Prey");
        pilot.State.AboardShip = false; // on foot on the surface — a target the machines hunt
        return server;
    }

    private BlockId Block(string key) => _content.GetBlock(key)!.NumericId;

    /// <summary>An iron strip (x = ArenaX-16 … ArenaX+4) with its top at <c>ground</c> and air above, the player on it at
    /// the east end. Returns the ground Y (the floor's top block; feet stand at ground + 1).</summary>
    private int Arena(SvGameServer server, PlayerSession pilot)
    {
        int ground = server.SurfaceHeightForTest(ArenaX, ArenaZ);
        var iron = Block("iron_wall");
        for (int x = ArenaX - 16; x <= ArenaX + 4; x++)
            for (int z = ArenaZ - 5; z <= ArenaZ + 5; z++)
            {
                for (int y = ground - 4; y <= ground; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), iron);
                }

                for (int y = ground + 1; y <= ground + 10; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }

        pilot.State.Position = new Vector3f(ArenaX + 0.5f, ground + 1, ArenaZ + 0.5f);
        return ground;
    }

    /// <summary>Cuts the pond into the arena: <paramref name="cells"/> from the bottom up, the top one level with the floor.</summary>
    private void Pond(SvGameServer server, int ground, params string[] cells)
    {
        for (int x = PondWest; x <= PondEast; x++)
            for (int z = ArenaZ - 3; z <= ArenaZ + 3; z++)
                for (int i = 0; i < cells.Length; i++)
                {
                    server.World.SetBlock(new Vector3i(x, ground - cells.Length + 1 + i, z), Block(cells[i]));
                }
    }

    private static CombatEntity Robot(SvGameServer server, Vector3f at)
    {
        server.SpawnPlanetEnemyAtForTest(at, CombatEntityKind.Creature, damagePerSecond: 0f);
        return server.PlanetEnemies[^1];
    }

    private static bool InPond(CombatEntity robot)
    {
        int x = (int)Math.Floor(robot.Position.X), z = (int)Math.Floor(robot.Position.Z);
        return x >= PondWest && x <= PondEast && z >= ArenaZ - 3 && z <= ArenaZ + 3;
    }

    [Theory]
    [InlineData("water|water")]                   // two blocks of plain water
    [InlineData("flora_seagrass|water")]          // seagrass on the bed, water above it — still two deep
    [InlineData("flora_kelp|flora_kelp|water")]   // a dense kelp forest: the stalks read standable, the lake is above them
    public void AChasingRobot_StopsAtTheShoreOfADeepPond(string cells)
    {
        string[] pond = cells.Split('|');
        var server = Started("shore" + pond.Length + pond[0], out var repo, out var pilot);
        using (repo)
        {
            int ground = Arena(server, pilot);
            Pond(server, ground, pond);
            var robot = Robot(server, new Vector3f(ArenaX - 12.5f, ground + 1, ArenaZ + 0.5f));
            float startX = robot.Position.X;

            for (int i = 0; i < 200; i++) // 20 s of hunting the player across the pond
            {
                server.TickEnemiesForTest(0.1);
                Assert.False(InPond(robot), $"the robot walked into the deep pond (x {robot.Position.X:F2}, y {robot.Position.Y:F2}) after {i} ticks");
                Assert.True(robot.Position.Y >= ground + 1, $"the robot went under water (y {robot.Position.Y:F2}, floor feet {ground + 1})");
            }

            Assert.True(robot.Position.X > startX + 1f, "the robot should still have closed in on the shore — it hunts, the water stops it");
            Assert.Equal(2, server.MachineWaterDepthForTest(PondWest, ArenaZ, ground - pond.Length + 1));
        }
    }

    [Fact]
    public void AChasingRobot_WadesThroughAOneDeepFord()
    {
        var server = Started("ford", out var repo, out var pilot);
        using (repo)
        {
            int ground = Arena(server, pilot);
            Pond(server, ground, "water");
            var robot = Robot(server, new Vector3f(ArenaX - 12.5f, ground + 1, ArenaZ + 0.5f));

            bool waded = false, crossed = false;
            for (int i = 0; i < 300 && !crossed; i++)
            {
                server.TickEnemiesForTest(0.1);
                waded |= InPond(robot) && Math.Abs(robot.Position.Y - ground) < 0.01f; // feet in the water cell
                crossed = robot.Position.X >= PondEast + 1;
            }

            Assert.True(waded, "the robot should wade the ford with its feet in the water");
            Assert.True(crossed, "one block of water is a ford, not a wall");
        }
    }

    [Fact]
    public void ARobotStandingInDeepWater_WalksOutToTheNearestBank_AndStaysOut()
    {
        var server = Started("walkout", out var repo, out var pilot);
        using (repo)
        {
            int ground = Arena(server, pilot);
            Pond(server, ground, "water", "water");
            var robot = Robot(server, new Vector3f(PondWest + 1.5f, ground - 1, ArenaZ + 0.5f)); // on the bed, two blocks under
            Assert.Equal(2, server.MachineWaterDepthForTest(PondWest + 1, ArenaZ, ground - 1));

            bool outOfWater = false;
            int ticks = 0;
            int searchesBefore = server.WaterExitSearchesForTest;
            for (; ticks < 100 && !outOfWater; ticks++) // 10 s
            {
                server.TickEnemiesForTest(0.1);
                outOfWater = !InPond(robot) && robot.Position.Y >= ground + 1;
            }

            Assert.True(outOfWater, $"the robot should have walked out of the pond (at x {robot.Position.X:F2}, y {robot.Position.Y:F2})");
            Assert.True(robot.Position.X < PondWest, "the west bank was the nearest one");

            // The way out is planned once per column crossed (at most every half second), not on every tick.
            int searches = server.WaterExitSearchesForTest - searchesBefore;
            Assert.InRange(searches, 1, 1 + (ticks / 5));
            Assert.True(searches < ticks, $"{searches} searches in {ticks} ticks — the plan should be kept between columns");

            for (int i = 0; i < 150; i++) // it hunts the player on the far side again — and stops at the shore
            {
                server.TickEnemiesForTest(0.1);
                Assert.False(InPond(robot), "once out, the robot does not walk back into the deep water");
            }
        }
    }

    [Fact]
    public void ARobotInAFloodedPitWithNoWayOut_PowersDown()
    {
        var server = Started("pit", out var repo, out var pilot);
        using (repo)
        {
            int ground = Arena(server, pilot);
            int wx = PondWest + 1, wz = ArenaZ;
            var iron = Block("iron_wall");
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        server.World.SetBlock(new Vector3i(wx, ground - 1, wz), Block("water"));
                        server.World.SetBlock(new Vector3i(wx, ground, wz), Block("water"));
                        continue;
                    }

                    for (int y = ground + 1; y <= ground + 4; y++) // a well whose sheer walls rise four blocks above the water
                    {
                        server.World.SetBlock(new Vector3i(wx + dx, y, wz + dz), iron);
                    }
                }

            var robot = Robot(server, new Vector3f(wx + 0.5f, ground - 1, wz + 0.5f));
            server.TickEnemiesForTest(0.1);

            Assert.DoesNotContain(robot, server.PlanetEnemies);
        }
    }

    [Fact]
    public void AWalkingRobot_NeverSpawnsWithWaterOnItsFeet()
    {
        var server = Started("spawnwet", out var repo, out var pilot);
        using (repo)
        {
            pilot.State.Position = new Vector3f(ArenaX + 0.5f, server.SurfaceHeightForTest(ArenaX, ArenaZ) + 1, ArenaZ + 0.5f);

            // The spawner's first spot is 35 blocks east of the player: flood a 9×9 square around it ONE block deep —
            // wadeable on the way, but no place to appear.
            int sx = ArenaX + 35, sz = ArenaZ;
            int level = server.SurfaceHeightForTest(sx, sz);
            var stone = Block("stone");
            var water = Block("water");
            for (int x = sx - 4; x <= sx + 4; x++)
                for (int z = sz - 4; z <= sz + 4; z++)
                {
                    for (int y = level - 3; y < level; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), stone);
                    }

                    server.World.SetBlock(new Vector3i(x, level, z), water);
                    for (int y = level + 1; y <= level + 10; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                    }
                }

            for (int i = 0; i < 100 && server.PlanetEnemies.Count == 0; i++)
            {
                server.TickEnemiesForTest(0.1); // the first spawn comes after 5 s
            }

            var robot = Assert.Single(server.PlanetEnemies);
            Assert.NotEqual(CombatEntityKind.ScanDrone, robot.Kind);
            float ddx = robot.Position.X - (sx + 0.5f), ddz = robot.Position.Z - (sz + 0.5f);
            Assert.True(ddx * ddx + ddz * ddz > 6f * 6f, $"the robot appeared in the flooded square ({robot.Position.X:F1}, {robot.Position.Z:F1})");
            Assert.Equal(0, server.MachineWaterDepthForTest((int)Math.Floor(robot.Position.X), (int)Math.Floor(robot.Position.Z),
                (int)Math.Floor(robot.Position.Y)));
        }
    }
}
