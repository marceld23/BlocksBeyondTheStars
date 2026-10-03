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
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The content fingerprint of the join (#2222). Chunks travel as raw numeric block ids and the ids follow the
/// sorted block keys, so a client and a server with different block sets draw each other's blocks wrong. The
/// fingerprint names the block palette; the server refuses a join whose fingerprint is missing or different, and
/// tells the client its own so the client can check the other direction (ContentFingerprintClientTests).
/// </summary>
public sealed class ContentFingerprintTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ContentFingerprintTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fingerprint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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
            // best effort
        }
    }

    private static GameContent ContentOf(params string[] blockKeys)
        => new(
            blocks: blockKeys.Select(k => new BlockDefinition { Key = k }).ToArray(),
            items: Array.Empty<ItemDefinition>(),
            recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>());

    // ---------------- The fingerprint itself ----------------

    [Fact]
    public void Fingerprint_IsFnv1a64OverTheKeysInIdOrder()
    {
        // Known answers of FNV-1a (64 bit): the hash of "a", and of "a\nb" — keys in numeric-id order, joined by '\n'.
        Assert.Equal("af63dc4c8601ec8c", ContentOf("a").BlockFingerprint);

        ulong hash = 14695981039346656037UL;
        foreach (byte b in "air\nstone\ntorch"u8)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        // Air is id 0, the others follow in ordinal key order — whatever order the data files list them in.
        Assert.Equal(hash.ToString("x16"), ContentOf("torch", "air", "stone").BlockFingerprint);
    }

    [Fact]
    public void Fingerprint_IsTheSameForTheSameBlockSet_AndDiffersForAnyOther()
    {
        string baseline = ContentOf("air", "stone", "torch").BlockFingerprint;

        Assert.Equal(baseline, ContentOf("stone", "torch", "air").BlockFingerprint); // input order does not matter
        Assert.Equal(_content.BlockFingerprint, ContentLoader.LoadFromDirectory(TestPaths.DataDir()).BlockFingerprint);

        Assert.NotEqual(baseline, ContentOf("air", "bio_lab", "stone", "torch").BlockFingerprint); // a block was added
        Assert.NotEqual(baseline, ContentOf("air", "stone").BlockFingerprint);                     // one was removed
        Assert.NotEqual(baseline, ContentOf("air", "stone", "torcH").BlockFingerprint);            // one was renamed
        Assert.NotEqual(ContentOf("ab", "c").BlockFingerprint, ContentOf("a", "bc").BlockFingerprint); // the separator counts
    }

    [Fact]
    public void Fingerprint_OfTheShippedContent_IsSixteenLowercaseHexDigits()
    {
        Assert.Matches("^[0-9a-f]{16}$", _content.BlockFingerprint);
        Assert.Equal(_content.BlockFingerprint, TestJoin.Fingerprint);
    }

    // ---------------- The wire ----------------

    [Fact]
    public void Fingerprint_SurvivesBothCodecs_OnRequestAndAnswer()
    {
        var request = new JoinRequest { PlayerName = "Pilot", ContentFingerprint = "0123456789abcdef" };
        Assert.Equal("0123456789abcdef", Assert.IsType<JoinRequest>(NetCodec.Decode(NetCodec.Encode(request))).ContentFingerprint);
        Assert.Equal("0123456789abcdef", Assert.IsType<JoinRequest>(NetCodec.Decode(NetCodec.EncodeJson(request))).ContentFingerprint);

        var answer = new JoinAccepted { PlayerId = "Pilot", ContentFingerprint = "fedcba9876543210" };
        Assert.Equal("fedcba9876543210", Assert.IsType<JoinAccepted>(NetCodec.Decode(NetCodec.Encode(answer))).ContentFingerprint);
        Assert.Equal("fedcba9876543210", Assert.IsType<JoinAccepted>(NetCodec.Decode(NetCodec.EncodeJson(answer))).ContentFingerprint);

        // A join that never set it carries none — that is what "missing" means on the server.
        Assert.Equal(string.Empty, new JoinRequest().ContentFingerprint);
    }

    // ---------------- The join gate ----------------

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) NewServer(string world)
    {
        var link = new LoopbackLink();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link), repo, _log);
        server.Start();
        return (server, new LoopbackClientTransport(link), repo);
    }

    /// <summary>Keeps what the server logs, so a test can read what an operator would.</summary>
    private sealed class CapturingLogger : BlocksBeyondTheStars.GameServer.IGameLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message)
        {
        }
    }

    private readonly CapturingLogger _log = new();

    private static (JoinAccepted? Accepted, JoinRejected? Rejected) Join(SvGameServer server, LoopbackClientTransport client, JoinRequest request)
    {
        JoinAccepted? accepted = null;
        JoinRejected? rejected = null;
        client.PayloadReceived += payload =>
        {
            switch (NetCodec.Decode(payload))
            {
                case JoinAccepted a: accepted = a; break;
                case JoinRejected r: rejected = r; break;
            }
        };
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(request), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        return (accepted, rejected);
    }

    [Fact]
    public void Join_WithTheServersFingerprint_IsAccepted_AndTheAnswerNamesIt()
    {
        var (server, client, repo) = NewServer("match");
        using (repo)
        using (client)
        {
            var (accepted, rejected) = Join(server, client, new JoinRequest { PlayerName = "Pilot", ContentFingerprint = _content.BlockFingerprint });

            Assert.Null(rejected);
            Assert.NotNull(accepted);
            Assert.Equal(_content.BlockFingerprint, accepted!.ContentFingerprint);
            Assert.True(server.Sessions[1].Joined);
            server.Stop();
        }
    }

    [Theory]
    [InlineData("")]                 // a client that sends none
    [InlineData("0123456789abcdef")] // a client built with another block set
    public void Join_WithAMissingOrDifferentFingerprint_IsRefused(string fingerprint)
    {
        var (server, client, repo) = NewServer("mismatch");
        using (repo)
        using (client)
        {
            var (accepted, rejected) = Join(server, client, new JoinRequest { PlayerName = "Pilot", ContentFingerprint = fingerprint });

            Assert.Null(accepted);
            Assert.NotNull(rejected);
            Assert.Equal(Protocol.ContentMismatchReason, rejected!.Reason); // the client shows it as "please update"
            Assert.DoesNotContain(server.Sessions.Values, s => s.Joined);
            Assert.Null(repo.LoadPlayer("Pilot")); // refused before any player record is touched
            server.Stop();
        }
    }

    [Fact]
    public void Refusals_AreLoggedOncePerFingerprint_AndAFloodCannotFillTheLog()
    {
        var (server, client, repo) = NewServer("flood");
        using (repo)
        using (client)
        {
            // The same outdated client retrying, then a flood of made-up fingerprints (one with a line break in it).
            for (int i = 0; i < 5; i++)
            {
                server.HandlePayloadForTest(100 + i, NetCodec.Encode(new JoinRequest { PlayerName = "Pilot", ContentFingerprint = "0123456789abcdef" }));
            }

            Assert.Single(_log.Warnings, w => w.Contains("0123456789abcdef"));
            Assert.Contains(_content.BlockFingerprint, _log.Warnings[0]); // names both sides: what came and what this server has

            server.HandlePayloadForTest(200, NetCodec.Encode(new JoinRequest { PlayerName = "Pilot", ContentFingerprint = "evil\nINFO forged line" }));
            Assert.DoesNotContain(_log.Warnings, w => w.Contains('\n'));

            for (int i = 0; i < 200; i++)
            {
                server.HandlePayloadForTest(300 + i, NetCodec.Encode(new JoinRequest { PlayerName = "Pilot", ContentFingerprint = "flood-" + i }));
            }

            Assert.InRange(_log.Warnings.Count, 2, 16);
            server.Stop();
        }
    }

    [Fact]
    public void Join_WithAnotherProtocolVersion_IsRefusedByTheVersionCheck_BeforeTheFingerprint()
    {
        // A client released before the fingerprint (protocol 8) sends none — it must read the version refusal it
        // has always understood, not the new reason.
        var (server, client, repo) = NewServer("oldclient");
        using (repo)
        using (client)
        {
            var (accepted, rejected) = Join(server, client, new JoinRequest { PlayerName = "Pilot", ProtocolVersion = 8 });

            Assert.Null(accepted);
            Assert.NotNull(rejected);
            Assert.NotEqual(Protocol.ContentMismatchReason, rejected!.Reason);
            Assert.Contains("8", rejected.Reason);
            server.Stop();
        }
    }
}
