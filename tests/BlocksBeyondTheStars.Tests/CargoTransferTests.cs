// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Moving items between the personal inventory and the ship's cargo hold: per-item both ways, bulk "stow all"
/// (materials/components only — tools stay) and "take all", and the aboard-ship gate. Mirrors the storage-crate
/// deposit behaviour, but for the ship's own hold.
/// </summary>
public sealed class CargoTransferTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CargoTransferTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cargo_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cargo"));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "cargo", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Records every server send so a test can read the feedback line the player got.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    /// <summary>The cargo feedback tokens the player was sent (rejections and info lines alike).</summary>
    private static List<string> CargoLines(RecordingTransport t)
        => t.Sent.Select(m => m switch
            {
                ActionRejected r when r.Action == "cargo" => r.Reason,
                ServerMessage s when s.Text.StartsWith("@srv.cargo.", StringComparison.Ordinal) => s.Text,
                _ => null,
            })
            .OfType<string>()
            .ToList();

    private BlocksBeyondTheStars.GameServer.PlayerSession AboardPilot(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Pilot");
        p.State.AboardShip = true; // in the cabin / cockpit → the cargo hold is reachable
        return p;
    }

    [Fact]
    public void StowAll_MovesMaterials_ButKeepsTools()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.Inventory.Add("iron_ore", 10, 99);
            int machetes = p.State.Inventory.CountOf("machete"); // starter kit carries one

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));        // loose material stowed
            Assert.Equal(10, server.Ship.Cargo.CountOf("iron_ore"));       // ...into the hold
            Assert.Equal(machetes, p.State.Inventory.CountOf("machete"));  // the tool stayed on the player
            Assert.Equal(0, server.Ship.Cargo.CountOf("machete"));
        }
    }

    [Fact]
    public void StowAll_MovesBuildingBlocksToo()
    {
        // #1562: "stow all" mirrored the crate rule from before #1264 — materials + components only — so a
        // builder's stone, glass and wall panels stayed in the pack while a helmet (component) was stowed.
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.Inventory.Add("stone", 40, 1024);
            p.State.Inventory.Add("glass_clear", 12, 1024);

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(0, p.State.Inventory.CountOf("stone"));
            Assert.Equal(40, server.Ship.Cargo.CountOf("stone"));
            Assert.Equal(12, server.Ship.Cargo.CountOf("glass_clear"));
        }
    }

    [Fact]
    public void TakeAll_PullsEverythingBackOut()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            server.Ship.Cargo.Add("iron_ore", 12, 99);

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: false, bulkAll: true));

            Assert.Equal(12, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void PerItem_MovesBothDirections()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.Inventory.Add("iron_ore", 6, 99);

            // Inventory → cargo for just that item.
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, item: "iron_ore"));
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(6, server.Ship.Cargo.CountOf("iron_ore"));

            // ...and back again.
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: false, item: "iron_ore"));
            Assert.Equal(6, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void NotAboard_IsRejected()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.AboardShip = false; // walked off the ship onto the surface
            p.State.Inventory.Add("iron_ore", 5, 99);

            Assert.False(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(5, p.State.Inventory.CountOf("iron_ore")); // nothing moved
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void StowAll_KeepsTheStackInTheHand()
    {
        // #2138 ("Frachtraum füllen"): what the player holds is what they are about to use — the selected
        // quick-bar slot stays; the rest of the quick-bar is stowed, since pickups land there first.
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            ClearPack(p);
            p.State.Inventory.SetSlot(2, new ItemStack("stone", 30));
            p.State.Inventory.SetSlot(5, new ItemStack("iron_ore", 7));
            p.State.SelectedHotbarSlot = 2; // stone in the hand

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(30, p.State.Inventory.CountOf("stone"));      // held → stays
            Assert.Equal(7, server.Ship.Cargo.CountOf("iron_ore"));    // quick-bar slot 5, not held → stowed
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void StowAll_KeepsSpareSuitGear()
    {
        // A spare helmet / oxygen tank is category "component" like a circuit board — but it is equipment, not cargo.
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            ClearPack(p);
            p.State.Inventory.SetSlot(10, new ItemStack("helmet", 1));
            p.State.Inventory.SetSlot(11, new ItemStack("oxygen_tank_1", 1));
            p.State.Inventory.SetSlot(12, new ItemStack("iron_ore", 4));

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(1, p.State.Inventory.CountOf("helmet"));
            Assert.Equal(1, p.State.Inventory.CountOf("oxygen_tank_1"));
            Assert.Equal(4, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void StowAll_TellsThePlayerWhatHappened()
    {
        // The button used to stay silent when nothing moved — indistinguishable from a broken one.
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = AboardPilot(server);
            ClearPack(p);
            p.State.Inventory.SetSlot(10, new ItemStack("iron_ore", 4));
            p.State.Inventory.SetSlot(11, new ItemStack("stone", 9));

            // Two stacks stowed → "2 stacks".
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));
            Assert.Equal("@srv.cargo.stowed:2", CargoLines(transport).Last());

            // Nothing loose left → "nothing to stow".
            Assert.False(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));
            Assert.Equal("@srv.loot.nothing_to_stash", CargoLines(transport).Last());

            // A full hold → "full", and the ore stays with the player.
            for (int i = 0; i < server.Ship.Cargo.SlotCount; i++)
            {
                server.Ship.Cargo.SetSlot(i, new ItemStack("scrap_metal", 99));
            }

            p.State.Inventory.SetSlot(12, new ItemStack("copper_ore", 3));
            Assert.False(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));
            Assert.Equal("@srv.cargo.full", CargoLines(transport).Last());
            Assert.Equal(3, p.State.Inventory.CountOf("copper_ore"));
        }
    }

    [Fact]
    public void AutoStow_StaysQuietWhenThereIsNothingToStow()
    {
        // Auto-stow fires on every boarding: "nothing to stow" each time would be noise the player never asked for.
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = AboardPilot(server);
            ClearPack(p);

            Assert.False(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true, quiet: true));
            Assert.Empty(CargoLines(transport));

            // ...but a real sweep still says what went into the hold.
            p.State.Inventory.SetSlot(10, new ItemStack("iron_ore", 4));
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true, quiet: true));
            Assert.Equal("@srv.cargo.stowed:1", CargoLines(transport).Last());
        }
    }

    /// <summary>Empties the pack except the tools, so a test controls exactly which loose stacks exist.</summary>
    private void ClearPack(BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        var inv = p.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } s && _content.GetItem(s.Item)?.Category is not ItemCategory.Tool)
            {
                inv.SetSlot(i, null);
            }
        }

        p.State.SelectedHotbarSlot = 0;
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
