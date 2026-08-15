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
/// This guard is parsed out of <c>CraftingTechShipUI.cs</c> / <c>UiKit.cs</c> rather than mirroring
/// copied numbers: every right-anchored <c>AddText(_detail, …)</c> is checked against the viewport
/// width and scrollbar width the source actually declares, so a new tag or a resized pane is covered
/// automatically. (The Unity files cannot be compiled into this suite — they need UnityEngine.)
/// </para>
/// </summary>
public sealed class CraftDetailLayoutTests
{
    private static string Read(string file)
    {
        string path = Path.Combine(
            ClientTestPaths.RepoRoot(),
            "client", "Assets", "BlocksBeyondTheStars", "Scripts", file);
        Assert.True(File.Exists(path), $"{file} not found at {path} — did the client scripts move?");
        return File.ReadAllText(path);
    }

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
        string ui = Read("CraftingTechShipUI.cs");
        float viewport = DetailViewportWidth(ui);
        float scrollbar = ScrollbarWidth(Read("UiKit.cs"));
        float limit = viewport - scrollbar;

        // Every AddText on the detail content with literal x / w and a right-side anchor. The call may
        // wrap over several lines, so scan up to the closing ');' for the anchor.
        var calls = Regex.Matches(
            ui,
            @"UiKit\.AddText\(_detail,\s*(?<x>[0-9.]+)f?,\s*[^,]+,\s*(?<w>[0-9.]+)f?,(?<rest>[^;]*?)\);",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(calls);

        var rightAnchored = calls
            .Where(m => Regex.IsMatch(
                m.Groups["rest"].Value,
                @"TextAnchor\.(Upper|Middle|Lower)Right",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(2)))
            .ToList();
        Assert.NotEmpty(rightAnchored); // the ingredient source tag (#1016) is one — if it vanished, revisit this guard

        var overshoot = rightAnchored
            .Select(m => (
                X: float.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture),
                W: float.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture),
                Line: ui.Take(m.Index).Count(c => c == '\n') + 1))
            .Where(t => t.X + t.W > limit)
            .ToList();

        Assert.True(
            overshoot.Count == 0,
            $"Right-anchored detail text ends past the visible edge (viewport {viewport} − scrollbar {scrollbar} = {limit}): "
            + string.Join(", ", overshoot.Select(t => $"line {t.Line}: x {t.X} + w {t.W} = {t.X + t.W}"))
            + " — the last glyphs get masked (#1057).");
    }

    [Fact]
    public void IngredientSourceTag_IsRightAnchoredInsideTheViewport()
    {
        // Pin the specific row from #1057 so a future refactor that drops the anchor (or moves the tag
        // to a wider rect) trips here with a pointed message rather than only through the generic sweep.
        string ui = Read("CraftingTechShipUI.cs");
        var m = Regex.Match(
            ui,
            @"UiKit\.AddText\(_detail,\s*(?<x>[0-9.]+)f?,\s*y,\s*(?<w>[0-9.]+)f?,\s*size \+ 8,\s*L\(craftable \? ""ui\.craft\.src_craftable"" : ""ui\.craft\.src_raw""\),\s*size - 4,\s*UiKit\.CyanDim,\s*TextAnchor\.UpperRight\);",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        Assert.True(m.Success, "The ingredient source tag row in IngredientRow was not found — did it move or lose its right anchor?");

        float right = float.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture)
                    + float.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
        float limit = DetailViewportWidth(ui) - ScrollbarWidth(Read("UiKit.cs"));
        Assert.True(right <= limit, $"Source tag right edge {right} exceeds the visible limit {limit} (#1057).");
    }
}
