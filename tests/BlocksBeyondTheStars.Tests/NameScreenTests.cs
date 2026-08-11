// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.Notifications;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Shared name screening (issue #938): the block list must catch leetspeak/separator/diacritic
/// evasions, the watch list must FLAG (never block) ambiguous terms with token semantics that keep
/// everyday names clean, and the game-server join gate must enforce the same screen on direct-connect
/// servers that the WorldHost enforces on hosted ones.
/// </summary>
public sealed class NameScreenTests : IDisposable
{
    private readonly string _root;

    public NameScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ns_" + Guid.NewGuid().ToString("N"));
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

    // ── Block list: evasions ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hitler")]
    [InlineData("Hitler88")]
    [InlineData("H-i-t-l-e-r")]
    [InlineData("h.i.t.l.e.r")] // dot separators — the old space/-/_ normalization missed these
    [InlineData("h1tl3r")] // leetspeak digits, 1→i
    [InlineData("hit1er")] // leetspeak digits, 1→l
    [InlineData("hïtler")] // diacritic folding
    [InlineData("n4z1")] // leet on a 4-letter word
    [InlineData("xX fuck Xx")]
    [InlineData("N_a_z_i")]
    [InlineData("fuuck")] // repeated-letter collapse
    [InlineData("1488")] // unambiguous code — block, not watch
    [InlineData("SiegHeil")]
    public void BlockList_CatchesEvasions(string name)
    {
        var screen = new NameScreen();
        Assert.Equal(NameVerdict.Block, screen.Screen(name).Verdict);
    }

    [Theory]
    [InlineData("Hilda")] // contains no blocked substring; fuzzy distance to "hitler" is > 1
    [InlineData("Rudolf")] // distance 2 from "adolf" — must NOT fuzzy-flag
    [InlineData("Tom1988")] // "88" only matches as a WHOLE token; "1988" is one token
    [InlineData("Builder_Bob")]
    [InlineData("Supporter")] // '=' pins "support" to token-only
    [InlineData("Staffan")] // same for "staff"
    [InlineData("Massimo")]
    [InlineData("")]
    [InlineData(null)]
    public void EverydayNames_PassClean(string? name)
    {
        var screen = new NameScreen();
        Assert.Equal(NameVerdict.Ok, screen.Screen(name).Verdict);
    }

    // ── Watch list: flag, never block ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("afd")] // party abbreviation as the whole name
    [InlineData("AfD-Fan")] // …and as a token
    [InlineData("Max88")] // number code as its own token
    [InlineData("xXadolfXx")] // ≥5-char watch entries also match as substrings
    [InlineData("Adof_Berg")] // fuzzy: one-letter miss of a high-severity core name
    [InlineData("adm1n")] // leet-folded token
    [InlineData("Dahmer")]
    public void WatchList_FlagsButDoesNotBlock(string name)
    {
        var screen = new NameScreen();
        var result = screen.Screen(name);
        Assert.Equal(NameVerdict.Watch, result.Verdict);
        Assert.NotEqual(string.Empty, result.MatchedTerm);
    }

    [Fact]
    public void BlockBeatsWatch()
    {
        // "hitler88" carries both a blocked word and a watch code — the verdict must be Block.
        var result = new NameScreen().Screen("hitler88");
        Assert.Equal(NameVerdict.Block, result.Verdict);
    }

    [Fact]
    public void OperatorExtensions_UseTheSameSemantics()
    {
        var screen = new NameScreen(
            blockedWords: new[] { "kackwurst" },
            watchWords: new[] { "=test" });

        Assert.Equal(NameVerdict.Block, screen.Screen("Kack-Wurst").Verdict); // separator folding applies to extensions
        Assert.Equal(NameVerdict.Watch, screen.Screen("test").Verdict); // token-only pin
        Assert.Equal(NameVerdict.Ok, screen.Screen("Tester").Verdict); // …so the substring does NOT flag
        Assert.Equal(NameVerdict.Ok, screen.Screen("fuck").Verdict); // custom lists REPLACE the defaults…
        Assert.Equal(NameVerdict.Watch, screen.Screen("hitler").Verdict); // …but the fuzzy high-severity core stays as a backstop
    }

    [Fact]
    public void Tokenize_SplitsSeparatorsAndLetterDigitBoundaries()
    {
        Assert.Equal(new[] { "xx", "max", "88" }, NameScreen.Tokenize("xX_Max88"));
        Assert.Equal(new[] { "tom", "1988" }, NameScreen.Tokenize("Tom1988"));
    }

    // ── AdminNotifier header hygiene ─────────────────────────────────────────────────────────────

    [Fact]
    public void NotifierHeaderValue_IsSingleLineAsciiAndCapped()
    {
        // Player/world names travel into the notification title — umlauts, emoji and smuggled
        // newlines must never reach an HTTP header raw.
        Assert.Equal("W?rld?name", AdminNotifier.HeaderValue("Wörld\nname", 40));
        Assert.Equal("abc", AdminNotifier.HeaderValue("  abc  ", 40));
        Assert.Equal("aaaa", AdminNotifier.HeaderValue("aaaaaa", 4));
        Assert.Equal(string.Empty, AdminNotifier.HeaderValue(null, 4));
    }

    // ── Game-server join gate (#938): the only gate on direct-connect/self-hosted servers ────────

    [Fact]
    public void Join_BlockedName_IsRejected_AndWatchedNameIsAllowed()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "gate"));
        var link = new LoopbackLink();
        using var serverTransport = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        var config = new ServerConfig
        {
            WorldName = "gate",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 4,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, ContentLoader.LoadFromDirectory(TestPaths.DataDir()), serverTransport, repo);
        server.Start();

        var blocked = Join(server, client, "h1tl3r");
        Assert.Null(blocked.Accepted);
        Assert.NotNull(blocked.Rejected);
        Assert.Equal("@srv.join.name_blocked", blocked.Rejected!.Reason);

        // A watch-list name joins normally — flagging is operator-side only (log + optional ping).
        var watched = Join(server, client, "Max88");
        Assert.NotNull(watched.Accepted);
        Assert.Null(watched.Rejected);
    }

    private static (JoinAccepted? Accepted, JoinRejected? Rejected) Join(
        SvGameServer server, LoopbackClientTransport client, string name)
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
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name, Token = "install-" + name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        client.PayloadReceived -= capture;
        return (accepted, rejected);
    }
}
