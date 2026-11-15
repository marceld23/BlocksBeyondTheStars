// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Client.Showcase;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3706: the one logo, built from real blocks — the font draws every letter of the title, the blocks are shipped blocks
/// with shipped tiles, the raster is deterministic, opaque where a block is, transparent in the margin and stored bottom-up
/// like the game's textures; and the image helpers the poster capture composes with.
/// </summary>
public sealed class BlockLogoTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static string TileDir => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");

    private static byte[]? Tile(string key)
    {
        string path = Path.Combine(TileDir, key + ".bytes");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    [Fact]
    public void Font_DrawsEveryLetterOfTheTitle()
    {
        foreach (char ch in string.Concat(BlockLogo.Lines).Where(c => c != ' '))
        {
            Assert.True(BlockLogoFont.Has(ch), $"the block font has no '{ch}'");
            Assert.Contains(BlockLogoFont.Glyph(ch), row => row.Contains('#'));
        }

        Assert.All("ABCDEFGHIJKLMNOPQRSTUVWXYZ", ch => Assert.Equal(BlockLogo.GlyphHeight, BlockLogoFont.Glyph(ch).Length));
        Assert.All("ABCDEFGHIJKLMNOPQRSTUVWXYZ", ch => Assert.All(BlockLogoFont.Glyph(ch), row => Assert.Equal(BlockLogo.GlyphWidth, row.Length)));
    }

    [Fact]
    public void Blocks_AreShippedBlocks_WithShippedTiles()
    {
        foreach (string key in BlockLogo.WordBlocks.Distinct())
        {
            Assert.NotNull(Content.GetBlock(key));
            Assert.Equal(64 * 64 * 4, Tile(key)?.Length ?? 0);
        }

        var blocks = BlockLogo.Layout();
        var (cols, rows) = BlockLogo.GridSize();
        Assert.Equal((73, 16), (cols, rows)); // "BLOCKS BEYOND" over "THE STARS"
        Assert.All(blocks, b => Assert.InRange(b.Column, 0, cols - 1));
        Assert.All(blocks, b => Assert.InRange(b.Row, 0, rows - 1));
        Assert.Equal(blocks.Count, blocks.Select(b => (b.Column, b.Row)).Distinct().Count());
        Assert.Equal(BlockLogo.WordBlocks[0], blocks.First().Block);  // "BLOCKS"
        Assert.Equal(BlockLogo.WordBlocks[3], blocks.Last().Block);   // "STARS"
    }

    [Fact]
    public void Render_IsDeterministic_OpaqueOnBlocks_TransparentInTheMargin()
    {
        var a = BlockLogo.Render(8, Tile);
        var b = BlockLogo.Render(8, Tile);
        Assert.Equal(BlockLogo.ImageSize(8), (a.Width, a.Height));
        Assert.Equal(a.Rgba, b.Rgba);

        // Rows are bottom-up: the first "B" (column 0, row 0 of the grid) has its top-left block near the image's TOP,
        // which is the LAST rows of the buffer. Margin = one block, the depth offset sits above the front faces.
        int s = 8, depth = (int)System.Math.Round(s * 0.42), margin = s;
        int xFront = margin + s / 2, yFrontTop = margin + depth + s / 2; // centre of the block, top-down
        int bufferRow = a.Height - 1 - yFrontTop;
        Assert.Equal(255, a.Rgba[((bufferRow * a.Width) + xFront) * 4 + 3]);

        Assert.Equal(0, a.Rgba[3]);                                  // a bottom corner: far from every block
        Assert.Equal(0, a.Rgba[((a.Height * a.Width) - 1) * 4 + 3]); // the top-right corner
        Assert.Contains(a.Rgba.Where((_, i) => i % 4 == 3), alpha => alpha is > 0 and < 255); // the soft halo
    }

    [Fact]
    public void Image_DrawOver_BlendsByAlpha_AndKeepsTheBaseOpaque()
    {
        var baseImg = Solid(4, 4, 0, 0, 255, 255);
        var stamp = Solid(2, 2, 255, 0, 0, 128);
        baseImg.DrawOver(stamp, 1, 1);

        int p = ((1 * 4) + 1) * 4;
        Assert.InRange(baseImg.Rgba[p], 126, 130);     // half red
        Assert.InRange(baseImg.Rgba[p + 2], 125, 129); // half blue
        Assert.Equal(255, baseImg.Rgba[p + 3]);
        Assert.Equal(255, baseImg.Rgba[2]);            // outside the stamp: untouched blue
        Assert.Equal(0, baseImg.Rgba[0]);
    }

    [Fact]
    public void Image_CropToAspect_AndResize_AverageAreas()
    {
        var img = new RgbaImage(4, 2);
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                byte v = (byte)(x < 2 ? 0 : 200);
                img.Put(x, y, v, v, v, 255);
            }
        }

        var square = img.CropToAspect(1.0);
        Assert.Equal((2, 2), (square.Width, square.Height)); // the middle two columns
        Assert.Equal(0, square.Rgba[0]);
        Assert.Equal(200, square.Rgba[4]);

        var small = img.Resize(2, 1);
        Assert.Equal(0, small.Rgba[0]);
        Assert.Equal(200, small.Rgba[4]);
        var one = img.Resize(1, 1);
        Assert.Equal(100, one.Rgba[0]); // the area average
    }

    [Fact]
    public void Image_FlipRows_SwapsTopAndBottom()
    {
        var img = new RgbaImage(1, 3);
        img.Put(0, 0, 1, 0, 0, 255);
        img.Put(0, 2, 3, 0, 0, 255);
        img.FlipRows();
        Assert.Equal(3, img.Rgba[0]);
        Assert.Equal(1, img.Rgba[8]);
    }

    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var img = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                img.Put(x, y, r, g, b, a);
            }
        }

        return img;
    }
}
