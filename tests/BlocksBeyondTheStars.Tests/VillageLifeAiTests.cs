// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2599/#2600 on hosted worlds (AI personalisation, the shared <c>/npc-lines</c> path): AI off never calls the backend
/// and keeps the authored lines; AI on — the first conversation is authored and asks for a variant per listening
/// language (both speakers' voices, the facts as tokens, the authored line count), the next one uses it with the names
/// filled in per conversation; an answer with the wrong line count, a digit or a foreign token is dropped (authored
/// again); an empty budget asks nothing; a news headline is re-worded around the same exact facts.
/// </summary>
public sealed class VillageLifeAiTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Testville";

    /// <summary>A scripted backend: answers per purpose, records every request.</summary>
    private sealed class FakeLines : IAiMissionProvider
    {
        private readonly Func<NpcLinesRequest, string[]?> _answer;
        private int _calls;

        public FakeLines(Func<NpcLinesRequest, string[]?> answer) => _answer = answer;

        public int Calls => Volatile.Read(ref _calls);

        public List<NpcLinesRequest> Requests { get; } = new();

        public MissionPlan? Generate(string context) => null;

        public string? GenerateNpcLine(NpcLineRequest request) => null;

        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;

        public string[]? GenerateNpcLines(NpcLinesRequest request)
        {
            Interlocked.Increment(ref _calls);
            lock (Requests)
            {
                Requests.Add(request);
            }

            return _answer(request);
        }
    }

    private static NpcLifeWorld World(string name, AiLevel level, FakeLines ai, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.AiLevel = level;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            configure?.Invoke(c);
        });
        w.Server.SetAiProviderForTest(ai);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0); // no extra variants — one request per miss
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        w.Server.SetChatterOrderForTest(lowerIdFirst: true);
        return w;
    }

    private static (int A, int B) Pair(NpcLifeWorld w)
        => (w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f)),
            w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 4.5f, w.Feet, NpcLifeWorld.Cz + 0.5f)));

    private static List<string> ChatterTexts(NpcLifeWorld w, PlayerSession player)
        => w.Transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.Kind == NpcGreetingKinds.Chatter).Select(g => g.Text).ToList();

    private static string NameOf(NpcLifeWorld w, int npcId) => w.Server.NpcRosterForTest().Single(n => n.Id == npcId).Name;

    /// <summary>One whole conversation about <paramref name="topic"/> (started, every line said, closed, rests cleared).</summary>
    private static void Converse(NpcLifeWorld w, string topic)
    {
        w.Server.ClearChatterRestForTest();
        w.Server.SetChatterTopicForTest(topic);
        w.Server.TickVillageLifeForTest();
        Assert.Single(w.Server.VillageChatsForTest);
        for (int i = 0; i < 5 && w.Server.VillageChatsForTest.Count > 0; i++)
        {
            w.Server.AdvanceUptimeForTest(3.7);
            w.Server.TickVillageLifeForTest();
        }

        Assert.Empty(w.Server.VillageChatsForTest);
    }

    private static string[] Crates(string locale) => locale == "de"
        ? new[] { "Na, {b}, schleppst du wieder?", "Immer doch, {a}!" }
        : new[] { "Hey {b}, carrying crates again?", "Always, {a}!" };

    [Fact]
    public void AiOff_NeverAsks_AndKeepsTheAuthoredLines()
    {
        var ai = new FakeLines(r => Crates(r.Language));
        using var w = World("ai_chat_off", AiLevel.Off, ai);
        var (a, _) = Pair(w);

        Converse(w, "crates");
        Converse(w, "crates");

        Assert.Equal(0, ai.Calls);
        Assert.Equal(new[] { "Need help with the crates?", $"Yes please! Thanks, {NameOf(w, a)}!" }, ChatterTexts(w, w.Player).Take(2));
    }

    [Fact]
    public void AiOn_FirstTalkIsAuthored_AndAsksPerLanguage_TheNextUsesTheVariant_WithTheNamesFilledIn()
    {
        var ai = new FakeLines(r => Crates(r.Language));
        using var w = World("ai_chat_on", AiLevel.TextOnly, ai);
        var german = w.Server.AddLocalPlayer("Zweite", "de");
        german.State.AboardShip = false;
        german.State.Position = new Vector3f(NpcLifeWorld.Cx + 3.5f, w.Feet, NpcLifeWorld.Cz - 4.5f);
        var (a, b) = Pair(w);

        Converse(w, "crates");
        Assert.Equal(new[] { "Need help with the crates?", $"Yes please! Thanks, {NameOf(w, a)}!" }, ChatterTexts(w, w.Player));
        Assert.Equal("Brauchst du Hilfe mit den Kisten?", ChatterTexts(w, german)[0]);

        // One request per listening language, both voices, the authored lines as the anchor, the same line count.
        Assert.Equal(2, ai.Calls);
        var en = ai.Requests.Single(r => r.Language == "en");
        Assert.Contains(ai.Requests, r => r.Language == "de");
        Assert.Equal(NpcLinePurposes.Chatter, en.Purpose);
        Assert.Equal(2, en.LineCount);
        Assert.Equal(56, en.MaxChars);
        Assert.Equal(new[] { "Need help with the crates?", "Yes please! Thanks, {a}!" }, en.Authored);
        Assert.Equal(new[] { "{a}", "{b}" }, en.Speakers.Select(s => s.Token));
        Assert.All(en.Speakers, s => Assert.False(string.IsNullOrEmpty(s.Persona)));
        Assert.Contains("{place}", en.Tokens);
        Assert.DoesNotContain(en.Tokens, t => t == "{player}"); // an everyday topic names nobody
        Assert.DoesNotContain(NameOf(w, a), string.Join("|", en.Authored.Concat(en.Facts).Append(en.Situation)), StringComparison.Ordinal);

        // The cache key names the topic and the two voices — never a person.
        string key = w.Server.ChatterCacheKeyForTest(w.Player.State.PlayerId, a, b, "crates", "en");
        Assert.StartsWith("chatter|crates|p", key, StringComparison.Ordinal);
        Assert.DoesNotContain(NameOf(w, a), key, StringComparison.Ordinal);
        Assert.Single(w.Server.AiLinesCacheForTest(key));

        // The next conversation of these two about it: the AI variant, names filled in, each player in their language.
        Converse(w, "crates");
        Assert.Equal(new[] { $"Hey {NameOf(w, b)}, carrying crates again?", $"Always, {NameOf(w, a)}!" }, ChatterTexts(w, w.Player).Skip(2));
        Assert.Equal(new[] { $"Na, {NameOf(w, b)}, schleppst du wieder?", $"Immer doch, {NameOf(w, a)}!" }, ChatterTexts(w, german).Skip(2));
        Assert.Equal(2, ai.Calls); // cached — nothing asked again
    }

    [Theory]
    [InlineData("count")]
    [InlineData("digit")]
    [InlineData("token")]
    public void AnUnfitAnswer_IsDropped_AndTheAuthoredLinesStay(string fault)
    {
        var ai = new FakeLines(_ => fault switch
        {
            "count" => new[] { "One.", "Two.", "Three." },
            "digit" => new[] { "I carried 5 crates, {b}!", "Wow, {a}!" },
            _ => new[] { "Did {trader} bring crates, {b}?", "Maybe, {a}!" }, // no trader in this topic
        });
        using var w = World("ai_chat_bad_" + fault, AiLevel.TextOnly, ai);
        var (a, _) = Pair(w);

        Converse(w, "crates");
        Converse(w, "crates");

        Assert.True(w.Server.AiLinesRejectedForTest >= 1);
        Assert.Equal($"Yes please! Thanks, {NameOf(w, a)}!", ChatterTexts(w, w.Player)[3]); // still authored
    }

    [Fact]
    public void AnEmptyBudget_AsksNothing()
    {
        var ai = new FakeLines(r => Crates(r.Language));
        using var w = World("ai_chat_budget", AiLevel.TextOnly, ai, c => c.AiLinesPerMinute = 0);
        Pair(w);

        Converse(w, "crates");
        Converse(w, "crates");

        Assert.Equal(0, ai.Calls);
        Assert.Equal("Need help with the crates?", ChatterTexts(w, w.Player)[2]);
    }

    [Fact]
    public void ADeedTalk_KeepsTheHeroAsAToken_FilledWithTheHerosName()
    {
        var ai = new FakeLines(r => r.Purpose == NpcLinePurposes.Chatter
            ? new[] { "{player} did it, {b}! The bandits ran!", "Hooray for {player}, {a}!" }
            : null);
        using var w = World("ai_chat_deed", AiLevel.TextOnly, ai);
        var (a, b) = Pair(w);
        string me = w.Player.State.PlayerId;
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, me, Village, "1:1"));
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, me, Village, "2:2"));

        Converse(w, "deed_camp");
        var request = ai.Requests.Single();
        Assert.Contains("{player}", request.Tokens);
        Assert.Contains(request.Facts, f => f.Contains("{player}", StringComparison.Ordinal));
        Assert.DoesNotContain(NpcLifeWorld.Owner, string.Join("|", request.Authored.Concat(request.Facts)), StringComparison.Ordinal);

        Converse(w, "deed_camp"); // the second deed: the cached variant, the hero's name filled in
        Assert.Equal(new[] { $"{NpcLifeWorld.Owner} did it, {NameOf(w, b)}! The bandits ran!", $"Hooray for {NpcLifeWorld.Owner}, {NameOf(w, a)}!" },
            ChatterTexts(w, w.Player).Skip(2));
    }

    [Fact]
    public void ANewsHeadline_IsAuthoredFirst_ThenReworded_AroundTheSameFacts()
    {
        var ai = new FakeLines(r => r.Purpose == NpcLinePurposes.NewsDeed ? new[] { "Big news: {player} freed {place} from the bandits!" } : null);
        using var w = World("ai_news", AiLevel.TextOnly, ai);
        int reporter = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), job: "reporter");
        string me = w.Player.State.PlayerId;
        Assert.True(w.Server.RecordDeedForTest(DeedKinds.CampCleared, me, Village, "1:1"));

        Assert.Equal("• " + NpcLifeWorld.Owner + " chased the bandits out of their camp near " + Village + "!",
            Assert.Single(w.Server.NewsDigestForTest(me, reporter)));
        var request = ai.Requests.Single();
        Assert.Equal(NpcLinePurposes.NewsDeed, request.Purpose);
        Assert.Equal(1, request.LineCount);
        Assert.Equal(140, request.MaxChars);
        Assert.Equal(new[] { "{player} chased the bandits out of their camp near {place}!" }, request.Authored);
        Assert.Equal(new[] { "{player}", "{place}" }, request.Tokens);

        Assert.Equal("• Big news: " + NpcLifeWorld.Owner + " freed " + Village + " from the bandits!",
            Assert.Single(w.Server.NewsDigestForTest(me, reporter)));
        Assert.Equal(1, ai.Calls);
    }
}
