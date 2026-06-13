using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Player alliance handshake, lifecycle and persistence. Allies co-own each other's stations/bases and
/// cannot harm one another; the relationship is pairwise + mutual and survives a server restart.</summary>
public sealed class AllianceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AllianceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ally_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>A started server with two joined local players ("Alice", "Bob").</summary>
    private SvGameServer NewServer(out SqliteWorldRepository repo, string tag = "a")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        return server;
    }

    [Fact]
    public void RequestThenAccept_AlliesBothPlayers()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.RequestAlliance("Alice", "Bob");
            Assert.False(server.AreAllied("Alice", "Bob")); // pending handshake, not allied yet

            server.RespondAlliance("Bob", "Alice", accept: true);

            Assert.True(server.AreAllied("Alice", "Bob"));
            Assert.True(server.AreAllied("Bob", "Alice")); // symmetric
        }
    }

    [Fact]
    public void Decline_DoesNotAlly()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: false);

            Assert.False(server.AreAllied("Alice", "Bob"));
        }
    }

    [Fact]
    public void MutualRequest_AutoAccepts()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            // Both sides ask independently — the second request meets the first's pending and forms the alliance.
            server.RequestAlliance("Alice", "Bob");
            server.RequestAlliance("Bob", "Alice");

            Assert.True(server.AreAllied("Alice", "Bob"));
        }
    }

    [Fact]
    public void Dissolve_EndsAllianceFromEitherSide()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);
            Assert.True(server.AreAllied("Alice", "Bob"));

            // The partner who did not initiate can still end it.
            server.DissolveAlliance("Bob", "Alice");

            Assert.False(server.AreAllied("Alice", "Bob"));
            Assert.False(server.AreAllied("Bob", "Alice"));
        }
    }

    [Fact]
    public void SelfAlliance_IsRejected()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.RequestAlliance("Alice", "Alice");
            server.RespondAlliance("Alice", "Alice", accept: true);
            Assert.False(server.AreAllied("Alice", "Alice"));
        }
    }

    [Fact]
    public void Alliance_PersistsAcrossReload()
    {
        var paths = new SaveGamePaths(_root, "persist");

        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.AddLocalPlayer("Bob");

            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);
            Assert.True(server.AreAllied("Alice", "Bob"));
        }

        // A fresh server on the same save restores the alliance graph at Start.
        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var config2 = new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 };
            var server2 = new SvGameServer(config2, _content, st2, repo2);
            server2.Start();

            Assert.True(server2.AreAllied("Alice", "Bob"));
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
