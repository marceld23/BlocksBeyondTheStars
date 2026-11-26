// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4029 — a uGUI <c>ScrollRect</c> only gets the mouse wheel when the pointer's ray hits a raycast target inside its
/// hierarchy. A list whose viewport had no hit graphic stopped scrolling as soon as the pointer was over a gap between
/// its rows (the Tab menu's panes, the trade rows, the arcade rail). <c>UiKit.AddInlineScrollbar</c> now gives such a
/// viewport a near-transparent hit image (<c>UiKit.EnsureScrollHit</c>). This guard reads the client scripts as text
/// (PR CI never compiles Unity): every <c>ScrollRect</c> built outside <c>UiKit.cs</c> either gets the inline scrollbar
/// (and with it the hit image) or adds a hit <c>Image</c> itself in the same method.
/// </summary>
public sealed class ScrollWheelHitGuardTests
{
    private static string ScriptsDir()
        => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    /// <summary>The next member declaration at class-member indent ends the method a match lies in.</summary>
    private static readonly Regex NextMember = new(@"\n {8}(?:public|private|internal|protected)\s",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheInlineScrollbar_GivesAViewportWithoutAGraphic_AHitImage()
    {
        string uiKit = File.ReadAllText(Path.Combine(ScriptsDir(), "UiKit.cs"));
        int bar = uiKit.IndexOf("public static Scrollbar AddInlineScrollbar(ScrollRect scroll", StringComparison.Ordinal);
        Assert.True(bar >= 0, "UiKit.AddInlineScrollbar not found — did it move?");
        Assert.Contains("EnsureScrollHit(scroll);", MethodFrom(uiKit, bar));

        int ensure = uiKit.IndexOf("public static void EnsureScrollHit(ScrollRect scroll)", StringComparison.Ordinal);
        Assert.True(ensure >= 0, "UiKit.EnsureScrollHit not found — did it move?");
        string body = MethodFrom(uiKit, ensure);
        Assert.Contains("GetComponent<Graphic>() != null", body); // a viewport with its own graphic keeps it
        Assert.Contains("AddComponent<Image>()", body);
        Assert.Contains("new Color(0f, 0f, 0f, 0.001f)", body);    // seen by the ray, not by the eye
        Assert.Contains("raycastTarget = true", body);
    }

    [Fact]
    public void EveryScrollRectOutsideUiKit_CatchesTheWheelOverItsGaps()
    {
        var files = Directory.GetFiles(ScriptsDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFileName(f), "UiKit.cs", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        var missing = new List<string>();
        int seen = 0;
        foreach (string file in files)
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (Match m in Regex.Matches(text, @"AddComponent<ScrollRect>\(\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                seen++;
                string method = MethodFrom(text, m.Index);
                if (!method.Contains("AddInlineScrollbar(", StringComparison.Ordinal)
                    && !method.Contains("EnsureScrollHit(", StringComparison.Ordinal)
                    && !method.Contains("AddComponent<Image>()", StringComparison.Ordinal))
                {
                    int line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    missing.Add($"{Path.GetFileName(file)}:{line}");
                }
            }
        }

        Assert.True(seen >= 4, "expected the hand-built scroll views (Tab menu, trade, arcade, wiki) — did the scripts move?");
        Assert.True(missing.Count == 0,
            "These ScrollRects have no hit graphic on their viewport, so the wheel stops over the gaps between their rows "
            + "(#4029). Call UiKit.AddInlineScrollbar(scroll) or UiKit.EnsureScrollHit(scroll), or build the view with "
            + "UiKit.AddScrollViewport:\n" + string.Join("\n", missing));
    }

    /// <summary>The text from <paramref name="start"/> to the next member declaration (or the end of the file).</summary>
    private static string MethodFrom(string text, int start)
    {
        var next = NextMember.Match(text, Math.Min(text.Length, start + 1));
        return next.Success ? text.Substring(start, next.Index - start) : text.Substring(start);
    }
}
