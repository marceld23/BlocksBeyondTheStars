// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2596 — news from the player's own base reaches them without a radio: VEGA passes it on when the NPC radio call does
/// not go out. A settler moving in is news only once (residents respawn after every world load — the call used to ring
/// again each time); with a worn radio the call rings as before and VEGA stays quiet; the "NPC calls" setting Off silences
/// both. A landed trader comes through VEGA only for an owner in the same star system. (The guard's report without a
/// radio is covered in NpcJobsTests.)
/// </summary>
public sealed class BaseNewsRelayTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static List<ShipAiLine> Vega(NpcLifeWorld w, string lineKey)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId && s.Msg is ShipAiLine l && l.LineKey == lineKey)
            .Select(s => (ShipAiLine)s.Msg).ToList();

    private static int Calls(NpcLifeWorld w)
        => w.Transport.Sent.Count(s => s.Conn == w.Player.ConnectionId && s.Msg is ChatMessage { IsNpcCall: true });

    /// <summary>Every "a settler moved in" line VEGA said — the founding settler's (#2715: with the bed rule) or a later one's.</summary>
    private static List<ShipAiLine> SettlerNews(NpcLifeWorld w)
        => Vega(w, "vega.news.settler_first").Concat(Vega(w, "vega.news.settler")).ToList();

    [Fact]
    public void ANewSettler_WithoutARadio_ComesThroughVega_ExactlyOnce()
    {
        using var w = new NpcLifeWorld(Content, "news_settler");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();

        // #2715: the founding settler's news says that every bed brings one more resident — up to five.
        var news = Assert.Single(SettlerNews(w));
        Assert.Equal("vega.news.settler_first", news.LineKey);
        Assert.Equal(3, news.Kind); // the system channel: base news is never muted by the hints switch
        string[] args = news.LineArg.Split('\u001f');
        Assert.Equal(3, args.Length);
        Assert.Equal(w.Player.State.NpcMemory[$"base_{baseId}:settler"].Name, args[0]);
        Assert.Equal("5", args[2]);
        Assert.Equal(0, Calls(w));

        // A world reload respawns the resident — the same person, no news.
        int first = w.Server.BaseSettlerForTest(baseId)!.Value;
        w.Server.RemoveNpcForTest(first);
        w.Server.AdvanceUptimeForTest(4000); // past every cooldown
        w.Server.ScanBaseLifeForTest();
        Assert.NotEqual(first, w.Server.BaseSettlerForTest(baseId));
        Assert.Single(SettlerNews(w));
        Assert.Equal(0, Calls(w));

        // A bed brings the next resident: their news is the plain one (the rule was told already).
        w.Server.AdvanceUptimeForTest(4000);
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Single(Vega(w, "vega.news.settler"));
    }

    [Fact]
    public void WithAWornRadio_TheSettlerCalls_AndVegaStaysQuiet()
    {
        using var w = new NpcLifeWorld(Content, "news_settler_radio");
        TestGear.Wear(w.Player.State, "comm_radio");
        w.FoundBase();
        w.Server.ScanBaseLifeForTest();

        Assert.Equal(1, Calls(w));
        Assert.Empty(SettlerNews(w));

        // #2715: the founding settler's call says that every bed inside the base brings one more resident.
        var loc = Content.CreateLocalizer(GameLocale.English);
        var call = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ChatMessage>().Single(c => c.IsNpcCall);
        Assert.StartsWith(loc.Get("npc.call.settler"), call.Text, System.StringComparison.Ordinal);
        Assert.EndsWith(loc.Get("npc.call.settler_beds").Replace("{max}", "5"), call.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NpcCallsOff_NoNewsAtAll()
    {
        using var w = new NpcLifeWorld(Content, "news_off");
        w.Player.State.NpcCallsMode = NpcCallsMode.Off;
        w.FoundBase();
        w.Server.ScanBaseLifeForTest();

        Assert.Equal(0, Calls(w));
        Assert.Empty(SettlerNews(w));
    }

    [Fact]
    public void MissionsOnly_KeepsTheSettlerNewsQuiet()
    {
        using var w = new NpcLifeWorld(Content, "news_missions_only");
        w.Player.State.NpcCallsMode = NpcCallsMode.MissionsOnly;
        w.FoundBase();
        w.Server.ScanBaseLifeForTest();

        Assert.Empty(SettlerNews(w));
    }

    [Fact]
    public void ATraderLanding_InTheOwnersSystem_ComesThroughVega_OncePerHour()
    {
        using var w = new NpcLifeWorld(Content, "news_trader");
        w.FoundBase();
        string body = w.Server.ActiveLocationId;
        Assert.NotNull(w.Server.Galaxy.FindBody(body)); // the arena is a real galaxy body

        w.Server.NpcRadioOnTraderLandedForTest(body, "Trader Tam");
        var news = Assert.Single(Vega(w, "vega.news.trader"));
        Assert.Equal(1, news.Kind); // an advisor line: the hints switch mutes an advert
        Assert.Equal(w.Server.Galaxy.FindBody(body)!.Name, news.LineArg);

        w.Server.AdvanceUptimeForTest(600);
        w.Server.NpcRadioOnTraderLandedForTest(body, "Trader Tam");
        Assert.Single(Vega(w, "vega.news.trader")); // the same news within the hour: nothing
    }

    [Fact]
    public void ATraderLanding_WhileTheOwnerIsInAnotherSystem_TellsNobody()
    {
        using var w = new NpcLifeWorld(Content, "news_trader_far");
        w.FoundBase();
        string body = w.Server.ActiveLocationId;
        string home = w.Server.Galaxy.FindBody(body)!.SystemId;
        var elsewhere = w.Server.Galaxy.Systems.SelectMany(s => s.Bodies).First(b => b.SystemId != home);
        w.Player.CurrentLocationId = elsewhere.Id;

        w.Server.NpcRadioOnTraderLandedForTest(body, "Trader Tam");
        Assert.Empty(Vega(w, "vega.news.trader"));
        Assert.Equal(0, Calls(w));
    }
}
