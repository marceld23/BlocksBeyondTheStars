// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless tests for the Codex HTML-lite → uGUI rich-text converter (Stream D native Wiki). Pure string
/// logic, so the paragraph/list/inline-tag handling is asserted deterministically without Unity.
/// </summary>
public sealed class WikiMarkupTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, WikiMarkup.ToUnityRichText(null));
        Assert.Equal(string.Empty, WikiMarkup.ToUnityRichText(""));
    }

    [Fact]
    public void Paragraphs_BecomeBlankLineSeparated()
    {
        Assert.Equal("A\n\nB", WikiMarkup.ToUnityRichText("<p>A</p><p>B</p>"));
    }

    [Fact]
    public void List_BecomesBullets()
    {
        Assert.Equal("• A\n• B", WikiMarkup.ToUnityRichText("<ul><li>A</li><li>B</li></ul>"));
    }

    [Fact]
    public void BoldAndItalic_ArePreserved()
    {
        // uGUI rich text supports <b>/<i> directly, so they pass through.
        Assert.Equal("<b>x</b> <i>y</i>", WikiMarkup.ToUnityRichText("<p><b>x</b> <i>y</i></p>"));
    }

    [Fact]
    public void LinkSpan_KeepsTextTinted_DropsNavigation()
    {
        Assert.Equal(
            "see <color=#6fd8ff>Blocks</color>",
            WikiMarkup.ToUnityRichText("<p>see <span class=\"link\" data-link=\"cat:blocks\">Blocks</span></p>"));
    }

    [Fact]
    public void UnknownTags_AreStripped_TextKept()
    {
        Assert.Equal("Title", WikiMarkup.ToUnityRichText("<h1>Title</h1>"));
        Assert.Equal("kept", WikiMarkup.ToUnityRichText("<div class=\"x\">kept</div>"));
    }

    [Fact]
    public void Entities_AreDecoded()
    {
        Assert.Equal("A & B < C > D", WikiMarkup.ToUnityRichText("<p>A &amp; B &lt; C &gt; D</p>"));
    }
}
