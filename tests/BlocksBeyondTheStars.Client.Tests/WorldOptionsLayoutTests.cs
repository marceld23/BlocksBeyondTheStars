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
}
