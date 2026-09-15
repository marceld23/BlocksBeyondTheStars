// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1900: a tile that is a PICTURE of an object (the bed seen from above) was drawn whole on every face of the form
/// the block is placed as — several beds on one bed. Blocks name texture slots per form part instead; these tests
/// hold the slot resolution and the shipped data, and make sure no stamped prop can come back without a decision.
/// </summary>
public sealed class BlockFaceTextureTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static BlockFaceTexture Slot(string part, string side, float x0) => new() { Part = part, Side = side, Rect = new[] { x0, 0f, 1f, 1f } };

    [Fact]
    public void Resolve_PrefersPartAndSide_ThenPart_ThenSide_ThenAny()
    {
        var any = Slot("*", "*", 0.1f);
        var anyTop = Slot("*", "top", 0.2f);
        var pillow = Slot("pillow", "*", 0.3f);
        var pillowTop = Slot("pillow", "top", 0.4f);
        var faces = new List<BlockFaceTexture> { pillowTop, any, pillow, anyTop };

        Assert.Same(pillowTop, BlockFaceTextures.Resolve(faces, ShapePart.Pillow, FaceSide.Top));
        Assert.Same(pillow, BlockFaceTextures.Resolve(faces, ShapePart.Pillow, FaceSide.Side));
        Assert.Same(anyTop, BlockFaceTextures.Resolve(faces, ShapePart.Headboard, FaceSide.Top));
        Assert.Same(any, BlockFaceTextures.Resolve(faces, ShapePart.Headboard, FaceSide.Bottom));
        Assert.Null(BlockFaceTextures.Resolve(new List<BlockFaceTexture> { pillow }, ShapePart.Body, FaceSide.Top));
        Assert.Null(BlockFaceTextures.Resolve(null, ShapePart.Body, FaceSide.Top));
    }

    [Fact]
    public void ToUv_TurnsImageRowsIntoTextureCoordinates()
    {
        // Image rows count DOWN from the top of the PNG; texture v counts UP from the bottom of the tile.
        var (u0, v0, u1, v1) = BlockFaceTextures.ToUv(new[] { 0.25f, 0.75f, 0.5f, 0.125f });
        Assert.Equal(0.25f, u0);
        Assert.Equal(0.25f, v0);
        Assert.Equal(0.5f, u1);
        Assert.Equal(0.875f, v1);
    }

    [Fact]
    public void PartAndSideNames_RoundTrip()
    {
        for (int p = 0; p < ShapeParts.PartCount; p++)
        {
            Assert.True(ShapeParts.TryParsePart(ShapeParts.Name((ShapePart)p), out var part));
            Assert.Equal((ShapePart)p, part);
        }

        for (int s = 0; s < ShapeParts.SideCount; s++)
        {
            Assert.True(ShapeParts.TryParseSide(ShapeParts.Name((FaceSide)s), out var side));
            Assert.Equal((FaceSide)s, side);
        }

        Assert.Equal(ShapeParts.PartCount, Enum.GetValues<ShapePart>().Length);
        Assert.Equal(ShapeParts.SideCount, Enum.GetValues<FaceSide>().Length);
        Assert.False(ShapeParts.TryParsePart("mattress", out _));
    }

    [Fact]
    public void Validate_ReportsUnknownNamesTilesAndRects()
    {
        var def = new BlockDefinition
        {
            Key = "test_bed",
            TileKind = "drawing",
            Faces = new List<BlockFaceTexture>
            {
                new() { Part = "mattress" },
                new() { Side = "front" },
                new() { Tile = "no_such_block" },
                new() { Rect = new[] { 0f, 0f, 1.5f, 1f } },
                new() { Rect = new[] { 0f, 0f, 1f } },
            },
        };

        var problems = BlockFaceTextures.Validate(def, key => Content.GetBlock(key) != null);
        Assert.Equal(6, problems.Count);
    }

    [Fact]
    public void ShippedBlocks_DeclareOnlyValidSlots()
    {
        var problems = Content.Blocks.Values
            .SelectMany(b => BlockFaceTextures.Validate(b, key => Content.GetBlock(key) != null))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryStampedProp_SaysWhatItsTileShows_AndPicturesDressTheirForm()
    {
        // A block the server stamps with a non-cube form must say whether its tile is a material (fine sliced on
        // any form) or a picture (needs slots). Adding a new prop without that decision fails here — the way the
        // bed and the flower pot slipped through before #1900.
        var stamped = Content.Blocks.Values.Where(b => PropShapes.DefaultPlaceShape(b.Key) != 0).ToList();
        Assert.Contains(stamped, b => b.Key == "bed");
        foreach (var b in stamped)
        {
            Assert.True(b.TileKind is BlockFaceTextures.Material or BlockFaceTextures.Picture,
                $"{b.Key} is stamped as a form but declares no tileKind (\"material\" or \"picture\") in data/blocks.json");
            if (b.TileKind == BlockFaceTextures.Picture)
            {
                Assert.True(b.Faces is { Count: > 0 }, $"{b.Key}: a picture tile on a non-cube form needs face slots");
            }
        }
    }

    [Fact]
    public void TheBed_ShowsOneBedAcrossItsTwoHalves()
    {
        var bed = Content.GetBlock("bed")!;
        var head = BlockFaceTextures.Resolve(bed.Faces, ShapePart.BedHead, FaceSide.Top)!;
        var foot = BlockFaceTextures.Resolve(bed.Faces, ShapePart.BedFoot, FaceSide.Top)!;

        // Both tops stretch a region of the drawing; the head's end row is exactly the foot's start row, so the two
        // cells continue one drawing instead of each showing a whole bed.
        Assert.NotNull(head.Rect);
        Assert.NotNull(foot.Rect);
        Assert.Equal(head.Rect![3], foot.Rect![1], 3);
        Assert.Equal(head.Rect[0], foot.Rect[0], 3);
        Assert.Equal(head.Rect[2], foot.Rect[2], 3);
        Assert.True(head.Rect[3] - head.Rect[1] < 0.5f, "a mattress top must show at most half of the drawing");
    }
}
