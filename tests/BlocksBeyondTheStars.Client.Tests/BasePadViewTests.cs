// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3069: a body's pad list carries its communal pads and the player-built pads of its bases. The views keep the communal
/// pads as they were, group the base pads by their base (one land-map marker per base: own first, then allies', then the
/// rest), count what the player may land on, and find a pad by its cell for the E dialog.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class BasePadViewTests
{
    private static NetLandingPad Communal(int index) => new() { Index = index, X = index * 100 };

    private static NetLandingPad BasePad(int x, int baseX, byte relation, string name = "", bool locked = false, bool occupied = false, bool mine = false)
        => new()
        {
            Index = -1,
            Base = true,
            X = x,
            CellX = x,
            CellY = 70,
            CellZ = 5,
            BaseX = baseX,
            BaseY = 70,
            BaseZ = 0,
            BaseName = "Base " + baseX,
            Name = name,
            Relation = relation,
            Locked = locked,
            Occupied = occupied,
            Mine = mine,
        };

    [Fact]
    public void TheCommunalPads_StayAsTheyWere()
    {
        var pads = new[] { Communal(0), BasePad(500, 480, NetBase.RelationOwn), Communal(1) };

        var communal = BasePadView.Communal(pads);

        Assert.Equal(new[] { 0, 1 }, communal.ConvertAll(p => p.Index));
        Assert.Empty(BasePadView.Communal(null));
    }

    [Fact]
    public void BasePads_AreGroupedByBase_OwnFirst_AndCountWhatThePlayerMayLandOn()
    {
        var pads = new[]
        {
            BasePad(900, 880, NetBase.RelationOther, "Gate", locked: true),
            BasePad(510, 480, NetBase.RelationOwn, "South"),
            BasePad(520, 480, NetBase.RelationOwn, "North", occupied: true),
            BasePad(530, 480, NetBase.RelationOwn, "Home", mine: true),
            BasePad(700, 690, NetBase.RelationAlly, "Ally field"),
            Communal(0),
        };

        var groups = BasePadView.GroupByBase(pads);

        Assert.Equal(new[] { 480, 690, 880 }, groups.ConvertAll(g => g.BaseX));
        Assert.Equal(new[] { "Home", "North", "South" }, groups[0].Pads.ConvertAll(p => p.Name));
        Assert.Equal(2, groups[0].Landable); // free + mine; the taken one is not
        Assert.Equal(1, groups[1].Landable);
        Assert.Equal(0, groups[2].Landable); // locked
    }

    [Fact]
    public void APad_IsFoundByItsCell_AndOnlyAmongTheBasePads()
    {
        var pads = new[] { Communal(0), BasePad(510, 480, NetBase.RelationOwn, "South") };

        Assert.Equal("South", BasePadView.Find(pads, 510, 70, 5)?.Name);
        Assert.Null(BasePadView.Find(pads, 510, 71, 5));
        Assert.Null(BasePadView.Find(null, 510, 70, 5));
        Assert.False(BasePadView.CanLand(BasePad(1, 1, NetBase.RelationOther, locked: true)));
        Assert.True(BasePadView.CanLand(BasePad(1, 1, NetBase.RelationOwn, occupied: true, mine: true)));
    }
}
