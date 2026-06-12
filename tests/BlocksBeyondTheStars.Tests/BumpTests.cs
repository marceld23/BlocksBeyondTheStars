using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The /bump debug command writes a persisted diagnostic snapshot (no comm radio needed).</summary>
public sealed class BumpTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BumpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bump_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void BumpCommand_WritesPersistedSnapshot()
    {
        var paths = new SaveGamePaths(_root, "bumpworld");
        using var repo = new SqliteWorldRepository(paths);
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "bumpworld", Seed = 1, AutoSaveIntervalMinutes = 9999 };

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Tester" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, server.BumpsWritten);

        // /bump works without a comm radio (it's intercepted before the chat gate).
        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump the ship interior glitches with flora" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);

        var dir = Path.Combine(paths.WorldDirectory, "bumps");
        Assert.True(Directory.Exists(dir));
        var files = Directory.GetFiles(dir, "bump_*.json");
        Assert.Single(files);

        string json = File.ReadAllText(files[0]);
        Assert.Contains("the ship interior glitches with flora", json); // description captured
        Assert.Contains("environment", json);                            // env snapshot present
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
