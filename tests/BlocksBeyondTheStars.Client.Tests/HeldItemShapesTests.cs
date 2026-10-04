// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Held tools look like what they are (#1931) — and since #1962 an item's look is DATA
/// (<c>heldModel</c> in <c>data/items.json</c>), with the model of its kind as the fallback.</summary>
public sealed class HeldItemShapesTests
{
    private static readonly HeldItemShapes.Rgb Tint = new(0.5f, 0.5f, 0.5f);
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static string Signature(string kind, string? item)
        => string.Join("|", HeldItemShapes.Parts(kind, Tint, item == null ? null : Content.GetItem(item)?.HeldModel)!
            .Select(p => string.Format(CultureInfo.InvariantCulture,
                "{0:0.###},{1:0.###},{2:0.###}/{3:0.###},{4:0.###},{5:0.###}/{6:0.##},{7:0.##},{8:0.##}",
                p.Position.X, p.Position.Y, p.Position.Z, p.Size.X, p.Size.Y, p.Size.Z, p.Color.R, p.Color.G, p.Color.B)));

    [Theory]
    [InlineData("Drill", "basic_drill", "titanium_drill", "diamond_drill", "mining_beam")]
    [InlineData("Gun", "some_gun", "scrap_pistol", "gauss_pistol", "laser_pistol", "plasma_blaster")]
    [InlineData("Blade", "some_blade", "machete", "vibro_knife", "plasma_sword")]
    [InlineData("Scanner", "hand_scanner", "advanced_scanner")]
    public void EveryItemOfAKind_LooksDifferent(string kind, params string[] items)
    {
        var signatures = items.Select(i => Signature(kind, i)).ToList();
        Assert.Equal(items.Length, signatures.Distinct().Count());
    }

    [Fact]
    public void TheBaseItems_KeepTheModelTheirKindHad()
    {
        var drill = HeldItemShapes.Parts("Drill", Tint, Content.GetItem("basic_drill")?.HeldModel)!;
        Assert.Equal(3, drill.Count);
        Assert.Equal(0.04f, drill[0].Position.Z);
        Assert.Equal(0.26f, drill[0].Size.Z);
        Assert.Equal(Tint.R, drill[1].Color.R); // the bit carries the kind's tint

        Assert.Equal(Signature("Drill", "basic_drill"), Signature("Drill", null));
        Assert.Equal(Signature("Gun", "unknown_gun"), Signature("Gun", null));
        Assert.Equal(3, HeldItemShapes.Parts("Gun", Tint)!.Count);
        Assert.Equal(2, HeldItemShapes.Parts("Blade", Tint)!.Count);
        Assert.Equal(3, HeldItemShapes.Parts("Scanner", Tint)!.Count);
    }

    [Theory]
    [InlineData("Block")]
    [InlineData("Gadget")]
    [InlineData("Hand")]
    [InlineData("Hoe")]
    public void OtherKinds_AreNotShapedHere(string kind)
    {
        Assert.Null(HeldItemShapes.Parts(kind, Tint, Content.GetItem("titanium_drill")!.HeldModel));
    }

    [Fact]
    public void TheModelsInTheItemData_AreOnesTheGameCanDraw()
    {
        var withModel = Content.Items.Values.Where(i => i.HeldModel != null).ToList();

        Assert.True(withModel.Count >= 11, "the eleven tools that had a look of their own in code carry it as data now");
        foreach (var item in withModel)
        {
            Assert.InRange(item.HeldModel!.Count, 1, HeldModelPart.MaxParts);
            Assert.All(item.HeldModel!, part => Assert.True(part.IsValid(), item.Key + " has a part the game cannot draw"));
            Assert.NotNull(item.Tool); // a look is for something that is held as a tool
        }

        // the titanium drill, as it was in code: a blue-grey body first, five parts
        var titanium = HeldItemShapes.Parts("Drill", Tint, Content.GetItem("titanium_drill")!.HeldModel)!;
        Assert.Equal(5, titanium.Count);
        Assert.Equal(0.16f, titanium[0].Size.X);
        Assert.Equal(0.46f, titanium[0].Color.R, 2);
        Assert.Equal(0.68f, titanium[0].Color.B, 2);
    }

    [Fact]
    public void AModelThatIsNotTrusted_IsCutDownToWhatCanBeDrawn()
    {
        var model = new List<HeldModelPart>
        {
            new() { P = new[] { 0f, 0f, 0.1f }, S = new[] { 0.1f, 0.1f, 0.2f }, C = "#ff8000", G = true },
            new() { P = new[] { 0f, 0f }, S = new[] { 0.1f, 0.1f, 0.2f }, C = "#ffffff" },              // two coordinates
            new() { P = new[] { 0f, 0f, 0f }, S = new[] { 0.1f, -0.1f, 0.2f }, C = "#ffffff" },         // a negative size
            new() { P = new[] { 0f, 9f, 0f }, S = new[] { 0.1f, 0.1f, 0.2f }, C = "#ffffff" },          // far from the hand
            new() { P = new[] { 0f, 0f, 0f }, S = new[] { float.NaN, 0.1f, 0.2f }, C = "#ffffff" },
            new() { P = new[] { 0f, 0f, 0f }, S = new[] { 0.1f, 0.1f, 0.2f }, C = "red" },              // not a colour
            new() { P = new[] { 0f, 0f, 0f }, S = new[] { 0.1f, 0.1f, 0.2f }, C = "tint" },
        };

        var parts = HeldItemShapes.Parts("Gun", Tint, model)!;

        Assert.Equal(2, parts.Count);
        Assert.True(parts[0].Glow);
        Assert.Equal(1f, parts[0].Color.R, 2);
        Assert.Equal(0.5f, parts[0].Color.G, 2);
        Assert.Equal(Tint.G, parts[1].Color.G);

        var tooMany = Enumerable.Range(0, 200).Select(_ => model[0]).ToList();
        Assert.Equal(HeldModelPart.MaxParts, HeldItemShapes.Parts("Gun", Tint, tooMany)!.Count);

        // nothing drawable at all → the kind's own model, never an empty hand
        Assert.Equal(3, HeldItemShapes.Parts("Gun", Tint, new List<HeldModelPart> { model[1] })!.Count);
    }

    // ---------------- #2278 two hands: the mirror ----------------

    [Fact]
    public void Mirror_FlipsOnlyX_AndTwiceGivesThePartsBack()
    {
        var right = HeldItemShapes.Glove(Content.GetItem("shock_gloves")!.HeldModel, Tint);
        var left = HeldItemShapes.Mirror(right);

        Assert.Equal(right.Count, left.Count);
        for (int i = 0; i < right.Count; i++)
        {
            Assert.Equal(-right[i].Position.X, left[i].Position.X);
            Assert.Equal(right[i].Position.Y, left[i].Position.Y);
            Assert.Equal(right[i].Position.Z, left[i].Position.Z);
            Assert.Equal(right[i].Size, left[i].Size);     // no negative scale: the faces stay outward
            Assert.True(left[i].Size.X > 0f);
            Assert.Equal(right[i].Color.R, left[i].Color.R);
            Assert.Equal(right[i].Glow, left[i].Glow);
        }

        var back = HeldItemShapes.Mirror(left);
        Assert.Equal(right.Select(p => p.Position), back.Select(p => p.Position));
    }

    [Fact]
    public void Mirror_PutsTheThumbOnTheOtherSide_SoBothHandsHaveItInside()
    {
        var right = HeldItemShapes.Glove(Content.GetItem("energy_gloves")!.HeldModel, Tint);
        var left = HeldItemShapes.Mirror(right);
        float RightThumb(List<HeldItemShapes.Part> parts) => parts.Where(p => System.Math.Abs(p.Position.X) > 0.07f).Select(p => p.Position.X).First();
        Assert.True(RightThumb(right) < 0f, "a right hand's thumb sits on its inner (−x) side");
        Assert.True(RightThumb(left) > 0f, "the mirrored left hand has it on +x — again the inner side");

        // A symmetric part list mirrors onto itself (as a set).
        var symmetric = HeldItemShapes.ClimbGear(gloves: true, claws: true);
        var mirrored = HeldItemShapes.Mirror(symmetric);
        Assert.Equal(
            symmetric.Select(p => (p.Position.X, p.Position.Y, p.Position.Z)).OrderBy(t => t),
            mirrored.Select(p => (p.Position.X == 0f ? 0f : p.Position.X, p.Position.Y, p.Position.Z)).OrderBy(t => t));
    }

    [Fact]
    public void TheTwoGloves_LookDifferent_AndAGloveWithoutDataIsStillAGlove()
    {
        string Sig(string key) => string.Join("|", HeldItemShapes.Glove(Content.GetItem(key)!.HeldModel, Tint)
            .Select(p => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}/{3:0.##}", p.Position.X, p.Position.Y, p.Position.Z, p.Color.R)));
        Assert.NotEqual(Sig("shock_gloves"), Sig("energy_gloves"));
        Assert.Contains(HeldItemShapes.Glove(Content.GetItem("shock_gloves")!.HeldModel, Tint), p => p.Glow); // the cyan emitter

        var plain = HeldItemShapes.Glove(null, Tint);
        Assert.True(plain.Count >= 3);
        Assert.Contains(plain, p => p.Glow && p.Color.R == Tint.R); // the knuckle plate takes the kind's tint
        Assert.False(HeldItemShapes.IsShapedKind(HeldItemShapes.GlovesKind)); // not offered by the tool-look editor
        Assert.Null(HeldItemShapes.Parts(HeldItemShapes.GlovesKind, Tint));
    }

    [Fact]
    public void ClimbGear_IsNothingWithoutGloves_PadsWithGloves_AndClawsOnTop()
    {
        Assert.Empty(HeldItemShapes.ClimbGear(gloves: false, claws: false));
        var gloves = HeldItemShapes.ClimbGear(gloves: true, claws: false);
        var claws = HeldItemShapes.ClimbGear(gloves: false, claws: true);
        Assert.NotEmpty(gloves);
        Assert.Equal(gloves.Count + 3, claws.Count); // three claws past the fingertips
        Assert.Contains(gloves, p => p.Color.R > 0.9f && p.Color.G is > 0.4f and < 0.6f); // the orange grip pad
        Assert.All(claws.Skip(gloves.Count), p => Assert.True(p.Position.Z > 0.15f, "claws reach past the fingertips"));
    }
}
