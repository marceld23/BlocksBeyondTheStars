// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3146 / #3147 — a landing pad somebody built over is closed. A singleplayer tester (every host is an admin, and the
/// no-build rule excepts admins) turned the communal pad in the middle of his spaceport into a tower foundation once his
/// ship landed on his own base pads, and the trader kept parking its hull inside the tower: "free" only asked who holds a
/// pad, never what stands on it. Now a placed block a hull cannot stand in closes the pad — for traders (pick and
/// touchdown), for the players' automatic pick and a hand-picked landing, and in the chooser; natural ground, open props,
/// the sky's snow and running water (#3176) and the player's own pad never close one. An admin who builds there is told so
/// once per pad.
/// </summary>
public sealed class LandingPadBuiltOverTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.FriendlyVisitors = false;
        });
        w.FoundBase();
        w.SetBaseFocus(BaseFocus.Market); // a Market base draws the traders (#3036)
        w.Server.ScanBaseLifeForTest();
        return w;
    }

    private static BlockId Stone => Content.Value.GetBlock("stone")!.NumericId;

    /// <summary>The pad a trader takes on this world: free (not the player's), dry, not molten — nearest the base core.</summary>
    private static int NearestFreeDryPad(NpcLifeWorld w)
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

    /// <summary>A player builds a block in a communal pad's landing volume (three above its ground).</summary>
    private static void BuildOn(NpcLifeWorld w, int padIndex, string block = "stone")
    {
        var pad = w.Server.LandingPadInfoForTest(padIndex);
        w.Server.World.SetBlock(new Vector3i(pad.X + 1, pad.Y + 3, pad.Z - 2), Content.Value.GetBlock(block)!.NumericId,
            owner: w.Player.State.PlayerId);
    }

    private static List<T> To<T>(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<T>().ToList();

    [Fact]
    public void ATrader_SkipsThePadSomebodyBuiltOn_AndClearingItReopensThePad()
    {
        int nearest;
        using (var control = World("builtover_control"))
        {
            nearest = NearestFreeDryPad(control);
            Assert.True(nearest >= 0, "the arena has no free dry pad");
            Assert.True(control.Server.ForcePlanetTraderForTest());
            Assert.Equal(nearest, control.Server.LandedTraderPadForTest(control.Server.ActiveLocationId)); // the fixture: unbuilt, it lands there
        }

        using var w = World("builtover_skip");
        string body = w.Server.ActiveLocationId;
        Assert.Equal(nearest, NearestFreeDryPad(w));

        BuildOn(w, nearest);
        Assert.True(w.Server.CommunalPadBuiltOverForTest(body, nearest));

        // #3175: every other public pad of the arena lies thousands of blocks off — beyond the traders' reach, so none comes
        // this time (it used to park on the far side of the planet and still say "nearby").
        Assert.False(w.Server.ForcePlanetTraderForTest(), "the built-over pad is skipped, and no other pad is in reach");
        Assert.Equal(0, w.Server.LandedTraderCountForTest());

        var pad = w.Server.LandingPadInfoForTest(nearest);
        w.Server.World.SetBlock(new Vector3i(pad.X + 1, pad.Y + 3, pad.Z - 2), BlockId.Air, owner: w.Player.State.PlayerId);
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, nearest), "a mined block is air — the pad is open again");
        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Equal(nearest, w.Server.LandedTraderPadForTest(body));
    }

    [Fact]
    public void NaturalBlocks_OpenProps_AndTheGround_NeverCloseAPad()
    {
        using var w = World("builtover_props");
        string body = w.Server.ActiveLocationId;
        int i = NearestFreeDryPad(w);
        var pad = w.Server.LandingPadInfoForTest(i);

        w.Server.World.SetBlock(new Vector3i(pad.X, pad.Y + 4, pad.Z), Stone); // a server write: no owner
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, i));

        BuildOn(w, i, "torch"); // a hull may stand in an open prop
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, i));
        BuildOn(w, i, "flora_daisy"); // flora is marked solid but is a walk-through prop — a flower bed closes nothing
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, i));
        BuildOn(w, i, "energy_gate"); // a membrane is no prop for a hull: it would stand inside one
        Assert.True(w.Server.CommunalPadBuiltOverForTest(body, i));
        BuildOn(w, i, "torch");

        w.Server.World.SetBlock(new Vector3i(pad.X - 3, pad.Y, pad.Z), Content.Value.GetBlock("concrete")!.NumericId,
            owner: w.Player.State.PlayerId); // paving the pad's own ground is no building on it
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, i));

        w.Server.World.SetBlock(new Vector3i(pad.X - 3, pad.Y + 16, pad.Z), Stone, owner: w.Player.State.PlayerId);
        Assert.True(w.Server.CommunalPadBuiltOverForTest(body, i), "the landing volume reaches the clearance height");
    }

    [Fact]
    public void WeatherSnow_AndRunningWater_InCellsThePlayerOnceTouched_NeverCloseAPad_ButHisOwnSnowBlockDoes()
    {
        // #3176: a server write keeps the owner of a cell a player once mined or paved — every spaceport is made of such cells,
        // so a snow drift (or a puddle that ran onto the pad) used to read as "built" and closed the pad until it melted.
        using var w = World("builtover_snow");
        string body = w.Server.ActiveLocationId;
        int i = NearestFreeDryPad(w);
        var pad = w.Server.LandingPadInfoForTest(i);
        var drift = new Vector3i(pad.X + 2, pad.Y + 1, pad.Z + 1);
        var puddle = new Vector3i(pad.X + 4, pad.Y + 1, pad.Z + 2);
        foreach (var cell in new[] { drift, puddle })
        {
            w.Server.World.SetBlock(cell, Stone, owner: w.Player.State.PlayerId); // the player built here once …
            w.Server.World.SetBlock(cell, BlockId.Air, owner: w.Player.State.PlayerId); // … and mined it again
        }

        w.Server.DepositWeatherSnowForTest(drift.X, drift.Y, drift.Z); // a blizzard lays a drift on the pad
        w.Server.World.SetBlock(puddle, Content.Value.GetBlock("water")!.NumericId); // a flow step runs onto it (no owner given)
        Assert.Equal(Content.Value.GetBlock("snow")!.NumericId, w.Server.World.GetBlock(drift));
        Assert.False(w.Server.CommunalPadBuiltOverForTest(body, i), "the sky's snow and running water are no building");

        w.Server.World.SetBlock(new Vector3i(pad.X + 3, pad.Y + 1, pad.Z + 3), Content.Value.GetBlock("snow")!.NumericId,
            owner: w.Player.State.PlayerId); // a snow block the player placed himself
        Assert.True(w.Server.CommunalPadBuiltOverForTest(body, i));
    }

    [Fact]
    public void APadBuiltOverDuringTheApproach_IsGivenUp_AtTouchdown()
    {
        using var w = World("builtover_touchdown");
        string body = w.Server.ActiveLocationId;
        Assert.True(w.Server.ForcePlanetTraderForTest());
        int pad = w.Server.LandedTraderPadForTest(body);
        Assert.True(pad >= 0);

        BuildOn(w, pad); // the player builds on the pad while the ship is still coming in
        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);

        Assert.Equal(0, w.Server.LandedTraderCountForTest());
        Assert.DoesNotContain(w.Server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
        Assert.Empty(To<ShipTransitFx>(w).Where(f => f.ApproachSeconds == 0f)); // the approach's contrail (#3177), but no descent
    }

    [Fact]
    public void APlayer_CannotPickABuiltOverPad_TheAutoPickSkipsIt_AndTheChooserSaysWhy()
    {
        using var w = World("builtover_player");
        string body = w.Server.ActiveLocationId;
        w.Player.AssignedPadIndex = -1; // up in the air, holding no pad
        int auto = w.Server.TryClaimPadForTest(w.Player, -1).Chosen;
        Assert.True(auto >= 0);

        BuildOn(w, auto);
        int next = w.Server.TryClaimPadForTest(w.Player, -1).Chosen;
        Assert.True(next >= 0);
        Assert.NotEqual(auto, next);
        Assert.Equal((-1, "@srv.land.pad_built_over"), w.Server.TryClaimPadForTest(w.Player, auto));

        w.Server.RequestLandingPadsForTest(w.Player, body);
        var list = To<LandingPadList>(w).Last(l => l.BodyId == body);
        var built = list.Pads.Single(p => !p.Base && p.Index == auto);
        Assert.True(built.BuiltOver);
        Assert.True(built.Occupied, "an older client reads it as taken");
        Assert.False(list.Pads.Single(p => !p.Base && p.Index == next).BuiltOver);
    }

    [Fact]
    public void ThePlayersOwnPad_StaysTheirs_WhenTheyBuildOnIt()
    {
        using var w = World("builtover_own");
        string body = w.Server.ActiveLocationId;
        w.Player.AssignedPadIndex = -1;
        int mine = w.Server.TryClaimPadForTest(w.Player, -1).Chosen;
        w.Player.AssignedPadIndex = mine; // their ship is parked there

        BuildOn(w, mine);
        Assert.Equal((mine, string.Empty), w.Server.TryClaimPadForTest(w.Player, mine));

        w.Server.RequestLandingPadsForTest(w.Player, body);
        var own = To<LandingPadList>(w).Last(l => l.BodyId == body).Pads.Single(p => !p.Base && p.Index == mine);
        Assert.False(own.BuiltOver);
    }

    [Fact]
    public void AMarketTrader_SkipsAPublicBasePad_ItsOwnerBuiltIn()
    {
        using var w = World("builtover_basepad");
        string body = w.Server.ActiveLocationId;
        var cell = new Vector3i(NpcLifeWorld.Cx + 14, w.PadY, NpcLifeWorld.Cz);
        Assert.Null(w.Server.PlaceBasePadForTest(w.Player, cell));
        w.Server.SetBasePadForTest(w.Player, cell, string.Empty, BasePadAccess.Everyone);
        Assert.Equal(cell, w.Server.MarketBasePadForTest(w.Player));

        // An admin owner may build in the square (the no-build rule excepts admins) — the merchants keep off it then.
        w.Server.World.SetBlock(new Vector3i(cell.X + 3, cell.Y + 2, cell.Z - 1), Stone, owner: w.Player.State.PlayerId);
        Assert.Null(w.Server.MarketBasePadForTest(w.Player));
        Assert.True(Assert.Single(w.Server.BasePadListForTest(w.Player, body)).BuiltOver);
    }

    [Fact]
    public void AnAdminBuildingOnAPad_IsToldOnce_PerPad()
    {
        using var w = World("builtover_hint");
        w.Player.State.Role = PlayerRole.WorldAdmin;
        w.Player.State.InstantBuild = true;
        int i = Enumerable.Range(0, w.Server.LandingPadCount).First(w.Server.LandingPadIsDry);
        var pad = w.Server.LandingPadInfoForTest(i);
        w.Player.State.Position = new Vector3f(pad.X + 2.5f, pad.Y + 1, pad.Z + 3.5f);

        w.Server.PlaceBlock(NpcLifeWorld.Owner, pad.X, pad.Y + 2, pad.Z, "stone");
        Assert.Equal(Stone, w.Server.World.GetBlock(new Vector3i(pad.X, pad.Y + 2, pad.Z))); // a hint, not a ban
        w.Server.PlaceBlock(NpcLifeWorld.Owner, pad.X - 1, pad.Y + 2, pad.Z, "stone");

        Assert.Single(w.MessagesToPlayer(), m => m == "@srv.place.pad_admin_hint");
        Assert.True(w.Server.CommunalPadBuiltOverForTest(w.Server.ActiveLocationId, i));
    }

    [Fact]
    public void ANonAdmin_IsStillRefused_AndHearsNoHint()
    {
        using var w = World("builtover_player_refused");
        w.Player.State.Role = PlayerRole.Player;
        w.Player.State.InstantBuild = true;
        int i = Enumerable.Range(0, w.Server.LandingPadCount).First(w.Server.LandingPadIsDry);
        var pad = w.Server.LandingPadInfoForTest(i);
        w.Player.State.Position = new Vector3f(pad.X + 2.5f, pad.Y + 1, pad.Z + 3.5f);

        w.Server.PlaceBlock(NpcLifeWorld.Owner, pad.X, pad.Y + 2, pad.Z, "stone");

        Assert.True(w.Server.World.GetBlock(new Vector3i(pad.X, pad.Y + 2, pad.Z)).IsAir);
        Assert.DoesNotContain("@srv.place.pad_admin_hint", w.MessagesToPlayer());
    }
}
