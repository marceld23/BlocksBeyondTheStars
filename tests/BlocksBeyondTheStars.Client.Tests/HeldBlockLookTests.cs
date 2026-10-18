// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2776: a dyed, glowing, shaped or painted block — and a prop with the form the server stamps on it — is held
/// as the world draws it. These pin which look the hand builds from the item key, and that it never promises what the
/// world would not draw.</summary>
public sealed class HeldBlockLookTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static readonly BlockDefinition Stone = new BlockDefinition { Key = "stone", Shapeable = true, Tintable = true };
    private static readonly BlockDefinition Plain = new BlockDefinition { Key = "machine_core", Shapeable = false };

    private static HeldBlockLook? Look(string key, BlockDefinition block, bool designKnown = true, string? voxels = null)
        => HeldBlockLook.For(key, block, _ => designKnown, _ => voxels);

    [Fact]
    public void APlainBlock_KeepsItsCube()
    {
        Assert.Null(Look("stone", Stone));
        Assert.Null(Look(null!, Stone));
        Assert.Null(HeldBlockLook.For("stone#t3f6fb0", null, null, null));
    }

    [Fact]
    public void ADyedBlock_IsACubeInItsColour()
    {
        var look = Look(ItemKey.Compose("stone", 0x3f6fb0, 0), Stone);
        Assert.NotNull(look);
        Assert.Equal(0x3f6fb0, look!.Tint);
        Assert.Equal(0, look.Glow);
        Assert.False(look.IsForm);
        Assert.Single(look.Cells);
        Assert.Equal(0, look.Cells[0].Descriptor);
    }

    [Fact]
    public void AGlowingBlock_CarriesItsLightColour()
    {
        var look = Look(ItemKey.Compose("stone", 0, 0x00ffff), Stone);
        Assert.NotNull(look);
        Assert.Equal(0x00ffff, look!.Glow);
    }

    [Fact]
    public void AShapedBlock_IsItsForm_TurnedToShowItsProfile()
    {
        var look = Look(ItemKey.Compose("stone", 0x3f6fb0, 0, (int)BlockShape.Stairs), Stone);
        Assert.NotNull(look);
        Assert.True(look!.IsForm);
        Assert.Equal((int)BlockShape.Stairs, look.Shape);
        int d = look.Cells.Single().Descriptor;
        Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(d));
        Assert.Equal(HeldBlockLook.DisplayYaw, ShapeCode.OrientationOf(d));
        Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(d));
        Assert.Equal((1, 0), ShapeCode.YawDirection(HeldBlockLook.DisplayYaw)); // rises to the eye's right, like the icon
    }

    [Fact]
    public void AFormOnABlockThatCannotTakeIt_IsHeldAsTheCubeTheServerPlaces()
    {
        // #1834: the server places a plain cube when the block is not Shapeable — the hand must not promise a slab.
        Assert.Null(Look(ItemKey.Compose("machine_core", 0, 0, (int)BlockShape.Slab), Plain));
    }

    [Fact]
    public void APaintedBlock_WearsItsDesign_OnlyOnceTheDesignIsKnown()
    {
        string key = ItemKey.Compose("stone", 0, 0, 0, 0x0005);
        var known = Look(key, Stone);
        Assert.NotNull(known);
        Assert.Equal(5, ShapeCode.DesignOf(known!.Cells.Single().Descriptor));
        Assert.Null(Look(key, Stone, designKnown: false)); // not in the paint atlas yet: the plain block

        var shapedAndPainted = Look(ItemKey.Compose("stone", 0, 0, (int)BlockShape.Slab, 0x0005), Stone);
        Assert.NotNull(shapedAndPainted);
        Assert.Equal((int)BlockShape.Slab, ShapeCode.ShapeOf(shapedAndPainted!.Cells.Single().Descriptor));
        Assert.Equal(5, ShapeCode.DesignOf(shapedAndPainted.Cells.Single().Descriptor));
    }

    [Fact]
    public void APlayerForm_NeedsItsVoxels_AndShowsEveryBlockOfIt()
    {
        int form = ShapeCode.FirstCustom;
        string key = ItemKey.Compose("stone", 0, 0, form);
        Assert.Null(Look(key, Stone, voxels: null)); // the voxels have not arrived: a cube, like the ghost shows

        var single = Look(key, Stone, voxels: SlabCell());
        Assert.NotNull(single);
        Assert.Single(single!.Cells);

        // A wardrobe 1 wide, 2 high, 1 long (#1961): both blocks, the upper one above the lower.
        string tall = CustomShape.ComposeMulti(1, 2, 1, new[] { SlabCell(), SlabCell(1) });
        Assert.False(string.IsNullOrEmpty(tall));
        var multi = Look(key, Stone, voxels: tall);
        Assert.NotNull(multi);
        Assert.Equal(2, multi!.Cells.Count);
        Assert.All(multi.Cells, c => Assert.True(c.Offset.X >= 0 && c.Offset.Y >= 0 && c.Offset.Z >= 0));
        Assert.Equal(new[] { 0, 1 }, multi.Cells.Select(c => ShapeCode.CellOf(c.Descriptor)).OrderBy(i => i).ToArray());
        Assert.Contains(multi.Cells, c => c.Offset.Y == 1);
    }

    [Fact]
    public void TheBed_IsHeldAsHeadAndFoot()
    {
        var bed = Content.GetBlock("bed");
        Assert.NotNull(bed);
        var look = HeldBlockLook.For("bed", bed, null, null);
        Assert.NotNull(look);
        Assert.Equal(2, look!.Cells.Count);
        Assert.Contains(look.Cells, c => ShapeCode.ShapeOf(c.Descriptor) == (int)BlockShape.BedHead);
        Assert.Contains(look.Cells, c => ShapeCode.ShapeOf(c.Descriptor) == (int)BlockShape.BedFoot);
        Assert.All(look.Cells, c => Assert.True(c.Offset.X >= 0 && c.Offset.Z >= 0));
    }

    [Theory]
    [InlineData("campfire", (int)BlockShape.Slab)]
    [InlineData("rug", (int)BlockShape.Sheet)]
    [InlineData("flower_pot", (int)BlockShape.Pot)]
    [InlineData("stairs", (int)BlockShape.Stairs)]
    public void APropBlock_IsHeldInTheFormItIsPlacedIn(string block, int shape)
    {
        var def = Content.GetBlock(block);
        Assert.NotNull(def);
        var look = HeldBlockLook.For(block, def, null, null);
        Assert.NotNull(look);
        Assert.Equal(shape, ShapeCode.ShapeOf(look!.Cells.Single().Descriptor));
    }

    [Fact]
    public void TheLadder_ShowsItsRungsToTheEye()
    {
        var look = HeldBlockLook.For("ladder", Content.GetBlock("ladder"), null, null);
        Assert.NotNull(look);
        int d = look!.Cells.Single().Descriptor;
        Assert.Equal(PropShapes.DefaultPlaceShape("ladder"), ShapeCode.ShapeOf(d));
        Assert.Equal(HeldBlockLook.LadderUpFace, ShapeCode.UpFaceOf(d));
    }

    [Fact]
    public void TheCacheKey_SeparatesEveryLook_AndSharesEqualOnes()
    {
        string a = Look(ItemKey.Compose("stone", 0x112233, 0), Stone)!.CacheKey;
        Assert.Equal(a, Look(ItemKey.Compose("stone", 0x112233, 0), Stone)!.CacheKey);
        Assert.NotEqual(a, Look(ItemKey.Compose("stone", 0x112234, 0), Stone)!.CacheKey);
        Assert.NotEqual(a, Look(ItemKey.Compose("stone", 0, 0x112233), Stone)!.CacheKey);
        Assert.NotEqual(a, Look(ItemKey.Compose("stone", 0x112233, 0, (int)BlockShape.Slab), Stone)!.CacheKey);
        Assert.NotEqual(a, HeldBlockLook.For(ItemKey.Compose("dirt", 0x112233, 0), new BlockDefinition { Key = "dirt" }, null, null)!.CacheKey);
    }

    /// <summary>One 8³ cell with its bottom layer filled (plus <paramref name="extra"/> voxels, so two cells differ).</summary>
    private static string SlabCell(int extra = 0)
    {
        var chars = new string('0', CustomShape.LargeChars).ToCharArray();
        for (int i = 0; i < (CustomShape.GridLarge * CustomShape.GridLarge) + extra; i++)
        {
            chars[i] = '1';
        }

        return new string(chars);
    }
}
