// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The editors' button-label rule (#3263): one line down to 14 px, else two lines broken only between words at 13 px or
/// more, else "does not fit" — never the 11 px and the mid-word break of the old Best Fit. A fixed-width measure keeps the
/// arithmetic readable: every character is half an em wide.
/// </summary>
public sealed class ButtonLabelFitTests
{
    private static float Half(string text, int size) => text.Length * size * 0.5f;

    [Fact]
    public void AShortLabel_KeepsItsFullSizeOnOneLine()
    {
        var fit = ButtonLabelFit.Fit("Fill", 100f, 40f, Half);
        Assert.True(fit.Fits);
        Assert.Equal(22, fit.Size);
        Assert.Equal(1, fit.Lines);
    }

    [Fact]
    public void ALongerLabel_ShrinksOnOneLine_NotBelow14()
    {
        // 12 characters: 22 px needs 132 px, 16 px 96 — the box (100 × 0.96 = 96) takes 16 on one line.
        var fit = ButtonLabelFit.Fit("Spiegeln abc", 100f, 40f, Half);
        Assert.Equal((16, 1, true), (fit.Size, fit.Lines, fit.Fits));
    }

    [Fact]
    public void BelowTheOneLineFloor_ItTakesTwoLines_BrokenBetweenWords()
    {
        // 14 px would need 147 px: two words of 10 then fit two lines at 15 px (75 px each in 96).
        var fit = ButtonLabelFit.Fit("Abcdefghij Klmnopqrst", 100f, 44f, Half);
        Assert.True(fit.Fits);
        Assert.Equal(2, fit.Lines);
        Assert.True(fit.Size >= ButtonLabelFit.TwoLineMin);
    }

    [Fact]
    public void AWordWiderThanTheBox_IsNeverBrokenInTheMiddle_ItFailsInstead()
    {
        // One 16-letter word: 13 px makes it 104 px in a 96 px line — the old rule split it, this one reports it.
        var fit = ButtonLabelFit.Fit("Voorbeeldwoordje", 100f, 44f, Half);
        Assert.False(fit.Fits);
        Assert.Equal(ButtonLabelFit.TwoLineMin, fit.Size);
    }

    [Fact]
    public void TwoLinesNeedTheHeight_ALowButtonStaysOnOneLine()
    {
        // Two lines at 13 px are 33.2 px: a 24 px button cannot take them.
        var fit = ButtonLabelFit.Fit("Abcdefghij Klmnopqrst", 100f, 24f, Half);
        Assert.False(fit.Fits);
    }

    [Fact]
    public void CjkTextBreaksBetweenCharacters_WithoutCountingAsABrokenWord()
    {
        int lines = ButtonLabelFit.CountLines("フレームを追加します", 50f, 10, Half, out bool broke);
        Assert.False(broke);
        Assert.Equal(1, lines); // ten characters of 5 px fill 50 px exactly

        lines = ButtonLabelFit.CountLines("フレームを追加しますね", 50f, 10, Half, out broke);
        Assert.False(broke);
        Assert.Equal(2, lines);
    }

    [Fact]
    public void LatinWrapping_CountsLinesLikeUgui()
    {
        Assert.Equal(1, ButtonLabelFit.CountLines("aa bb", 50f, 10, Half, out _));
        Assert.Equal(2, ButtonLabelFit.CountLines("aaaa bbbb", 30f, 10, Half, out bool broke));
        Assert.False(broke);
        Assert.Equal(2, ButtonLabelFit.CountLines("aaaaaaaaaaaa", 30f, 10, Half, out broke)); // 60 px split into 30 + 30: mid-word
        Assert.True(broke);
        Assert.Equal(2, ButtonLabelFit.CountLines("a\nb", 30f, 10, Half, out _));
    }
}

/// <summary>The headless width estimate against widths Unity measured (<c>ChipRowLayoutTests</c>): within ±2 %.</summary>
public sealed class UiTextMeasureTests
{
    [Theory]
    [InlineData("Lagerstätten 12", 22, 139f)]
    [InlineData("Месторождения 12", 22, 201f)]
    [InlineData("Месторождения 12", 18, 164f)]
    public void TheEstimate_MatchesUnitysMeasurement(string text, int size, float unity)
    {
        float estimate = UiTextMeasure.Width(text, size);
        Assert.InRange(estimate, unity * 0.98f, unity * 1.02f);
    }

    [Fact]
    public void TheTable_CoversTheGamesScripts()
    {
        Assert.Equal(1000, UiTextMeasure.Advance(0x4E2D)); // 中
        Assert.Equal(920, UiTextMeasure.Advance(0xD55C));  // 한
        Assert.NotEqual(1000, UiTextMeasure.Advance('a'));
        Assert.NotEqual(1000, UiTextMeasure.Advance('Ж'));
    }
}
