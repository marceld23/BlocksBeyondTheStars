// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ground drop packets (#853): mining with a full backpack must keep working. The block breaks, the drop
/// lands on the ground as a packet, packets STACK instead of littering one bundle per block, and walking
/// near one with free slots collects it again by itself.
/// </summary>
public sealed class DropPacketTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DropPacketTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_drops_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "drops")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 31,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Packs every slot with a full stack, so nothing new fits and no stack can be topped up.</summary>
    private void FillEverySlot(Inventory inv, string filler = "stone")
    {
        int max = _content.MaxStackOf(filler);
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, new ItemStack(filler, max));
        }
    }

    /// <summary>A player standing next to <paramref name="pos"/> with a drill in hand and — unless
    /// <paramref name="room"/> — no room anywhere. Off the ship, so the cargo hold cannot absorb the drop
    /// (a test player with no starter ship counts as aboard by default).</summary>
    private BlocksBeyondTheStars.GameServer.PlayerSession Miner(SvGameServer server, Vector3i pos, bool room = false)
    {
        var p = server.AddLocalPlayer("Justus");
        p.State.AboardShip = false;
        if (!room)
        {
            FillEverySlot(p.State.Inventory);
        }

        p.State.Position = new Vector3f(pos.X + 1.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        return p;
    }

    private void PutStone(SvGameServer server, Vector3i pos)
        => server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);

    // ---------------- Spilling ----------------

    [Fact]
    public void MiningOn_WithAFullPack_KeepsBreakingBlocks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var start = new Vector3i(20, 60, 20);
            var p = Miner(server, start);

            // Four blocks in a row, all in reach — the drill used to stop dead on the first one.
            for (int i = 0; i < 4; i++)
            {
                var pos = new Vector3i(start.X + i, start.Y, start.Z);
                PutStone(server, pos);
                p.State.Position = new Vector3f(pos.X + 1.2f, pos.Y + 0.5f, pos.Z + 0.5f);
                server.MineBlock("Justus", pos.X, pos.Y, pos.Z);
                Assert.True(server.World.GetBlock(pos).IsAir);
            }

            // All four stones are on the ground, and NOT as four separate bundles.
            Assert.Equal(4, server.DropPackets.Sum(c => c.Items.Where(s => s.Item == "stone").Sum(s => s.Count)));
            Assert.Single(server.DropPackets);
        }
    }

    [Fact]
    public void SpillsFarApart_StayTwoPackets()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Justus").State.AboardShip = false;
            server.SpillToGroundForTest(new Vector3i(0, 60, 0), "iron_ore", 3);
            server.SpillToGroundForTest(new Vector3i(40, 60, 40), "iron_ore", 2);

            Assert.Equal(2, server.DropPackets.Count);
        }
    }

    [Fact]
    public void DyedVariant_KeepsItsOwnStackInsideThePacket()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Justus").State.AboardShip = false;
            var cell = new Vector3i(5, 60, 5);
            string dyed = ItemKey.Compose("stone", 0x3366CC, 0, 0);

            server.SpillToGroundForTest(cell, "stone", 4);
            server.SpillToGroundForTest(cell, dyed, 2);

            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(4, packet.Items.First(s => s.Item == "stone").Count);
            Assert.Equal(2, packet.Items.First(s => s.Item == dyed).Count); // never merged into the plain stone
        }
    }

    [Fact]
    public void WorldCap_MergesInsteadOfGrowingForever()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Justus").State.AboardShip = false;

            // Far enough apart that every spill would otherwise start its own packet.
            for (int i = 0; i < 90; i++)
            {
                server.SpillToGroundForTest(new Vector3i(i * 20, 60, 0), "stone", 1);
            }

            Assert.True(server.DropPackets.Count <= 64, $"packet count {server.DropPackets.Count} exceeds the world cap");
            Assert.Equal(90, server.DropPackets.Sum(c => c.Items.Sum(s => s.Count))); // and nothing was thrown away
        }
    }

    // ---------------- Automatic pickup ----------------

    [Fact]
    public void WalkingOverAPacket_WithRoom_CollectsItByItself()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = new Vector3i(8, 60, 8);
            var p = Miner(server, cell, room: true);
            server.SpillToGroundForTest(cell, "iron_ore", 7);
            p.State.Position = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

            server.SweepDropPacketsForTest();

            Assert.Equal(7, p.State.Inventory.CountOf("iron_ore"));
            Assert.Empty(server.DropPackets); // emptied packets despawn
        }
    }

    [Fact]
    public void APacketOutOfReach_IsLeftAlone()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = new Vector3i(8, 60, 8);
            var p = Miner(server, cell, room: true);
            server.SpillToGroundForTest(cell, "iron_ore", 5);
            p.State.Position = new Vector3f(cell.X + 12f, cell.Y + 0.5f, cell.Z);

            server.SweepDropPacketsForTest();

            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
            Assert.Single(server.DropPackets);
        }
    }

    [Fact]
    public void AFullPackStandingOnAPacket_ChangesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = new Vector3i(8, 60, 8);
            var p = Miner(server, cell);
            server.SpillToGroundForTest(cell, "iron_ore", 5);
            p.State.Position = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

            server.SweepDropPacketsForTest();

            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(5, packet.Items.First(s => s.Item == "iron_ore").Count);
        }
    }

    [Fact]
    public void APacketKeepsWhatStillDoesNotFit()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var cell = new Vector3i(8, 60, 8);
            var p = Miner(server, cell);

            // Exactly one slot free, and the packet holds more than one stack's worth of two different items.
            p.State.Inventory.SetSlot(3, null);
            int max = _content.MaxStackOf("iron_ore");
            server.SpillToGroundForTest(cell, "iron_ore", max);
            server.SpillToGroundForTest(cell, "titanium_ore", 6);
            p.State.Position = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

            server.SweepDropPacketsForTest();

            Assert.Equal(max, p.State.Inventory.CountOf("iron_ore")); // the free slot took the first stack …
            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(6, packet.Items.First(s => s.Item == "titanium_ore").Count); // … the rest stays lying
        }
    }

    [Fact]
    public void MiningThenMakingRoom_GetsTheDropBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(14, 60, 14);
            var p = Miner(server, pos);
            PutStone(server, pos);

            server.MineBlock("Justus", pos.X, pos.Y, pos.Z);
            Assert.Single(server.DropPackets);

            // Make room (throw a stack away) and step onto the packet — no key press, no prompt.
            p.State.Inventory.SetSlot(5, null);
            p.State.Position = new Vector3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
            server.SweepDropPacketsForTest();

            Assert.Empty(server.DropPackets);
        }
    }

    // ---------------- Persistence ----------------

    [Fact]
    public void Packets_SurviveAServerRestart()
    {
        {
            var s1 = Started(out var repo1, "drops_persist");
            using (repo1)
            {
                s1.AddLocalPlayer("Justus").State.AboardShip = false;
                s1.SpillToGroundForTest(new Vector3i(9, 60, 9), "iron_ore", 12);
                Assert.Single(s1.DropPackets);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = Started(out var repo2, "drops_persist");
        using (repo2)
        {
            var packet = Assert.Single(s2.DropPackets);
            Assert.Equal(12, packet.Items.First(s => s.Item == "iron_ore").Count);

            // And it is still a drop packet, not something the loot prompt would offer.
            Assert.DoesNotContain(s2.Containers, c => c.Id == packet.Id);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
