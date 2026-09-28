// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// VEGA's panel folds into a tab after a while (#2126) — never dismissing a line: only a fully revealed, non-prologue
/// page folds, only after <see cref="VegaCollapse.CollapseAfterSeconds"/> in view, and opening it again restarts the wait.
/// </summary>
public sealed class VegaCollapseTests
{
    private const float Frame = 1f / 60f;

    private static bool Run(VegaCollapse c, float seconds, bool revealed = true, bool prologue = false, bool hudHidden = false)
    {
        bool folded = false;
        for (float t = 0f; t < seconds; t += Frame)
        {
            folded |= c.Tick(Frame, revealed, prologue, hudHidden);
        }

        return folded;
    }

    [Fact]
    public void ARevealedLine_FoldsOnlyAfterTheWait()
    {
        var c = new VegaCollapse();
        Assert.False(Run(c, VegaCollapse.CollapseAfterSeconds - 1f));
        Assert.False(c.Collapsed);
        Assert.True(Run(c, 2f));
        Assert.True(c.Collapsed);
    }

    [Fact]
    public void TheFoldFires_Once()
    {
        var c = new VegaCollapse();
        Run(c, VegaCollapse.CollapseAfterSeconds + 1f);
        Assert.True(c.Collapsed);
        Assert.False(c.Tick(Frame, fullyRevealed: true, prologue: false, hudHidden: false));
        Assert.True(c.Collapsed);
    }

    [Fact]
    public void APageStillTyping_NeverFolds_AndRestartsTheWait()
    {
        var c = new VegaCollapse();
        Run(c, 30f);
        Assert.False(Run(c, 120f, revealed: false));
        Assert.Equal(0f, c.IdleSeconds);
        Assert.False(Run(c, 30f)); // the wait starts over once it is revealed again
    }

    [Fact]
    public void AProloguePage_NeverFolds()
    {
        var c = new VegaCollapse();
        Assert.False(Run(c, 600f, prologue: true));
        Assert.False(c.Collapsed);
    }

    [Fact]
    public void AMenuOverTheHud_PausesTheWait()
    {
        var c = new VegaCollapse();
        Run(c, 30f);
        Assert.False(Run(c, 300f, hudHidden: true));
        Assert.False(c.Collapsed);
        Assert.True(Run(c, 16f)); // 30 + 16 s actually in view
    }

    [Fact]
    public void Expanding_OpensTheTab_AndGivesTheLineAFreshWait()
    {
        var c = new VegaCollapse();
        Run(c, VegaCollapse.CollapseAfterSeconds + 1f);
        c.Expand();
        Assert.False(c.Collapsed);
        Assert.False(Run(c, VegaCollapse.CollapseAfterSeconds - 1f));
        Assert.True(Run(c, 2f));
    }

    [Fact]
    public void ANewPage_Resets()
    {
        var c = new VegaCollapse();
        Run(c, 40f);
        c.Reset();
        Assert.Equal(0f, c.IdleSeconds);
        Assert.False(Run(c, 40f));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 4)]
    [InlineData(-2, 1)]
    public void TheTab_CountsTheLineOnScreen_PlusTheQueue(int queued, int shown)
        => Assert.Equal(shown, VegaCollapse.WaitingLines(queued));
}
