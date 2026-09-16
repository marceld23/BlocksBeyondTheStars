// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1927 ("switch between Explorer, Creative and Sandbox with chat commands"): <c>/gamemode</c> sets on a running world the
/// switches the new-world screen bakes in at creation, tells every online player at once and is saved with the world.
/// </summary>
public sealed class AdminWorldModeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_worldmode_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static void Command(LoopbackClientTransport client, SvGameServer server, string mode)
    {
        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "set_world_mode", StringArg = mode }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    [Theory]
    [InlineData("explorer", "explorer")]
    [InlineData("Entdecker", "explorer")]
    [InlineData("survival", "explorer")]
    [InlineData("kreativ", "creative")]
    [InlineData("SANDBOX", "sandbox")]
    [InlineData("sandkasten", "sandbox")]
    [InlineData("minecraft", null)]
    public void ModeWords_EnglishAndGerman(string word, string? mode)
    {
        Assert.Equal(mode, SvGameServer.WorldModeFromWord(word));
    }

    [Fact]
    public void Sandbox_ThenExplorer_SetTheRules_ReachTheClientAtOnce_AndKeepWhatWasGranted()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "modes"));
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

        var server = new SvGameServer(new BlocksBeyondTheStars.Shared.Configuration.ServerConfig { WorldName = "modes", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        var self = server.Sessions[1].State;
        int unlockedBefore = self.UnlockedBlueprints.Count;
        Assert.Equal("explorer", server.WorldModeForTest());
        Assert.Equal("Survival", rules!.GameMode);

        Command(client, server, "sandbox");

        Assert.Equal("sandbox", server.WorldModeForTest());
        Assert.Equal("Creative", rules!.GameMode);
        Assert.False(rules.OxygenEnabled);
        Assert.True(status!.CanFly);
        int unlockedAll = self.UnlockedBlueprints.Count;
        Assert.True(unlockedAll > unlockedBefore, "sandbox unlocks every blueprint for the players online");

        Command(client, server, "explorer");

        Assert.Equal("explorer", server.WorldModeForTest());
        Assert.Equal("Survival", rules!.GameMode);
        Assert.False(status!.CanFly);
        Assert.Equal(unlockedAll, self.UnlockedBlueprints.Count); // nothing granted is taken back

        Command(client, server, "creative");
        Assert.Equal("creative", server.WorldModeForTest());
        Assert.Equal("Survival", rules!.GameMode); // Creative is the head start: survival rules stay on
        Assert.True(status!.CanFly);
    }

    [Fact]
    public void TheMode_IsSavedWithTheWorld()
    {
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "keep"));
            var link = new LoopbackLink();
            using var st = new LoopbackServerTransport(link);
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(new BlocksBeyondTheStars.Shared.Configuration.ServerConfig { WorldName = "keep", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            Command(client, server, "sandbox");
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var again = new SqliteWorldRepository(new SaveGamePaths(_root, "keep"));
        var restarted = new SvGameServer(new BlocksBeyondTheStars.Shared.Configuration.ServerConfig { WorldName = "keep", Seed = 1, AutoSaveIntervalMinutes = 9999 },
            _content, new LoopbackServerTransport(new LoopbackLink()), again);
        restarted.Start();
        Assert.Equal("sandbox", restarted.WorldModeForTest());
    }

    [Fact]
    public void ANonAdmin_CannotChangeTheWorldMode()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "guest"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(new BlocksBeyondTheStars.Shared.Configuration.ServerConfig { WorldName = "guest", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Guest" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.Role = PlayerRole.Player;

        Command(client, server, "sandbox");

        Assert.Equal("explorer", server.WorldModeForTest());
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
        }
    }
}
