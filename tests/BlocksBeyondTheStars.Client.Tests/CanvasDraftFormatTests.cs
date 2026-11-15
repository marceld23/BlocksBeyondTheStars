// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3689: the paint and shape editors' draft envelope — a header naming the editor and the layout version, so a
/// draft from another editor or a newer game is refused instead of misread.</summary>
public sealed class CanvasDraftFormatTests
{
    [Fact]
    public void Wrap_ThenUnwrap_GivesThePayloadBack()
    {
        string draft = CanvasDraftFormat.Wrap("form", 2, "{\"name\":\"Stairs\"}\nsecond line")!;

        Assert.StartsWith("bbts-draft form v2\n", draft);
        Assert.True(CanvasDraftFormat.TryUnwrap(draft, "form", 2, out int version, out string payload));
        Assert.Equal(2, version);
        Assert.Equal("{\"name\":\"Stairs\"}\nsecond line", payload);
    }

    [Fact]
    public void EmptyWork_IsNoDraft() => Assert.Null(CanvasDraftFormat.Wrap("texture", 1, string.Empty));

    [Theory]
    [InlineData("bbts-draft texture v1\npixels")]   // another editor's draft
    [InlineData("bbts-draft form v3\nvoxels")]      // a newer layout than this editor reads
    [InlineData("bbts-draft form v0\nvoxels")]
    [InlineData("bbts-draft form vx\nvoxels")]
    [InlineData("bbts-draft form v1\n")]            // a header without work
    [InlineData("form v1\nvoxels")]                 // no magic word
    [InlineData("just some text")]
    [InlineData("")]
    [InlineData(null)]
    public void ForeignOrBrokenDrafts_AreRefused(string? draft)
    {
        Assert.False(CanvasDraftFormat.TryUnwrap(draft, "form", 2, out _, out string payload));
        Assert.Equal(string.Empty, payload);
    }

    [Fact]
    public void WindowsLineEndings_InTheHeader_AreRead()
        => Assert.True(CanvasDraftFormat.TryUnwrap("bbts-draft form v1\r\nvoxels", "form", 1, out _, out string payload)
                       && payload == "voxels");

    [Fact]
    public void AKindWithSpaces_StaysOneWord()
    {
        string draft = CanvasDraftFormat.Wrap("tool looks", 1, "x")!;
        Assert.True(CanvasDraftFormat.TryUnwrap(draft, "tool looks", 1, out _, out _));
        Assert.StartsWith("bbts-draft tool_looks v1\n", draft);
    }
}
