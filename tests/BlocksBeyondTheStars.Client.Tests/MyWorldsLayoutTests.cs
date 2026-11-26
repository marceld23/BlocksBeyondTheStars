// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4039: the rows of the New-world form (<see cref="MyWorldsLayout"/>) stay apart — the hosting row's divider ran through the
/// "World options…" button, which had a row of its own that did not fit between the mode cards and the divider. Pure
/// geometry plus a source check that the screen places its controls by these numbers.
/// </summary>
public sealed class MyWorldsLayoutTests
{
    /// <summary>The smallest gap between two rows of the form.</summary>
    private const float MinGap = 8f;

    [Fact]
    public void WorldOptions_EndsAboveTheHostingDivider()
    {
        Assert.True(MyWorldsLayout.OptionsY + MyWorldsLayout.OptionsH + MinGap <= MyWorldsLayout.DividerY,
            $"\"World options…\" ends at {MyWorldsLayout.OptionsY + MyWorldsLayout.OptionsH}, the divider is at {MyWorldsLayout.DividerY}");
    }

    [Fact]
    public void TheModeBox_AndItsHints_FitBetweenTheCardsAndTheDivider()
    {
        float cardsEnd = MyWorldsLayout.ModeCardsY + MyWorldsLayout.ModeCardH;
        Assert.True(MyWorldsLayout.ModeBoxY >= cardsEnd + MinGap, $"the mode box starts at {MyWorldsLayout.ModeBoxY}, the cards end at {cardsEnd}");
        Assert.True(MyWorldsLayout.ModeBoxY + MyWorldsLayout.ModeBoxH + MinGap <= MyWorldsLayout.DividerY, "the Creative box runs into the divider");
        float hintEnd = MyWorldsLayout.ModeBoxY + MyWorldsLayout.HintInset + MyWorldsLayout.HintH;
        Assert.True(hintEnd + MinGap <= MyWorldsLayout.DividerY, $"the mode hints end at {hintEnd}, the divider is at {MyWorldsLayout.DividerY}");
    }

    [Fact]
    public void WorldOptions_SitsBesideTheModeBox_UnderTheFourthCard()
    {
        Assert.True(MyWorldsLayout.Margin + MyWorldsLayout.ModeBoxW + MinGap <= MyWorldsLayout.OptionsX, "the mode box reaches under the button");
        float fourthCard = MyWorldsLayout.Margin + (3 * MyWorldsLayout.ModeCardStep);
        Assert.Equal(MyWorldsLayout.OptionsX, fourthCard);
        Assert.Equal(MyWorldsLayout.ModeCardW, MyWorldsLayout.OptionsW);
        Assert.True(MyWorldsLayout.OptionsX + MyWorldsLayout.OptionsW <= MyWorldsLayout.PaneW - MyWorldsLayout.Margin, "the button leaves the pane's margin");
        Assert.True(MyWorldsLayout.OptionsH >= 44f, "a button is at least 44 px high (a finger's target)");
    }

    [Fact]
    public void TheScreen_PlacesItsRows_ByTheLayout()
    {
        string screen = ClientTestPaths.ScriptPartsText("UiSaveSelect");
        Assert.Contains("PaneW = MyWorldsLayout.PaneW", screen);
        Assert.Contains("LanY = MyWorldsLayout.LanY", screen);
        Assert.Contains("UiKit.AddImage(pane, 32f, LanY - 14f", screen); // the divider = MyWorldsLayout.DividerY
        Assert.Equal(MyWorldsLayout.LanY - 14f, MyWorldsLayout.DividerY);
        Assert.Contains("UiKit.AddTierButton(c, MyWorldsLayout.OptionsX, MyWorldsLayout.OptionsY, MyWorldsLayout.OptionsW, MyWorldsLayout.OptionsH,", screen);
        Assert.Contains("UiKit.AddPanel(c, x, y, w, MyWorldsLayout.ModeBoxH", screen);
        Assert.Contains("const float x = MyWorldsLayout.Margin, y = MyWorldsLayout.ModeBoxY, w = MyWorldsLayout.ModeBoxW;", screen);
        Assert.DoesNotContain("470f", screen);
    }
}
