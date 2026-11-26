// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4043: the browser main menu says that the desktop version looks even better. Guarded as source text (the browser branch
/// never compiles headless or in the Editor): only the browser menu calls the card, the card itself compiles on every
/// platform (so the Windows player build checks it), it sits between the browser column's last button and the community
/// row, and it links to the website.
/// </summary>
public sealed class BrowserDesktopHintTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static string Menu() => ClientTestPaths.ScriptPartsText("UiMainMenu");

    [Fact]
    public void OnlyTheBrowserMenu_CallsTheCard()
    {
        string menu = Menu();
        var browserBranch = Regex.Match(menu, @"#if UNITY_WEBGL && !UNITY_EDITOR\s+b\.BuildBrowserMenu\(\);(?<body>.*?)#else", RegexOptions.Singleline,
            RegexTimeout);
        Assert.True(browserBranch.Success, "the browser branch of Build() is not where it was");
        Assert.Contains("b.BuildDesktopHint();", browserBranch.Groups["body"].Value);
        Assert.Single(Regex.Matches(menu, @"b\.BuildDesktopHint\(\);", RegexOptions.None, RegexTimeout));
    }

    [Fact]
    public void TheCard_CompilesOnEveryPlatform_AndLinksToTheWebsite()
    {
        string menu = Menu();
        int method = menu.IndexOf("public void BuildDesktopHint()", StringComparison.Ordinal);
        Assert.True(method > 0, "BuildDesktopHint is gone");

        // No open #if around the method: count the directives in front of it.
        int ifs = Regex.Matches(menu[..method], @"^\s*#if\b", RegexOptions.Multiline, RegexTimeout).Count;
        int endifs = Regex.Matches(menu[..method], @"^\s*#endif\b", RegexOptions.Multiline, RegexTimeout).Count;
        Assert.Equal(ifs, endifs);

        string body = menu[method..menu.IndexOf("private void OpenWebsite()", method, StringComparison.Ordinal)];
        Assert.Contains("\"ui.menu.desktop_hint_title\"", body);
        Assert.Contains("\"ui.menu.desktop_hint_body\"", body);
        Assert.Contains("_shell.L(\"ui.menu.desktop_hint_link\"), OpenWebsite)", body);
        Assert.Contains("\"set_graphics\"", body);
    }

    [Fact]
    public void TheCard_FitsBetweenTheLastButtonAndTheCommunityRow()
    {
        string menu = Menu();
        float y = Constant(menu, "DesktopHintY"), h = Constant(menu, "DesktopHintH");
        // The browser column: the buttons start 112 under the name panel (by = 322) and step by 62; the longest variant has
        // five rows under the first (Settings and Credits last) — Credits ends at 434 + 4 × 62 + 54 = 736.
        Assert.Contains("private const float by = 322f;", menu);
        Assert.Contains("bh = 54f, gap = 62f", menu);
        const float creditsEnd = 322f + 112f + (4 * 62f) + 54f;
        Assert.True(y >= creditsEnd + 16f, $"the card starts at {y}, Credits ends at {creditsEnd}");
        // The community row's kicker sits 32 over its buttons at y 1000.
        Assert.Contains("const float y = 1000f, h = 50f;", menu);
        Assert.True(y + h + 16f <= 1000f - 32f, $"the card ends at {y + h}, the community row starts at 968");
    }

    private static float Constant(string source, string name)
    {
        var m = Regex.Match(source, name + @" = (?<v>[0-9.]+)f", RegexOptions.None, RegexTimeout);
        Assert.True(m.Success, $"{name} not found");
        return float.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
