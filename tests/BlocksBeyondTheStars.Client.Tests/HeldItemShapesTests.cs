// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1931 ("all drills, pistols and knives look the same in the hand"): every item of those kinds has its own held
/// model, and the base items keep the model their kind always had.</summary>
public sealed class HeldItemShapesTests
{
    private static readonly HeldItemShapes.Rgb Tint = new(0.5f, 0.5f, 0.5f);

    private static string Signature(string kind, string? item)
        => string.Join("|", HeldItemShapes.Parts(kind, item, Tint)!.Select(p => string.Format(CultureInfo.InvariantCulture,
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
        var drill = HeldItemShapes.Parts("Drill", "basic_drill", Tint)!;
        Assert.Equal(3, drill.Count);
        Assert.Equal(0.04f, drill[0].Position.Z);
        Assert.Equal(0.26f, drill[0].Size.Z);
        Assert.Equal(Tint.R, drill[1].Color.R); // the bit carries the kind's tint

        Assert.Equal(Signature("Drill", "basic_drill"), Signature("Drill", null));
        Assert.Equal(Signature("Gun", "unknown_gun"), Signature("Gun", null));
        Assert.Equal(3, HeldItemShapes.Parts("Gun", null, Tint)!.Count);
        Assert.Equal(2, HeldItemShapes.Parts("Blade", null, Tint)!.Count);
        Assert.Equal(3, HeldItemShapes.Parts("Scanner", "hand_scanner", Tint)!.Count);
    }

    [Theory]
    [InlineData("Block")]
    [InlineData("Gadget")]
    [InlineData("Hand")]
    [InlineData("Hoe")]
    public void OtherKinds_AreNotShapedHere(string kind)
    {
        Assert.Null(HeldItemShapes.Parts(kind, "anything", Tint));
    }
}
