// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The canonical community-rules text, single-sourced so the portal's /rules page and the
/// <c>GET /api/terms</c> endpoint (the desktop client renders the rules in-game before signup /
/// re-acceptance) can never drift apart: the API's plain text is DERIVED from the page's HTML card.
/// The wording itself lives in the portal locale tables, so the rules exist in every language the game
/// ships — in the browser AND on the in-game rules screen (issue #970).
/// </summary>
public static class CommunityRules
{
    /// <summary>The rules card as shown on the portal's /rules page and inside the signup flow. The
    /// <c>&lt;li&gt;</c>/<c>&lt;/p&gt;</c> structure is load-bearing — <see cref="PlainText"/> derives
    /// the in-game rendering from it.</summary>
    public static string HtmlCard(string lang)
    {
        var t = PortalLocales.For(lang);
        return $@"
<div class='card'>
 <p>{t.T("rules.intro")}</p>
 <ul>
  <li>{t.T("rules.item1")}</li>
  <li>{t.T("rules.item2")}</li>
  <li>{t.T("rules.item3")}</li>
  <li>{t.T("rules.item4")}</li>
  <li>{t.T("rules.item5")}</li>
  <li>{t.T("rules.item6")}</li>
 </ul>
 <p class='beta'>⚠ <b>{t.T("rules.beta.label")}</b> {t.T("rules.beta.text")}</p>
</div>";
    }

    /// <summary>Plain-text rendering of <see cref="HtmlCard"/> for the game client's rules screen
    /// (a Unity <c>Text</c> can't show HTML): bullets become "• " lines, all other tags are stripped
    /// and entities decoded. Derived, never hand-written — so it always matches the page.</summary>
    public static string PlainText(string lang)
    {
        string text = StripTags(HtmlCard(lang)
            .Replace("<li>", "• ")
            .Replace("</li>", "\n")
            .Replace("</p>", "\n\n"));
        // Decode AFTER stripping, so the literal "&lt;Name&gt;" of the /report example survives as <Name>.
        text = System.Net.WebUtility.HtmlDecode(text);

        // Collapse the HTML source's hard-wrapped indentation into clean single-space prose lines.
        var lines = text.Split('\n');
        var sb = new System.Text.StringBuilder();
        var paragraph = new System.Text.StringBuilder();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                FlushParagraph();
            }

            paragraph.Append(paragraph.Length > 0 ? " " : string.Empty).Append(line);
        }

        FlushParagraph();
        return sb.ToString().TrimEnd() + "\n";

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                sb.Append(paragraph).Append('\n');
                paragraph.Clear();
            }
        }
    }

    /// <summary>Removes HTML tags by a plain scan (no regex — MA0009). Entities pass through untouched.</summary>
    private static string StripTags(string html)
    {
        var sb = new System.Text.StringBuilder(html.Length);
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>' && inTag)
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
