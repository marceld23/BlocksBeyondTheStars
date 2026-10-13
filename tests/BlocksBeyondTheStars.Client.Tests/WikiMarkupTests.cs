// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Localization;
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

    /// <summary>#2259: a body is cut at its pictures, in order; only plain wiki/img/*.png paths count, and the rich-text
    /// conversion drops the tag itself.</summary>
    [Fact]
    public void Segments_CutTheBodyAtItsPictures_AndKeepOnlySafePaths()
    {
        var parts = WikiMarkup.Segments("<p>A</p><img src=\"wiki/img/circuit_lift.png\"><p>B</p><img src=\"../secret.png\"><p>C</p>");

        Assert.Equal(4, parts.Count);
        Assert.Equal("<p>A</p>", parts[0].Text);
        Assert.True(parts[1].IsImage);
        Assert.Equal("wiki/img/circuit_lift.png", parts[1].Image);
        Assert.Equal("<p>B</p>", parts[2].Text);
        Assert.Equal("<p>C</p>", parts[3].Text); // the unsafe picture is gone, the text stays

        Assert.Single(WikiMarkup.Segments("<p>just text</p>"));
        Assert.Equal("A\n\nB", WikiMarkup.ToUnityRichText("<p>A</p><img src=\"wiki/img/x.png\"><p>B</p>"));
    }

    /// <summary>#2508: an article's <c>{key:Action}</c> tokens pass the conversion untouched and are filled after it, so a
    /// glyph that looks like markup lands verbatim — the other order would strip "&lt;Tab&gt;" as an unknown tag.</summary>
    [Fact]
    public void KeyTokens_AreFilledAfterTheConversion_SoAGlyphIsNeverTakenForMarkup()
    {
        static string Expand(string text) => LineText.ExpandKeyTokens(text, action => action switch
        {
            "Interact" => "<Tab>",
            "PlanetMap" => "&lt;",
            _ => "[" + action + "]",
        });

        const string body = "<p>Press <b>{key:Interact}</b> &amp; go.</p><p>The map ({key:PlanetMap}), then {key:Jump}.</p>";
        Assert.Equal("Press <b><Tab></b> & go.\n\nThe map (&lt;), then [Jump].", WikiMarkup.ToUnityRichText(body, Expand));

        // Filled first, the glyph would be lost: the conversion drops "<Tab>" and decodes "&lt;".
        Assert.Equal("Press <b></b> & go.\n\nThe map (<), then [Jump].", WikiMarkup.ToUnityRichText(Expand(body)));

        // A pad glyph that wears its own parentheses is not doubled; no expander leaves the tokens for later.
        Assert.Equal("The map (X)", WikiMarkup.ToUnityRichText("<p>The map ({key:PlanetMap})</p>", t => LineText.ExpandKeyTokens(t, _ => "(X)")));
        Assert.Equal("press {key:Interact}", WikiMarkup.ToUnityRichText("<p>press {key:Interact}</p>", null));
        Assert.Equal(string.Empty, WikiMarkup.ToUnityRichText(null, Expand));
    }
}
