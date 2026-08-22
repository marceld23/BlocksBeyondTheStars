// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Moderation;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Chat content screening (#1207): whole-token semantics that keep everyday German compounds clean, folding
/// that still catches leetspeak / repeated letters / spaced-out letters / Cyrillic homoglyphs, a tiny hate core
/// that drops the line, profanity that is masked in place, watch terms that only flag, and personal data that
/// is masked in Filtered and blocks the line in Safe.
/// </summary>
public sealed class ChatScreenTests
{
    private static readonly ChatScreen Screen = new();

    [Theory]
    [InlineData("you are an asshole", "you are an *******")]
    [InlineData("Das ist scheisse", "Das ist ********")]
    [InlineData("Scheiße!", "********")]
    [InlineData("what the fuuuuck", "what the *******")]
    [InlineData("sh!t happens", "**** happens")]
    [InlineData("a$$hole", "*******")]
    [InlineData("F-U-C-K you", "******* you")]
    [InlineData("du Arschloch", "du *********")]
    public void Filtered_MasksProfanityInPlace(string line, string expected)
    {
        var r = Screen.Screen(line, ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Mask, r.Verdict);
        Assert.Equal(expected, r.Text);
        Assert.False(r.Pii);
    }

    [Theory]
    [InlineData("hitler was right")]
    [InlineData("h.i.t.l.e.r")]
    [InlineData("Sieg Heil")]
    [InlineData("1488")]
    [InlineData("n a z i")]
    public void Filtered_BlocksHateTerms(string line)
    {
        var r = Screen.Screen(line, ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Block, r.Verdict);
        Assert.False(r.Pii);
        Assert.NotEqual(string.Empty, r.MatchedTerm);
    }

    [Fact]
    public void CyrillicHomoglyph_IsFoldedAndFlagged()
    {
        // 'а' is the Cyrillic a — the line reads "nazi" to a human and must not slip through as a new word.
        var r = Screen.Screen("nаzi rules", ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Block, r.Verdict);
        Assert.True(r.Watch, "a Latin/Cyrillic mix is the homoglyph trick and should be flagged to the operator");
    }

    [Fact]
    public void PureCyrillicLine_IsNotFlagged()
    {
        var r = Screen.Screen("привет всем", ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.False(r.Watch);
    }

    [Theory]
    [InlineData("Assistent gesucht")]
    [InlineData("Die Klasse 4b spielt mit")]
    [InlineData("im Dickicht ist es dunkel")]
    [InlineData("Staatsexamen Analyse Cocktail Mittwoch")]
    [InlineData("Tom1988 ist online")]
    [InlineData("ich habe 88 Blöcke gesetzt")]
    [InlineData("Sieg! wir haben gewonnen")]
    [InlineData("the massachusetts shuttle")]
    [InlineData("hello there :) how are you?")]
    public void EverydayLines_PassUntouched(string line)
    {
        var r = Screen.Screen(line, ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.Equal(line, r.Text);
        Assert.False(r.Watch);
    }

    [Fact]
    public void WatchTerm_IsRelayedButFlagged()
    {
        var r = Screen.Screen("kkk forever", ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.True(r.Watch);
        Assert.Equal("kkk", r.MatchedTerm);
    }

    [Theory]
    [InlineData("call me 0151 2345678", "0151 2345678")]
    [InlineData("ruf an +49 30 123 45 67 8", "+49 30 123 45 67 8")]
    [InlineData("mail me at kid@example.com pls", "kid@example.com")]
    [InlineData("join discord.gg/abc123 now", "discord.gg/abc123")]
    [InlineData("look at www.example.com/page", "www.example.com/page")]
    [InlineData("my site is blocks.de ok", "blocks.de")]
    public void Filtered_MasksPersonalData(string line, string piece)
    {
        var r = Screen.Screen(line, ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Mask, r.Verdict);
        Assert.True(r.Pii);
        Assert.DoesNotContain(piece, r.Text);
        Assert.Equal(line.Length, r.Text.Length); // masked in place, never shortened
    }

    [Theory]
    [InlineData("call me 0151 2345678")]
    [InlineData("mail me at kid@example.com pls")]
    [InlineData("join discord.gg/abc123 now")]
    public void Safe_BlocksPersonalData(string line)
    {
        var r = Screen.Screen(line, ChatMode.Safe);

        Assert.Equal(ChatVerdict.Block, r.Verdict);
        Assert.True(r.Pii);
    }

    [Theory]
    [InlineData("version 2026.8.20 is live")]
    [InlineData("coords 1234 64 -567")]
    [InlineData("I have 200 iron and 150 copper")]
    public void NumbersThatAreNotPhones_PassInSafe(string line)
    {
        var r = Screen.Screen(line, ChatMode.Safe);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.False(r.Pii);
    }

    [Fact]
    public void Open_ReturnsTheLineUntouched()
    {
        var r = Screen.Screen("what the fuck, hitler", ChatMode.Open);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.Equal("what the fuck, hitler", r.Text);
    }

    [Fact]
    public void AllowList_WinsOverTheOtherLists()
    {
        var lenient = new ChatScreen(allowWords: new[] { "arsch" });

        var r = lenient.Screen("du arsch", ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Ok, r.Verdict);
        Assert.Equal("du arsch", r.Text);
    }

    [Fact]
    public void OperatorLists_ExtendTheDefaults()
    {
        var custom = new ChatScreen(
            blockedWords: new[] { "zorkblat" },
            maskedWords: new[] { "flibber" },
            watchWords: new[] { "qwx" });

        Assert.Equal(ChatVerdict.Block, custom.Screen("ZORKBLAT!", ChatMode.Filtered).Verdict);
        Assert.Equal(ChatVerdict.Block, custom.Screen("z.o.r.k.b.l.a.t", ChatMode.Filtered).Verdict);
        Assert.Equal("a ******* b", custom.Screen("a flibber b", ChatMode.Filtered).Text);
        Assert.True(custom.Screen("qwx", ChatMode.Filtered).Watch);
        // The custom lists REPLACE the defaults for this instance (the server config copies the defaults in first).
        Assert.Equal(ChatVerdict.Ok, custom.Screen("fuck", ChatMode.Filtered).Verdict);
    }

    [Fact]
    public void MaskedLine_CanAlsoCarryAWatchFlag()
    {
        var r = Screen.Screen("kkk is shit", ChatMode.Filtered);

        Assert.Equal(ChatVerdict.Mask, r.Verdict);
        Assert.True(r.Watch);
        Assert.Equal("kkk is ****", r.Text);
    }

    [Fact]
    public void EmptyLine_IsOk()
    {
        Assert.Equal(ChatVerdict.Ok, Screen.Screen(string.Empty, ChatMode.Safe).Verdict);
        Assert.Equal(ChatVerdict.Ok, Screen.Screen(null, ChatMode.Safe).Verdict);
    }
}
