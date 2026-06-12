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

/// <summary>
/// In-game multiplayer hosting S1: player-name reservation + token-based name verification on join,
/// and the <c>--admins</c> CLI override that lets the in-game host grant itself the Admin role.
/// </summary>
public sealed class NameVerificationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NameVerificationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_nv_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static ServerConfig Config(string world) => new()
    {
        WorldName = world,
        Seed = 4242,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false,
    };

    private static (JoinAccepted? Accepted, JoinRejected? Rejected) Join(
        SvGameServer server, LoopbackClientTransport client, string name, string? token)
    {
        JoinAccepted? accepted = null;
        JoinRejected? rejected = null;
        Action<byte[]> capture = payload =>
        {
            switch (NetCodec.Decode(payload))
            {
                case JoinAccepted a: accepted = a; break;
                case JoinRejected r: rejected = r; break;
            }
        };
        client.PayloadReceived += capture;

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name, Token = token }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        client.PayloadReceived -= capture;
        return (accepted, rejected);
    }

    [Fact]
    public void Join_WhileSameNameIsOnline_IsRejected()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "dup"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var server = new SvGameServer(Config("dup"), _content, serverTransport, repo);
        server.Start();

        // First "Pilot" is online (local session; the loopback transport models one networked client).
        server.AddLocalPlayer("Pilot");

        // A second client joining under the same name (any casing) must be rejected.
        var (accepted, rejected) = Join(server, client, "pilot", token: "other-install");
        Assert.Null(accepted);
        Assert.NotNull(rejected);
        Assert.Contains("online", rejected!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Join_ClaimedName_RequiresTheMatchingToken_AcrossServerRestarts()
    {
        // Session 1: the first join under a name claims it with the client's token.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "claim")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config("claim"), _content, serverTransport, repo);
            server.Start();

            var (accepted, rejected) = Join(server, client, "Host", token: "secret-A");
            Assert.NotNull(accepted);
            Assert.Null(rejected);
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Session 2 (fresh server on the same save): a different token is rejected, the right one joins.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "claim")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config("claim"), _content, serverTransport, repo);
            server.Start();

            var spoof = Join(server, client, "Host", token: "secret-B");
            Assert.Null(spoof.Accepted);
            Assert.NotNull(spoof.Rejected);

            var owner = Join(server, client, "Host", token: "secret-A");
            Assert.NotNull(owner.Accepted);
        }
    }

    [Fact]
    public void Join_UnclaimedName_IsAdoptedByTheFirstToken()
    {
        // Session 1: a tokenless join (old client) leaves the name unclaimed.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "adopt")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config("adopt"), _content, serverTransport, repo);
            server.Start();

            var legacy = Join(server, client, "Veteran", token: null);
            Assert.NotNull(legacy.Accepted);
            Assert.Equal(string.Empty, repo.LoadPlayer("Veteran")!.NameTokenHash);
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Session 2: the first join that brings a token claims the name, and the claim persists.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "adopt")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config("adopt"), _content, serverTransport, repo);
            server.Start();

            var adopt = Join(server, client, "Veteran", token: "new-secret");
            Assert.NotNull(adopt.Accepted);
            Assert.False(string.IsNullOrEmpty(repo.LoadPlayer("Veteran")!.NameTokenHash));
        }
    }

    [Fact]
    public void AdminsCliArg_GrantsAdminRoleOnJoin()
    {
        var config = Config("admins");
        var applied = config.ApplyCommandLine(new[] { "--admins", "Alice, Bob" });
        Assert.Contains("admins", applied);
        Assert.Equal(new[] { "Alice", "Bob" }, config.AdminPlayers);

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "admins"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();

        // The world creator (first ever player) is WorldAdmin regardless of the list…
        var first = server.AddLocalPlayer("Creator");
        Assert.Equal(PlayerRole.WorldAdmin, first.State.Role);

        // …and a listed name gets the Admin role on join (the in-game host passes its own name here).
        var (accepted, _) = Join(server, client, "Alice", token: "alice-secret");
        Assert.NotNull(accepted);
        Assert.Equal(PlayerRole.Admin, server.Sessions[1].State.Role);
    }

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
