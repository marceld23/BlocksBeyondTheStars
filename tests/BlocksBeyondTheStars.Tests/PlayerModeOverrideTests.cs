// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Per-player mode override (#1121): one family world, mixed modes — the kid plays creative
/// (free crafting, flight, no needs, hostiles ignore them) while the parent's survival stays untouched.</summary>
public sealed class PlayerModeOverrideTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlayerModeOverrideTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pmode_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void RuleGetters_ConsultTheOverride()
    {
        var survivalWorld = new GameRules { GameMode = GameMode.Survival };

        // No override: the world's mode governs, and the parameterless getters agree with their twins.
        Assert.Equal(GameMode.Survival, survivalWorld.ModeFor(PlayerModeOverride.None));
        Assert.True(survivalWorld.CraftingCostsMaterialsFor(PlayerModeOverride.None));
        Assert.Equal(survivalWorld.CraftingCostsMaterials, survivalWorld.CraftingCostsMaterialsFor(PlayerModeOverride.None));
        Assert.Equal(survivalWorld.OxygenEnabled, survivalWorld.OxygenEnabledFor(PlayerModeOverride.None));

        // Creative override in a survival world: the kid's rules read like a creative world's.
        Assert.Equal(GameMode.Creative, survivalWorld.ModeFor(PlayerModeOverride.Creative));
        Assert.False(survivalWorld.CraftingCostsMaterialsFor(PlayerModeOverride.Creative));
        Assert.False(survivalWorld.OxygenEnabledFor(PlayerModeOverride.Creative));
        Assert.False(survivalWorld.HungerEnabledFor(PlayerModeOverride.Creative));
        Assert.False(survivalWorld.TemperatureHazardsEnabledFor(PlayerModeOverride.Creative));
        Assert.True(survivalWorld.CreativeFlightFor(PlayerModeOverride.Creative));

        // The reverse: a survival override in a creative world restores the full survival rule set —
        // including flight, even though the creative world itself grants it to everyone else.
        var creativeWorld = new GameRules { GameMode = GameMode.Creative, CreativeFlight = true };
        Assert.Equal(GameMode.Survival, creativeWorld.ModeFor(PlayerModeOverride.Survival));
        Assert.True(creativeWorld.CraftingCostsMaterialsFor(PlayerModeOverride.Survival));
        Assert.True(creativeWorld.OxygenEnabledFor(PlayerModeOverride.Survival));
        Assert.True(creativeWorld.HungerEnabledFor(PlayerModeOverride.Survival));
        Assert.False(creativeWorld.CreativeFlightFor(PlayerModeOverride.Survival));
        Assert.True(creativeWorld.CreativeFlightFor(PlayerModeOverride.None));

        // The world's Off sliders still win over a survival override — the override swaps the MODE,
        // never the world's difficulty settings.
        var softWorld = new GameRules { GameMode = GameMode.Creative, OxygenConsumption = OxygenConsumption.Off };
        Assert.False(softWorld.OxygenEnabledFor(PlayerModeOverride.Survival));
    }

    [Fact]
    public void Snapshot_RoundTripsTheOverride_AndDefendsUnknownValues()
    {
        var p = new PlayerState { PlayerId = "p1", Name = "Kid", ModeOverride = PlayerModeOverride.Creative };
        var restored = StateMapper.FromSnapshot(StateMapper.ToSnapshot(p));
        Assert.Equal(PlayerModeOverride.Creative, restored.ModeOverride);

        // A snapshot from a newer build with an unknown value degrades to None, never throws.
        var snap = StateMapper.ToSnapshot(p);
        snap.ModeOverride = 99;
        Assert.Equal(PlayerModeOverride.None, StateMapper.FromSnapshot(snap).ModeOverride);

        // Hostile targeting treats the creative-override player like god mode / cloak.
        Assert.True(p.IgnoredByHostiles);
        Assert.False(new PlayerState().IgnoredByHostiles);
    }

    [Fact]
    public void AdminCommand_SetsAndClearsTheOverride_NonAdminsAreRefused()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cmd"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        var config = new ServerConfig { WorldName = "cmd", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Papa" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var kid = server.AddLocalPlayer("Kid Junior"); // names contain spaces (#980)

        client.Send(NetCodec.Encode(new AdminCommandIntent
        {
            Command = "set_mode",
            TargetPlayer = "Kid Junior",
            StringArg = "creative",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(PlayerModeOverride.Creative, kid.State.ModeOverride);

        // "world" clears the override.
        client.Send(NetCodec.Encode(new AdminCommandIntent
        {
            Command = "set_mode",
            TargetPlayer = "Kid Junior",
            StringArg = "world",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(PlayerModeOverride.None, kid.State.ModeOverride);

        // A non-admin cannot hand themselves (or anyone) a mode.
        server.Sessions[1].State.Role = PlayerRole.Player;
        client.Send(NetCodec.Encode(new AdminCommandIntent
        {
            Command = "set_mode",
            TargetPlayer = "Kid Junior",
            StringArg = "creative",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(PlayerModeOverride.None, kid.State.ModeOverride);
    }

    [Fact]
    public void CreativeOverride_InSurvivalWorld_CraftsFree_Flies_AndTellsTheClient()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "mix"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        ServerRules? rules = null;
        PlayerStateUpdate? status = null;
        client.PayloadReceived += payload =>
        {
            switch (NetCodec.Decode(payload))
            {
                case ServerRules r: rules = r; break;
                case PlayerStateUpdate u: status = u; break;
            }
        };

        var config = new ServerConfig { WorldName = "mix", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Papa" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        // Survival world: the join-time rules say so, crafting from nothing is refused.
        Assert.Equal("Survival", rules!.GameMode);
        var self = server.Sessions[1].State;
        client.Send(NetCodec.Encode(new CraftIntent { RecipeKey = "iron_ingot", Count = 3 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(0, self.Inventory.CountOf("iron_ingot"));

        // The admin flips their own mode (the family case is a target player; self exercises the same path).
        client.Send(NetCodec.Encode(new AdminCommandIntent
        {
            Command = "set_mode",
            TargetPlayer = "Papa",
            StringArg = "creative",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        // The client was told right away: effective mode, oxygen off, flight on — and the admin's own
        // roster row carries the override for the Settings-tab rows.
        Assert.Equal("Creative", rules!.GameMode);
        Assert.False(rules.OxygenEnabled);
        Assert.True(status!.CanFly);
        int row = Array.IndexOf(rules.PlayerModeNames, "Papa");
        Assert.True(row >= 0, "admin receivers get the player-mode roster");
        Assert.Equal("Creative", rules.PlayerModeValues[row]);

        // And the survival world now crafts free for exactly this player.
        client.Send(NetCodec.Encode(new CraftIntent { RecipeKey = "iron_ingot", Count = 3 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(3, self.Inventory.CountOf("iron_ingot"));
    }

    [Fact]
    public void Override_SurvivesARestart()
    {
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "keep"));
            var link = new LoopbackLink();
            using var st = new LoopbackServerTransport(link);
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(new ServerConfig { WorldName = "keep", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Papa" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Send(NetCodec.Encode(new AdminCommandIntent
            {
                Command = "set_mode",
                TargetPlayer = "Papa",
                StringArg = "creative",
            }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            repo.Flush();
        }

        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "keep"));
            var link = new LoopbackLink();
            using var st = new LoopbackServerTransport(link);
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(new ServerConfig { WorldName = "keep", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Papa" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal(PlayerModeOverride.Creative, server.Sessions[1].State.ModeOverride);
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
