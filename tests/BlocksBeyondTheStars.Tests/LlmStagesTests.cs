// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// LLM stages L0/L2/L3 + VEGA banter. L0: the mission context handed to the provider carries the
/// allowed content keys (so the backend LLM can't hallucinate items the validator rejects). L2: the
/// greeting context carries a stable persona — and, since the line is cached per tier and shared (#2618), no
/// player's own history. L3: board missions get provider flavour text (cached per mission+locale), with the
/// static localized board text whenever AI is off or the provider declines. Banter: VEGA's smalltalk uses the
/// same provider path (role "ship_ai"), is silent when AI is off, sends a request that is a pure function of its
/// cache key and takes the world's AI line budget (#2618).
/// </summary>
public sealed class LlmStagesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LlmStagesTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_llm_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeProvider : IAiMissionProvider
    {
        public string? MissionContext;
        public NpcLineRequest? LastLine;
        public MissionTextRequest? LastText;
        public int TextCalls;
        public int LineCalls;

        public string? Line { get; set; }
        public MissionTextResult? Text { get; set; }

        public MissionPlan? Generate(string context) { MissionContext = context; return null; }
        public string? GenerateNpcLine(NpcLineRequest request) { LastLine = request; LineCalls++; return Line; }
        public MissionTextResult? GenerateMissionText(MissionTextRequest request) { LastText = request; TextCalls++; return Text; }
        public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
    }

    /// <summary>Starts a world with an inhabited settlement and a player at its vendor (the same seed
    /// search the greeting tests use), so boards, NPCs and relationships all exist.</summary>
    private SvGameServer StartedAtSettlement(AiLevel level, IAiMissionProvider provider, string locale,
        out SqliteWorldRepository repo, out PlayerSession player, double aiLinesPerMinute = 4.0)
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, $"l_{level}_{seed}"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = $"l_{seed}",
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = true,
                PlaceWrecks = false,
                AiLevel = level,
                AiLinesPerMinute = aiLinesPerMinute,
            };
            var server = new SvGameServer(config, _content, st, repo, logger: null, aiProvider: provider);
            server.Start();

            if (server.HasSettlement && !server.SettlementRuined
                && server.NpcSnapshots.Any(n => n.Role == "vendor"))
            {
                player = server.AddLocalPlayer("Visitor", locale);
                player.State.Position = server.NpcSnapshots.First(n => n.Role == "vendor").Home;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with a vendor found across 120 seeds.");
    }

    // ---- L0: the mission context advertises the allowed content keys --------------------------

    [Fact]
    public void L0_MissionContext_CarriesAllowedTargetsAndRewards()
    {
        var provider = new FakeProvider();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "l0"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig { WorldName = "l0", Seed = 1, AutoSaveIntervalMinutes = 9999, AiLevel = AiLevel.Auto };
            var server = new SvGameServer(config, _content, st, repo, logger: null, aiProvider: provider);
            server.Start();

            server.TryGenerateAiMission("we need ore for the outpost");

            Assert.NotNull(provider.MissionContext);
            Assert.Contains("we need ore for the outpost", provider.MissionContext);
            Assert.Contains("Allowed objective targets", provider.MissionContext);
            Assert.Contains("iron_ore", provider.MissionContext);   // a real mineable content key
            Assert.Contains("Allowed reward items", provider.MissionContext);
        }
    }

    // ---- L2: the persona reaches the provider — the player's own history does not (#2618) -------

    [Fact]
    public void L2_GreetingContext_CarriesPersona_ButNoPlayerHistory()
    {
        var provider = new FakeProvider { Line = "hello" };
        var server = StartedAtSettlement(AiLevel.Suggest, provider, "en", out var repo, out var p);
        using (repo)
        {
            // Seed the NPC's memory of this player: trades, a taken mission and a chat (item 14's key format).
            string npcKey = server.SettlementLocationKeyForTest(server.SettlementName) + ":vendor"; // #2693: body + name
            p.State.NpcMemory[npcKey] = new NpcRelationship
            {
                Name = "Old Mira",
                Role = "vendor",
                Value = 33,
                Log =
                {
                    new NpcInteraction { Kind = NpcInteractionKind.Trade },
                    new NpcInteraction { Kind = NpcInteractionKind.Trade },
                    new NpcInteraction { Kind = NpcInteractionKind.MissionAccepted },
                    new NpcInteraction { Kind = NpcInteractionKind.Dialog },
                    new NpcInteraction { Kind = NpcInteractionKind.Trade },
                },
            };

            server.GreetingLineForTest(p.State.PlayerId, "vendor");

            Assert.NotNull(provider.LastLine);
            Assert.False(string.IsNullOrEmpty(provider.LastLine!.Persona)); // stable per-NPC voice

            // The line is cached per NPC, standing tier and language and shared by every player there — so the request
            // carries the "known" tier's representative numbers, never this player's score (33) or log (5 entries) …
            Assert.Equal(25, provider.LastLine.Relationship);
            Assert.Equal(3, provider.LastLine.PastInteractions);
            // … and no memory of this player's own deeds ("after yesterday's trade" would reach players who never traded).
            Assert.Equal(string.Empty, provider.LastLine.RecentEvents);
        }
    }

    [Fact]
    public void L2_Persona_IsStablePerNpc()
    {
        var provider = new FakeProvider { Line = "hello" };
        var server = StartedAtSettlement(AiLevel.Suggest, provider, "en", out var repo, out var p);
        using (repo)
        {
            server.GreetingLineForTest(p.State.PlayerId, "vendor");
            string first = provider.LastLine!.Persona;

            server.GreetingLineForTest(p.State.PlayerId, "vendor"); // cache hit — call again via fresh tier
            p.State.NpcMemory.Clear();
            server.GreetingLineForTest(p.State.PlayerId, "vendor");

            Assert.Equal(first, provider.LastLine!.Persona); // same NPC ⇒ same voice, every time
        }
    }

    // ---- L3: board-mission flavour text --------------------------------------------------------

    [Fact]
    public void L3_BoardMission_GetsProviderText_AndCachesIt()
    {
        var provider = new FakeProvider { Text = new MissionTextResult { Title = "Erz für die Schmelze", Description = "Die Öfen stehen still." } };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "de", out var repo, out var p);
        using (repo)
        {
            var boardIds = server.AvailableBoardMissions(p.State.PlayerId);
            Assert.NotEmpty(boardIds);

            var text = server.MissionTextForTest(p.State.PlayerId, boardIds[0]);
            Assert.NotNull(text);
            Assert.Equal("Erz für die Schmelze", text!.Title);

            // The request carried the fixed job + the player's language; a second call hits the cache.
            Assert.NotNull(provider.LastText);
            Assert.Equal("de", provider.LastText!.Language);
            Assert.False(string.IsNullOrEmpty(provider.LastText.NeedItem));
            Assert.True(provider.LastText.Required > 0);

            server.MissionTextForTest(p.State.PlayerId, boardIds[0]);
            Assert.Equal(1, provider.TextCalls);
        }
    }

    [Fact]
    public void L3_AiOff_KeepsStaticBoardText()
    {
        var provider = new FakeProvider { Text = new MissionTextResult { Title = "x", Description = "y" } };
        var server = StartedAtSettlement(AiLevel.Off, provider, "en", out var repo, out var p);
        using (repo)
        {
            var boardIds = server.AvailableBoardMissions(p.State.PlayerId);
            Assert.NotEmpty(boardIds);
            Assert.Null(server.MissionTextForTest(p.State.PlayerId, boardIds[0]));
            Assert.Equal(0, provider.TextCalls);
        }
    }

    [Fact]
    public void L3_ProviderDeclines_KeepsStaticBoardText()
    {
        var provider = new FakeProvider { Text = null };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "en", out var repo, out var p);
        using (repo)
        {
            var boardIds = server.AvailableBoardMissions(p.State.PlayerId);
            Assert.Null(server.MissionTextForTest(p.State.PlayerId, boardIds[0]));
            Assert.Equal(1, provider.TextCalls); // asked once, declined ⇒ static text stays, no cache entry
        }
    }

    // ---- VEGA banter: same provider path, role "ship_ai" ---------------------------------------

    [Fact]
    public void VegaBanter_UsesShipAiRole_AndSituation()
    {
        var provider = new FakeProvider { Line = "Noch ein Sonnenuntergang. Ich zähle sie nicht. Doch." };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "de", out var repo, out var p);
        using (repo)
        {
            var line = server.VegaBanterForTest(p.State.PlayerId);
            Assert.Equal("Noch ein Sonnenuntergang. Ich zähle sie nicht. Doch.", line);

            Assert.NotNull(provider.LastLine);
            Assert.Equal("ship_ai", provider.LastLine!.Role);
            Assert.Equal("de", provider.LastLine.Language);
            Assert.False(string.IsNullOrEmpty(provider.LastLine.Situation)); // world/time/progress context
            Assert.Contains("VEGA", provider.LastLine.NpcName);

            // Cached per situation bucket: a second ask doesn't re-call the provider.
            var again = server.VegaBanterForTest(p.State.PlayerId);
            Assert.Equal(line, again);
        }
    }

    [Fact]
    public void VegaBanter_StrayPlaceholder_IsDropped_NeverCached()
    {
        // Same answer check as greetings (#2590): only {player} may survive — {name} would show raw on screen.
        var provider = new FakeProvider { Line = "Good morning, {name}. Systems nominal." };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "en", out var repo, out var p, aiLinesPerMinute: 60);
        using (repo)
        {
            Assert.Null(server.VegaBanterForTest(p.State.PlayerId));
            Assert.Null(server.VegaBanterForTest(p.State.PlayerId));
            Assert.Equal(2, provider.LineCalls); // nothing cached, so the next tick asks again
        }
    }

    [Fact]
    public void VegaBanter_AiOff_IsSilent()
    {
        var provider = new FakeProvider { Line = "should never appear" };
        var server = StartedAtSettlement(AiLevel.Off, provider, "en", out var repo, out var p);
        using (repo)
        {
            Assert.Null(server.VegaBanterForTest(p.State.PlayerId));
            Assert.True(server.StartVegaBanterForTest(p.State.PlayerId));
            Assert.Null(provider.LastLine);
        }
    }

    [Fact]
    public void VegaBanter_RequestIsAPureFunctionOfItsKey_AndTheFactsItTalksAboutAreInTheKey()
    {
        // #2618: one cached banter line serves every pilot whose request lands on the same key — so two pilots with the
        // same facts but very different histories send the SAME request, and a fact the line talks about changes the key.
        var server = StartedAtSettlement(AiLevel.TextOnly, new FakeProvider { Line = "x" }, "en", out var repo, out var p);
        using (repo)
        {
            var veteran = server.AddLocalPlayer("Veteran", "en");
            veteran.State.Position = p.State.Position;
            veteran.State.AboardShip = p.State.AboardShip;
            for (int i = 0; i < 40; i++)
            {
                veteran.State.Milestones.Add("test:milestone_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var fresh = server.VegaBanterRequestForTest(p.State.PlayerId)!.Value;
            var seasoned = server.VegaBanterRequestForTest(veteran.State.PlayerId)!.Value;
            Assert.Equal(fresh.CacheKey, seasoned.CacheKey);
            Assert.Equal(JsonSerializer.Serialize(fresh.Request), JsonSerializer.Serialize(seasoned.Request)); // the wire body
            Assert.Equal(string.Empty, fresh.Request.RecentEvents);
            Assert.Equal(AiLineTokens.Player, fresh.Request.PlayerName);

            // Aboard vs on foot is in the situation — so it is in the key too.
            veteran.State.AboardShip = !p.State.AboardShip;
            var moved = server.VegaBanterRequestForTest(veteran.State.PlayerId)!.Value;
            Assert.NotEqual(fresh.CacheKey, moved.CacheKey);
            Assert.NotEqual(fresh.Request.Situation, moved.Request.Situation);
        }
    }

    [Fact]
    public void VegaBanter_TakesTheWorldsAiBudget_AndACacheHitIsFree()
    {
        var provider = new FakeProvider { Line = "Clear skies. Suspiciously clear." };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "en", out var repo, out var p);
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            double before = server.AiLineTokensForTest;

            // The real path past the timer: a miss takes one token at Vega priority, then asks the backend.
            Assert.True(server.StartVegaBanterForTest(p.State.PlayerId));
            Assert.Equal(1, provider.LineCalls);
            Assert.Equal(before - 1.0, server.AiLineTokensForTest, 6);
            Assert.Equal(0, server.AiLinesInFlightForTest); // the in-flight slot is freed once the line is back

            // The same facts again: served from the cache — no backend call, no token.
            Assert.True(server.StartVegaBanterForTest(p.State.PlayerId));
            Assert.Equal(1, provider.LineCalls);
            Assert.Equal(before - 1.0, server.AiLineTokensForTest, 6);
        }
    }

    [Fact]
    public void VegaBanter_ARefusedBudget_NeverCallsTheBackend()
    {
        // A world whose operator set the AI line budget to 0 (authored lines only): VEGA's banter stays silent too.
        var provider = new FakeProvider { Line = "should never be asked for" };
        var server = StartedAtSettlement(AiLevel.TextOnly, provider, "en", out var repo, out var p, aiLinesPerMinute: 0);
        using (repo)
        {
            server.RunAiLinesSynchronouslyForTest();
            Assert.True(server.StartVegaBanterForTest(p.State.PlayerId));
            Assert.Null(server.VegaBanterForTest(p.State.PlayerId));
            Assert.Equal(0, provider.LineCalls);
            Assert.Equal(0, server.AiLinesInFlightForTest);
        }
    }

    // ---- HTTP provider: /mission-text contract --------------------------------------------------

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
    public void HttpProvider_ParsesMissionText()
    {
        var p = HttpProvider(HttpStatusCode.OK, "{\"Title\":\"Ore Run\",\"Description\":\"The smelters are cold.\"}");
        var text = p.GenerateMissionText(new MissionTextRequest());
        Assert.NotNull(text);
        Assert.Equal("Ore Run", text!.Title);
    }

    [Fact]
    public void HttpProvider_EmptyMissionText_MeansNoLlm_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.OK, "{\"Title\":\"\",\"Description\":\"\"}")
            .GenerateMissionText(new MissionTextRequest()));

    [Fact]
    public void HttpProvider_MissionTextError_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.InternalServerError, "{}")
            .GenerateMissionText(new MissionTextRequest()));
}
