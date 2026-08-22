// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Companion payoff (#1210): fetch pours ground packets to the OWNER only (owner within leash range), a
/// companion growls at a hostile in sight once per cooldown (toast + Alerting flag), a robber on approach stalls
/// at the companion, a high-bond companion wards its owner from hold-ups, and a present companion drops its
/// species' produce when due.
/// </summary>
public sealed class CompanionPayoffTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public CompanionPayoffTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_companionpayoff_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var r in _repos)
        {
            r.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Records every message the server sends, per connection (local players have connection ids too).</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Started(string name, RecordingTransport? transport = null, Action<GameRules>? rules = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna — the taming fixture's world
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
        };
        rules?.Invoke(config.Rules);
        IServerTransport st = transport is not null ? transport : new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Ranger(SvGameServer server, string name = "Ranger")
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Inventory.Add("creature_translator", 1, 1);
        p.State.Inventory.Add("forage_bait", 50, 50);
        p.State.Inventory.Add("meat_bait", 50, 50);
        p.State.Inventory.Add("nectar_lure", 50, 50);
        p.State.SuitEnergy = 100f;
        return p;
    }

    /// <summary>Tames the nearest passive wild creature (the taming fixture's ritual) and returns the live companion entity.</summary>
    private static CombatEntity TameOne(SvGameServer server, PlayerSession p)
    {
        // A land walker, preferably: an aquatic or burrowing pet sits in water / under ground, where sight lines
        // are occluded. Fauna trickles in over the first seconds, so wait for one (falling back to any wild animal).
        CombatEntity? creature = null;
        for (int i = 0; i < 30 && creature is null; i++)
        {
            server.Tick(2.0);
            creature = server.Creatures.FirstOrDefault(c => c.OwnerId.Length == 0
                && server.SpeciesRoster.First(s => s.Id == c.SpeciesId) is { Habitat: CreatureHabitat.Land, HasWings: false });
        }

        creature ??= server.Creatures.First(c => c.OwnerId.Length == 0);
        var sp = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
        sp.Temperament = CreatureTemperament.Passive;
        p.State.Position = creature.Position;
        server.TameDecodeForTest(p.State.PlayerId);
        for (int i = 0; i < 30; i++)
        {
            string need = server.TameCurrentNeedForTest(p.State.PlayerId);
            if (string.IsNullOrEmpty(need))
            {
                break;
            }

            server.TameRespondForTest(p.State.PlayerId, need);
        }

        return Assert.Single(server.CompanionEntitiesForTest(p.State.PlayerId));
    }

    private static Vector3i Floor(Vector3f v) => new((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y), (int)MathF.Floor(v.Z));

    /// <summary>Y of the topmost non-air block in a column (the local ground), or 0 if the column is empty.</summary>
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

    // ---------------- fetch ----------------

    [Fact]
    public void Companion_FetchesPacketsInItsReach_ForItsOwnerOnly_WhileTheOwnerIsNear()
    {
        var server = Started("fetch");
        var owner = Ranger(server);
        var pet = TameOne(server, owner);
        var other = Ranger(server, "Other");

        // Packet 4 blocks from the pet (inside its 7.5 fetch reach), owner 9 blocks from it (outside their own
        // 2.5 reach but inside the 24-block leash), the other player near the pet but also out of own reach.
        var petPos = pet.Position;
        owner.State.Position = new Vector3f(petPos.X - 5f, petPos.Y, petPos.Z);
        other.State.Position = new Vector3f(petPos.X - 2f, petPos.Y, petPos.Z + 3f);
        var packet = Floor(new Vector3f(petPos.X + 4f, petPos.Y, petPos.Z));

        int ownerBefore = owner.State.Inventory.CountOf("iron_ore");
        int otherBefore = other.State.Inventory.CountOf("iron_ore");
        server.SpillToGroundForTest(packet, "iron_ore", 3);
        server.SweepDropPacketsForTest();

        Assert.Equal(ownerBefore + 3, owner.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(otherBefore, other.State.Inventory.CountOf("iron_ore"));
        Assert.Empty(server.DropPackets);
        Assert.Contains("vega:hint:companion_fetch", owner.State.Milestones); // VEGA explained the trick once

        // Owner far away (beyond the leash): the pet does not teleport loot across the world — the packet stays.
        owner.State.Position = new Vector3f(petPos.X - 40f, petPos.Y, petPos.Z);
        server.SpillToGroundForTest(packet, "iron_ore", 2);
        server.SweepDropPacketsForTest();
        Assert.Equal(ownerBefore + 3, owner.State.Inventory.CountOf("iron_ore"));
        Assert.Single(server.DropPackets);
    }

    // ---------------- alert ----------------

    [Fact]
    public void Companion_GrowlsAtAHostileInSight_OncePerCooldown()
    {
        var transport = new RecordingTransport();
        var server = Started("alert", transport);
        var owner = Ranger(server);
        var pet = TameOne(server, owner);

        // Put pet + owner on the open surface above the loaded start column (the taming spot may lie in a
        // cave / under the canopy, where no sight line exists).
        int top = SurfaceTopY(server, 0, 0);
        pet.Position = new Vector3f(0.5f, top + 1f, 0.5f);
        owner.State.Position = new Vector3f(2.5f, top + 1f, 0.5f);

        int Alerts() => transport.Sent.Count(s => s.Conn == owner.ConnectionId && s.Msg is ServerMessage m
            && m.Text.StartsWith("@srv.companion.alert:", StringComparison.Ordinal));

        server.TickCompanionPayoffForTest();
        Assert.Equal(0, Alerts()); // nothing hostile around — no growl

        // A Guardian machine a few blocks from the pet with a clear sight line (jungle foliage can stand in the way
        // of any single bearing, so pick the first open one; straight above as the last resort).
        var petPos = pet.Position;
        var candidates = new List<Vector3f>();
        foreach (float d in new[] { 3f, 5f, 8f })
        {
            candidates.Add(new Vector3f(petPos.X + d, petPos.Y, petPos.Z));
            candidates.Add(new Vector3f(petPos.X - d, petPos.Y, petPos.Z));
            candidates.Add(new Vector3f(petPos.X, petPos.Y, petPos.Z + d));
            candidates.Add(new Vector3f(petPos.X, petPos.Y, petPos.Z - d));
            candidates.Add(new Vector3f(petPos.X, petPos.Y + d, petPos.Z));
        }

        var spot = candidates.FirstOrDefault(c => server.HasLineOfSightForTest(c, petPos));
        string col = string.Join(",", Enumerable.Range(-2, 7).Select(dy => server.World.GetBlock(new Vector3i((int)MathF.Floor(petPos.X), (int)MathF.Floor(petPos.Y) + dy, (int)MathF.Floor(petPos.Z))).IsAir ? "." : "#"));
        Assert.True(server.HasLineOfSightForTest(spot, petPos), $"no open sight line around the pet at {petPos} (column y-2..y+4: {col}); species {pet.SpeciesId}");
        server.SpawnPlanetEnemyAtForTest(spot, damagePerSecond: 0f);
        server.TickCompanionPayoffForTest();
        Assert.Equal(1, Alerts());
        Assert.True(server.CompanionAlertingForTest(pet.Id));
        var toast = transport.Sent.Select(s => s.Msg).OfType<ServerMessage>().Last(m => m.Text.StartsWith("@srv.companion.alert:", StringComparison.Ordinal));
        Assert.EndsWith(":" + pet.CustomName, toast.Text);

        // Still in sight a moment later: the growl is rate-limited, not repeated every scan.
        server.TickCompanionPayoffForTest();
        Assert.Equal(1, Alerts());

        // After the cooldown the companion growls again (the 31 s tick lets pet and machine wander — pin both back
        // onto the open surface so the second growl is about the cooldown, not about who walked where).
        server.Tick(31.0);
        pet.Position = new Vector3f(0.5f, top + 1f, 0.5f);
        owner.State.Position = new Vector3f(2.5f, top + 1f, 0.5f);
        server.PlanetEnemies.Last().Position = spot;
        server.TickCompanionPayoffForTest();
        Assert.Equal(2, Alerts());
    }

    // ---------------- robber stall + bond ward ----------------

    [Fact]
    public void ARobberOnApproach_StallsAtTheCompanion()
    {
        var server = Started("stall", rules: r => { r.PlanetEnemies = AlienActivity.Off; r.Bandits = AlienActivity.Normal; });
        var owner = Ranger(server);
        var pet = TameOne(server, owner);
        server.TamedCreaturesForTest(owner.State.PlayerId)[0].Bond = 10; // not bonded enough to ward — the robber comes

        // Mark 15 blocks past the pet, robber 3 blocks on the other side of the pet: on approach, within the stall range.
        owner.State.Position = new Vector3f(pet.Position.X + 15f, pet.Position.Y, pet.Position.Z);
        server.SpawnBanditAtForTest(new Vector3f(pet.Position.X - 3f, pet.Position.Y, pet.Position.Z), owner.State.PlayerId);
        var robber = server.Bandits.Last();
        Assert.Equal(BanditPhase.Approach, robber.BanditPhase);

        server.TickCompanionPayoffForTest();
        Assert.True(server.BanditStalledForTest(robber.Id));

        var before = robber.Position;
        for (int i = 0; i < 6; i++)
        {
            server.Tick(0.5); // 3 s — well inside the 8 s stall
        }

        Assert.Equal(before, robber.Position); // held at the pet, not closing on the mark
        Assert.Equal(BanditPhase.Approach, robber.BanditPhase);
    }

    [Fact]
    public void AHighBondCompanionAtYourSide_WardsOffRobbers_ButOnlyWhileItIsClose()
    {
        var server = Started("ward", rules: r => { r.PlanetEnemies = AlienActivity.Off; r.Bandits = AlienActivity.Normal; });
        var owner = Ranger(server);
        var pet = TameOne(server, owner);
        var tc = server.TamedCreaturesForTest(owner.State.PlayerId)[0];
        owner.State.Position = new Vector3f(pet.Position.X + 2f, pet.Position.Y, pet.Position.Z);

        tc.Bond = 69;
        Assert.False(server.BanditWardedByCompanionForTest(owner.State.PlayerId));
        tc.Bond = 70;
        Assert.True(server.BanditWardedByCompanionForTest(owner.State.PlayerId));

        // A robber already on its way thinks better of it.
        server.SpawnBanditAtForTest(new Vector3f(pet.Position.X + 20f, pet.Position.Y, pet.Position.Z), owner.State.PlayerId);
        var robber = server.Bandits.Last();
        server.Tick(0.1);
        Assert.Equal(BanditPhase.Leaving, robber.BanditPhase);

        // Wander off from the pet (beyond the 12-block ward range) and hold-ups are possible again.
        owner.State.Position = new Vector3f(pet.Position.X + 20f, pet.Position.Y, pet.Position.Z);
        Assert.False(server.BanditWardedByCompanionForTest(owner.State.PlayerId));
    }

    // ---------------- produce ----------------

    [Fact]
    public void APresentCompanion_DropsItsSpeciesProduceWhenDue_AndFetchesItToTheOwner()
    {
        var server = Started("produce");
        var owner = Ranger(server);
        var pet = TameOne(server, owner);
        owner.State.Position = new Vector3f(pet.Position.X + 2f, pet.Position.Y, pet.Position.Z);
        server.SpeciesRoster.First(s => s.Id == pet.SpeciesId).DropItem = "iron_ore";

        server.TickCompanionPayoffForTest(); // first scan only arms the timer
        Assert.Empty(server.DropPackets);

        int before = owner.State.Inventory.CountOf("iron_ore");
        server.DueCompanionProduceForTest();
        server.TickCompanionPayoffForTest();
        Assert.Single(server.DropPackets); // one produce packet at the pet's feet…
        server.SweepDropPacketsForTest();  // …which the pet fetches to its owner on the next sweep
        Assert.Equal(before + 1, owner.State.Inventory.CountOf("iron_ore"));
        Assert.Empty(server.DropPackets);

        // Not due again right away.
        server.TickCompanionPayoffForTest();
        Assert.Empty(server.DropPackets);
    }
}
