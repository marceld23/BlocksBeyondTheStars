// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2777: every held item is held by the first-person suit hand, in a grip that fits it. These pin the grip
/// choice (data first, the kind's default otherwise) and that each grip's hand actually touches what it holds while its
/// forearm runs back out of view.</summary>
public sealed class HeldHandsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData("Block", HeldGrips.Palm)]
    [InlineData("Vehicle", HeldGrips.Palm)]
    [InlineData("Plant", HeldGrips.Stem)]
    [InlineData("Icon", HeldGrips.Pinch)]
    [InlineData("Drill", HeldGrips.Fist)]
    [InlineData("Gun", HeldGrips.Fist)]
    [InlineData("Blade", HeldGrips.Fist)]
    [InlineData("Scanner", HeldGrips.Fist)]
    [InlineData("Gadget", HeldGrips.Fist)]
    [InlineData("Tool", HeldGrips.Fist)]
    public void EveryKind_HasAGrip(string kind, string grip)
        => Assert.Equal(grip, HeldHands.GripFor(null, kind));

    [Theory]
    [InlineData("None")]
    [InlineData("Hand")]
    [InlineData("Gloves")]
    public void TheBareHandAndTheGloves_BringTheirOwnHands(string kind)
        => Assert.Null(HeldHands.GripFor(null, kind));

    [Fact]
    public void AnItemsOwnGrip_Wins_ButNotTheGlovesOne()
    {
        Assert.Equal(HeldGrips.Palm, HeldHands.GripFor(new ItemDefinition { Key = "x", HeldGrip = HeldGrips.Palm }, "Icon"));
        Assert.Equal(HeldGrips.Stem, HeldHands.GripFor(new ItemDefinition { Key = "x", HeldGrip = HeldGrips.Stem }, "Drill"));
        Assert.Equal(HeldGrips.Fist, HeldHands.GripFor(new ItemDefinition { Key = "x", HeldGrip = "nonsense" }, "Gun"));
    }

    [Fact]
    public void EveryHeldGripInTheData_IsOneTheClientKnows()
        => Assert.All(Content.Items.Values, i => Assert.True(HeldGrips.IsKnown(i.HeldGrip), $"{i.Key}: heldGrip '{i.HeldGrip}'"));

    [Fact]
    public void APistol_IsGrippedRoundItsGrip_RightUnderTheBody()
    {
        var gun = HeldItemShapes.Parts("Gun", new HeldItemShapes.Rgb(0.5f, 0.5f, 0.5f))!;
        var point = HeldHands.HandlePoint(gun);
        var grip = gun.OrderBy(p => p.Position.Y).First();
        Assert.Equal(grip.Position.Z, point.Z, 3);
        Assert.True(point.Y < 0f, "the fist sits below the barrel");
        float fistTop = point.Y + HeldHands.FistHalfHeight;
        Assert.InRange(fistTop, grip.Position.Y + (grip.Size.Y * 0.5f) - 0.02f, grip.Position.Y + (grip.Size.Y * 0.5f));
    }

    [Fact]
    public void ABlade_IsHeldAtTheModelsOrigin()
        => Assert.Equal(Vector3.Zero, HeldHands.HandlePoint(HeldItemShapes.Parts("Blade", new HeldItemShapes.Rgb(1f, 1f, 1f))));

    [Fact]
    public void EveryToolModel_IsTouchedByTheFist()
    {
        var models = Content.Items.Values.Where(i => i.Tool != null && i.HeldModel != null && !HeldGrips.IsGloves(i)).ToList();
        Assert.NotEmpty(models);
        foreach (var item in models)
        {
            var parts = HeldItemShapes.FromModel(item.HeldModel, new HeldItemShapes.Rgb(1f, 1f, 1f));
            var hand = HeldHands.For(HeldGrips.Fist, parts, Vector3.Zero, Vector3.Zero);
            var fist = hand.Where(b => b.Piece == HeldHands.Piece.Hand).ToList();
            Assert.True(parts.Any(p => fist.Any(f => Overlaps(f.Position, f.Size, p.Position, p.Size))), $"{item.Key}: the fist holds nothing");
        }
    }

    [Fact]
    public void ThePalm_CarriesTheItem_WithTheThumbAtItsInnerSide()
    {
        var min = new Vector3(-0.11f, -0.11f, 0.05f);
        var max = new Vector3(0.11f, 0.11f, 0.27f);
        var hand = HeldHands.For(HeldGrips.Palm, null, min, max);
        var palm = hand.Where(b => b.Piece == HeldHands.Piece.Hand).OrderByDescending(b => b.Size.X).First();
        Assert.Equal(min.Y, palm.Position.Y + (palm.Size.Y * 0.5f), 4); // its top touches the item's bottom
        Assert.True(palm.Position.X - (palm.Size.X * 0.5f) <= min.X + 0.001f, "the palm reaches the item's inner edge");
        var thumb = hand.Where(b => b.Piece == HeldHands.Piece.Hand).OrderBy(b => b.Size.X).First();
        Assert.True(thumb.Position.X + (thumb.Size.X * 0.5f) <= min.X + 0.001f, "the thumb is beside the item, not in it");
        Assert.True(thumb.Position.Y + (thumb.Size.Y * 0.5f) > min.Y, "the thumb rises against its side");
    }

    [Theory]
    [InlineData(HeldGrips.Pinch, HeldHands.PinchDepth)]
    [InlineData(HeldGrips.Stem, HeldHands.StemDepth)]
    public void ThePinchAndTheStem_CloseOnTheItemsFoot(string grip, float depth)
    {
        var min = new Vector3(-0.15f, -0.11f, 0.14f);
        var max = new Vector3(0.15f, 0.19f, 0.16f);
        var fist = HeldHands.For(grip, null, min, max).Where(b => b.Piece == HeldHands.Piece.Hand).OrderByDescending(b => b.Size.Z).First();
        Assert.Equal(min.Y + depth, fist.Position.Y + (fist.Size.Y * 0.5f), 4);
    }

    [Theory]
    [InlineData(HeldGrips.Fist)]
    [InlineData(HeldGrips.Palm)]
    [InlineData(HeldGrips.Pinch)]
    [InlineData(HeldGrips.Stem)]
    public void EveryGrip_HasAForearmRunningBackAndDown_FromTheHand(string grip)
    {
        var parts = HeldItemShapes.Parts("Drill", new HeldItemShapes.Rgb(1f, 1f, 1f));
        var hand = HeldHands.For(grip, parts, new Vector3(-0.1f, -0.1f, 0.05f), new Vector3(0.1f, 0.1f, 0.25f));
        var forearm = Assert.Single(hand, b => b.Piece == HeldHands.Piece.Forearm);
        Assert.Single(hand, b => b.Piece == HeldHands.Piece.Cuff);
        var hands = hand.Where(b => b.Piece == HeldHands.Piece.Hand).ToList();
        Assert.Equal(2, hands.Count);
        Assert.True(forearm.Position.Z < hands.Min(h => h.Position.Z), "the forearm is behind the hand");
        Assert.True(forearm.Position.Y < hands.Max(h => h.Position.Y), "and below it");
        Assert.True(forearm.Size.Z > 0.25f, "long enough to leave the view");
    }

    [Fact]
    public void TheSwingTurnsRoundTheForearm_ALittleBehindTheHand()
    {
        var hand = HeldHands.Fist(new Vector3(0f, -0.1f, 0f), HeldHands.ItemForearm);
        var pivot = HeldHands.SwingPivot(hand);
        var fist = new Vector3(0f, -0.1f, 0f);
        Assert.Equal(HeldHands.SwingPivotDistance, Vector3.Distance(pivot, fist), 4);
        Assert.True(pivot.Z < fist.Z && pivot.Y < fist.Y, "behind and below the fist, toward the elbow");
        Assert.Equal(Vector3.Zero, HeldHands.SwingPivot(System.Array.Empty<HeldHands.Box>()));
    }

    [Fact]
    public void TheEmptySlotFist_IsTheBareHandOf1033()
    {
        var hand = HeldHands.Fist(new Vector3(0f, 0f, 0.07f));
        Assert.Contains(hand, b => b.Piece == HeldHands.Piece.Forearm && Near(b.Position, new Vector3(0.02f, -0.06f, -0.16f)));
        Assert.Contains(hand, b => b.Piece == HeldHands.Piece.Cuff && Near(b.Position, new Vector3(0f, -0.02f, -0.02f)));
        Assert.Contains(hand, b => b.Piece == HeldHands.Piece.Hand && Near(b.Position, new Vector3(-0.075f, 0.01f, 0.05f)));
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-4f;

    private static bool Overlaps(Vector3 ca, Vector3 sa, Vector3 cb, Vector3 sb)
        => Math.Abs(ca.X - cb.X) <= (sa.X + sb.X) * 0.5f
           && Math.Abs(ca.Y - cb.Y) <= (sa.Y + sb.Y) * 0.5f
           && Math.Abs(ca.Z - cb.Z) <= (sa.Z + sb.Z) * 0.5f;
}
