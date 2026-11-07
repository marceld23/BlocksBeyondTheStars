// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.HudFrames;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The HUD frame design (#3317) as the client settings keep it and a submission sends it: the string form round-trips,
/// a transparent tile costs nothing, and anything a hand-edited settings file could hold that is not a whole valid frame is
/// rejected whole — the game never paints half a frame.
/// </summary>
public sealed class HudFrameDesignTests
{
    /// <summary>A tile with a recognisable pattern: every cell its own index, cycling through the palette.</summary>
    internal static string Pattern(int seed)
    {
        var chars = new char[HudFrameDesign.TilePixels];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = HudFrameDesign.SymbolOf((i * 7 + seed) % HudFrameDesign.PaletteSize);
        }

        return new string(chars);
    }

    internal static HudFrameDesign Sample()
    {
        Assert.True(HudFrameDesign.TryCreate(new[] { Pattern(1), Pattern(2), Pattern(3), string.Empty }, new[] { 25, 27 },
            new[] { "#fff4d6", "#7cff8a" }, out var design));
        return design;
    }

    [Fact]
    public void TheStringForm_RoundTrips_AndATransparentTileIsWrittenEmpty()
    {
        var design = Sample();
        string text = design.Encode();

        Assert.StartsWith(HudFrameDesign.Tag + "|", text, StringComparison.Ordinal);
        Assert.EndsWith("||25,27|#fff4d6,#7cff8a", text, StringComparison.Ordinal); // the bottom tile is transparent
        Assert.True(HudFrameDesign.TryParse(text, out var back));
        Assert.Equal(text, back.Encode());
        foreach (HudFrameTile tile in Enum.GetValues(typeof(HudFrameTile)))
        {
            Assert.Equal(design.Tile(tile), back.Tile(tile));
        }

        Assert.Equal(new[] { 25, 27 }, back.Twinkle);
        Assert.Equal(new[] { "#fff4d6", "#7cff8a" }, back.TwinkleColors);
        Assert.Equal(new string('0', HudFrameDesign.TilePixels), back.Tile(HudFrameTile.Bottom));
    }

    [Fact]
    public void TheEmptyString_IsNoFrame_AndAnAllTransparentDesignWritesNothing()
    {
        Assert.True(HudFrameDesign.TryParse(string.Empty, out var none));
        Assert.True(none.IsEmpty);
        Assert.True(HudFrameDesign.TryParse(null, out _));
        Assert.Equal(string.Empty, HudFrameDesign.Empty.Encode());

        Assert.True(HudFrameDesign.TryCreate(new[] { "", "", "", "" }, new[] { 25 }, Array.Empty<string>(), out var blank));
        Assert.True(blank.IsEmpty);
        Assert.Equal(string.Empty, blank.Encode()); // twinkle without a single painted pixel is still no frame
        Assert.False(Sample().IsEmpty);
    }

    [Fact]
    public void UpperCaseSymbols_AndColours_AreNormalised()
    {
        string text = $"hf1|{Pattern(5).ToUpperInvariant()}||||12|#ABCDEF";

        Assert.True(HudFrameDesign.TryParse(text, out var design));
        Assert.Equal(Pattern(5), design.Tile(HudFrameTile.Corner));
        Assert.Equal("#abcdef", Assert.Single(design.TwinkleColors));
    }

    public static IEnumerable<object[]> BadInput()
    {
        string tile = Pattern(1);
        yield return new object[] { "garbage" };
        yield return new object[] { $"hf2|{tile}|||||" };                        // an unknown version
        yield return new object[] { $"hf1|{tile}||||" };                          // a part missing
        yield return new object[] { $"hf1|{tile}||||||" };                        // a part too many
        yield return new object[] { $"hf1|{tile.Substring(1)}|||||" };            // 1023 pixels
        yield return new object[] { $"hf1|{tile}0|||||" };                        // 1025 pixels
        yield return new object[] { $"hf1|{"w" + tile.Substring(1)}|||||" };      // not in the alphabet
        yield return new object[] { $"hf1|{tile}||||0|" };                        // transparent cannot twinkle
        yield return new object[] { $"hf1|{tile}||||32|" };                       // past the palette
        yield return new object[] { $"hf1|{tile}||||5,5|" };                      // twice the same index
        yield return new object[] { $"hf1|{tile}||||1,2,3,4,5,6,7,8,9|" };        // more than eight
        yield return new object[] { $"hf1|{tile}||||x|" };
        yield return new object[] { $"hf1|{tile}||||5,|" };
        yield return new object[] { $"hf1|{tile}|||||fff4d6" };                   // no '#'
        yield return new object[] { $"hf1|{tile}|||||#ggg000" };
        yield return new object[] { $"hf1|{tile}|||||#fff" };
        yield return new object[] { $"hf1|{tile}|||||" + string.Join(",", Enumerable.Repeat("#ffffff", 9)) };
    }

    [Theory]
    [MemberData(nameof(BadInput))]
    public void AnythingThatIsNotAWholeValidFrame_IsRejectedWhole(string text)
    {
        Assert.False(HudFrameDesign.TryParse(text, out var design));
        Assert.Same(HudFrameDesign.Empty, design);
        Assert.Same(HudFrameDesign.Empty, HudFrameDesign.Parse(text));
    }

    [Fact]
    public void TheEditorGrid_HoldsTheFourTilesSideBySide_AndKeepsTheTwinkle()
    {
        var design = Sample();
        int[] grid = design.ToEditorGrid();

        Assert.Equal(HudFrameDesign.EditorGridW * HudFrameDesign.EditorGridH, grid.Length);
        // Row 3, column 5 of the TOP tile (the second one) sits 32 cells further right.
        Assert.Equal(HudFrameDesign.ValueOf(Pattern(2)[3 * 32 + 5]), grid[3 * HudFrameDesign.EditorGridW + 32 + 5]);
        Assert.Equal(design.TileGrid(HudFrameTile.Side)[31 * 32 + 31], grid[31 * HudFrameDesign.EditorGridW + 64 + 31]);

        grid[0] = 31; // paint the corner's top-left pixel
        grid[HudFrameDesign.EditorGridW - 1] = 99; // out of range → transparent
        var edited = design.WithEditorGrid(grid);
        Assert.Equal('v', edited.Tile(HudFrameTile.Corner)[0]);
        Assert.Equal('0', edited.Tile(HudFrameTile.Bottom)[31]);
        Assert.Equal(design.Twinkle, edited.Twinkle);
        Assert.Equal(design.TwinkleColors, edited.TwinkleColors);
        Assert.Same(HudFrameDesign.Empty, design.WithEditorGrid(new int[10]));
    }

    [Fact]
    public void ATemplate_BecomesAnEditableDesign_ItsEmptyTilesTransparent()
    {
        var template = new HudFrameDefinition
        {
            Key = "t",
            Corner = Pattern(4).ToUpperInvariant(),
            Top = Pattern(6),
            Twinkle = new[] { 10 },
            TwinkleColors = new[] { "#FFD34D" },
        };

        Assert.True(HudFrameDesign.TryFromTemplate(template, out var design));
        Assert.Equal(Pattern(4), design.Tile(HudFrameTile.Corner));
        Assert.Equal(new string('0', HudFrameDesign.TilePixels), design.Tile(HudFrameTile.Side));
        Assert.Equal("#ffd34d", design.TwinkleColors[0]);

        template.TwinkleColors = new[] { "gold" };
        Assert.False(HudFrameDesign.TryFromTemplate(template, out _));
        Assert.False(HudFrameDesign.TryFromTemplate(null, out _));
    }

    [Fact]
    public void TheTwinkle_RunsThroughTheColours_OneStepApartPerIndex_AndStandsStillWhenAsked()
    {
        var design = Sample(); // twinkle 25, 27 over two colours

        Assert.Equal(0, HudFrameTwinkle.ColorSlot(design, 25, 0));
        Assert.Equal(1, HudFrameTwinkle.ColorSlot(design, 27, 0)); // the next bulb shows the next colour
        Assert.Equal(1, HudFrameTwinkle.ColorSlot(design, 25, 1));
        Assert.Equal(0, HudFrameTwinkle.ColorSlot(design, 25, 2));
        Assert.Equal(-1, HudFrameTwinkle.ColorSlot(design, 3, 0));
        Assert.Equal(-1, HudFrameTwinkle.ColorSlot(design, 0, 0));

        Assert.Equal(0, HudFrameTwinkle.Step(12.3, still: true));
        Assert.Equal(24, HudFrameTwinkle.Step(12.3, still: false));
        Assert.True(HudFrameTwinkle.TryParseRgb("#ff7a7a", out int rgb));
        Assert.Equal(0xFF7A7A, rgb);

        Assert.True(HudFrameDesign.TryCreate(new[] { Pattern(1), "", "", "" }, new[] { 9 }, Array.Empty<string>(), out var plain));
        Assert.Same(HudFrameTwinkle.DefaultColors, HudFrameTwinkle.ColorsOf(plain)); // no colours named → the default lights
    }
}
