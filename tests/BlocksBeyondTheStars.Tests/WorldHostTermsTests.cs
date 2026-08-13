// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The single-sourced community rules behind GET /api/terms (issue #268): the desktop client renders
/// the rules in-game before signup / re-acceptance, so the plain text must be a faithful, readable
/// derivation of the /rules page card — never a second hand-written copy that could drift.
/// </summary>
public sealed class WorldHostTermsTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void PlainText_IsHtmlFree_AndCarriesTheCoreRules(string lang)
    {
        string text = CommunityRules.PlainText(lang);

        // No HTML markup may leak (the only allowed angle brackets are the decoded /report <name> example).
        Assert.DoesNotContain("<b>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<li>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("</", text, StringComparison.Ordinal);
        Assert.DoesNotContain("class=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;", text, StringComparison.Ordinal); // entities decoded, not leaked

        // The pillars every rules rendering must carry: parental hint, ban policy, report path, beta warning.
        Assert.Contains(lang == "en" ? "ask your parents" : "Frag bitte zuerst deine Eltern", text, StringComparison.Ordinal);
        Assert.Contains(lang == "en" ? "immediate ban" : "sofortigen Bann", text, StringComparison.Ordinal);
        Assert.Contains("/report", text, StringComparison.Ordinal);
        Assert.Contains(lang == "en" ? "Beta notice" : "Beta-Hinweis", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void PlainText_RendersEveryBulletOnItsOwnLine(string lang)
    {
        // The HTML card has six <li> bullets; the derived text must keep all of them as "• " lines.
        string[] bullets = Array.FindAll(CommunityRules.PlainText(lang).Split('\n'),
            line => line.StartsWith("• ", StringComparison.Ordinal));
        Assert.Equal(6, bullets.Length);
    }

    [Fact]
    public void RulesPage_UsesTheSharedCard()
    {
        var config = new WorldHostConfig();
        foreach (string lang in new[] { "de", "en" })
        {
            // The page embeds HtmlCard verbatim — the guarantee that /rules and /api/terms can't drift.
            Assert.Contains(CommunityRules.HtmlCard(lang).Trim(), WorldHostPortalPages.Rules(config, lang), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownLanguage_FallsBackToGerman()
    {
        Assert.Equal(CommunityRules.PlainText("de"), CommunityRules.PlainText("xx"));
        Assert.Equal(CommunityRules.HtmlCard("de"), CommunityRules.HtmlCard(null!));
    }

    [Fact]
    public void EveryPortalLanguage_RendersItsOwnRules()
    {
        // The rules ride the portal locale tables, so the /rules page AND the in-game rules screen
        // exist in every game language (#970): six bullets, no HTML, and never the German text.
        string german = CommunityRules.PlainText("de");
        foreach (var locale in PortalLocales.Supported)
        {
            string code = locale.Code();
            string text = CommunityRules.PlainText(code);
            Assert.DoesNotContain("</", text, StringComparison.Ordinal);
            Assert.DoesNotContain("&lt;", text, StringComparison.Ordinal);
            Assert.Contains("/report", text, StringComparison.Ordinal);
            Assert.Equal(6, Array.FindAll(text.Split('\n'),
                line => line.StartsWith("• ", StringComparison.Ordinal)).Length);
            if (code != "de")
            {
                Assert.NotEqual(german, text);
            }
        }
    }
}
