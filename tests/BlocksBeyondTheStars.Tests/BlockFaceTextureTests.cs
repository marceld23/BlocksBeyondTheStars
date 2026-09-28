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
/// <para>
/// #2124: plain CUBES read the same slots per side — "still many blocks have the same texture on all sides" (Justus).
/// A machine keeps its picture on its front (or on top, for a picture seen from above) and names its own casing, lid or
/// side tiles, which may be bundled textures that are no block. The tests below also pin the front's mapping.
/// </para>
/// </summary>
public sealed class BlockFaceTextureTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string TexturesDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");

    /// <summary>A tile key names a tile: a block, or a texture bundled with the build (a face tile, #2124).</summary>
    private static bool TileExists(string key)
        => Content.GetBlock(key) != null || File.Exists(Path.Combine(TexturesDir, key + ".bytes"));

    /// <summary>Blocks outside the "machine" category that are objects too (a picture of a thing, not a surface).</summary>
    private static readonly string[] OtherObjectBlocks =
    {
        "crate", "wood_crate", "beam_block", "gaming_pc", "gaming_monitor", "gaming_keyboard", "gaming_mouse",
        "broken_machine", "geyser_vent", "crystal_conduit", "engine_nozzle",
    };

    private static IEnumerable<BlockDefinition> ObjectBlocks()
        => Content.Blocks.Values.Where(b => b.Category == "machine" || Array.IndexOf(OtherObjectBlocks, b.Key) >= 0);

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
                new() { Side = "back" },
                new() { Side = "front" }, // valid side, but the block has no facing (#2124)
                new() { Tile = "no_such_block" },
                new() { Rect = new[] { 0f, 0f, 1.5f, 1f } },
                new() { Rect = new[] { 0f, 0f, 1f } },
            },
        };

        var problems = BlockFaceTextures.Validate(def, TileExists);
        Assert.Equal(7, problems.Count);

        // A facing block may dress its front; an unknown facing value is an error of its own.
        def.Faces = new List<BlockFaceTexture> { new() { Side = "front", Tile = "face_tech_side" } };
        def.TileKind = BlockFaceTextures.Picture;
        def.Facing = CubeFacing.Toward;
        Assert.Empty(BlockFaceTextures.Validate(def, TileExists));
        def.Facing = "sideways";
        Assert.Single(BlockFaceTextures.Validate(def, TileExists));
    }

    [Fact]
    public void ShippedBlocks_DeclareOnlyValidSlots()
    {
        // Every tile key resolves: a block's tile, or a bundled texture (the face tiles of #2124). An unknown key would
        // otherwise show the block's own tile on that face without anybody noticing.
        var problems = Content.Blocks.Values
            .SelectMany(b => BlockFaceTextures.Validate(b, TileExists))
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void FaceTiles_AreBundledOpaqueTiles_AndNoBlockKeys()
    {
        var keys = BlockFaceTextures.TextureOnlyKeys(Content.Blocks.Values, key => Content.GetBlock(key) != null);
        Assert.NotEmpty(keys);
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys); // the order the atlas deals its slots in
        foreach (string key in keys)
        {
            Assert.StartsWith("face_", key); // the texture editor groups them by this prefix
            string path = Path.Combine(TexturesDir, key + ".bytes");
            Assert.True(File.Exists(path), $"face tile '{key}' is named in blocks.json but not bundled ({path})");
            byte[] raw = File.ReadAllBytes(path);
            Assert.Equal(64 * 64 * 4, raw.Length);
            for (int i = 3; i < raw.Length; i += 4)
            {
                Assert.True(raw[i] == 255, $"face tile '{key}' has a see-through pixel — block tiles ship opaque");
            }
        }
    }

    [Fact]
    public void EveryObjectBlock_SaysWhatItsTileShows_AndPicturesGetTheirOwnSides()
    {
        // #2124: a block that is a THING must say whether its tile is a surface (fine on all six faces) or a picture.
        // A picture cube must give its other faces tiles of their own — the picture belongs on one face (its front) or
        // on top (a picture seen from above) — and a block with a front must dress its sides, or the front would be
        // indistinguishable from them. Adding a machine without that decision fails here.
        var objects = ObjectBlocks().ToList();
        Assert.Contains(objects, b => b.Key == "station_vendor");
        foreach (var b in objects)
        {
            Assert.True(b.TileKind is BlockFaceTextures.Material or BlockFaceTextures.Picture,
                $"{b.Key} is an object block but declares no tileKind (\"material\" or \"picture\") in data/blocks.json");
            if (b.TileKind != BlockFaceTextures.Picture || PropShapes.DefaultPlaceShape(b.Key) != 0)
            {
                continue; // a surface, or a stamped form (EveryStampedProp_SaysWhatItsTileShows_AndPicturesDressTheirForm)
            }

            int dressed = 0;
            for (int s = 0; s < ShapeParts.SideCount; s++)
            {
                var slot = BlockFaceTextures.Resolve(b.Faces, ShapePart.Body, (FaceSide)s);
                if (slot?.Tile != null)
                {
                    dressed++;
                }
            }

            Assert.True(dressed > 0, $"{b.Key}: a picture cube shows its picture on all six faces — give it side / top tiles");
            if (b.Facing != null)
            {
                Assert.NotNull(BlockFaceTextures.Resolve(b.Faces, ShapePart.Body, FaceSide.Side)?.Tile);
            }
        }
    }

    [Fact]
    public void FacingBlocks_ArePlainCubes()
    {
        // The front lives in a CUBE's descriptor (CubeFacing); a form or a shapeable material would overwrite it.
        var facing = Content.Blocks.Values.Where(b => b.Facing != null).ToList();
        Assert.Contains(facing, b => b.Key == "forge");
        Assert.Contains(facing, b => b.Key == "watcher" && b.Facing == CubeFacing.Away);
        foreach (var b in facing)
        {
            Assert.Equal(0, PropShapes.DefaultPlaceShape(b.Key));
            Assert.False(b.Shapeable, $"{b.Key} has a front, so it must not be reshaped into a form");
        }
    }

    [Fact]
    public void TheStretcher_IsAPictureOnSteelLegs()
    {
        // It was declared a "material", so its legs and frame showed slices of the canvas-and-cross picture.
        var stretcher = Content.GetBlock("stretcher")!;
        Assert.Equal(BlockFaceTextures.Picture, stretcher.TileKind);
        Assert.Null(BlockFaceTextures.Resolve(stretcher.Faces, ShapePart.Body, FaceSide.Top)?.Tile);
        Assert.Equal("steel_wall", BlockFaceTextures.Resolve(stretcher.Faces, ShapePart.Body, FaceSide.Side)?.Tile);
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
