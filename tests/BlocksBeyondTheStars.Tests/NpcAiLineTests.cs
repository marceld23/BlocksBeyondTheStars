// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2591 — the ONE AI path for every new NPC/VEGA line (<c>POST /npc-lines</c>, GameServerAiLines.cs). The authored
/// line is always the truth; an AI answer is only used when it passes every check (line count, length, allowed and
/// required placeholder tokens, no digits outside tokens, the content screen), its tokens are filled per receiving
/// player, and a per-world budget with priorities bounds the cost. AiLevel Off never calls the backend.
/// </summary>
public sealed class NpcAiLineTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_ailines_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    private static readonly string[] PlayerOnly = { AiLineTokens.Player };
    private static readonly string[] NoTokens = Array.Empty<string>();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { /* best effort */ }
    }

    // ---------------- Validation (pure) ----------------

    [Fact]
    public void Validate_AcceptsAGoodAnswer_AndTrimsQuotesAndWhitespace()
    {
        var lines = AiLineText.Validate(new[] { "  \"Hello, {player}!\"  ", "„Gut für die Beete,\n {a}!“" }, 2, 56,
            new[] { AiLineTokens.Player, AiLineTokens.A }, PlayerOnly);

        Assert.NotNull(lines);
        Assert.Equal(new[] { "Hello, {player}!", "Gut für die Beete, {a}!" }, lines);
    }

    [Fact]
    public void Validate_RejectsAWrongLineCount()
    {
        Assert.Null(AiLineText.Validate(new[] { "One.", "Two." }, 1, 56, NoTokens, NoTokens));
        Assert.Null(AiLineText.Validate(new[] { "One." }, 2, 56, NoTokens, NoTokens));
        Assert.Null(AiLineText.Validate(null, 1, 56, NoTokens, NoTokens));
        Assert.Null(AiLineText.Validate(new[] { "   \"\"  " }, 1, 56, NoTokens, NoTokens)); // empty after trimming
    }

    [Fact]
    public void Validate_RejectsUnknownTokens_StrayBraces_AndDigits()
    {
        Assert.Null(AiLineText.Validate(new[] { "Hi {name}!" }, 1, 56, PlayerOnly, NoTokens));    // unknown token
        Assert.Null(AiLineText.Validate(new[] { "Hi {player!" }, 1, 56, PlayerOnly, NoTokens));    // unclosed
        Assert.Null(AiLineText.Validate(new[] { "Hi player}!" }, 1, 56, PlayerOnly, NoTokens));    // stray close
        Assert.Null(AiLineText.Validate(new[] { "Hi {{player}}!" }, 1, 56, PlayerOnly, NoTokens)); // nested
        Assert.Null(AiLineText.Validate(new[] { "Bring me 5 berries, {player}." }, 1, 80, PlayerOnly, NoTokens));
        Assert.Null(AiLineText.Validate(new[] { "Bring me ５ berries." }, 1, 80, NoTokens, NoTokens)); // full-width digit

        // A digit INSIDE a token is fine: {0} is a positional token.
        Assert.NotNull(AiLineText.Validate(new[] { "{0} moved into {1}!" }, 1, 80, new[] { "{0}", "{1}" }, new[] { "{0}" }));
    }

    [Fact]
    public void Validate_RequiresEveryRequiredToken_SomewhereInTheSet()
    {
        var allowed = new[] { AiLineTokens.Player, AiLineTokens.A, AiLineTokens.B };
        Assert.Null(AiLineText.Validate(new[] { "Rain again!", "Good for the gardens." }, 2, 56, allowed, PlayerOnly));
        Assert.NotNull(AiLineText.Validate(new[] { "Look, it's {player}!", "Hello there!" }, 2, 56, allowed, PlayerOnly));
    }

    [Fact]
    public void Validate_KeepsSlack_ThenCutsAtAWordBoundary()
    {
        string withinSlack = new string('a', 10) + " " + new string('b', 50); // 61 chars, max 50 + 16 slack
        Assert.Equal(withinSlack, AiLineText.Validate(new[] { withinSlack }, 1, 50, NoTokens, NoTokens)![0]);

        string tooLong = "Welcome back to our little village, {player}, the gardens missed you so very much today!";
        var cut = AiLineText.Validate(new[] { tooLong }, 1, 40, PlayerOnly, NoTokens)![0];
        Assert.True(cut.Length <= 40, cut);
        Assert.Equal("Welcome back to our little village…", cut); // at a space, trailing comma dropped

        // A cut that loses a required token drops the whole answer.
        string late = "Our gardens are growing so well this year that everyone smiles, {player}!";
        Assert.Null(AiLineText.Validate(new[] { late }, 1, 30, PlayerOnly, PlayerOnly));
    }

    [Fact]
    public void Validate_HardCut_WithoutSpaces_NeverSplitsAToken()
    {
        // CJK text has no spaces: the cut is hard, but it moves in front of a token it would split.
        string cjk = new string('春', 20) + "{player}" + new string('花', 20);
        var line = AiLineText.Validate(new[] { cjk }, 1, 24, PlayerOnly, NoTokens)![0];
        Assert.Equal(new string('春', 20) + "…", line);
        Assert.True(AiLineText.TokensAllowed(line, PlayerOnly));
    }

    [Fact]
    public void TokensIn_ListsTheAuthoredTokens()
        => Assert.Equal(new[] { "{player}", "{place}", "{0}" },
            AiLineText.TokensIn(new[] { "Hi {player}, welcome to {place}!", "{0} and {player}" }));

    // ---------------- Filling tokens per receiver (pure) ----------------

    [Fact]
    public void FillTokens_FillsByTokenOrBareName_AndRefusesLeftovers()
    {
        var values = new Dictionary<string, string> { ["{player}"] = "Justus", ["count"] = "5" };
        Assert.Equal("Bring me 5 berries, Justus!", AiLineText.FillTokens("Bring me {count} berries, {player}!", values));
        Assert.Null(AiLineText.FillTokens("Hi {item}!", values));          // no value → authored line instead
        Assert.Null(AiLineText.FillTokens("Hi {player}!}", values));       // leftover brace
        Assert.Null(AiLineText.FillTokens("Hi {player", values));

        // A value is inserted verbatim and never scanned again: a player called "{count}" cannot inject a token.
        var tricky = new Dictionary<string, string> { ["{player}"] = "{count}", ["{count}"] = "99" };
        Assert.Equal("Hello, {count}!", AiLineText.FillTokens("Hello, {player}!", tricky));

        Assert.Equal(new[] { "A Justus", "B 5" }, AiLineText.FillTokens(new[] { "A {player}", "B {count}" }, values));
        Assert.Null(AiLineText.FillTokens(new[] { "A {player}", "B {thing}" }, values));
    }

    // ---------------- Budget (pure) ----------------

    [Fact]
    public void Budget_Reserves_FollowThePriorityTable()
    {
        Assert.Equal(0, AiLineBudget.ReserveFor(AiLinePriority.PlayerInitiated));
        Assert.Equal(1, AiLineBudget.ReserveFor(AiLinePriority.Personal));
        Assert.Equal(1, AiLineBudget.ReserveFor(AiLinePriority.Vega));
        Assert.Equal(2, AiLineBudget.ReserveFor(AiLinePriority.News));
        Assert.Equal(3, AiLineBudget.ReserveFor(AiLinePriority.Chatter));
    }

    [Fact]
    public void Budget_CapsRequestsInFlight_AtTwo()
    {
        var budget = new AiLineBudget();
        Assert.True(budget.TryTake(AiLinePriority.PlayerInitiated, 0, 4));
        Assert.True(budget.TryTake(AiLinePriority.PlayerInitiated, 0, 4));
        Assert.False(budget.TryTake(AiLinePriority.PlayerInitiated, 0, 4)); // tokens left, but two are running
        Assert.Equal(2, budget.InFlight);

        budget.Release();
        Assert.True(budget.TryTake(AiLinePriority.PlayerInitiated, 0, 4));
        Assert.Equal(3.0, budget.TokensAt(0, 4), 6); // a finished request does not refund its token
    }

    [Fact]
    public void Budget_RateZero_RefusesEverything()
        => Assert.False(new AiLineBudget().TryTake(AiLinePriority.PlayerInitiated, 0, 0));

    // ---------------- Variant cache (pure) ----------------

    [Fact]
    public void VariantCache_IsBounded_FirstInFirstOut()
    {
        var cache = new AiVariantCache<string>(capacity: 4, maxVariants: 3, (a, b) => a == b);
        for (int i = 0; i < 6; i++)
        {
            cache.Add("k" + i, "line");
        }

        Assert.Equal(4, cache.Count);
        Assert.Null(cache.Pick("k0"));  // the oldest went first
        Assert.Null(cache.Pick("k1"));
        Assert.Equal("line", cache.Pick("k5"));
    }

    [Fact]
    public void VariantCache_HoldsAtMostThreeDistinctVariants()
    {
        var cache = new AiVariantCache<string>(capacity: 8, maxVariants: 3, (a, b) => a == b);
        Assert.True(cache.Add("k", "a"));
        Assert.False(cache.Add("k", "a")); // the same line twice is one variant
        Assert.True(cache.Add("k", "b"));
        Assert.True(cache.Add("k", "c"));
        Assert.False(cache.Add("k", "d")); // full
        Assert.Equal(3, cache.VariantCount("k"));

        var picked = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            picked.Add(cache.Pick("k")!);
        }

        Assert.Equal(3, picked.Count); // picks rotate over all variants
        Assert.Equal("b", cache.PickAt("k", 1));
        Assert.Equal(1, cache.RemoveWhere(k => k.StartsWith("k", StringComparison.Ordinal)));
        Assert.Equal(0, cache.Count);
    }

    // ---------------- Persona index ----------------

    [Fact]
    public void PersonaIndex_IsStable_AndSeparatesOrganicRobotAndGuardian()
    {
        Assert.Equal(SvGameServer.PersonaIndexFor("settle_1#bed@1.2.3:settler", false, "settler"),
            SvGameServer.PersonaIndexFor("settle_1#bed@1.2.3:settler", false, "settler"));

        var organic = Enumerable.Range(0, 60).Select(i => SvGameServer.PersonaIndexFor($"settle_9#bed@{i}.0.0:settler", false, "settler")).ToList();
        Assert.All(organic, i => Assert.InRange(i, 0, 9));
        Assert.True(organic.Distinct().Count() >= 6); // ~10 kid-friendly voices are actually used

        var robot = Enumerable.Range(0, 30).Select(i => SvGameServer.PersonaIndexFor($"r{i}", true, "vendor")).ToList();
        Assert.All(robot, i => Assert.InRange(i, 10, 12));
        Assert.Equal(13, SvGameServer.PersonaIndexFor("anything", false, "guardian"));
    }

    // ---------------- Providers ----------------

    private static NpcLinesRequest ChatterRequest() => new()
    {
        Purpose = NpcLinePurposes.Chatter,
        Language = "de",
        Speakers = { new NpcLinesSpeaker { Token = AiLineTokens.A, Role = "settler", Job = "gardener" } },
        Tokens = { AiLineTokens.A },
        Authored = { "Schon wieder Regen!", "Gut für die Beete, {a}!" },
        LineCount = 2,
        MaxChars = 56,
    };

    [Fact]
    public void HttpProvider_PostsToNpcLines_WithPascalCaseJson_AndReadsLines()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "{\"Lines\":[\"Regen!\",\"Prima, {a}!\"]}");
        var provider = new HttpAiMissionProvider("http://test.local/", new HttpClient(handler));

        Assert.Equal(new[] { "Regen!", "Prima, {a}!" }, provider.GenerateNpcLines(ChatterRequest()));
        Assert.Equal("http://test.local/npc-lines", handler.Url);
        Assert.Contains("\"Purpose\":\"chatter\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"Speakers\":[{\"Token\":\"{a}\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"LineCount\":2", handler.RequestBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"detail\":\"Not Found\"}")] // a backend from before /npc-lines
    [InlineData(HttpStatusCode.OK, "{\"Lines\":[]}")]                    // a backend without a model
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"Lines\":[\"x\"]}")]
    public void HttpProvider_NoUsableAnswer_IsNull(HttpStatusCode code, string body)
        => Assert.Null(new HttpAiMissionProvider("http://test.local", new HttpClient(new StubHttpHandler(code, body))).GenerateNpcLines(ChatterRequest()));

    [Fact]
    public void NullProvider_HasNoLines()
        => Assert.Null(new NullAiMissionProvider().GenerateNpcLines(ChatterRequest()));

    [Fact]
    public void ScreenedProvider_DropsTheWholeSet_WhenOneLineIsRefused()
    {
        var inner = new FakeNpcLines(_ => new[] { "All good.", "bad words" });
        Assert.Null(new ScreenedAiTextProvider(inner, t => t == "bad words" ? null : t).GenerateNpcLines(ChatterRequest()));
        Assert.Equal(new[] { "All good.", "bad words" }, new ScreenedAiTextProvider(inner, t => t).GenerateNpcLines(ChatterRequest()));
    }

    // ---------------- The server path ----------------

    private SvGameServer NewServer(string name, AiLevel level, IAiMissionProvider? ai, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 5,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            AiLevel = level,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo, logger: null, aiProvider: ai);
        server.Start();
        return server;
    }

    private static NpcLinesRequest OneLine(string language = "en") => new()
    {
        Purpose = NpcLinePurposes.PassingHello,
        Language = language,
        Tokens = { AiLineTokens.Player },
        Authored = { "Hello, {player}!" },
        LineCount = 1,
        MaxChars = 60,
    };

    [Fact]
    public void AiOff_NeverCallsTheBackend()
    {
        var provider = new FakeNpcLines(_ => new[] { "Hi, {player}!" });
        var server = NewServer("off", AiLevel.Off, provider);
        server.RunAiLinesSynchronouslyForTest();

        Assert.False(server.RequestAiLinesForTest("hello|x|p0|en|known", OneLine(), AiLinePriority.PlayerInitiated));
        Assert.Null(server.AiLinesForTest("hello|x|p0|en|known", AiLinePriority.PlayerInitiated, OneLine()));
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, server.AiLinesCacheCountForTest);
    }

    [Fact]
    public void AGoodAnswer_IsCached_AndFilledPerReceiver_InTheirOwnLanguage()
    {
        var provider = new FakeNpcLines(r => r.Language == "de" ? new[] { "Hallo, {player}!" } : new[] { "Hi there, {player}!" });
        var server = NewServer("fill", AiLevel.TextOnly, provider);
        server.RunAiLinesSynchronouslyForTest();
        server.SetAiVariantRollForTest(1.0); // no extra variants here
        var alice = server.AddLocalPlayer("Alice", "en");
        var bruno = server.AddLocalPlayer("Bruno", "de");

        Assert.True(server.RequestAiLinesForTest("hello|x|p0|en|known", OneLine("en"), AiLinePriority.Personal, PlayerOnly));
        Assert.True(server.RequestAiLinesForTest("hello|x|p0|de|known", OneLine("de"), AiLinePriority.Personal, PlayerOnly));

        // One cached line per LANGUAGE, holding only the token — never a name.
        var en = server.AiLinesForTest("hello|x|p0|en|known", AiLinePriority.Personal, OneLine("en"))!;
        var de = server.AiLinesForTest("hello|x|p0|de|known", AiLinePriority.Personal, OneLine("de"))!;
        Assert.Equal("Hi there, {player}!", en[0]);
        Assert.Equal("Hi there, Alice!", server.FillAiLineForTest(alice.State.PlayerId, en[0]));
        Assert.Equal("Hallo, Bruno!", server.FillAiLineForTest(bruno.State.PlayerId, de[0]));

        // Extra values per receiver; a token without a value refuses (→ the authored line).
        Assert.Equal("Bring 3 berries, Alice!", server.FillAiLineForTest(alice.State.PlayerId, "Bring {count} berries, {player}!",
            new Dictionary<string, string> { [AiLineTokens.Count] = "3" }));
        Assert.Null(server.FillAiLineForTest(alice.State.PlayerId, "Bring {item}!"));
    }

    [Fact]
    public void ABadAnswer_IsDropped_AndTheAuthoredLineStays()
    {
        var answers = new Queue<string[]?>(new[]
        {
            new[] { "One.", "Two." },                      // wrong count
            new[] { "Bring me 5 berries, {player}!" },     // a digit outside a token
            new[] { "Hello, {name}!" },                    // a token nobody allowed
            new[] { "Hello there!" },                      // the required {player} is missing
            null,                                          // 404 / backend down
            new[] { "h.i.t.l.e.r was right, {player}" },   // refused by the content screen
        });
        var provider = new FakeNpcLines(_ => answers.Dequeue());
        var server = NewServer("bad", AiLevel.TextOnly, provider, c => c.AiLinesPerMinute = 600);
        server.RunAiLinesSynchronouslyForTest();

        for (int i = 0; i < 6; i++)
        {
            Assert.True(server.RequestAiLinesForTest($"hello|bad{i}|p0|en|known", OneLine(), AiLinePriority.PlayerInitiated, PlayerOnly));
            server.AdvanceUptimeForTest(10);
        }

        Assert.Equal(6, provider.Calls);
        Assert.Equal(0, server.AiLinesCacheCountForTest);
        Assert.Equal(6, server.AiLinesRejectedForTest);
    }

    [Fact]
    public void Budget_KeepsReserves_SoChatterNeverStarvesAPlayer()
    {
        var provider = new FakeNpcLines(_ => new[] { "Hello, {player}!" });
        var server = NewServer("budget", AiLevel.TextOnly, provider);
        server.RunAiLinesSynchronouslyForTest();
        bool Ask(string key, AiLinePriority p) => server.RequestAiLinesForTest(key, OneLine(), p);

        // A full bucket of 6: chatter keeps 3 back, news 2, VEGA / personal 1, the player 0.
        Assert.True(Ask("c1", AiLinePriority.Chatter));
        Assert.True(Ask("c2", AiLinePriority.Chatter));
        Assert.True(Ask("c3", AiLinePriority.Chatter));
        Assert.False(Ask("c4", AiLinePriority.Chatter));
        Assert.True(Ask("n1", AiLinePriority.News));
        Assert.False(Ask("n2", AiLinePriority.News));
        Assert.True(Ask("v1", AiLinePriority.Vega));
        Assert.False(Ask("p1", AiLinePriority.Personal));
        Assert.True(Ask("x1", AiLinePriority.PlayerInitiated));
        Assert.False(Ask("x2", AiLinePriority.PlayerInitiated)); // empty
        Assert.Equal(6, provider.Calls);

        server.AdvanceUptimeForTest(15); // 4 per minute → one token back
        Assert.True(Ask("x3", AiLinePriority.PlayerInitiated));
        Assert.Equal(7, provider.Calls);
    }

    [Fact]
    public void Budget_RateZero_MakesNoRequests()
    {
        var provider = new FakeNpcLines(_ => new[] { "Hello, {player}!" });
        var server = NewServer("rate0", AiLevel.TextOnly, provider, c => c.AiLinesPerMinute = 0);
        server.RunAiLinesSynchronouslyForTest();

        Assert.False(server.RequestAiLinesForTest("k", OneLine(), AiLinePriority.PlayerInitiated));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void InFlight_IsCappedAtTwo_AndOneKeyAsksOnlyOnce()
    {
        using var gate = new ManualResetEventSlim(false);
        var provider = new FakeNpcLines(_ => new[] { "Hello, {player}!" }, gate);
        var server = NewServer("inflight", AiLevel.TextOnly, provider);

        Assert.True(server.RequestAiLinesForTest("k1", OneLine(), AiLinePriority.PlayerInitiated));
        Assert.False(server.RequestAiLinesForTest("k1", OneLine(), AiLinePriority.PlayerInitiated)); // already running
        Assert.True(server.RequestAiLinesForTest("k2", OneLine(), AiLinePriority.PlayerInitiated));
        Assert.False(server.RequestAiLinesForTest("k3", OneLine(), AiLinePriority.PlayerInitiated)); // two in flight
        Assert.Equal(2, server.AiLinesInFlightForTest);

        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => server.AiLinesInFlightForTest == 0 && server.AiLinesCacheCountForTest == 2, 10_000));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public void AiLinesFor_MissAsks_HitRotates_UpToThreeVariants()
    {
        int n = 0;
        var provider = new FakeNpcLines(_ => new[] { $"Hello {(char)('A' + n++)}, {{player}}!" });
        var server = NewServer("variants", AiLevel.TextOnly, provider, c => c.AiLinesPerMinute = 600);
        server.RunAiLinesSynchronouslyForTest();
        const string key = "passing_hello|resident|p3|en|known";

        server.SetAiVariantRollForTest(1.0); // never top up
        Assert.Null(server.AiLinesForTest(key, AiLinePriority.Personal, OneLine(), PlayerOnly)); // a miss asks…
        Assert.NotNull(server.AiLinesForTest(key, AiLinePriority.Personal, OneLine(), PlayerOnly)); // …next time it is there
        Assert.Equal(1, provider.Calls);

        server.SetAiVariantRollForTest(0.0); // always top up while < 3
        for (int i = 0; i < 5; i++)
        {
            server.AiLinesForTest(key, AiLinePriority.Personal, OneLine(), PlayerOnly);
            server.AdvanceUptimeForTest(5);
        }

        Assert.Equal(3, server.AiLinesCacheForTest(key).Count);
        Assert.Equal(3, provider.Calls); // full at three
        Assert.Equal(3, server.AiLinesCacheForTest(key).Select(v => v[0]).Distinct().Count());
    }
}
