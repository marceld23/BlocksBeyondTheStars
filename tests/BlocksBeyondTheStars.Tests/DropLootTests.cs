// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// Drops and loot — Lyxette's third round (2026-08-27): a flier's drop packet hung in the sky (#1311), the
/// meat-and-gland carpet around a base never went away (#1312 — only CREATURE loot expires, mining overflow
/// keeps the #853 promise), and asteroid ore vanished into the hold without a word (#1317).
/// </summary>
public sealed class DropLootTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DropLootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_droploot_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every server send so a test can assert on the loot toast.</summary>
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

        public IEnumerable<string> LootToasts => Sent.OfType<ServerMessage>()
            .Select(m => m.Text)
            .Where(t => t.StartsWith("@srv.space.loot_to_", StringComparison.Ordinal));
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world, string planet = "rocky",
        IServerTransport? transport = null, Action<GameRules>? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 31,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    private void FillEverySlot(Inventory inv, string filler = "stone")
    {
        int max = _content.MaxStackOf(filler);
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, new ItemStack(filler, max));
        }
    }

    // ---------------- #1311: packets fall ----------------

    [Fact]
    public void ASpillHighInTheAir_FallsToTheGround()
    {
        var server = Started(out var repo, "fall");
        using (repo)
        {
            server.AddLocalPlayer("Hunter").State.AboardShip = false;
            const int x = 10, z = 10;
            int top = SurfaceTopY(server, x, z);

            server.SpillToGroundForTest(new Vector3i(x, top + 8, z), "iron_ore", 2); // where a flier died

            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(top + 1, packet.Position.Y);
        }
    }

    [Fact]
    public void ASpillInsideAWall_StillSurfacesToTheFreeCellAbove()
    {
        var server = Started(out var repo, "entombed");
        using (repo)
        {
            server.AddLocalPlayer("Miner").State.AboardShip = false;
            var stone = _content.GetBlock("stone")!.NumericId;
            const int x = 20, z = 20;
            int y = SurfaceTopY(server, x, z) + 10;
            server.World.SetBlock(new Vector3i(x, y, z), stone);
            server.World.SetBlock(new Vector3i(x, y + 1, z), stone);

            server.SpillToGroundForTest(new Vector3i(x, y, z), "iron_ore", 1);

            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(y + 2, packet.Position.Y); // up out of the masonry, resting on it — not through it
        }
    }

    // ---------------- #1312: creature loot expires, mining overflow does not ----------------

    [Fact]
    public void CreatureLootExpires_MiningOverflowNextToItDoesNot()
    {
        var server = Started(out var repo, "expire");
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, SurfaceTopY(server, 0, 0) + 1, 0.5f); // far from both packets

            int top = SurfaceTopY(server, 30, 30);
            server.SpillToGroundForTest(new Vector3i(30, top + 1, 30), "iron_ore", 3, creatureLoot: true);
            server.SpillToGroundForTest(new Vector3i(36, top + 1, 36), "stone", 3);
            Assert.Equal(2, server.DropPackets.Count);

            for (int i = 0; i < 31; i++)
            {
                server.Tick(10.0); // 310 s with a player on the world
            }

            var left = Assert.Single(server.DropPackets);
            Assert.Contains(left.Items, s => s.Item == "stone");
            Assert.Equal(0, left.LifetimeLeft);
        }
    }

    [Fact]
    public void LootAndMiningSpills_NeverShareAPacket()
    {
        var server = Started(out var repo, "nomerge");
        using (repo)
        {
            server.AddLocalPlayer("Settler").State.AboardShip = false;
            int top = SurfaceTopY(server, 30, 30);
            server.SpillToGroundForTest(new Vector3i(30, top + 1, 30), "iron_ore", 3, creatureLoot: true);
            server.SpillToGroundForTest(new Vector3i(30, top + 1, 30), "iron_ore", 3);

            Assert.Equal(2, server.DropPackets.Count);
            Assert.Single(server.DropPackets, c => c.LifetimeLeft > 0);
            Assert.Single(server.DropPackets, c => c.LifetimeLeft == 0);
        }
    }

    [Fact]
    public void ALootPacketsTimer_ResumesAfterARestart()
    {
        const string world = "resume";
        string id;
        var server = Started(out var repo, world);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, SurfaceTopY(server, 0, 0) + 1, 0.5f);
            int top = SurfaceTopY(server, 30, 30);
            server.SpillToGroundForTest(new Vector3i(30, top + 1, 30), "iron_ore", 3, creatureLoot: true);
            id = Assert.Single(server.DropPackets).Id;

            for (int i = 0; i < 10; i++)
            {
                server.Tick(10.0); // 100 s: past several 30 s checkpoints
            }
        }

        var reloaded = Started(out var repo2, world);
        using (repo2)
        {
            reloaded.AddLocalPlayer("Settler").State.AboardShip = false;
            var packet = Assert.Single(reloaded.DropPackets);
            Assert.Equal(id, packet.Id);
            Assert.InRange(packet.LifetimeLeft, 1.0, 240.0); // ~200 left, at worst one checkpoint behind
        }
    }

    [Fact]
    public void KillingACreatureWithAFullPack_LeavesAnExpiringPacket()
    {
        var server = Started(out var repo, "kill", planet: "jungle");
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            FillEverySlot(p.State.Inventory);

            server.Tick(6.0); // seed fauna
            var creature = server.Creatures.OrderBy(c => c.HullMax).First();
            p.State.Position = creature.Position;
            for (int i = 0; i < 40 && server.Creatures.Any(c => c.Id == creature.Id); i++)
            {
                server.AttackEntity("Hunter", creature.Id);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == creature.Id);
            var packet = Assert.Single(server.DropPackets);
            Assert.True(packet.LifetimeLeft > 0, "a creature's drop with a full pack must lie there as an EXPIRING packet");
        }
    }

    [Fact]
    public void FreshLootMergedIntoAnAgingPacket_RestartsItsTimer()
    {
        // #1350: the merge kept the OLD packet's LifetimeLeft — a second kill beside a 4:50-old bundle put the
        // fresh meat into it, and everything vanished ten seconds later.
        var server = Started(out var repo, "merge-timer");
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, SurfaceTopY(server, 0, 0) + 1, 0.5f); // far from the packet (no pickup)
            int top = SurfaceTopY(server, 30, 30);
            var origin = new Vector3i(30, top + 1, 30);
            server.SpillToGroundForTest(origin, "iron_ore", 3, creatureLoot: true);
            for (int i = 0; i < 29; i++)
            {
                server.Tick(10.0); // 290 s with a player on the world: ten seconds left
            }

            var aging = Assert.Single(server.DropPackets);
            Assert.InRange(aging.LifetimeLeft, 1.0, 20.0);

            server.SpillToGroundForTest(origin, "iron_ore", 2, creatureLoot: true); // a second kill next to it
            var merged = Assert.Single(server.DropPackets); // merged into the bundle, not a new one
            Assert.Equal(aging.Id, merged.Id);
            Assert.Equal(5, merged.Items.Single(s => s.Item == "iron_ore").Count);
            Assert.InRange(merged.LifetimeLeft, 290.0, 300.0);

            server.Tick(20.0); // the old expiry passes…
            Assert.Single(server.DropPackets); // …and the bundle is still there
        }
    }

    // ---------------- #1317: the stow toast ----------------

    private SvGameServer SpaceServer(string world, RecordingTransport transport, out SqliteWorldRepository repo)
        => Started(out repo, world, transport: transport, rules: r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        });

    [Fact]
    public void BreakingASmallAsteroid_WithoutATractor_SaysWhereTheOreWent()
    {
        var transport = new RecordingTransport();
        var server = SpaceServer("toast-bank", transport, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Remove("tractor_beam"); // direct-to-inventory loot
            server.EnterSpace("Pilot");

            for (int i = 0; i < 30 && !transport.LootToasts.Any(); i++)
            {
                server.TickForTest(2.0);
                var a = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
                if (a != null)
                {
                    server.FireWeapon("Pilot", "asteroid_breaker", a.Id);
                }
            }

            string toast = Assert.Single(transport.LootToasts.Take(1));
            Assert.StartsWith("@srv.space.loot_to_backpack:+", toast); // backpack has room → that is where it went
            Assert.DoesNotContain("_ore", toast); // the item is named, not keyed
        }
    }

    [Fact]
    public void TractoringASalvageDrop_SaysItWentToTheHold()
    {
        var transport = new RecordingTransport();
        var server = SpaceServer("toast-stow", transport, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Add("tractor_beam");
            server.EnterSpace("Pilot");

            CombatEntity? drop = null;
            for (int i = 0; i < 24 && drop == null; i++)
            {
                server.TickForTest(2.0);
                var a = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
                if (a != null)
                {
                    server.FireWeapon("Pilot", "asteroid_breaker", a.Id);
                }

                drop = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.ResourceDrop);
            }

            Assert.NotNull(drop);
            Assert.Empty(transport.LootToasts); // floating salvage is not banked yet — nothing to announce

            server.ShipMove("Pilot", drop!.Position.X, drop.Position.Y, drop.Position.Z);
            server.Tick(0.1);

            string toast = Assert.Single(transport.LootToasts);
            Assert.StartsWith("@srv.space.loot_to_cargo:+", toast);
        }
    }

    // ---------------- #1367: the colliding rule + the shutdown checkpoint ----------------

    [Fact]
    public void AKillOverGrass_LeavesThePacketInTheGrass_NotACellAboveIt()
    {
        var server = Started(out var repo, "grass");
        using (repo)
        {
            server.AddLocalPlayer("Hunter").State.AboardShip = false;
            const int x = 12, z = 12;
            int top = SurfaceTopY(server, x, z);
            server.World.SetBlock(new Vector3i(x, top + 1, z), _content.GetBlock("flora_fern")!.NumericId); // a meadow tuft on the ground

            server.SpillToGroundForTest(new Vector3i(x, top + 8, z), "iron_ore", 2, creatureLoot: true);

            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(top + 1, packet.Position.Y); // in the grass, on the ground — the old rule stopped on the tuft
        }
    }

    [Fact]
    public void TheShutdownSave_WritesALootPacketsExactAge()
    {
        const string world = "shutdown";
        var server = Started(out var repo, world);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, SurfaceTopY(server, 0, 0) + 1, 0.5f);
            int top = SurfaceTopY(server, 30, 30);
            server.SpillToGroundForTest(new Vector3i(30, top + 1, 30), "iron_ore", 3, creatureLoot: true);

            server.Tick(10.0); // ten seconds of ageing — well short of the 30 s checkpoint
            Assert.InRange(Assert.Single(server.DropPackets).LifetimeLeft, 289.0, 291.0);
            server.SaveAllForTest(); // the shutdown / checkpoint save
        }

        var reloaded = Started(out var repo2, world);
        using (repo2)
        {
            reloaded.AddLocalPlayer("Settler").State.AboardShip = false;
            Assert.InRange(Assert.Single(reloaded.DropPackets).LifetimeLeft, 280.0, 291.0); // not the 300 the spill wrote
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
