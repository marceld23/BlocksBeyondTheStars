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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2603 — trader ships land on planets even when nobody flies: a planet-side scheduler per occupied body, gaps by the
/// system's traffic (a present base owner draws them sooner; a system without traffic only sees one for a base), the free
/// dry pad NEAREST the player's base (so the radio's "parked near your base" is true), a 20 s approach with the pad
/// reserved and nothing standing on it yet, then the real descent FX, the hull, the pilot, the toast and the hail. One
/// trader per body; nothing for a body without players or with the rule off; the flight scene's landing still sets down
/// at once, but on the same pad rule (K12). A trader on its approach is no village talk and no VEGA "trader near" tip yet.
/// With AI on, the hail is asked for at the decision and used at the touchdown.
/// </summary>
public sealed class PlanetTraderArrivalTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.FriendlyVisitors = false; // only the trader comes by in these tests
            configure?.Invoke(c);
        });
        w.FoundBase();
        w.Server.ScanBaseLifeForTest(); // the settler's "moved in" news is out of the way before a trader comes
        return w;
    }

    private static List<T> To<T>(NpcLifeWorld w) => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<T>().ToList();

    /// <summary>The pad the trader must take: free (not the player's), dry, not molten — nearest the base core.</summary>
    private static int ExpectedPad(NpcLifeWorld w)
    {
        int circ = w.Server.World.Circumference;
        int mine = w.Server.AssignedPadForTest(w.Player.State.PlayerId);
        int best = -1;
        double bestSq = double.MaxValue;
        for (int i = 0; i < w.Server.LandingPadCount; i++)
        {
            var pad = w.Server.LandingPadInfoForTest(i);
            if (i == mine || pad.Wet || w.Server.LandingPadLavaForTest(i).Molten)
            {
                continue;
            }

            double dx = Math.Abs(pad.X + 0.5 - (NpcLifeWorld.Cx + 0.5));
            if (circ > 0)
            {
                dx = Math.Min(dx, circ - dx);
            }

            double dy = pad.Y - w.Feet, dz = pad.Z + 0.5 - (NpcLifeWorld.Cz + 0.5);
            double d = (dx * dx) + (dy * dy) + (dz * dz);
            if (d < bestSq)
            {
                bestSq = d;
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void TheGaps_FollowTheTraffic_AndABaseOwnerAtHomeDrawsThemSooner()
    {
        Assert.Equal(900.0, SvGameServer.PlanetTraderGapForTest("Often", false, 0.0));
        Assert.Equal(1500.0, SvGameServer.PlanetTraderGapForTest("Often", false, 1.0));
        Assert.Equal(1800.0, SvGameServer.PlanetTraderGapForTest("Rare", false, 0.0));
        Assert.Equal(2700.0, SvGameServer.PlanetTraderGapForTest("Rare", false, 1.0));
        Assert.Equal(900.0 * 0.75, SvGameServer.PlanetTraderGapForTest("Often", true, 0.0));
        Assert.Equal(2700.0 * 0.75, SvGameServer.PlanetTraderGapForTest("Rare", true, 1.0));

        // No traffic: only for a body with a base whose owner is there — 45 to 60 minutes; without one never.
        Assert.Equal(2700.0, SvGameServer.PlanetTraderGapForTest("None", true, 0.0));
        Assert.Equal(3600.0, SvGameServer.PlanetTraderGapForTest("None", true, 1.0));
        Assert.True(double.IsPositiveInfinity(SvGameServer.PlanetTraderGapForTest("None", false, 0.5)));
    }

    [Fact]
    public void AFirstSighting_ArmsTheWindow_InsteadOfLandingAtOnce()
    {
        using var w = World("ptrader_arm");
        double? next = w.Server.TickPlanetTradersForTest();
        Assert.NotNull(next);
        Assert.InRange(next!.Value, 900.0 * 0.75 * 0.3 * 0.99, 3600.0 * 0.6 * 1.01); // 0.3–0.6 of a gap of any traffic
        Assert.Equal(0, w.Server.LandedTraderCountForTest());
        Assert.DoesNotContain("@srv.trader.approaching", w.MessagesToPlayer());
    }

    [Fact]
    public void ATrader_ComesIn_OnTheFreeDryPadNearestTheBase_AndSetsDownAfterTheApproach()
    {
        using var w = World("ptrader_land");
        string body = w.Server.ActiveLocationId;
        int expected = ExpectedPad(w);
        Assert.True(expected >= 0, "the arena has no free dry pad");
        int freeBefore = w.Server.FreePadsForTest(body);

        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Equal(expected, w.Server.LandedTraderPadForTest(body));
        Assert.Equal(freeBefore - 1, w.Server.FreePadsForTest(body)); // reserved for the whole approach
        Assert.InRange(w.Server.LandedTraderTouchdownInForTest(body)!.Value, 19.9, 20.1);
        Assert.Contains("@srv.trader.approaching", w.MessagesToPlayer());
        string name = w.Server.LandedTraderNameForTest(body)!;

        // On the approach nothing stands on the pad yet: no hull, no pilot, no barter.
        Assert.False(w.Server.MaterializeLandedTraderForTest());
        w.Server.AdvanceUptimeForTest(18.0);
        w.Server.TickForTest(0.1);
        Assert.Null(w.Server.LandedTraderPilotPosForTest(body));
        Assert.DoesNotContain(w.Server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
        Assert.Empty(To<ShipTransitFx>(w));

        // Touchdown: the real ship descends, the pilot steps out, the toast and VEGA (no radio worn) tell the owner.
        w.Server.AdvanceUptimeForTest(3.0);
        w.Server.TickForTest(0.1);
        Assert.NotNull(w.Server.LandedTraderPilotPosForTest(body));
        Assert.Contains(w.Server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
        var fx = Assert.Single(To<ShipTransitFx>(w));
        Assert.True(fx.Landing);
        Assert.Equal(name, fx.Name);
        Assert.Contains("@srv.trader.landed:" + name, w.MessagesToPlayer());
        var news = Assert.Single(To<ShipAiLine>(w).Where(l => l.LineKey == "vega.news.trader"));
        Assert.Equal(w.Server.Galaxy.FindBody(body)!.Name, news.LineArg);

        // It stays the usual 10–15 minutes from the touchdown.
        Assert.InRange(w.Server.LandedTraderSecondsLeftForTest(body)!.Value, SvGameServer.TraderLandDwellMin - 2.0, SvGameServer.TraderLandDwellMax);
    }

    [Fact]
    public void OneTraderPerBody_AndNoneWithoutPlayersOnTheSurface_OrWithTheRuleOff()
    {
        using (var w = World("ptrader_one"))
        {
            Assert.True(w.Server.ForcePlanetTraderForTest());
            Assert.False(w.Server.ForcePlanetTraderForTest()); // the body already has its trader
            Assert.Equal(1, w.Server.LandedTraderCountForTest());
        }

        using (var w = World("ptrader_nobody"))
        {
            w.Player.Spectating = true; // an invisible observer is nobody to land for
            Assert.False(w.Server.ForcePlanetTraderForTest());
            Assert.Equal(0, w.Server.LandedTraderCountForTest());
        }

        using (var w = World("ptrader_rule_off", c => c.Rules.NeutralNpcShips = false))
        {
            Assert.False(w.Server.ForcePlanetTraderForTest());
            Assert.Null(w.Server.TickPlanetTradersForTest());
            Assert.Equal(0, w.Server.LandedTraderCountForTest());
        }
    }

    [Fact]
    public void TheFlightScenesLanding_StillSetsDownAtOnce()
    {
        using var w = World("ptrader_space");
        string body = w.Server.ActiveLocationId;
        w.Player.State.AboardShip = true; // the in-flight landing decision runs in the pilot's space instance
        w.Server.EnterSpace(w.Player.State.PlayerId);
        Assert.True(w.Server.InSpace(w.Player.State.PlayerId));

        Assert.True(w.Server.LandTraderFromFlightForTest(w.Player.State.PlayerId, body));
        Assert.True(w.Server.LandedTraderTouchdownInForTest(body)!.Value <= 0.0);
        Assert.True(w.Server.MaterializeLandedTraderForTest());
        Assert.NotNull(w.Server.LandedTraderPilotPosForTest(body));
    }

    [Fact]
    public void TheFlightScenesLanding_TakesTheFreeDryPadNearestTheBase_NotTheLowestFreeOne()
    {
        // K12: the hail says "we're parked near your base" — the flight scene's trader must take the same pad as a
        // planet-side arrival, even with the owner up in space (nobody on the surface: the hailed owner's base anchors it).
        using var w = World("ptrader_space_pad");
        string body = w.Server.ActiveLocationId;
        w.Player.AssignedPadIndex = 0; // the owner's ship holds pad 0 (the one beside the arena), so the nearest is far off
        int mine = w.Server.AssignedPadForTest(w.Player.State.PlayerId);
        int expected = ExpectedPad(w);
        Assert.True(expected >= 0, "the arena has no free dry pad");
        int lowestFree = Enumerable.Range(0, w.Server.LandingPadCount).First(i => i != mine);
        Assert.NotEqual(lowestFree, expected); // otherwise the old lowest-free-pad rule would pass too

        w.Player.State.AboardShip = true;
        w.Server.EnterSpace(w.Player.State.PlayerId);
        Assert.True(w.Server.InSpace(w.Player.State.PlayerId));
        Assert.Equal(mine, w.Server.AssignedPadForTest(w.Player.State.PlayerId)); // still reserved while in space

        Assert.True(w.Server.LandTraderFromFlightForTest(w.Player.State.PlayerId, body));
        Assert.Equal(expected, w.Server.LandedTraderPadForTest(body));
    }

    [Fact]
    public void ATraderOnItsApproach_IsNoVillageTalkYet_OnlyOnceItHasSetDown()
    {
        using var w = World("ptrader_chatter");
        w.Server.RegisterTestSettlementForTest("Testville",
            new Vector3i(NpcLifeWorld.Cx + 4, w.PadY, NpcLifeWorld.Cz - 10), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 10));
        int a = w.Server.SpawnVillagerForTest("Testville", new Vector3f(NpcLifeWorld.Cx + 8.5f, w.Feet, NpcLifeWorld.Cz + 0.5f));
        int b = w.Server.SpawnVillagerForTest("Testville", new Vector3f(NpcLifeWorld.Cx + 10.5f, w.Feet, NpcLifeWorld.Cz + 0.5f));
        string me = w.Player.State.PlayerId;

        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.DoesNotContain(w.Server.ChatterTopicsForTest(a, b, me), t => t.Topic == "trader"); // still on its 20 s approach

        w.Server.AdvanceUptimeForTest(21.0);
        Assert.Contains(w.Server.ChatterTopicsForTest(a, b, me), t => t.Topic == "trader");
    }

    [Fact]
    public void ATraderOnItsApproach_IsNoTraderNearTipForVega_UntilItsPilotStandsThere()
    {
        using var w = World("ptrader_vega_tip");
        string me = w.Player.State.PlayerId;
        string body = w.Server.ActiveLocationId;
        var home = w.Player.State.Position;

        Assert.True(w.Server.ForcePlanetTraderForTest());

        // On the approach nobody stands anywhere yet — the record's pilot position is still the default origin.
        w.Player.State.Position = new Vector3f(0.5f, 0f, 0.5f);
        Assert.DoesNotContain("trader_near", w.Server.VegaTipCandidatesForTest(me).Candidates);
        w.Player.State.Position = home;

        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);
        var pilot = w.Server.LandedTraderPilotPosForTest(body);
        Assert.NotNull(pilot);
        w.Player.State.Position = pilot!.Value;
        Assert.Contains("trader_near", w.Server.VegaTipCandidatesForTest(me).Candidates);
    }

    /// <summary>The scripted backend's answer: the trader's hail, nothing else.</summary>
    private static readonly Func<NpcLinesRequest, string[]?> TraderHail = r => r.Purpose == NpcLinePurposes.TraderHail
        ? new[] { "{trader} here, parked right by your base on {place}! Come and trade, {player}!" }
        : null;

    [Fact]
    public void AiOn_TheHailIsAskedAtTheDecision_AndTheRadioCallUsesIt()
    {
        var ai = new FakeNpcLines(TraderHail);
        using var w = World("ptrader_ai", c => c.AiLevel = AiLevel.TextOnly);
        w.Server.SetAiProviderForTest(ai);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0);
        TestGear.Wear(w.Player.State, "comm_radio");
        string body = w.Server.ActiveLocationId;

        Assert.True(w.Server.ForcePlanetTraderForTest());
        var asked = Assert.Single(ai.Requests, r => r.Purpose == NpcLinePurposes.TraderHail);
        Assert.Equal("en", asked.Language);
        Assert.DoesNotContain(w.Player.State.Name, string.Join("|", asked.Authored) + asked.Place + asked.Situation, StringComparison.Ordinal);
        Assert.Single(w.Server.AiLinesCacheForTest(w.Server.TraderHailCacheKeyForTest(body, "en")!));

        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);

        string trader = w.Server.LandedTraderNameForTest(body)!;
        string place = w.Server.Galaxy.FindBody(body)!.Name;
        var call = Assert.Single(To<ChatMessage>(w).Where(m => m.IsNpcCall));
        Assert.Equal($"{trader} here, parked right by your base on {place}! Come and trade, {w.Player.State.Name}!", call.Text);
        Assert.Empty(To<ShipAiLine>(w).Where(l => l.LineKey == "vega.news.trader")); // the radio reached — VEGA stays quiet
    }

    [Fact]
    public void AiOff_AsksNothing_AndTheAuthoredHailRings()
    {
        var ai = new FakeNpcLines(TraderHail);
        using var w = World("ptrader_ai_off");
        w.Server.SetAiProviderForTest(ai);
        TestGear.Wear(w.Player.State, "comm_radio");
        string body = w.Server.ActiveLocationId;

        Assert.True(w.Server.ForcePlanetTraderForTest());
        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);

        Assert.Equal(0, ai.Calls);
        string place = w.Server.Galaxy.FindBody(body)!.Name;
        string authored = Content.Value.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.English)
            .Get("npc.call.trader").Replace("{0}", place, StringComparison.Ordinal);
        Assert.Equal(authored, Assert.Single(To<ChatMessage>(w).Where(m => m.IsNpcCall)).Text);
    }
}
