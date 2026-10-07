// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Right-click gadgets (item 36): the field medkit area heal, the stasis projector, the terrain
/// blaster — all reusable, suit-energy + cooldown gated.</summary>
public sealed class GadgetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GadgetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_gadget_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "gadget"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "gadget",
            Seed = 3,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FieldMedkit_HealsTheUserAndNearbyAllies_ButNotTheFarAway()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var user = server.AddLocalPlayer("Medic");
            var near = server.AddLocalPlayer("Buddy");
            var far = server.AddLocalPlayer("Stranger");

            user.State.Inventory.Add("field_medkit", 1, 1);
            user.State.Health = 40f;
            near.State.Health = 50f;
            far.State.Health = 50f;

            // Place the user + nearby ally together; the stranger well outside the heal radius (6 blocks).
            user.State.Position = new Vector3f(0f, 64f, 0f);
            near.State.Position = new Vector3f(2f, 64f, 1f);
            far.State.Position = new Vector3f(40f, 64f, 0f);

            server.UseGadgetForTest("Medic", "field_medkit", user.State.Position);

            Assert.True(user.State.Health > 40f, "the user heals themselves");
            Assert.True(near.State.Health > 50f, "a nearby ally is healed");
            Assert.Equal(50f, far.State.Health);                 // out of range — untouched
            Assert.True(user.State.SuitEnergy < 100f);           // use cost suit energy
            Assert.True(server.GadgetCooldownForTest("Medic", "field_medkit") > 0); // now on cooldown
        }
    }

    [Fact]
    public void TerrainBlaster_ClearsASphereOfTerrain_WithNoLoot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Demo");
            p.State.AboardShip = false;
            p.State.Inventory.Add("terrain_blaster", 1, 1);

            var stone = _content.GetBlock("stone")!.NumericId;
            var center = new Vector3i(12, 40, 12);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        server.World.SetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), stone);
                    }

            int stoneBefore = p.State.Inventory.CountOf("stone");
            p.State.Position = Beside(center); // #2376: within reach of the aim point

            server.UseGadgetForTest("Demo", "terrain_blaster",
                new Vector3f(center.X + 0.5f, center.Y + 0.5f, center.Z + 0.5f));

            Assert.True(server.World.GetBlock(center).IsAir, "the blast clears the centre");
            Assert.True(server.World.GetBlock(new Vector3i(center.X + 1, center.Y, center.Z)).IsAir);
            Assert.Equal(stoneBefore, p.State.Inventory.CountOf("stone")); // a clearing blast yields no loot
        }
    }

    /// <summary>A standing spot two blocks east of a cell — inside the gadget reach (#2376), like a real player aiming at it.</summary>
    private static Vector3f Beside(Vector3i cell) => new(cell.X + 2.5f, cell.Y, cell.Z + 0.5f);

    /// <summary>#2376: the gadgets that act at the aim point refuse a point beyond the player's reach — a modified client
    /// used to pump a far pocket or blast ground it never stood near. A refused use costs neither energy nor cooldown.</summary>
    [Fact]
    public void TargetedGadgets_RefuseAnAimPointOutOfReach_ButWorkBesideIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Faraway");
            p.State.AboardShip = false;
            p.State.Inventory.Add("fluid_pump", 1, 1);
            p.State.Inventory.Add("terrain_blaster", 1, 1);

            var oil = _content.GetBlock("oil")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            var pocket = new Vector3i(40, 40, 14);
            var ground = new Vector3i(60, 40, 14);
            server.World.SetBlock(pocket, oil);
            server.World.SetBlock(ground, stone);
            p.State.Position = new Vector3f(14.5f, 40f, 14.5f); // 25+ blocks from both
            float energy = p.State.SuitEnergy;

            server.UseGadgetForTest("Faraway", "fluid_pump", new Vector3f(pocket.X + 0.5f, pocket.Y + 0.5f, pocket.Z + 0.5f));
            server.UseGadgetForTest("Faraway", "terrain_blaster", new Vector3f(ground.X + 0.5f, ground.Y + 0.5f, ground.Z + 0.5f));
            Assert.Equal(oil, server.World.GetBlock(pocket));
            Assert.Equal(stone, server.World.GetBlock(ground));
            Assert.Equal(0, p.State.Inventory.CountOf("oil"));
            Assert.Equal(energy, p.State.SuitEnergy);
            Assert.Equal(0, server.GadgetCooldownForTest("Faraway", "fluid_pump"));
            Assert.Equal(0, server.GadgetCooldownForTest("Faraway", "terrain_blaster"));

            // Walk over: the same aim point is in reach now.
            p.State.Position = Beside(pocket);
            server.UseGadgetForTest("Faraway", "fluid_pump", new Vector3f(pocket.X + 0.5f, pocket.Y + 0.5f, pocket.Z + 0.5f));
            Assert.True(server.World.GetBlock(pocket).IsAir);
            Assert.Equal(1, p.State.Inventory.CountOf("oil"));
        }
    }

    [Fact]
    public void FluidPump_PullsAnOilCellIntoThePack_AndLeavesAir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pumper");
            p.State.AboardShip = false;
            p.State.Inventory.Add("fluid_pump", 1, 1);

            var oil = _content.GetBlock("oil")!.NumericId;
            var cell = new Vector3i(14, 40, 14);
            server.World.SetBlock(cell, oil);
            p.State.Position = Beside(cell); // #2376: within reach of the aim point

            server.UseGadgetForTest("Pumper", "fluid_pump", new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));

            Assert.True(server.World.GetBlock(cell).IsAir, "a pumped oil cell stays air — oil never refills (#2106)");
            Assert.Equal(1, p.State.Inventory.CountOf("oil"));
            Assert.True(p.State.SuitEnergy < 100f, "a pull costs suit energy");
            Assert.True(server.GadgetCooldownForTest("Pumper", "fluid_pump") > 0);
        }
    }

    [Fact]
    public void FluidPump_HarvestsWaterToo_ButNothingElse()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pumper");
            p.State.AboardShip = false;
            p.State.Inventory.Add("fluid_pump", 1, 1);

            var water = _content.GetBlock("water")!.NumericId;
            var wet = new Vector3i(14, 40, 14);
            server.World.SetBlock(wet, water);
            p.State.Position = Beside(wet); // #2376: within reach of the aim point
            server.UseGadgetForTest("Pumper", "fluid_pump", new Vector3f(wet.X + 0.5f, wet.Y + 0.5f, wet.Z + 0.5f));
            Assert.Equal(1, p.State.Inventory.CountOf("water"));

            float energy = p.State.SuitEnergy;
            var stone = _content.GetBlock("stone")!.NumericId;
            var rock = new Vector3i(18, 40, 18);
            server.World.SetBlock(rock, stone);
            p.State.Position = Beside(rock);
            server.UseGadgetForTest("Pumper", "fluid_pump", new Vector3f(rock.X + 0.5f, rock.Y + 0.5f, rock.Z + 0.5f));
            Assert.Equal(stone, server.World.GetBlock(rock));      // rock is not pumped
            Assert.Equal(energy, p.State.SuitEnergy);              // a miss is free
            Assert.Equal(0, p.State.Inventory.CountOf("stone"));
        }
    }

    [Fact]
    public void FieldMedkit_DoesNothing_WithoutTheGadget()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var user = server.AddLocalPlayer("NoKit");
            user.State.Health = 30f;
            server.UseGadgetForTest("NoKit", "field_medkit", user.State.Position);
            Assert.Equal(30f, user.State.Health); // no gadget in inventory → rejected, no heal
        }
    }

    [Fact]
    public void TerrainScanner_FindsNearbyOre_CostsEnergy_AndCoolsDown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Prospector");
            p.State.Inventory.Add("terrain_scanner", 1, 1);
            p.State.Position = new Vector3f(0f, 64f, 0f);

            // Bury valuables near the player (underground — the scanner sees through terrain) and one
            // valuable well outside the pulse radius (20) that must NOT be reported.
            var iron = _content.GetBlock("iron_ore")!.NumericId;
            var crystal = _content.GetBlock("crystal")!.NumericId;
            // Positive X only — block X is stored canonically (longitude wraps), so negative test
            // coordinates would canonicalise to circumference−x and the assertions wouldn't match.
            server.World.SetBlock(new Vector3i(3, 60, 2), iron);
            server.World.SetBlock(new Vector3i(5, 58, -3), crystal);
            server.World.SetBlock(new Vector3i(60, 64, 0), iron); // far beyond the 20-block pulse

            var scan = server.OreScanForTest("Prospector");
            Assert.Contains(System.Linq.Enumerable.Range(0, scan.X.Length), i => scan.X[i] == 3 && scan.Y[i] == 60 && scan.Z[i] == 2);
            Assert.Contains(System.Linq.Enumerable.Range(0, scan.X.Length), i => scan.X[i] == 5 && scan.Y[i] == 58 && scan.Z[i] == -3);
            Assert.DoesNotContain(System.Linq.Enumerable.Range(0, scan.X.Length), i => scan.X[i] == 60);

            // The real use debits suit energy + starts the cooldown.
            server.UseGadgetForTest("Prospector", "terrain_scanner", p.State.Position);
            Assert.True(p.State.SuitEnergy < 100f);
            Assert.True(server.GadgetCooldownForTest("Prospector", "terrain_scanner") > 0);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }
}
