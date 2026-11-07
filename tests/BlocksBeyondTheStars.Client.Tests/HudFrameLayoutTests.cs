// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.HudFrames;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The 9-slice layout of the HUD frame (#3317) on every screen shape the game meets — 16:9, 21:9, 4:3 and a portrait
/// 9:16 phone: crisp tiles (whole multiples of the 32-pixel art), mirrored corners, edges of whole tiles that meet the
/// corners, nothing outside the screen or overlapping, and a frame that keeps off the visor's corner marker.
/// </summary>
public sealed class HudFrameLayoutTests
{
    public static IEnumerable<object[]> Screens() => new[]
    {
        new object[] { 1920, 1080 }, // 16:9
        new object[] { 2560, 1080 }, // 21:9
        new object[] { 1024, 768 },  // 4:3
        new object[] { 1080, 1920 }, // 9:16
        new object[] { 3840, 2160 },
        new object[] { 1280, 720 },
    };

    [Fact]
    public void TheTileSizes_AreWholeArtPixels_AndGrowWithTheSetting()
    {
        Assert.Equal(new[] { 64, 96, 128 }, Sizes(1920, 1080));
        Assert.Equal(new[] { 64, 96, 128 }, Sizes(2560, 1080)); // the shorter side decides: 21:9 frames like 16:9
        Assert.Equal(Sizes(1920, 1080), Sizes(1080, 1920));     // portrait
        Assert.Equal(new[] { 32, 64, 96 }, Sizes(1024, 768));
        Assert.Equal(new[] { 128, 192, 256 }, Sizes(3840, 2160));
        Assert.Equal(new[] { 32, 32, 32 }, Sizes(160, 100));    // never bigger than a third of the shorter side
    }

    private static int[] Sizes(int w, int h)
        => new[] { 0, 1, 2 }.Select(size => HudFrameLayout.TilePixels(w, h, size)).ToArray();

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryPiece_SitsOnTheScreen_WithoutOverlap_AndTheEdgesAreWholeTiles(int w, int h)
    {
        foreach (int size in new[] { 0, 1, 2 })
        {
            int tile = HudFrameLayout.TilePixels(w, h, size);
            Assert.Equal(0, tile % HudFrameDesign.TileSide);
            var pieces = HudFrameLayout.Compute(w, h, tile, 0);

            Assert.Equal(HudFrameLayout.PieceCount, pieces.Length);
            Assert.All(pieces, p => Assert.True(p.Visible && p.X >= 0 && p.Y >= 0 && p.X + p.W <= w + 0.01f && p.Y + p.H <= h + 0.01f));
            Assert.All(pieces.Take(4), p => Assert.Equal((tile, tile, 1, 1), ((int)p.W, (int)p.H, p.RepeatX, p.RepeatY)));
            for (int i = 0; i < pieces.Length; i++)
            {
                for (int j = i + 1; j < pieces.Length; j++)
                {
                    Assert.False(Overlap(pieces[i], pieces[j]), $"pieces {i} and {j} overlap on {w}×{h}");
                }
            }

            var top = pieces[4];
            var left = pieces[6];
            Assert.Equal(w - (2 * tile), top.W, 3);
            Assert.Equal(h - (2 * tile), left.H, 3);
            Assert.True(Math.Abs(top.W - (top.RepeatX * tile)) <= tile / 2f, "a top edge stretches by half a tile at most");
            Assert.True(Math.Abs(left.H - (left.RepeatY * tile)) <= tile / 2f, "a side edge stretches by half a tile at most");
            Assert.Equal(top.RepeatX, pieces[5].RepeatX);
            Assert.Equal(left.RepeatY, pieces[7].RepeatY);
        }
    }

    private static bool Overlap(HudFramePiece a, HudFramePiece b)
        => a.X < b.X + b.W - 0.01f && b.X < a.X + a.W - 0.01f && a.Y < b.Y + b.H - 0.01f && b.Y < a.Y + a.H - 0.01f;

    [Fact]
    public void TheCornerIsMirroredIntoTheOtherThree_AndTheSideIntoTheRightEdge()
    {
        var p = HudFrameLayout.Compute(1920, 1080, 96, 0);

        Assert.Equal(new[] { HudFrameTile.Corner, HudFrameTile.Corner, HudFrameTile.Corner, HudFrameTile.Corner, HudFrameTile.Top,
            HudFrameTile.Bottom, HudFrameTile.Side, HudFrameTile.Side }, p.Select(x => x.Tile).ToArray());
        Assert.Equal(new[] { (false, false), (true, false), (false, true), (true, true), (false, false), (false, false),
            (false, false), (true, false) }, p.Select(x => (x.FlipX, x.FlipY)).ToArray());

        // Unity's uvRect: a negative extent mirrors; an edge repeats its tile a whole number of times.
        Assert.Equal((1f, 0f, -1f, 1f), (p[1].U, p[1].V, p[1].UvW, p[1].UvH));
        Assert.Equal((0f, 1f, 1f, -1f), (p[2].U, p[2].V, p[2].UvW, p[2].UvH));
        Assert.Equal((1f, 1f, -1f, -1f), (p[3].U, p[3].V, p[3].UvW, p[3].UvH));
        Assert.Equal((0f, 0f, 18f, 1f), (p[4].U, p[4].V, p[4].UvW, p[4].UvH));
        Assert.Equal((1f, 0f, -1f, 9f), (p[7].U, p[7].V, p[7].UvW, p[7].UvH));
        Assert.Equal((1824f, 0f), (p[1].X, p[1].Y));
        Assert.Equal((0f, 984f), (p[2].X, p[2].Y));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void WithTheVisor_TheFrameKeepsOffTheMaskedLeftCorners(int w, int h)
    {
        int inset = HudFrameLayout.VisorInset(w, h);
        Assert.True(inset >= HudFrameLayout.VisorMarkerPixels);
        Assert.True(inset >= Math.Max(w, h) * 0.007);

        var pieces = HudFrameLayout.Compute(w, h, HudFrameLayout.TilePixels(w, h, 1), inset);
        Assert.All(pieces, p => Assert.True(p.X >= inset && p.Y >= inset && p.X + p.W <= w - inset + 0.01f && p.Y + p.H <= h - inset + 0.01f));
    }

    [Fact]
    public void ATinyWindow_LosesItsEdges_NotItsSanity()
    {
        var pieces = HudFrameLayout.Compute(60, 40, 32, 0);

        Assert.All(pieces.Take(4), p => Assert.Equal(20f, p.W)); // the corners shrink to meet
        Assert.False(pieces[6].Visible);                          // no room left for a side edge
        Assert.Equal(0, HudFrameLayout.Repeats(0f, 32f));
        Assert.Equal(1, HudFrameLayout.Repeats(10f, 32f));
        Assert.Throws<ArgumentException>(() => HudFrameLayout.Compute(10, 10, 4, 0, new HudFramePiece[3]));
    }

    [Fact]
    public void TheComposedPicture_ShowsTheCornerInAllFourCorners_TheTwinkle_AndTheOpacity()
    {
        var corner = new char[HudFrameDesign.TilePixels];
        Array.Fill(corner, '0');
        corner[0] = HudFrameDesign.SymbolOf(8);   // top-left pixel red
        corner[33] = HudFrameDesign.SymbolOf(25); // a light at (1, 1)
        Assert.True(HudFrameDesign.TryCreate(new[] { new string(corner), "", "", "" }, new[] { 25 }, new[] { "#000001", "#000002" }, out var design));
        int[] palette = Enumerable.Range(0, 32).Select(i => i * 0x010101).ToArray();
        palette[8] = 0xC43C32;

        byte[] picture = HudFrameComposer.Compose(design, 320, 180, 32, 4, palette, 0x101820);

        Assert.Equal(320 * 180 * 4, picture.Length);
        Assert.Equal(0xC43C32, Rgb(picture, 320, 4, 4));      // top-left
        Assert.Equal(0xC43C32, Rgb(picture, 320, 315, 4));    // top-right, mirrored
        Assert.Equal(0xC43C32, Rgb(picture, 320, 4, 175));    // bottom-left, mirrored
        Assert.Equal(0xC43C32, Rgb(picture, 320, 315, 175));  // bottom-right
        Assert.Equal(0x101820, Rgb(picture, 320, 1, 1));      // the inset stays clear
        Assert.Equal(0x101820, Rgb(picture, 320, 160, 90));   // and so does the middle of the view
        Assert.Equal(0x000001, Rgb(picture, 320, 5, 5));      // the light, step 0
        Assert.Equal(0x000002, Rgb(HudFrameComposer.Compose(design, 320, 180, 32, 4, palette, 0x101820, 1f, 1), 320, 5, 5));

        byte[] half = HudFrameComposer.Compose(design, 320, 180, 32, 4, palette, 0x000000, 0.5f);
        Assert.Equal(0x621E19, Rgb(half, 320, 4, 4));         // half of the red over black
    }

    private static int Rgb(byte[] rgba, int width, int x, int y)
    {
        int at = ((y * width) + x) * 4;
        return (rgba[at] << 16) | (rgba[at + 1] << 8) | rgba[at + 2];
    }
}
