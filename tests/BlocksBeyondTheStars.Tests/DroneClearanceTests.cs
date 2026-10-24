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
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2950 (a player report): a scan-drone patrolled inside the tunnels under a spaceport the player had raised five or
/// six blocks over the natural ground, its body in the tunnel roof. The spawn probe started at the generator's surface —
/// inside the fill — and took the tunnel for the ground; nothing checked the cells up to the hovering body; nothing
/// lifted a buried drone out. Every test here is red without its part of the fix.
/// </summary>
public sealed class DroneClearanceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DroneClearanceTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_drone_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo)
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
        config.Rules.CreatureAbundance = AlienActivity.Off;
        config.Rules.PlanetDrones = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Per column of the box: a floor on the generator's surface, a three-high tunnel over it, a five-thick roof
    /// (the raised ground) and open air above — the player's spaceport. Returns nothing; the roof top is surface + 8.</summary>
    private void RaisedBaseWithTunnel(SvGameServer server, int x0, int x1, int z0, int z1)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                int nat = server.SurfaceHeightForTest(x, z);
                server.World.SetBlock(new Vector3i(x, nat, z), stone);
                for (int y = nat + 1; y <= nat + 30; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), y >= nat + 4 && y <= nat + 8 ? stone : BlockId.Air);
                }
            }
    }

    private static PlayerSession OnFoot(SvGameServer server, out int px, out int pz)
    {
        var pilot = server.AddLocalPlayer("Lyxette");
        pilot.State.AboardShip = false;
        px = (int)Math.Floor(pilot.State.Position.X);
        pz = (int)Math.Floor(pilot.State.Position.Z);
        pilot.State.Position = new Vector3f(px + 0.5f, pilot.State.Position.Y, pz + 0.5f);
        return pilot;
    }

    private bool BodyInBlock(SvGameServer server, CombatEntity drone)
    {
        var id = server.World.GetBlock(new Vector3i(
            (int)Math.Floor(drone.Position.X), (int)Math.Floor(drone.Position.Y), (int)Math.Floor(drone.Position.Z)));
        return !id.IsAir && _content.BlockById(id)?.Key is not ("tree_leaves" or "water");
    }

    [Fact]
    public void ADrone_SpawnsOverARaisedBase_NotInTheTunnelUnderIt()
    {
        var server = Started("raised", out var repo);
        using (repo)
        {
            var pilot = OnFoot(server, out int px, out int pz);
            int sx = (int)Math.Round(px + 0.5f + 35f); // the first spawn: golden angle 0, 35 blocks east
            RaisedBaseWithTunnel(server, sx - 6, sx + 6, pz - 6, pz + 6);

            Assert.True(server.SpawnPlanetDroneNearForTest(pilot.State));
            var drone = server.PlanetEnemies[^1];
            Assert.Equal(CombatEntityKind.ScanDrone, drone.Kind);
            int dx = (int)Math.Floor(drone.Position.X), dz = (int)Math.Floor(drone.Position.Z);
            Assert.InRange(dx, sx - 1, sx + 1);

            int roofTop = server.SurfaceHeightForTest(dx, dz) + 8;
            Assert.True(drone.Position.Y >= roofTop + 1 + 4 - 0.01f,
                $"the drone spawned at Y {drone.Position.Y:0.0} — the roof tops out at {roofTop}, the tunnel under it at {roofTop - 7}");
            Assert.False(BodyInBlock(server, drone), "the drone spawned inside a block");
        }
    }

    [Theory]
    [InlineData("stone", false)]       // a low roof over a tunnel: the push stops at its edge
    [InlineData("tree_leaves", true)]  // a tree crown at the same height: the drone weaves through it as before
    public void APushedDrone_StopsAtALowRoof_ButPassesATreeCrown(string roofKey, bool passes)
    {
        var server = Started("push_" + roofKey, out var repo);
        using (repo)
        {
            var pilot = OnFoot(server, out int px, out int pz);
            pilot.State.Inventory.SetSlot(0, new ItemStack("shock_gloves", 1));
            pilot.State.SelectedHotbarSlot = 0;

            // A flat floor east of the player, air above it, and from x + 5 on a roof two cells over the floor — the
            // tunnel under it is standable (two cells of headroom), so the old probe let the drone in at its hover.
            var stone = _content.GetBlock("stone")!.NumericId;
            var roof = _content.GetBlock(roofKey)!.NumericId;
            int ground = (int)Math.Floor(pilot.State.Position.Y) - 1;
            int roofX = px + 5;
            for (int x = px - 2; x <= px + 16; x++)
                for (int z = pz - 3; z <= pz + 3; z++)
                {
                    server.World.SetBlock(new Vector3i(x, ground, z), stone);
                    for (int y = ground + 1; y <= ground + 12; y++)
                    {
                        bool isRoof = x >= roofX && y >= ground + 3 && y <= ground + 5;
                        server.World.SetBlock(new Vector3i(x, y, z), isRoof ? roof : BlockId.Air);
                    }
                }

            server.SpawnPlanetEnemyAtForTest(new Vector3f(px + 2.5f, ground + 1 + 4, pz + 0.5f), CombatEntityKind.ScanDrone, 0f);
            var drone = server.PlanetEnemies[^1];
            pilot.State.Position = new Vector3f(px + 0.5f, ground + 1, pz + 0.5f);

            Assert.True(server.KnockbackForTest(pilot.State.PlayerId, drone.Id), "the push must move the drone");
            Assert.False(BodyInBlock(server, drone), $"the push left the drone inside a block at X {drone.Position.X - px:0.0}");
            if (passes)
            {
                Assert.True(drone.Position.X >= roofX, $"the drone stopped at X {drone.Position.X - px:0.0} before the crown");
            }
            else
            {
                Assert.True(drone.Position.X < roofX, $"the drone was pushed under the roof to X {drone.Position.X - px:0.0}");
            }
        }
    }

    [Fact]
    public void ADroneBuriedInTheRoof_IsLiftedOverTheRaisedGround()
    {
        var server = Started("buried", out var repo);
        using (repo)
        {
            var pilot = OnFoot(server, out int px, out int pz);
            RaisedBaseWithTunnel(server, px + 4, px + 12, pz - 4, pz + 4);
            int bx = px + 8, nat = server.SurfaceHeightForTest(bx, pz);

            // Where the report found it: tunnel feet + hover − bob, its body in the roof.
            server.SpawnPlanetEnemyAtForTest(new Vector3f(bx + 0.5f, nat + 1 + 4 - 0.6f, pz + 0.5f), CombatEntityKind.ScanDrone, 0f);
            var drone = server.PlanetEnemies[^1];
            Assert.True(BodyInBlock(server, drone), "the fixture must bury the drone in the roof");

            server.TickEnemiesForTest(0.1);

            Assert.Contains(drone, server.PlanetEnemies);
            Assert.False(BodyInBlock(server, drone), $"the drone is still buried at Y {drone.Position.Y:0.0}");
            Assert.True(drone.Position.Y >= nat + 9 + 4 - 0.01f, $"the drone was not lifted over the roof: Y {drone.Position.Y:0.0}, roof top {nat + 8}");
        }
    }
}
