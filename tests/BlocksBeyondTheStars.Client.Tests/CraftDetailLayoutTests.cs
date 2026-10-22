// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Crafting / Tech / Ship detail pane is a fixed-width scroll view: its content is clipped by a
/// <c>RectMask2D</c> at the viewport width, and an auto-hide inline scrollbar overlays the viewport's
/// right-most pixels. Left-anchored rows tolerate a little overshoot (their text ends long before the
/// edge), but a RIGHT-anchored text whose rect ends past the viewport loses its last glyphs — #1057:
/// the ingredient source tag ("craftable" / "raw resource") rendered as "craftabl|e".
/// <para>
/// This guard is parsed out of the <c>CraftingTechShipUI*.cs</c> partial files / <c>UiKit.cs</c> rather than
/// mirroring copied numbers: every right-anchored <c>AddText(_detail, …)</c> is checked against the viewport
/// width and scrollbar width the source actually declares, so a new tag or a resized pane is covered
/// automatically. (The Unity files cannot be compiled into this suite — they need UnityEngine.)
/// </para>
/// </summary>
public sealed class CraftDetailLayoutTests
{
    private static string ScriptsDir() =>
        Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    private static string ReadScript(string file)
    {
        string path = Path.Combine(ScriptsDir(), file);
        Assert.True(File.Exists(path), $"{file} not found at {path} — did the client scripts move?");
        return File.ReadAllText(path);
    }

    /// <summary>Every part of the <c>CraftingTechShipUI</c> partial class (the shell + one file per tab and the
    /// shared Recipes / Stations / Scaffolding parts), in a stable order.</summary>
    private static IReadOnlyList<(string Name, string Text)> UiSources()
    {
        var list = Directory.GetFiles(ScriptsDir(), "CraftingTechShipUI*.cs")
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Name: f, Text: File.ReadAllText(f)))
            .ToList();
        Assert.True(list.Count >= 2, "expected the CraftingTechShipUI partial files — did the client scripts move?");
        return list;
    }

    private static string AllUiText(IReadOnlyList<(string Name, string Text)> sources) =>
        string.Join("\n", sources.Select(s => s.Text));

    private static float Constant(string source, string pattern, string what)
    {
        var m = Regex.Match(source, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Assert.True(m.Success, $"Could not read {what} — this guard needs updating.");
        return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Viewport width of the detail scroll (<c>_detail = MakeScroll(root, x, y, W, h)</c>).</summary>
    private static float DetailViewportWidth(string ui) =>
        Constant(ui, @"_detail = MakeScroll\(root,\s*[0-9.]+f?,\s*[0-9.]+f?,\s*([0-9.]+)f?,", "the detail viewport width");

    /// <summary>Default width of <c>UiKit.AddInlineScrollbar</c>, which anchors to the viewport's right edge.</summary>
    private static float ScrollbarWidth(string uiKit) =>
        Constant(uiKit, @"AddInlineScrollbar\(ScrollRect scroll, float width = ([0-9.]+)f\)", "the inline scrollbar width");

    [Fact]
    public void RightAnchoredDetailTexts_EndInsideViewportAndClearOfScrollbar()
    {
        var sources = UiSources();
        float viewport = DetailViewportWidth(AllUiText(sources));
        float scrollbar = ScrollbarWidth(ReadScript("UiKit.cs"));
        float limit = viewport - scrollbar;

        // Every AddText on the detail content with literal x / w and a right-side anchor, swept per partial file. The
        // call may wrap over several lines, so scan up to the closing ');' for the anchor.
        var calls = sources
            .SelectMany(s => Regex.Matches(
                    s.Text,
                    @"UiKit\.AddText\(_detail,\s*(?<x>[0-9.]+)f?,\s*[^,]+,\s*(?<w>[0-9.]+)f?,(?<rest>[^;]*?)\);",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline,
                    TimeSpan.FromSeconds(5))
                .Select(m => (s.Name, s.Text, Match: m)))
            .ToList();
        Assert.NotEmpty(calls);

        var rightAnchored = calls
            .Where(c => Regex.IsMatch(
                c.Match.Groups["rest"].Value,
                @"TextAnchor\.(Upper|Middle|Lower)Right",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(2)))
            .ToList();
        Assert.NotEmpty(rightAnchored); // the ingredient source tag (#1016) is one — if it vanished, revisit this guard

        var overshoot = rightAnchored
            .Select(c => (
                X: float.Parse(c.Match.Groups["x"].Value, CultureInfo.InvariantCulture),
                W: float.Parse(c.Match.Groups["w"].Value, CultureInfo.InvariantCulture),
                Where: $"{Path.GetFileName(c.Name)}:{c.Text.Take(c.Match.Index).Count(ch => ch == '\n') + 1}"))
            .Where(t => t.X + t.W > limit)
            .ToList();

        Assert.True(
            overshoot.Count == 0,
            $"Right-anchored detail text ends past the visible edge (viewport {viewport} − scrollbar {scrollbar} = {limit}): "
            + string.Join(", ", overshoot.Select(t => $"{t.Where}: x {t.X} + w {t.W} = {t.X + t.W}"))
            + " — the last glyphs get masked (#1057).");
    }

    [Fact]
    public void IngredientSourceTag_IsRightAnchoredInsideTheViewport()
    {
        // Pin the specific row from #1057 so a future refactor that drops the anchor (or moves the tag
        // to a wider rect) trips here with a pointed message rather than only through the generic sweep.
        string ui = AllUiText(UiSources());
        var m = Regex.Match(
            ui,
            @"UiKit\.AddText\(_detail,\s*(?<x>[0-9.]+)f?,\s*y,\s*(?<w>[0-9.]+)f?,\s*size \+ 8,\s*L\(craftable \? ""ui\.craft\.src_craftable"" : ""ui\.craft\.src_raw""\),\s*size - 4,\s*UiKit\.CyanDim,\s*TextAnchor\.UpperRight\);",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        Assert.True(m.Success, "The ingredient source tag row in IngredientRow was not found — did it move or lose its right anchor?");

        float right = float.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture)
                    + float.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
        float limit = DetailViewportWidth(ui) - ScrollbarWidth(ReadScript("UiKit.cs"));
        Assert.True(right <= limit, $"Source tag right edge {right} exceeds the visible limit {limit} (#1057).");
    }
}
