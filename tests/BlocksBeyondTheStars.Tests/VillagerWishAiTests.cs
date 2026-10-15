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
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2601 on hosted worlds (AI personalisation through the shared <c>/npc-lines</c> path): AI off never calls the backend;
/// with AI on the request is prefetched when the player stands in the village with an offer and shown on the NEXT
/// talk in the villager's voice, the facts filled in for this player and the exact fact line always authored after it;
/// an open panel is never re-worded; an answer without the <c>{count}</c>/<c>{item}</c> facts and an old backend
/// (no answer) keep the authored line; the thanks is warmed when the wish turns ready and used at the hand-over.
/// </summary>
public sealed class VillagerWishAiTests
{
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

    private static string[]? Voice(NpcLinesRequest r) => r.Purpose switch
    {
        NpcLinePurposes.WishAsk => new[] { "{player}, could you find {count} {item} for me?" },
        NpcLinePurposes.WishThanks => new[] { "Thank you for the {item}, {player}!" },
        _ => null,
    };

    private static NpcLifeWorld World(string name, AiLevel level, FakeLines ai)
    {
        var w = VillagerWishTests.World(name, c => c.AiLevel = level);
        w.Server.SetAiProviderForTest(ai);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0); // no extra variants — one request per miss
        VillagerWishTests.Settlers(w);
        w.Server.SetWishTemplateForTest("berries");
        return w;
    }

    private static int WishCalls(FakeLines ai, string purpose)
    {
        lock (ai.Requests)
        {
            return ai.Requests.Count(r => r.Purpose == purpose);
        }
    }

    [Fact]
    public void AiOff_NeverAsks_AndEverythingStaysAuthored()
    {
        var ai = new FakeLines(Voice);
        using var w = World("wish_ai_off", AiLevel.Off, ai);
        var pid = w.Player.State.PlayerId;
        w.Server.SendNpcStandingsForTest(pid);
        var offer = VillagerWishTests.Accept(w, w.Player);
        VillagerWishTests.Give(w, w.Player, "berries", offer.Count);
        w.Server.TickWishesForTest(pid);
        w.Server.TalkToNpcForTest(pid, offer.GiverId);

        Assert.Equal(0, ai.Calls);
        Assert.StartsWith("Berries! They smell wonderful", VillagerWishTests.Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AiOn_PrefetchesTheAsk_TheNextTalkSpeaksInTheVillagersVoice_TheFactLineStaysAuthored()
    {
        var ai = new FakeLines(Voice);
        using var w = World("wish_ai_ask", AiLevel.TextOnly, ai);
        var pid = w.Player.State.PlayerId;
        var offer = VillagerWishTests.Offer(w, w.Player);

        // The standings go out while the player stands in the village → the offers' wording is asked for.
        w.Server.SendNpcStandingsForTest(pid);
        NpcLinesRequest request;
        lock (ai.Requests)
        {
            request = ai.Requests.First(r => r.Purpose == NpcLinePurposes.WishAsk);
        }

        Assert.Contains(AiLineTokens.Count, request.Tokens);
        Assert.Contains(AiLineTokens.Item, request.Tokens);
        Assert.Contains("{count}", request.Authored.Single(), StringComparison.Ordinal); // the authored line, tokens unfilled
        Assert.DoesNotContain("Homesteader", string.Join(" ", request.Facts.Concat(request.Authored)), StringComparison.Ordinal);
        Assert.Single(request.Speakers);
        Assert.False(string.IsNullOrEmpty(request.Speakers[0].Persona));

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.Equal($"Homesteader, could you find {offer.Count} Berries for me?\n\nYou need: {offer.Count}× Berries",
            VillagerWishTests.Last<NpcDialogState>(w, w.Player)!.Text);
    }

    [Fact]
    public void AnOpenPanel_IsNeverReworded_TheAnswerOnlyReachesTheNextTalk()
    {
        var ai = new FakeLines(Voice);
        using var w = World("wish_ai_panel", AiLevel.TextOnly, ai);
        var pid = w.Player.State.PlayerId;
        var offer = VillagerWishTests.Offer(w, w.Player);

        // Nothing prefetched: the first panel is authored — even though the (synchronous) answer arrives right away.
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        var panels = VillagerWishTests.Sent(w, w.Player).OfType<NpcDialogState>().ToList();
        Assert.Single(panels);
        Assert.StartsWith("My berry bushes didn't grow this year.", panels[0].Text, StringComparison.Ordinal);
        Assert.Equal(1, WishCalls(ai, NpcLinePurposes.WishAsk));

        // The next talk is a new panel — now in the villager's voice.
        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        Assert.StartsWith("Homesteader, could you find", VillagerWishTests.Last<NpcDialogState>(w, w.Player)!.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("without facts")]
    [InlineData("old backend")]
    public void AnAnswerWithoutTheFacts_OrNoAnswerAtAll_KeepsTheAuthoredRequest(string backend)
    {
        var ai = new FakeLines(r => backend == "old backend" ? null : new[] { "Could you help me out a little?" });
        using var w = World("wish_ai_bad_" + backend.Replace(' ', '_'), AiLevel.TextOnly, ai);
        var pid = w.Player.State.PlayerId;
        var offer = VillagerWishTests.Offer(w, w.Player);

        w.Server.SendNpcStandingsForTest(pid);
        Assert.InRange(WishCalls(ai, NpcLinePurposes.WishAsk), 1, 2); // one per offer (two lanes, maybe two voices)
        w.Server.TalkToNpcForTest(pid, offer.GiverId);

        var text = VillagerWishTests.Last<NpcDialogState>(w, w.Player)!.Text;
        Assert.StartsWith($"My berry bushes didn't grow this year. Could you bring me {offer.Count} berries?", text, StringComparison.Ordinal);
        Assert.EndsWith($"You need: {offer.Count}× Berries", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThanks_IsWarmedWhenTheWishTurnsReady_AndSpokenAtTheHandOver()
    {
        var ai = new FakeLines(Voice);
        using var w = World("wish_ai_thanks", AiLevel.TextOnly, ai);
        var pid = w.Player.State.PlayerId;
        var offer = VillagerWishTests.Accept(w, w.Player);
        Assert.Equal(0, WishCalls(ai, NpcLinePurposes.WishThanks));

        VillagerWishTests.Give(w, w.Player, "berries", offer.Count);
        w.Server.TickWishesForTest(pid);
        Assert.Equal(1, WishCalls(ai, NpcLinePurposes.WishThanks));

        w.Server.TalkToNpcForTest(pid, offer.GiverId);
        string giver = w.Server.NpcRosterForTest().Single(n => n.Id == offer.GiverId).Name;
        Assert.Equal($"Thank you for the Berries, Homesteader!\n\n{giver} gives you: 3× Berry Seed",
            VillagerWishTests.Last<NpcDialogState>(w, w.Player)!.Text);
    }
}
