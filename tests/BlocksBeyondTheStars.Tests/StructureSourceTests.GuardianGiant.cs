// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3680: the <c>guardian_giant</c> source is <see cref="GuardianGiantBuilder"/> on flat ground — the lying body of
/// <see cref="GuardianGiantBody.Lying"/>, its back sunk one row into the ground, every quarter turn.</summary>
public sealed partial class StructureSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GuardianGiant_IsTheBuildersLyingBody_ItsBackSunkIntoTheGround(int turns)
    {
        int seed = unchecked((int)(SeedA ^ (SeedA >> 32)));
        const int AnchorY = Flat + GuardianGiantBuilder.AnchorAboveGround;
        var world = new FlatWorld(Stone);
        int written = GuardianGiantBuilder.Build(world, Content, "stone", 0, AnchorY, 0, seed, turns, lit: false);
        var b = Build(GuardianGiantRules.StructureKind, SeedA, ("turns", turns.ToString(CultureInfo.InvariantCulture)));
        AssertIsTheBuilder(b, world);

        var lying = GuardianGiantBody.Lying(seed, lit: false, turns);
        Assert.Equal(lying.Count, written); // on flat ground nothing is filled and nothing cleared
        Assert.Equal(lying.Count, b.CountBlocks());
        foreach (var (dx, dy, dz, key, _) in lying)
        {
            Assert.Equal(Content.GetBlock(key)!.NumericId, world.Get(dx, AnchorY + dy, dz));
        }

        // The bottom row replaces the ground's top cell (the structure's ground row is its lowest row); every column reaches
        // down onto the ground or into it, so nothing hangs in the air.
        Assert.Equal(Flat, AnchorY + GuardianGiantBody.LyingBottom);
        Assert.Equal(0, b.GroundRow);
        Assert.Equal(GuardianGiantBody.LyingTop - GuardianGiantBody.LyingBottom + 1, b.Height);
        foreach (var column in lying.GroupBy(c => (c.Dx, c.Dz)))
        {
            int bottom = AnchorY + column.Min(c => c.Dy);
            Assert.True(bottom <= Flat + 1, $"column {column.Key} hangs at {bottom}");
        }

        var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(turns);
        Assert.Equal((maxX - minX + 1, maxZ - minZ + 1), (b.Width, b.Length));
    }

    [Fact]
    public void GuardianGiant_FillsADipUnderItsBody_AndClearsAPlantOnTop()
    {
        var world = new FlatWorld(Stone);
        const int AnchorY = Flat + GuardianGiantBuilder.AnchorAboveGround;
        var lying = GuardianGiantBody.Lying(7, lit: false, 0);
        var low = lying.Where(c => c.Dy == -1).First(c => !lying.Any(o => o.Dx == c.Dx && o.Dz == c.Dz && o.Dy < -1));
        var top = lying.Where(c => c.Dx == low.Dx && c.Dz == low.Dz).Max(c => c.Dy);
        var air = BlocksBeyondTheStars.Shared.Primitives.BlockId.Air;
        world.Set(low.Dx, Flat, low.Dz, air, 0, 0, 0); // a dip two deep under one column
        world.Set(low.Dx, Flat - 1, low.Dz, air, 0, 0, 0);
        var flower = Content.GetBlock("flora_flower")!;
        Assert.Equal("flora", flower.Category);
        world.Set(low.Dx, AnchorY + top + 1, low.Dz, flower.NumericId, 0, 0, 0);

        GuardianGiantBuilder.Build(world, Content, "dirt", 0, AnchorY, 0, 7, 0, lit: false);

        var dirt = Content.GetBlock("dirt")!.NumericId;
        Assert.Equal(dirt, world.Get(low.Dx, Flat, low.Dz));
        Assert.Equal(dirt, world.Get(low.Dx, Flat - 1, low.Dz));
        Assert.True(world.Get(low.Dx, AnchorY + top + 1, low.Dz).IsAir, "the plant on top of the body is cleared");
    }
}
