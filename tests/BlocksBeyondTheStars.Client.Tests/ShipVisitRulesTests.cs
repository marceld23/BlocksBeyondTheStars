// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2724: kids in flight can dock with a friend and go over to the friend's ship. K picks the nearest real pilot within the
/// server's docking range (never an NPC trader), and once docked the prompt says whether E goes over (the friend's ship
/// floats parked within reach), whether the friend is flying, or just that the two ships are docked.
/// </summary>
public sealed class ShipVisitRulesTests
{
    private static NetSpacePlayer Pilot(string id, float x, float y = 0f, float z = 0f, string? name = null)
        => new NetSpacePlayer { PlayerId = id, Name = name ?? id, X = x, Y = y, Z = z };

    private static NetSpacePlayer Parked(string id, float x, string? name = null)
    {
        var p = Pilot(id, x, name: name);
        p.Parked = true;
        return p;
    }

    private static NetSpacePlayer Spacewalk(string id, float suitX, float? shipX)
    {
        var p = Pilot(id, suitX);
        p.Eva = true;
        if (shipX is { } sx)
        {
            p.HasShipPose = true;
            p.ShipX = sx;
        }

        return p;
    }

    // ---- K: whom to dock with -------------------------------------------------------------------------------

    [Fact]
    public void K_PicksTheNearestPilotWithinTheDockRange()
    {
        var players = new[] { Pilot("Lena", 30f), Pilot("Tom", 12f), Pilot("Mia", 40f) };

        Assert.Equal("Tom", ShipVisitRules.NearestDockPilot(players, 0f, 0f, 0f)?.PlayerId);
    }

    [Fact]
    public void K_NeverPicksAnNpcTrader_EvenWhenItFliesCloser()
    {
        var players = new[] { Pilot(SpaceTargeting.TraderIdPrefix + "7", 3f, name: "Captain Vex"), Pilot("Lena", 20f) };

        Assert.Equal("Lena", ShipVisitRules.NearestDockPilot(players, 0f, 0f, 0f)?.PlayerId);
        Assert.False(ShipVisitRules.IsDockCandidate(players[0]));
        Assert.False(ShipVisitRules.IsDockCandidate(new NetSpacePlayer()));
    }

    [Fact]
    public void K_FindsNobody_BeyondTheServersDockRange()
    {
        Assert.Equal(48f, ShipVisitRules.DockRange);
        Assert.Equal("Lena", ShipVisitRules.NearestDockPilot(new[] { Pilot("Lena", 0f, 48f) }, 0f, 0f, 0f)?.PlayerId);
        Assert.Null(ShipVisitRules.NearestDockPilot(new[] { Pilot("Lena", 0f, 0f, 48.5f) }, 0f, 0f, 0f));
        Assert.Null(ShipVisitRules.NearestDockPilot(null, 0f, 0f, 0f));
    }

    [Fact]
    public void K_FindsAParkedShipWhosePilotWalksInside_MeasuredToTheShip()
    {
        var players = new[] { Parked("Lena", 4f), Pilot("Tom", 30f) };

        Assert.Equal("Lena", ShipVisitRules.NearestDockPilot(players, 0f, 0f, 0f)?.PlayerId);
        Assert.Null(ShipVisitRules.NearestDockPilot(new[] { Parked("Lena", 60f) }, 0f, 0f, 0f));
    }

    [Fact]
    public void K_MeasuresASpacewalkerToTheSuit_LikeTheServer()
    {
        // The suit floats near, the ship far away: the server's pose is the suit, so docking is in reach.
        var walker = Spacewalk("Lena", suitX: 10f, shipX: 200f);

        Assert.Same(walker, ShipVisitRules.NearestDockPilot(new[] { walker }, 0f, 0f, 0f));
    }

    // ---- The prompt once docked -----------------------------------------------------------------------------

    [Fact]
    public void NotDocked_SaysNothing()
    {
        var view = ShipVisitRules.Evaluate(true, false, "Lena", new[] { Parked("Lena", 5f) }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.None, view.Prompt);
        Assert.False(view.Docked);
        Assert.Equal(string.Empty, view.PartnerId); // never null — the flight view formats it without a check
        Assert.Equal(string.Empty, view.PartnerName);
        Assert.Equal(ShipVisitPrompt.None, ShipVisitRules.Evaluate(true, true, string.Empty, null, 0f, 0f, 0f).Prompt);
    }

    [Fact]
    public void PartnerParkedWithinReach_OffersGoingOver_WithHerIdAndName()
    {
        var view = ShipVisitRules.Evaluate(true, true, "lena-7", new[] { Parked("lena-7", 30f, name: "Lena") }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.GoOver, view.Prompt);
        Assert.True(view.Docked);
        Assert.Equal("lena-7", view.PartnerId); // what the visit intent carries
        Assert.Equal("Lena", view.PartnerName); // what the prompt shows
        Assert.Equal(30f, view.Distance, 3);
    }

    [Fact]
    public void PartnerParkedOutOfReach_IsJustDocked()
    {
        var view = ShipVisitRules.Evaluate(true, true, "Lena", new[] { Parked("Lena", 60f) }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.Docked, view.Prompt);
        Assert.Equal(60f, view.Distance, 3);
    }

    [Fact]
    public void PartnerAtTheHelm_IsFlying_WhereverSheIs_AndHowFarSheFlies_IsKnown()
    {
        var near = ShipVisitRules.Evaluate(true, true, "Lena", new[] { Pilot("Lena", 10f) }, 0f, 0f, 0f);
        var far = ShipVisitRules.Evaluate(true, true, "Lena", new[] { Pilot("Lena", 900f) }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.PartnerFlying, near.Prompt);
        Assert.Equal(ShipVisitPrompt.PartnerFlying, far.Prompt);
        Assert.Equal(10f, near.Distance, 3); // within the visit range: E may ask her to let us over
        Assert.Equal(900f, far.Distance, 3);
    }

    [Fact]
    public void PartnerOnASpacewalk_IsVisitedAtHerFloatingShip_NotAtHerSuit()
    {
        // Her ship floats within reach while she drifts far off: E goes over.
        Assert.Equal(ShipVisitPrompt.GoOver,
            ShipVisitRules.Evaluate(true, true, "Lena", new[] { Spacewalk("Lena", suitX: 300f, shipX: 20f) }, 0f, 0f, 0f).Prompt);

        // Her suit is close but the ship is far: nothing to board here.
        Assert.Equal(ShipVisitPrompt.Docked,
            ShipVisitRules.Evaluate(true, true, "Lena", new[] { Spacewalk("Lena", suitX: 5f, shipX: 300f) }, 0f, 0f, 0f).Prompt);
    }

    [Fact]
    public void ASpacewalkWithoutTheShipPose_FromAnOlderServer_OffersNoVisit()
    {
        var view = ShipVisitRules.Evaluate(true, true, "Lena", new[] { Spacewalk("Lena", suitX: 5f, shipX: null) }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.Docked, view.Prompt);
    }

    [Fact]
    public void AServerWithoutVisits_KeepsTheDockedLine_ButNoVisitPrompt()
    {
        Assert.Equal(ShipVisitPrompt.Docked, ShipVisitRules.Evaluate(false, true, "Lena", new[] { Parked("Lena", 5f) }, 0f, 0f, 0f).Prompt);
        Assert.Equal(ShipVisitPrompt.Docked, ShipVisitRules.Evaluate(false, true, "Lena", new[] { Pilot("Lena", 5f) }, 0f, 0f, 0f).Prompt);
    }

    [Fact]
    public void APartnerNotInThisFlight_IsJustDocked_UnderTheirId()
    {
        var view = ShipVisitRules.Evaluate(true, true, "Lena", new[] { Parked("Tom", 5f) }, 0f, 0f, 0f);

        Assert.Equal(ShipVisitPrompt.Docked, view.Prompt);
        Assert.Equal("Lena", view.PartnerName);
    }

    [Fact]
    public void TheParkedShipPose_IsTheParkedPose_OrTheSpacewalksShipPose_NeverAFlyingShip()
    {
        Assert.True(ShipVisitRules.TryParkedShip(Parked("Lena", 7f), out float px, out _, out _));
        Assert.Equal(7f, px);
        Assert.True(ShipVisitRules.TryParkedShip(Spacewalk("Lena", 1f, 9f), out float sx, out _, out _));
        Assert.Equal(9f, sx);
        Assert.False(ShipVisitRules.TryParkedShip(Pilot("Lena", 3f), out _, out _, out _));
        Assert.False(ShipVisitRules.AwayFromHelm(Pilot("Lena", 3f)));
    }
}
