// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class GameModeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_mode_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void Preset_PeacefulCreative_HasExpectedRules()
    {
        var rules = ServerPresets.Get("peaceful-creative")!;
        Assert.Equal(GameMode.Creative, rules.GameMode);
        Assert.False(rules.OxygenEnabled);
        Assert.False(rules.CraftingCostsMaterials);
        Assert.Equal(WeaponMode.None, rules.WeaponMode);
    }

    [Fact]
    public void Rules_OxygenDrain_MatchesConfiguredRate()
    {
        Assert.True(new GameRules { OxygenConsumption = OxygenConsumption.Fast }.OxygenDrainPerSecond > 0);
        Assert.False(new GameRules { GameMode = GameMode.Creative }.OxygenEnabled);
        Assert.Equal(0f, new GameRules { OxygenConsumption = OxygenConsumption.Off }.OxygenDrainPerSecond);
    }

    [Fact]
    public void CreativeMode_CraftsWithoutMaterials()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cr"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        var config = new ServerConfig { WorldName = "cr", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules.GameMode = GameMode.Creative;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Builder" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var player = server.Sessions[1].State;
        Assert.Equal(0, player.Inventory.CountOf("iron_ore"));

        client.Send(NetCodec.Encode(new CraftIntent { RecipeKey = "iron_ingot", Count = 3 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // No materials were present, but Creative mode produces the output for free.
        Assert.Equal(3, player.Inventory.CountOf("iron_ingot"));
    }

    [Fact]
    public void ServerRules_AreSentOnJoin()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rules"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        ServerRules? received = null;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is ServerRules r) received = r; };

        var config = new ServerConfig { WorldName = "rules", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules = ServerPresets.Get("coop-survival")!;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(received);
        Assert.Equal("Survival", received!.GameMode);
        Assert.True(received.OxygenEnabled);
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
