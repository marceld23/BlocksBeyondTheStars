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
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Item 15 — contextual NPC greetings. Covers the HTTP provider's <c>/npc-line</c> parsing, and the server
/// greeting flow: the static-fallback path (AI off → empty line so the client localizes), the LLM path
/// (provider line is produced + cached + reused), the proximity gate, and that the live context (role +
/// player language) reaches the provider.
/// </summary>
public sealed class NpcGreetingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcGreetingTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_greet_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    // ---- Provider: HTTP contract parsing -----------------------------------------------------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public StubHandler(HttpStatusCode code, string body) { _code = code; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) });
    }

    private static HttpAiMissionProvider HttpProvider(HttpStatusCode code, string body)
        => new("http://test.local", new HttpClient(new StubHandler(code, body)));

    [Fact]
    public void HttpProvider_ParsesTextField()
    {
        var p = HttpProvider(HttpStatusCode.OK, "{\"Text\":\"Welcome aboard, pilot.\"}");
        Assert.Equal("Welcome aboard, pilot.", p.GenerateNpcLine(new NpcLineRequest { Role = "vendor" }));
    }

    [Fact]
    public void HttpProvider_NonSuccess_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.InternalServerError, "{\"Text\":\"x\"}").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void HttpProvider_MalformedBody_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.OK, "not json").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void HttpProvider_BlankText_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.OK, "{\"Text\":\"   \"}").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void NullProvider_ReturnsNull()
        => Assert.Null(new NullAiMissionProvider().GenerateNpcLine(new NpcLineRequest { Role = "vendor" }));

    // ---- Server greeting flow ----------------------------------------------------------------

    private sealed class FakeNpcProvider : IAiMissionProvider
    {
        private readonly string? _line;
        public NpcLineRequest? Last;
        public int Calls;
        public FakeNpcProvider(string? line) => _line = line;
        public MissionPlan? Generate(string context) => null;
        public string? GenerateNpcLine(NpcLineRequest request) { Last = request; Calls++; return _line; }
        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;
        public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
    }

    /// <summary>Starts a world with an inhabited settlement that staffs a vendor, then drops a player onto the
    /// vendor's marker so the greeting is in reach. Searches seeds like the other settlement tests.</summary>
    private SvGameServer StartedWithVendor(AiLevel level, IAiMissionProvider? provider, string locale,
        out SqliteWorldRepository repo, out PlayerSession player, out Vector3f vendorHome,
        IServerTransport? transport = null, string playerName = "Visitor")
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, $"g_{level}_{seed}"));
            var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = $"g_{seed}",
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = true,
                PlaceWrecks = false,
                AiLevel = level,
            };
            var server = new SvGameServer(config, _content, st, repo, logger: null, aiProvider: provider);
            server.Start();

            if (server.HasSettlement && !server.SettlementRuined
                && server.NpcSnapshots.Any(n => n.Role == "vendor"))
            {
                vendorHome = server.NpcSnapshots.First(n => n.Role == "vendor").Home;
                player = server.AddLocalPlayer(playerName, locale);
                player.State.Position = vendorHome;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with a vendor found across 120 seeds.");
    }

    [Fact]
    public void AiOff_GreetingIsEmpty_SoClientUsesStaticFallback()
    {
        var server = StartedWithVendor(AiLevel.Off, new FakeNpcProvider("LLM line"), "en", out var repo, out var p, out _);
        using (repo)
        {
            var line = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal(string.Empty, line);       // empty ⇒ client renders its localized fallback
            Assert.Equal(0, server.GreetingCacheCount); // nothing generated/cached while AI is off
        }
    }

    [Fact]
    public void AiOn_ProducesAndCachesProviderLine()
    {
        var provider = new FakeNpcProvider("Willkommen, Pilot!");
        var server = StartedWithVendor(AiLevel.Suggest, provider, "de", out var repo, out var p, out _);
        using (repo)
        {
            var first = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal("Willkommen, Pilot!", first);
            Assert.Equal(1, server.GreetingCacheCount);

            var second = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal("Willkommen, Pilot!", second);
            Assert.Equal(1, provider.Calls);           // second hit served from cache, provider not re-called
        }
    }

    [Fact]
    public void Context_CarriesRole_AndPlayerLanguage()
    {
        var provider = new FakeNpcProvider("hi");
        var server = StartedWithVendor(AiLevel.Suggest, provider, "de", out var repo, out var p, out _);
        using (repo)
        {
            server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.NotNull(provider.Last);
            Assert.Equal("vendor", provider.Last!.Role);
            Assert.Equal("de", provider.Last.Language);  // the player's join locale reaches the LLM
        }
    }

    [Fact]
    public void OutOfReach_NoGreeting()
    {
        var server = StartedWithVendor(AiLevel.Suggest, new FakeNpcProvider("hi"), "en", out var repo, out var p, out var home);
        using (repo)
        {
            p.State.Position = new Vector3f(home.X + 1000f, home.Y, home.Z); // walk far from the vendor
            Assert.Null(server.GreetingLineForTest(p.State.PlayerId, "vendor"));
        }
    }

    // ---- #2590: a cached AI greeting never carries another player's name ----------------------

    /// <summary>An AI backend that writes the player name it was given into the line — exactly what a model (or the
    /// backend's no-model template) does. Before #2590 that was the real name, and the line — cached per NPC,
    /// standing and language, not per player — greeted every other player with it.</summary>
    private sealed class EchoNameProvider : IAiMissionProvider
    {
        public NpcLineRequest? Last;
        public int Calls;
        public MissionPlan? Generate(string context) => null;
        public string? GenerateNpcLine(NpcLineRequest request) { Last = request; Calls++; return $"Good to see you, {request.PlayerName}!"; }
        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;
        public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
    }

    /// <summary>A different line on every call, like a model at temperature 0.8 — so greeting variants can pile up.</summary>
    private sealed class CountingProvider : IAiMissionProvider
    {
        public int Calls;
        public MissionPlan? Generate(string context) => null;
        public string? GenerateNpcLine(NpcLineRequest request) => $"Line {(char)('A' + Calls++)}, {request.PlayerName}.";
        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;
        public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
    }

    private static List<string> GreetingsTo(NpcLifeWorld.RecordingTransport transport, int connectionId)
        => transport.Sent.Where(s => s.Conn == connectionId && s.Msg is NpcGreeting)
            .Select(s => ((NpcGreeting)s.Msg).Text).ToList();

    [Fact]
    public void AiGreeting_SecondPlayer_NeverHearsTheFirstPlayersName()
    {
        var transport = new NpcLifeWorld.RecordingTransport();
        var provider = new EchoNameProvider();
        var server = StartedWithVendor(AiLevel.TextOnly, provider, "en", out var repo, out var alice, out var home, transport, "Alice");
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            server.SetAiVariantRollForTest(1.0); // no extra variants in this test
            var bob = server.AddLocalPlayer("Bob", "en");
            bob.State.Position = home;

            // Alice first: nothing cached → instant empty line, the AI line arrives as the bubble's upgrade.
            Assert.True(server.EmitGreetingForTest(alice.State.PlayerId, "vendor"));
            server.TickGreetingsForTest();
            // Bob at the same NPC, same standing, same language → the cache hit, filled with HIS name.
            Assert.True(server.EmitGreetingForTest(bob.State.PlayerId, "vendor"));
            server.TickGreetingsForTest();

            Assert.Equal(AiLineTokens.Player, provider.Last!.PlayerName); // the backend never sees a real name
            Assert.Equal(1, provider.Calls);
            Assert.Contains("Good to see you, Alice!", GreetingsTo(transport, alice.ConnectionId));
            var toBob = GreetingsTo(transport, bob.ConnectionId);
            Assert.Contains("Good to see you, Bob!", toBob);
            Assert.DoesNotContain(toBob, t => t.Contains("Alice", StringComparison.Ordinal));

            // The cache holds the token, never a name; the synchronous seam shows each player their own.
            Assert.All(server.GreetingVariantsForTest(bob.State.PlayerId, "vendor"),
                v => Assert.Equal("Good to see you, {player}!", v));
            Assert.Equal("Good to see you, Bob!", server.GreetingLineForTest(bob.State.PlayerId, "vendor"));
            Assert.Equal("Good to see you, Alice!", server.GreetingLineForTest(alice.State.PlayerId, "vendor"));
        }
    }

    [Fact]
    public void AiGreeting_KeepsUpToThreeVariants_RotatesThem_AndStaysBounded()
    {
        var provider = new CountingProvider();
        var server = StartedWithVendor(AiLevel.TextOnly, provider, "en", out var repo, out var p, out _);
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            server.SetAiVariantRollForTest(0.0); // every cache hit asks for one more variant while fewer than 3
            string id = p.State.PlayerId;
            for (int i = 0; i < 6; i++)
            {
                Assert.True(server.EmitGreetingForTest(id, "vendor"));
                server.TickGreetingsForTest();
                server.AdvanceUptimeForTest(30); // past the 25 s per-NPC cooldown (and refills the AI budget)
            }

            var variants = server.GreetingVariantsForTest(id, "vendor");
            Assert.Equal(3, variants.Count);
            Assert.Equal(3, provider.Calls); // full at three — no more backend calls
            Assert.All(variants, v => Assert.Contains(AiLineTokens.Player, v, StringComparison.Ordinal));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 60; i++)
            {
                seen.Add(server.GreetingLineForTest(id, "vendor")!);
            }

            Assert.True(seen.Count >= 2, "the cached variants rotate");
            Assert.All(seen, s => Assert.EndsWith(", Visitor.", s, StringComparison.Ordinal));
            Assert.Equal(1, server.GreetingCacheCount);
            Assert.Equal(2048, server.GreetingCacheCapacityForTest);
        }
    }

    [Fact]
    public void AiGreeting_ALineWithAForeignPlaceholder_IsDropped()
    {
        // Only {player} may come back — anything else would reach the player as raw braces.
        var provider = new FakeNpcProvider("Welcome, {name}!");
        var server = StartedWithVendor(AiLevel.TextOnly, provider, "en", out var repo, out var p, out _);
        using (repo)
        {
            Assert.Equal(string.Empty, server.GreetingLineForTest(p.State.PlayerId, "vendor"));
            Assert.Equal(1, provider.Calls);
            Assert.Equal(0, server.GreetingCacheCount);
        }
    }

    [Fact]
    public void AiOff_EmitGreeting_NeverCallsTheProvider()
    {
        var provider = new FakeNpcProvider("LLM line");
        var server = StartedWithVendor(AiLevel.Off, provider, "en", out var repo, out var p, out _);
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            server.SetAiVariantRollForTest(0.0);
            Assert.True(server.EmitGreetingForTest(p.State.PlayerId, "vendor"));
            server.TickGreetingsForTest();

            Assert.Equal(0, provider.Calls);
            Assert.Equal(0, server.GreetingCacheCount);
        }
    }

    // ---- #2593: the bubble's id (added by the client agent) ----------------------------------------

    [Fact]
    public void AGreeting_CarriesAnId_ItsAiUpgradeReusesIt_AndTheNextGreetingCountsOn()
    {
        // The client plays the voice and the gesture once per id and only swaps the text of an upgrade with the same id.
        var transport = new NpcLifeWorld.RecordingTransport();
        var server = StartedWithVendor(AiLevel.TextOnly, new CountingProvider(), "en", out var repo, out var p, out _, transport);
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            server.SetAiVariantRollForTest(1.0); // no extra variants
            List<NpcGreeting> Sent() => transport.Sent.Where(s => s.Conn == p.ConnectionId && s.Msg is NpcGreeting)
                .Select(s => (NpcGreeting)s.Msg).ToList();

            Assert.True(server.EmitGreetingForTest(p.State.PlayerId, "vendor"));
            server.TickGreetingsForTest();
            var first = Sent();
            Assert.Equal(2, first.Count); // the instant (empty) line + its AI upgrade
            Assert.True(first[0].GreetingId > 0);
            Assert.Equal(first[0].GreetingId, first[1].GreetingId);
            Assert.Equal(NpcGreetingKinds.Addressed, first[0].Kind);
            Assert.Equal(NpcGestures.None, first[1].Gesture);

            server.AdvanceUptimeForTest(30); // past the 25 s per-NPC cooldown
            Assert.True(server.EmitGreetingForTest(p.State.PlayerId, "vendor"));
            server.TickGreetingsForTest();
            Assert.True(Sent()[^1].GreetingId > first[0].GreetingId);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
