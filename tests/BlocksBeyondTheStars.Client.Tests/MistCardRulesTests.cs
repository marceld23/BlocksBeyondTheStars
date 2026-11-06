// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3273: fog banks in the air — a few large mist cards at the valley fog floor. <see cref="MistCardRules"/> holds the
/// budget per preset (small hardware is a hard constraint), the fades that keep a card out of the player's face, where a
/// card may stand, and the cap on the screen area all cards together cover.
/// </summary>
public sealed class MistCardRulesTests
{
    private const int Potato = 0, Low = 1, Medium = 2, High = 3;

    [Fact]
    public void Budget_FollowsThePreset_AndTheBrowserGetsNone()
    {
        Assert.Equal(0, MistCardRules.Budget(Potato, browser: false));
        Assert.Equal(4, MistCardRules.Budget(Low, browser: false));
        Assert.Equal(10, MistCardRules.Budget(Medium, browser: false));
        Assert.Equal(20, MistCardRules.Budget(High, browser: false));
        Assert.Equal(MistCardRules.MaxBudget, MistCardRules.Budget(High, browser: false));
        for (int preset = Potato; preset <= High; preset++)
        {
            Assert.Equal(0, MistCardRules.Budget(preset, browser: true));
        }
    }

    [Fact]
    public void CoverageCap_GrowsWithThePreset()
    {
        Assert.True(MistCardRules.CoverageCap(Low) < MistCardRules.CoverageCap(Medium));
        Assert.True(MistCardRules.CoverageCap(Medium) < MistCardRules.CoverageCap(High));
        Assert.InRange(MistCardRules.CoverageCap(High), 1f, 2f);
    }

    [Fact]
    public void NoMist_ShowsNoCard_AndThickMist_ShowsTheWholeBudget()
    {
        Assert.Equal(0, MistCardRules.ActiveCount(20, 0f));
        Assert.Equal(0, MistCardRules.ActiveCount(20, MistCardRules.MistThreshold));
        Assert.Equal(0f, MistCardRules.Opacity(0f));
        Assert.Equal(1, MistCardRules.ActiveCount(4, MistCardRules.MistThreshold + 0.01f)); // a little mist: one faint bank
        Assert.Equal(20, MistCardRules.ActiveCount(20, 0.6f));
        Assert.Equal(20, MistCardRules.ActiveCount(20, 1f));
        Assert.Equal(0, MistCardRules.ActiveCount(0, 1f));
        Assert.Equal(MistCardRules.MaxOpacity, MistCardRules.Opacity(1f), 3);
    }

    [Fact]
    public void MoreMist_NeverMeansFewerOrFainterBanks()
    {
        int lastCount = 0;
        float lastOpacity = 0f;
        for (int step = 0; step <= 100; step++)
        {
            float mist = step / 100f;
            int count = MistCardRules.ActiveCount(MistCardRules.MaxBudget, mist);
            float opacity = MistCardRules.Opacity(mist);
            Assert.True(count >= lastCount, $"count fell at mist {mist}");
            Assert.True(opacity >= lastOpacity, $"opacity fell at mist {mist}");
            Assert.InRange(opacity, 0f, MistCardRules.MaxOpacity);
            lastCount = count;
            lastOpacity = opacity;
        }
    }

    [Fact]
    public void ACard_IsNeverInYourFace()
    {
        Assert.Equal(0f, MistCardRules.PixelNearFade(0f));
        Assert.Equal(0f, MistCardRules.PixelNearFade(MistCardRules.PixelNearStart));
        Assert.Equal(1f, MistCardRules.PixelNearFade(MistCardRules.PixelNearStart + MistCardRules.PixelNearLength));
        Assert.True(MistCardRules.PixelNearStart + MistCardRules.PixelNearLength <= 10f, "fully there only ~8 m out");

        // A whole card: gone while the camera would stand in it, there once its nearest edge is well away.
        Assert.Equal(0f, MistCardRules.NearFade(distance: 8f, halfWidth: 10f));
        Assert.Equal(0f, MistCardRules.NearFade(distance: 11f, halfWidth: 10f));
        Assert.Equal(1f, MistCardRules.NearFade(distance: 16f, halfWidth: 10f));
        Assert.Equal(1f, MistCardRules.NearFade(distance: MistCardRules.RingInner, halfWidth: MistCardRules.MinWidth * 0.5f));
    }

    [Fact]
    public void Cards_FadeTowardTheFarEdge_AndLeaveTheRingBeyondIt()
    {
        Assert.Equal(1f, MistCardRules.FarFade(MistCardRules.RingInner));
        Assert.Equal(1f, MistCardRules.FarFade(MistCardRules.RingOuter - MistCardRules.FarFadeLength));
        Assert.Equal(0f, MistCardRules.FarFade(MistCardRules.RingOuter));
        Assert.InRange(MistCardRules.FarFade(MistCardRules.RingOuter - (MistCardRules.FarFadeLength * 0.5f)), 0.4f, 0.6f);

        Assert.False(MistCardRules.OutOfRing(40f, 10f));
        Assert.True(MistCardRules.OutOfRing(MistCardRules.RingOuter + 6f, 10f));
        Assert.True(MistCardRules.OutOfRing(3f, 3f));  // the camera walked into it
        Assert.False(MistCardRules.OutOfRing(MistCardRules.RingInner, MistCardRules.MaxWidth * 0.5f));
    }

    [Fact]
    public void Lifecycle_FadesInAndOut_NeverPops()
    {
        Assert.Equal(0f, MistCardRules.Lifecycle(0f, 60f));
        Assert.Equal(0f, MistCardRules.Lifecycle(60f, 60f));
        Assert.Equal(1f, MistCardRules.Lifecycle(30f, 60f));
        Assert.InRange(MistCardRules.Lifecycle(3f, 60f), 0.4f, 0.6f);
        Assert.InRange(MistCardRules.Lifecycle(57f, 60f), 0.4f, 0.6f);
        Assert.Equal(1f, MistCardRules.Lifecycle(2f, 4f)); // a short life fades over half of it each way
        Assert.Equal(0f, MistCardRules.Lifecycle(1f, 0f));

        float last = 0f;
        for (int t = 1; t <= 600; t++)
        {
            float v = MistCardRules.Lifecycle(t * 0.1f, 60f);
            Assert.True(Math.Abs(v - last) <= 0.1f / MistCardRules.LifeFade + 1e-4f, $"jump at {t * 0.1f} s");
            last = v;
        }
    }

    [Fact]
    public void Banks_LieInTheValleys_NeverInsideTheGround()
    {
        const float Floor = 40f;
        Assert.True(MistCardRules.SpotHoldsMist(Floor - 6f, Floor));
        Assert.True(MistCardRules.SpotHoldsMist(Floor + MistCardRules.HillTolerance, Floor));
        Assert.False(MistCardRules.SpotHoldsMist(Floor + MistCardRules.HillTolerance + 0.5f, Floor)); // a hill: no bank

        Assert.Equal(Floor + MistCardRules.MinLift, MistCardRules.CentreY(Floor, Floor - 10f, 0f, 4f), 3);
        Assert.Equal(Floor + MistCardRules.MaxLift, MistCardRules.CentreY(Floor, Floor - 10f, 1f, 4f), 3);
        // Ground right under the card: the centre rises so the ground cuts at most its bottom edge.
        float y = MistCardRules.CentreY(Floor, Floor + 3f, 0f, 8f);
        Assert.True(y - (8f * 0.5f) >= Floor + 3f - (8f * 0.15f) - 1e-3f);
    }

    [Fact]
    public void ScreenArea_ShrinksWithDistance_AndNeverExceedsOneScreen()
    {
        float tan = (float)Math.Tan(Math.PI / 6.0); // 60° vertical field of view
        float near = MistCardRules.ScreenArea(20f, 8f, 15f, tan, 16f / 9f);
        float far = MistCardRules.ScreenArea(20f, 8f, 60f, tan, 16f / 9f);
        Assert.True(near > far);
        Assert.InRange(near, 0.1f, 0.5f);
        Assert.Equal(1f, MistCardRules.ScreenArea(200f, 200f, 1f, tan, 16f / 9f));
        Assert.Equal(1f, MistCardRules.ScreenArea(20f, 8f, 0f, tan, 16f / 9f)); // degenerate distance: clamped, no NaN
    }

    [Fact]
    public void CoverageCap_LetsTheLargestCardsGoFirst()
    {
        float[] areas = { 0.1f, 0.9f, 0.3f, 0.5f, 0.05f };
        bool[] keep = { true, true, true, false, true };
        int dropped = MistCardRules.ApplyCoverageCap(areas, keep, areas.Length, 0.5f);
        Assert.Equal(1, dropped);
        Assert.Equal(new[] { true, false, true, false, true }, keep); // 0.9 went; the hidden 0.5 never counted

        bool[] all = { true, true, true, true, true };
        MistCardRules.ApplyCoverageCap(areas, all, areas.Length, 0.2f);
        float kept = areas.Where((_, i) => all[i]).Sum();
        Assert.True(kept <= 0.2f + 1e-5f);
        Assert.True(all[0] && all[4]); // the small far ones stay

        Assert.Equal(0, MistCardRules.ApplyCoverageCap(areas, new bool[5], 5, 0f));
    }
}
