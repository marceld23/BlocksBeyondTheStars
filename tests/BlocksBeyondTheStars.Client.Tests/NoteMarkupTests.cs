// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The §-markup of player notes (#1844): colour and bold codes become uGUI rich text, unknown codes vanish,
/// styles never leak past a line break, and a player's own "&lt;tag&gt;" is neutralised before any of it.
/// </summary>
public sealed class NoteMarkupTests
{
    [Fact]
    public void Render_ColoursASpan_AndClosesItAtTheEnd()
        => Assert.Equal("plain <color=#FF6B6B>red</color>", NoteMarkup.Render("plain §4red"));

    [Fact]
    public void Render_BoldAndColour_StayNested()
        => Assert.Equal("<color=#5FD37A>green <b>bold</b></color> plain",
            NoteMarkup.Render("§2green §lbold§r plain"));

    [Fact]
    public void Render_ChangingTheColour_ClosesThePreviousOne_AndKeepsBold()
        => Assert.Equal("<b>a<color=#6FA8FF>b</color><color=#FFF27A>c</color></b>",
            NoteMarkup.Render("§la§1b§ec"));

    /// <summary>Bold opened INSIDE a colour: the colour change must close the bold first and re-open it inside
    /// the new colour, or the tags cross.</summary>
    [Fact]
    public void Render_ColourChange_ReopensBoldInsideTheNewColour()
        => Assert.Equal("<color=#FF6B6B>a<b>b</b></color><color=#5FD37A><b>c</b></color>",
            NoteMarkup.Render("§4a§lb§2c"));

    [Fact]
    public void Render_StylesEndAtTheLineBreak()
        => Assert.Equal("<color=#FFC94D>gold</color>\nplain again", NoteMarkup.Render("§6gold\nplain again"));

    [Theory]
    [InlineData("§zoops", "oops")]         // unknown code: both characters dropped
    [InlineData("§", "")]                  // a trailing lone escape
    [InlineData("tail §", "tail ")]
    [InlineData("§§4x", "4x")]             // "§§" is one unknown pair — the "4" after it is plain text
    public void Render_StripsUnknownAndDanglingCodes(string input, string expected)
        => Assert.Equal(expected, NoteMarkup.Render(input));

    [Fact]
    public void Render_AcceptsUpperCaseCodes()
        => Assert.Equal("<color=#FF9C9C><b>x</b></color>", NoteMarkup.Render("§C§Lx"));

    [Fact]
    public void Render_RepeatingTheSameCode_EmitsNothingTwice()
        => Assert.Equal("<color=#FF6B6B>ab</color>", NoteMarkup.Render("§4a§4b"));

    /// <summary>A typed rich-text tag must never reach the Text component as markup — only our own tags do.</summary>
    [Fact]
    public void Render_NeutralisesPlayerTypedTags_First()
        => Assert.Equal("< size=200>big <color=#FFFFFF>< b>x</color>", NoteMarkup.Render("<size=200>big §f<b>x"));

    [Theory]
    [InlineData("hello there")]
    [InlineData("")]
    [InlineData("5 < 10")]
    public void Render_LeavesPlainTextAlone(string input)
        => Assert.Equal(input, NoteMarkup.Render(input));

    [Fact]
    public void Render_HandlesNull()
        => Assert.Equal(string.Empty, NoteMarkup.Render(null));

    [Fact]
    public void Palette_HasSixteenSixDigitEntries()
    {
        Assert.Equal(16, NoteMarkup.Palette.Length);
        Assert.All(NoteMarkup.Palette, hex => Assert.Matches("^[0-9A-F]{6}$", hex));
    }

    [Theory]
    [InlineData("§4red §lbold", "red bold")]
    [InlineData("no codes", "no codes")]
    [InlineData("§", "")]
    [InlineData(null, "")]
    public void Strip_RemovesEveryCode(string? input, string expected)
        => Assert.Equal(expected, NoteMarkup.Strip(input));

    [Theory]
    [InlineData("§6Shopping§r\niron ×4", "Shopping")]
    [InlineData("\n\n  §lsecond line is first non-empty  \nthird", "second line is first non-empty")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void FirstLine_IsTheFirstNonEmptyLine_WithoutMarkup(string? input, string expected)
        => Assert.Equal(expected, NoteMarkup.FirstLine(input));
}
