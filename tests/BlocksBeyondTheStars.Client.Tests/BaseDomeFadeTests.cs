// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The base dome is the start zone (#2659): visible while the base has no sealed room with air, faded out once it has
/// one, back when the last one is lost — and the fade never overshoots. Also the tips log's place for the base-hunger
/// hint (#2658).
/// </summary>
public sealed class BaseDomeFadeTests
{
    private const float Visible = 0.10f;

    [Fact]
    public void TargetAlpha_VisibleWithoutRooms_HiddenWithAny()
    {
        Assert.Equal(Visible, BaseDomeFade.TargetAlpha(0, Visible));
        Assert.Equal(0f, BaseDomeFade.TargetAlpha(1, Visible));
        Assert.Equal(0f, BaseDomeFade.TargetAlpha(7, Visible));
    }

    [Fact]
    public void Step_FadesOutOverFadeSeconds_WithoutOvershooting()
    {
        float alpha = Visible;
        alpha = BaseDomeFade.Step(alpha, 0f, Visible, BaseDomeFade.FadeSeconds / 2f);
        Assert.InRange(alpha, Visible * 0.49f, Visible * 0.51f); // halfway after half the fade time

        alpha = BaseDomeFade.Step(alpha, 0f, Visible, BaseDomeFade.FadeSeconds); // more than enough time left
        Assert.Equal(0f, alpha);
    }

    [Fact]
    public void Step_FadesBackIn_AndStaysAtTarget()
    {
        float alpha = BaseDomeFade.Step(0f, Visible, Visible, BaseDomeFade.FadeSeconds * 2f);
        Assert.Equal(Visible, alpha);
        Assert.Equal(Visible, BaseDomeFade.Step(alpha, Visible, Visible, 0.5f));
    }

    [Fact]
    public void Step_NegativeFrameTime_ChangesNothing()
    {
        Assert.Equal(0.05f, BaseDomeFade.Step(0.05f, 0f, Visible, -1f));
    }

    [Fact]
    public void JournalKeys_ListTheBaseHungerHint_RightAfterTheShipFoodHint()
    {
        var keys = VegaText.JournalKeys(new[] { "vega:hint:base_hunger", "vega:hint:invfull", "vega:hint:shipfood" });
        int ship = keys.IndexOf("vega.hint.shipfood");
        int baseHunger = keys.IndexOf("vega.hint.base_hunger");
        Assert.True(ship >= 0 && baseHunger == ship + 1, string.Join(", ", keys));
    }
}
