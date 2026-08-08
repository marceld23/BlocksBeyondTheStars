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
/// Regressions for the entombed-rescue vs. swimmer bug (playtest 2026-08-09): "wenn ich in Wasser
/// hineingehe, das tief genug ist, sinke ich ein, aber ich steige sofort wieder auf". Water carried
/// <c>Solid=true</c> in the data, so a player two blocks deep counted as sealed-in-rock and the 1 Hz
/// void/entombed rescue "dug them out" onto the surface every second. Same pattern for ladders (a
/// climber stands INSIDE the ladder cell) and stacked flora (kelp/vine strands).
/// </summary>
public sealed class SwimVoidRescueTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SwimVoidRescueTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_swim_" + Guid.NewGuid().ToString("N"));
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
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Passable-on-the-client blocks must not carry the Solid flag: the client meshes them without
    /// a collider (you swim into water, stand inside a ladder, walk through a torch/lantern), so the server
    /// treating them as solid is what turned divers and climbers into "entombed" rescue cases.</summary>
    [Theory]
    [InlineData("water")]
    [InlineData("ladder")]
    [InlineData("torch")]
    [InlineData("lantern")]
    public void PassableBlocks_AreNotSolidInTheData(string key)
    {
        Assert.False(_content.GetBlock(key)!.Solid, $"'{key}' has no client collider and must not be Solid");
    }

    /// <summary>The core bug: a swimmer two blocks under is NOT sealed in rock, and the 1 Hz rescue must
    /// leave them exactly where they are instead of teleporting them onto the surface every second.</summary>
    [Fact]
    public void SubmergedPlayer_IsNotEntombed_AndVoidRescueLeavesThemAlone()
    {
        var server = Started("diver", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Diver");
            pilot.State.AboardShip = false;

            // A small pool carved at the spawn column: stone floor, three blocks of water above it.
            var at = pilot.State.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y), pz = (int)Math.Floor(at.Z);
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(px + dx, py - 1, pz + dz), stone);
                    for (int dy = 0; dy <= 2; dy++)
                    {
                        server.World.SetBlock(new Vector3i(px + dx, py + dy, pz + dz), water);
                    }
                }

            var submerged = new Vector3f(px + 0.5f, py + 0.2f, pz + 0.5f); // feet AND head in water
            pilot.State.Position = submerged;

            Assert.False(server.IsEntombedForTest(submerged), "a swimmer inside water is not sealed in rock");
            Assert.False(server.IsInVoidForTest(submerged), "the pool floor is right below — not the void");

            server.RunVoidRescueForTest();

            Assert.Equal(submerged.X, pilot.State.Position.X);
            Assert.Equal(submerged.Y, pilot.State.Position.Y);
            Assert.Equal(submerged.Z, pilot.State.Position.Z);
        }
    }

    /// <summary>Same on join: a save persisted while diving must load back under water, not on the bank.</summary>
    [Fact]
    public void SubmergedPlayer_KeepsTheirPositionOnJoin()
    {
        var server = Started("diverJoin", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("DiverJoin");
            pilot.State.AboardShip = false;

            var at = pilot.State.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y), pz = (int)Math.Floor(at.Z);
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;
            server.World.SetBlock(new Vector3i(px, py - 1, pz), stone);
            for (int dy = 0; dy <= 2; dy++)
            {
                server.World.SetBlock(new Vector3i(px, py + dy, pz), water);
            }

            var submerged = new Vector3f(px + 0.5f, py + 0.2f, pz + 0.5f);
            pilot.State.Position = submerged;

            server.EnsureSafeSpawnForTest(pilot);

            Assert.Equal(submerged.Y, pilot.State.Position.Y);
        }
    }

    /// <summary>A climber stands INSIDE the ladder cells (#803) — two stacked ladders must not read as
    /// "sealed inside blocks" (the rescue would yank them off the ladder once per second).</summary>
    [Fact]
    public void LadderClimber_IsNotEntombed()
    {
        var server = Started("climber", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Climber");
            pilot.State.AboardShip = false;

            var at = pilot.State.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y), pz = (int)Math.Floor(at.Z);
            var stone = _content.GetBlock("stone")!.NumericId;
            var ladder = _content.GetBlock("ladder")!.NumericId;
            server.World.SetBlock(new Vector3i(px, py - 1, pz), stone);
            server.World.SetBlock(new Vector3i(px, py, pz), ladder);
            server.World.SetBlock(new Vector3i(px, py + 1, pz), ladder);

            var climbing = new Vector3f(px + 0.5f, py + 0.2f, pz + 0.5f);
            pilot.State.Position = climbing;

            Assert.False(server.IsEntombedForTest(climbing), "a climber inside ladder cells is not entombed");

            server.RunVoidRescueForTest();
            Assert.Equal(climbing.Y, pilot.State.Position.Y);
        }
    }

    /// <summary>Kelp/vine strands stack into columns and have no client collider — swimming through a kelp
    /// forest (feet and head both in flora cells) must not trigger the rescue either. Flora stays Solid in
    /// the data (the NPC/flora systems key on it), so this pins the rescue's own body-blocking predicate.</summary>
    [Fact]
    public void KelpForestSwimmer_IsNotEntombed()
    {
        var server = Started("kelp", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("KelpSwimmer");
            pilot.State.AboardShip = false;

            var at = pilot.State.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y), pz = (int)Math.Floor(at.Z);
            var stone = _content.GetBlock("stone")!.NumericId;
            var kelp = _content.GetBlock("flora_kelp")!.NumericId;
            server.World.SetBlock(new Vector3i(px, py - 1, pz), stone);
            server.World.SetBlock(new Vector3i(px, py, pz), kelp);
            server.World.SetBlock(new Vector3i(px, py + 1, pz), kelp);

            var inKelp = new Vector3f(px + 0.5f, py + 0.2f, pz + 0.5f);
            pilot.State.Position = inKelp;

            Assert.False(server.IsEntombedForTest(inKelp), "a swimmer inside a kelp strand is not entombed");
        }
    }

    /// <summary>A player genuinely sealed in stone must STILL be rescued — the predicate got stricter, not
    /// the rescue weaker. Pins the original #834 behaviour against this change.</summary>
    [Fact]
    public void GenuinelyEntombedPlayer_IsStillFreed()
    {
        var server = Started("stillBuried", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("StillBuried");
            pilot.State.AboardShip = false;

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
            Assert.True(server.IsEntombedForTest(buried), "stone in feet + head cells must still count as entombed");

            server.EnsureSafeSpawnForTest(pilot);
            Assert.False(server.IsEntombedForTest(pilot.State.Position));
        }
    }

    /// <summary>Water lost its Solid flag, but NPC line-of-sight must keep treating a body of water as an
    /// occluder — no aggro through a lake (the pre-fix behaviour, preserved on purpose).</summary>
    [Fact]
    public void LineOfSight_IsStillBlockedByWater()
    {
        var server = Started("waterlos", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Looker");
            pilot.State.AboardShip = false;

            var at = pilot.State.Position;
            var open = new Vector3f(at.X, at.Y + 40f, at.Z); // clear air, so only our wall can occlude
            var target = new Vector3f(open.X + 4f, open.Y, open.Z);
            Assert.True(server.HasLineOfSightForTest(open, target), "baseline: nothing between them yet");

            var water = _content.GetBlock("water")!.NumericId;
            int wallX = (int)Math.Floor(open.X) + 2;
            for (int dy = 0; dy <= 4; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(wallX, (int)Math.Floor(open.Y) + dy, (int)Math.Floor(open.Z) + dz), water);
                }

            Assert.False(server.HasLineOfSightForTest(open, target), "a wall of water must still break the sightline");
        }
    }
}
