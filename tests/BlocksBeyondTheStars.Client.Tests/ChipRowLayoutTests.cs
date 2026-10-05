// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The sample filter chips (#2324) are sized by their texts: a chip gets its label plus padding, the effect button
/// takes the rest, a row that still does not fit shrinks every chip by one factor (and the label's font with it, on
/// one line), and a row with room lets the chips share it. The widths below are the measured 22 pt labels of the
/// inventory row: German ("Alle 4", "Pflanzen 8", "Tiere 5", "Lagerstätten 12") and Russian, whose "Месторождения 12"
/// used to break in the middle of the word.
/// </summary>
public sealed class ChipRowLayoutTests
{
    private const float Pad = 28f, Min = 72f, Max = 190f, Gap = 8f, Row = 796f, TailMin = 170f, TailPref = 300f;

    [Fact]
    public void German_FitsWithTheEffectButtonAtItsPreferredWidth()
    {
        var r = ChipRowLayout.Fit(new[] { 50f, 95f, 65f, 139f }, Pad, Min, Max, Gap, Row, TailMin, TailPref);

        Assert.Equal(TailPref, r.Tail);
        Assert.True(r.Chips[3] >= 139f + Pad, "the longest chip keeps its whole word");
        Assert.True(r.Chips.Sum() + (Gap * 4) + r.Tail <= Row + 0.01f);
        Assert.True(r.Chips.All(c => c >= Min));
    }

    [Fact]
    public void Russian_ClampsTheLongestChip_ShrinksTheRow_AndKeepsTheEffectButtonsMinimum()
    {
        var r = ChipRowLayout.Fit(new[] { 60f, 124f, 138f, 201f }, Pad, Min, Max, Gap, Row, TailMin, TailPref);

        Assert.Equal(TailMin, r.Tail);
        Assert.True(r.Chips[3] <= Max, "a chip never grows past its cap — the font shrinks instead");
        Assert.True(r.Chips.Sum() + (Gap * 4) + r.Tail <= Row + 0.01f, "the row fits");
        Assert.True(r.Chips[3] > r.Chips[0], "the long label still gets the widest chip");
    }

    [Fact]
    public void WithoutATail_TheChipsFillTheRow()
    {
        var r = ChipRowLayout.Fit(new[] { 30f, 40f, 35f, 60f }, Pad, Min, Max, 6f, 430f);

        Assert.Equal(0f, r.Tail);
        Assert.Equal(430f, r.Chips.Sum() + (6f * 3), 2);
        Assert.True(r.Chips[3] > r.Chips[0]);
    }

    [Fact]
    public void ANarrowColumn_ShrinksEveryChipByTheSameFactor()
    {
        var r = ChipRowLayout.Fit(new[] { 50f, 95f, 65f, 139f }, Pad, Min, Max, 6f, 430f);

        Assert.Equal(430f, r.Chips.Sum() + (6f * 3), 2);
        float k = r.Chips[3] / (139f + Pad);
        Assert.True(k < 1f);
        Assert.Equal(k, r.Chips[1] / (95f + Pad), 3);
    }

    [Fact]
    public void TheSingleLineFontSize_ShrinksOnlyWhenTheLabelOverflows()
    {
        Assert.Equal(22, ChipRowLayout.SingleLineFontSize(80f, 84f, 11, 22));
        Assert.Equal(13, ChipRowLayout.SingleLineFontSize(139f, 84f, 11, 22)); // "Lagerstätten 12" in the old 112 px chip
        Assert.Equal(17, ChipRowLayout.SingleLineFontSize(201f, 162f, 11, 22)); // "Месторождения 12" in its clamped chip
        Assert.Equal(11, ChipRowLayout.SingleLineFontSize(400f, 40f, 11, 22));  // never below the floor
        Assert.Equal(22, ChipRowLayout.SingleLineFontSize(0f, 10f, 11, 22));
    }

    [Fact]
    public void AnEmptyRow_IsJustTheTail()
    {
        var r = ChipRowLayout.Fit(System.Array.Empty<float>(), Pad, Min, Max, Gap, Row, TailMin, TailPref);
        Assert.Empty(r.Chips);
        Assert.Equal(TailPref, r.Tail);
    }
}
