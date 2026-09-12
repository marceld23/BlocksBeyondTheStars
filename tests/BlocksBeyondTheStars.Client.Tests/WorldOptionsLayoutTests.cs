// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The world-options page is a fixed grid of absolute coordinates: two columns of slider rows over a
/// shared footer button row. Nothing scrolls and nothing clips, so a column that outgrows the footer
/// line slides UNDER the buttons — which then swallow its clicks and make that world rule unreachable
/// (#983: "keep ship when destroyed" was the 11th left row and landed exactly there).
/// <para>
/// This guard is deliberately parsed out of <c>UiWorldOptions.cs</c> rather than mirroring copied
/// numbers: the row calls are counted from the source, so a row appended tomorrow is included
/// automatically and fails here instead of on screen. (The Unity file itself cannot be compiled into
/// this suite — it needs UnityEngine.)
/// </para>
/// </summary>
public sealed class WorldOptionsLayoutTests
{
    /// <summary>Row label height in <c>AddSliderRow</c> — a row occupies <c>[y, y + LabelH]</c>.</summary>
    private const float LabelH = 40f;

    /// <summary>How far the row block must stay clear of the footer to read as separate (not a hard
    /// requirement, but a column ending 2 px above the buttons is the same bug one row earlier).</summary>
    private const float MinClearance = 8f;

    private static string Source()
    {
        string path = Path.Combine(
            ClientTestPaths.RepoRoot(),
            "client", "Assets", "BlocksBeyondTheStars", "Scripts", "UiWorldOptions.cs");
        Assert.True(File.Exists(path), $"UiWorldOptions.cs not found at {path} — did the client layout move?");
        return File.ReadAllText(path);
    }

    private static float Constant(string source, string pattern, string what)
    {
        var m = Regex.Match(source, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Assert.True(m.Success, $"Could not read {what} from UiWorldOptions.cs — this guard needs updating.");
        return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Replays the main page's vertical cursor for one column: the start value, every literal
    /// <c>ly/ry += n f</c> step (section headers and gaps) and one <c>RowH</c> per <c>Row(leftCol, …)</c>
    /// call, in source order. Returns the y the column ended on and how many rows it drew.</summary>
    private static (float End, int Rows) Column(string source, bool left)
    {
        string cursor = left ? "ly" : "ry";
        float rowH = Constant(source, @"const float RowH = ([0-9.]+)f;", "RowH");
        float y = Constant(
            source,
            left ? @"float ly = ([0-9.]+)f, ry = [0-9.]+f;" : @"float ly = [0-9.]+f, ry = ([0-9.]+)f;",
            $"the {cursor} start");
        int rows = 0;

        // One pass over the file so header gaps and rows stay in their real order. `ly += RowH;` inside
        // the Row() helper is not matched (no numeric literal), so rows are counted exactly once.
        var steps = Regex.Matches(
            source,
            $@"(?<step>\b{cursor} \+= (?<n>[0-9.]+)f;)|(?<row>\bRow\((?<side>true|false),)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        foreach (Match m in steps)
        {
            if (m.Groups["step"].Success)
            {
                y += float.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (m.Groups["row"].Success && (m.Groups["side"].Value == "true") == left)
            {
                y += rowH;
                rows++;
            }
        }

        return (y, rows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MainPageColumn_EndsAboveTheFooterButtons(bool left)
    {
        string source = Source();
        float footerY = Constant(source, @"FooterY = ([0-9.]+)f;", "FooterY");
        float rowH = Constant(source, @"const float RowH = ([0-9.]+)f;", "RowH");
        var (end, rows) = Column(source, left);

        // Sanity: the parse must actually see the page. Both columns carry ten-plus rows today; a regex
        // that silently matched nothing would otherwise "pass" forever.
        Assert.True(rows >= 8, $"Only {rows} rows parsed for the {(left ? "left" : "right")} column — the guard lost track of the layout.");

        float bottom = end - rowH + LabelH; // the last row starts one pitch back
        Assert.True(
            bottom + MinClearance <= footerY,
            $"The {(left ? "left" : "right")} world-options column ends at y={bottom} with {rows} rows, but the footer "
            + $"buttons start at y={footerY}. The bottom row would be drawn underneath them and could not be "
            + $"clicked (#983). Reduce RowH, drop a row, or move the row to the other column.");
    }

    /// <summary>The planet-type page is data-driven (one row per selectable type in <c>data/planets.json</c>),
    /// so its rows live in a scrolling viewport. That viewport — not the row count — must end above the
    /// footer (#1811: a fitted pitch clamped at 40 px ran 37 types 68 px under the buttons).</summary>
    [Fact]
    public void AdvancedPage_ListViewport_EndsAboveTheFooterButtons()
    {
        string source = Source();
        float footerY = Constant(source, @"FooterY = ([0-9.]+)f;", "FooterY");
        float listY = Constant(source, @"const float AdvancedListY = ([0-9.]+)f;", "AdvancedListY");
        float listH = Constant(source, @"const float AdvancedListH = ([0-9.]+)f;", "AdvancedListH");

        Assert.True(
            listY + listH + MinClearance <= footerY,
            $"The planet-type list viewport ends at y={listY + listH}, but the footer buttons start at y={footerY} (#1811).");
    }

    /// <summary>The list must scroll, never squeeze: its content is sized from the row count at a fixed pitch
    /// that leaves a gap between rows — so adding planet types lengthens the scroll range instead of pushing
    /// rows under the footer or into each other.</summary>
    [Fact]
    public void AdvancedPage_Rows_ScrollAtAFixedPitch()
    {
        string source = Source();
        float pitch = Constant(source, @"const float AdvancedRowPitch = ([0-9.]+)f;", "AdvancedRowPitch");

        Assert.True(pitch >= LabelH + MinClearance, $"AdvancedRowPitch {pitch} leaves no gap between {LabelH} px rows.");
        Assert.Matches(@"content\.sizeDelta = new Vector2\(0f, perColumn \* AdvancedRowPitch\);", source);
        Assert.Matches(@"y \+= AdvancedRowPitch;", source);
        Assert.DoesNotMatch(@"Mathf\.Clamp\(\(FooterY", source); // the fitted, clamped pitch that overflowed
    }

    /// <summary>A horizontal uGUI Slider stretches its handle over the slider height and ADDS sizeDelta.y, so the
    /// handle's real height is slider height + sizeDelta.y. It must fit inside a row, or the handles of stacked
    /// rows merge into one white column (#1811: 16 + 26 = 42 px on a 40 px pitch).</summary>
    [Fact]
    public void SliderHandle_FitsInsideARow()
    {
        string source = Source();
        float sliderH = Constant(source, @"UiKit\.Place\(go, x \+ 290f, y \+ 12f, w - 290f - 150f, ([0-9.]+)f\);", "the slider height");
        float handleExtra = Constant(source, @"handleRt\.sizeDelta = new Vector2\([0-9.]+f, ([0-9.]+)f\);", "the handle sizeDelta.y");

        Assert.True(
            sliderH + handleExtra <= LabelH - MinClearance,
            $"The slider handle is {sliderH + handleExtra} px tall, but a row is {LabelH} px — stacked handles would touch.");
    }
}
