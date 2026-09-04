// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1555: the codec interns short strings per decoding thread and leaves long ones alone.</summary>
public sealed class InterningStringFormatterTests
{
    [Fact]
    public void ShortStrings_DecodeToTheSameInstanceAcrossMessages()
    {
        var first = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "Vega", Text = "hello" }))!;
        var second = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "Vega", Text = "hello" }))!;

        Assert.Equal("Vega", first.Sender);
        Assert.Same(first.Sender, second.Sender);
        Assert.Same(first.Text, second.Text);
    }

    [Fact]
    public void LongStrings_AreNotInterned_AndStillRoundTrip()
    {
        string text = new string('x', InterningStringFormatter.MaxInternedBytes + 1);
        var first = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "a", Text = text }))!;
        var second = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "a", Text = text }))!;

        Assert.Equal(text, first.Text);
        Assert.Equal(text, second.Text);
        Assert.NotSame(first.Text, second.Text);
    }

    [Fact]
    public void EqualBytesDifferentContent_NeverCollideSilently()
    {
        // Two different short strings with the same length must decode to their own content whatever the hash does.
        var a = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "ab", Text = "über" }))!;
        var b = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = "ba", Text = "ubér" }))!;

        Assert.Equal("ab", a.Sender);
        Assert.Equal("ba", b.Sender);
        Assert.Equal("über", a.Text);
        Assert.Equal("ubér", b.Text);
    }

    [Fact]
    public void EmptyAndNullStrings_RoundTrip()
    {
        var m = (ChatMessage)NetCodec.Decode(NetCodec.Encode(new ChatMessage { Sender = string.Empty, Text = "" }))!;
        Assert.Equal(string.Empty, m.Sender);
        Assert.Equal(string.Empty, m.Text);
    }
}
