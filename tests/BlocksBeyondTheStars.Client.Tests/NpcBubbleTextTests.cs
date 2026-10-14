// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2605 — the speech bubble's text (<see cref="NpcBubbleText"/>): at most 200 characters, cut at a sentence end, else a
/// word, else hard for CJK with an ellipsis; held 7–15 s by length.
/// </summary>
public sealed class NpcBubbleTextTests
{
    [Fact]
    public void AShortLine_StaysAsItIs_ButItsWhitespaceCollapses()
    {
        Assert.Equal("Hello, pilot!", NpcBubbleText.Clamp("Hello, pilot!"));
        Assert.Equal("Hello, pilot! Nice day.", NpcBubbleText.Clamp("  Hello,\n  pilot!\t\tNice day.  "));
        Assert.Equal(string.Empty, NpcBubbleText.Clamp(null));
        Assert.Equal(string.Empty, NpcBubbleText.Clamp("   "));
    }

    [Fact]
    public void ALongLine_IsCutAtItsLastSentenceEnd_WithinTheLimit()
    {
        string first = "The berries grow well this year and the rain was kind to the gardens of our little village.";
        string second = " I think we will bake pies for everybody, and you are invited too, of course, my friend!";
        string third = " Come by the tavern tonight when the stars are out and the soup is warm and ready for you.";
        string text = first + second + third;
        Assert.True(text.Length > NpcBubbleText.MaxChars);

        string clamped = NpcBubbleText.Clamp(text);
        Assert.True(clamped.Length <= NpcBubbleText.MaxChars);
        Assert.Equal(first + second, clamped); // complete sentences, no ellipsis
    }

    [Fact]
    public void WithoutASentenceEnd_TheCutFallsOnAWordBoundary_WithAnEllipsis()
    {
        string text = string.Join(" ", Enumerable.Repeat("lantern", 60)); // 479 chars, no full stop
        string clamped = NpcBubbleText.Clamp(text);
        Assert.True(clamped.Length <= NpcBubbleText.MaxChars);
        Assert.EndsWith("lantern" + NpcBubbleText.Ellipsis, clamped); // a whole word before the ellipsis
        Assert.DoesNotContain("  ", clamped);
    }

    [Fact]
    public void ASentenceEndTooEarly_LosesToTheWordCut()
    {
        string text = "Hi. " + string.Join(" ", Enumerable.Repeat("garden", 60));
        string clamped = NpcBubbleText.Clamp(text);
        Assert.NotEqual("Hi.", clamped); // not three characters out of three hundred
        Assert.EndsWith(NpcBubbleText.Ellipsis, clamped);
        Assert.True(clamped.Length > 150);
    }

    [Fact]
    public void Cjk_WithoutSpaces_IsCutHard_WithAnEllipsis()
    {
        string text = new string('星', 300);
        string clamped = NpcBubbleText.Clamp(text);
        Assert.Equal(NpcBubbleText.MaxChars, clamped.Length);
        Assert.EndsWith(NpcBubbleText.Ellipsis, clamped);

        // A CJK sentence end within the limit wins.
        string sentences = new string('月', 120) + "。" + new string('花', 200);
        Assert.Equal(new string('月', 120) + "。", NpcBubbleText.Clamp(sentences));
    }

    [Fact]
    public void AHardCut_NeverSplitsASurrogatePair()
    {
        string text = string.Concat(Enumerable.Repeat("😀", 150)); // 300 UTF-16 units
        string clamped = NpcBubbleText.Clamp(text);
        Assert.True(clamped.Length <= NpcBubbleText.MaxChars);
        string body = clamped.Substring(0, clamped.Length - NpcBubbleText.Ellipsis.Length);
        Assert.False(char.IsHighSurrogate(body[^1]));
        Assert.Equal(0, body.Length % 2);
    }

    [Fact]
    public void ACustomLimit_IsHonoured()
    {
        Assert.True(NpcBubbleText.Clamp(new string('a', 50) + " " + new string('b', 50), 60).Length <= 60);
        Assert.Equal(string.Empty, NpcBubbleText.Clamp("text", 0));
    }

    [Theory]
    [InlineData(0, 7f)]
    [InlineData(20, 7f)]
    [InlineData(70, 9f)]
    [InlineData(98, 11f)]
    [InlineData(154, 15f)]
    [InlineData(350, 15f)]
    public void TheHold_GrowsWithTheText_Between7And15Seconds(int length, float seconds)
        => Assert.Equal(seconds, NpcBubbleText.HoldSeconds(length), 3);
}
