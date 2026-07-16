// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Energy fence + gate: fauna (wild creatures, companions and planet enemies) ignore the voxel world
/// for collision, so ordinary walls cannot pen them — the fence sweep makes exactly the energy_fence
/// and energy_gate blocks read as walls to fauna. Players and NPCs keep the normal Solid rules: the
/// pylon is solid (blocks both), the gate membrane is not (both walk through) — a door with no state.
/// </summary>
public sealed class EnergyFenceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EnergyFenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fence_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fence"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "fence",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Generated surface (first solid cell scanning down) at a column — fauna snap to the generated
    /// surface height, so fences in the movement tests are stood on the real terrain.</summary>
    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    /// <summary>Builds a square fence ring (Chebyshev radius <paramref name="r"/>) around a centre column,
    /// each pylon column spanning a generous band around its own terrain surface.</summary>
    private void BuildFenceRing(SvGameServer server, int cx, int cz, int r)
    {
        var fence = _content.GetBlock("energy_fence")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != r)
                {
                    continue; // ring cells only
                }

                int sy = SurfaceY(server, cx + dx, cz + dz);
                for (int y = sy - 2; y <= sy + 4; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), fence);
                }
            }
        }
    }

    // ---------------- Content wiring ----------------

    [Fact]
    public void FenceAndGate_ContentIsWired()
    {
        // The pylon is a solid block (walls players + NPCs); the gate membrane is not (both walk through).
        var fence = _content.GetBlock("energy_fence");
        var gate = _content.GetBlock("energy_gate");
        Assert.NotNull(fence);
        Assert.NotNull(gate);
        Assert.True(fence!.Solid, "the fence pylon must be solid so players and NPCs are walled too");
        Assert.False(gate!.Solid, "the gate membrane must be non-solid so NPCs walk through it");

        // Placeable items + ungated workshop recipes (door-tier, no blueprint).
        Assert.Equal("energy_fence", _content.GetItem("energy_fence")!.PlacesBlock);
        Assert.Equal("energy_gate", _content.GetItem("energy_gate")!.PlacesBlock);
        foreach (var key in new[] { "energy_fence", "energy_gate" })
        {
            var recipe = _content.Recipes[key];
            Assert.Equal(CraftingStation.Workshop, recipe.Station);
            Assert.True(string.IsNullOrEmpty(recipe.RequiredBlueprint));
            Assert.Contains(recipe.Outputs, o => o.Item == key);
        }
    }

    // ---------------- The fauna fence sweep ----------------

    [Fact]
    public void FenceSweep_BlocksAStepAcrossTheLine_ButNotBeside_It()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            // High above the terrain so the probe cells are otherwise empty air.
            server.World.SetBlock(new Vector3i(3, 300, 0), _content.GetBlock("energy_fence")!.NumericId);

            Assert.True(server.BlockedByEnergyFenceForTest(new Vector3f(0.5f, 300, 0.5f), new Vector3f(6.5f, 300, 0.5f)),
                "a step whose path crosses the pylon cell must be blocked");
            Assert.False(server.BlockedByEnergyFenceForTest(new Vector3f(0.5f, 300, 0.5f), new Vector3f(2.5f, 300, 0.5f)),
                "a step stopping short of the pylon stays free");
            Assert.False(server.BlockedByEnergyFenceForTest(new Vector3f(0.5f, 300, 3.5f), new Vector3f(6.5f, 300, 3.5f)),
                "a step on a parallel line beside the pylon stays free");
        }
    }

    [Fact]
    public void FenceSweep_CoversTheBodyColumn_SoHoppersAndSnapJitterCannotSlipOver()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var fence = _content.GetBlock("energy_fence")!.NumericId;
            var from = new Vector3f(0.5f, 300, 0.5f);
            var to = new Vector3f(6.5f, 300, 0.5f);

            // One buried below the sampled Y (terrain seam) and one at head height both still register…
            server.World.SetBlock(new Vector3i(3, 299, 0), fence);
            Assert.True(server.BlockedByEnergyFenceForTest(from, to));
            server.World.SetBlock(new Vector3i(3, 299, 0), BlockId.Air);

            server.World.SetBlock(new Vector3i(3, 302, 0), fence);
            Assert.True(server.BlockedByEnergyFenceForTest(from, to));
            server.World.SetBlock(new Vector3i(3, 302, 0), BlockId.Air);

            // …but far above the body column a pylon no longer walls the step (you can bridge OVER a pen).
            server.World.SetBlock(new Vector3i(3, 304, 0), fence);
            Assert.False(server.BlockedByEnergyFenceForTest(from, to));
        }
    }

    [Fact]
    public void FenceSweep_TreatsTheGateAsAFence_ButIgnoresOrdinaryBlocks()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var from = new Vector3f(0.5f, 300, 0.5f);
            var to = new Vector3f(6.5f, 300, 0.5f);

            // The gate membrane walls fauna exactly like the pylon (that is what makes it a fauna-proof door)…
            server.World.SetBlock(new Vector3i(3, 300, 0), _content.GetBlock("energy_gate")!.NumericId);
            Assert.True(server.BlockedByEnergyFenceForTest(from, to));

            // …while ordinary solid blocks stay invisible to fauna movement (they never consult voxels).
            server.World.SetBlock(new Vector3i(3, 300, 0), _content.GetBlock("stone")!.NumericId);
            Assert.False(server.BlockedByEnergyFenceForTest(from, to));
        }
    }

    // ---------------- Fauna behaviour through real ticks ----------------

    [Fact]
    [Trait("Category", "Slow")]
    public void AWildCreature_StaysInside_AFencedPen()
    {
        var server = Started("jungle", out var repo); // jungle always has a species roster
        using (repo)
        {
            var p = server.AddLocalPlayer("Keeper"); // keeps the creature simulated (and in despawn range)
            p.State.AboardShip = false;
            int sy = SurfaceY(server, 0, 0);
            p.State.Position = new Vector3f(0.5f, sy + 1, 0.5f);

            BuildFenceRing(server, 0, 0, r: 3);
            string id = server.SpawnCreatureAtForTest(new Vector3f(0.5f, sy + 1, 0.5f));

            for (int i = 0; i < 120; i++)
            {
                server.Tick(0.5); // a minute of wandering
            }

            var c = server.Creatures.First(x => x.Id == id);
            Assert.True(System.Math.Abs(c.Position.X - 0.5f) <= 3.5f && System.Math.Abs(c.Position.Z - 0.5f) <= 3.5f,
                $"the creature escaped the pen (at {c.Position.X:F1},{c.Position.Z:F1})");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ACompanion_StaysInsideThePen_WhileItsOwnerWalksOut()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Owner");
            p.State.AboardShip = false;
            int sy = SurfaceY(server, 0, 0);
            p.State.Position = new Vector3f(0.5f, sy + 1, 0.5f);

            BuildFenceRing(server, 0, 0, r: 3);

            // Register a tame directly (skip the translator ritual) — the reconciler materialises it
            // beside its owner, i.e. inside the pen.
            var sp = server.SpeciesRoster[0];
            p.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "tc_pen",
                HomeBodyId = server.World.LocationId,
                Name = "Penny",
                SpeciesId = sp.Id,
                Species = sp,
            });
            server.Tick(0.5);
            Assert.Contains(server.Creatures, c => c.CompanionId == "tc_pen");

            // The owner steps well outside the pen — the follow steering pulls the companion straight
            // at the fence line, which must hold it (the pen works even on your own animals).
            p.State.Position = new Vector3f(12.5f, SurfaceY(server, 12, 0) + 1, 0.5f);
            for (int i = 0; i < 60; i++)
            {
                server.Tick(0.5);
            }

            var c = server.Creatures.First(x => x.CompanionId == "tc_pen");
            Assert.True(System.Math.Abs(c.Position.X - 0.5f) <= 3.5f && System.Math.Abs(c.Position.Z - 0.5f) <= 3.5f,
                $"the companion slipped out of the pen (at {c.Position.X:F1},{c.Position.Z:F1})");
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void APlanetEnemy_CannotCross_AFenceLine()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Defender");
            p.State.AboardShip = false;
            int psy = SurfaceY(server, 0, 0);
            p.State.Position = new Vector3f(0.5f, psy + 1, 0.5f);

            // A fence wall between the player (x=0) and the machine (x=6), long enough that the roam
            // steering can't just arc around its ends within the test window.
            var fence = _content.GetBlock("energy_fence")!.NumericId;
            for (int z = -8; z <= 8; z++)
            {
                int sy = SurfaceY(server, 3, z);
                for (int y = sy - 2; y <= sy + 4; y++)
                {
                    server.World.SetBlock(new Vector3i(3, y, z), fence);
                }
            }

            server.SpawnPlanetEnemyAtForTest(new Vector3f(6.5f, SurfaceY(server, 6, 0) + 1, 0.5f), 40f);
            var enemy = server.PlanetEnemies[^1]; // hold the reference — the ambient spawner may add more
            for (int i = 0; i < 60; i++)
            {
                server.Tick(0.5);
            }

            Assert.True(enemy.Position.X > 3.5f || System.Math.Abs(enemy.Position.Z - 0.5f) > 8.5f,
                $"the machine crossed the fence line (at {enemy.Position.X:F1},{enemy.Position.Z:F1})");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
