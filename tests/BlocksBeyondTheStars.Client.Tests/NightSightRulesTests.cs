// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3414 (the Ressaw lamp only made a lit bubble): the values night sight lifts the look of the world with
/// (<see cref="NightSightRules"/>) — nothing at strength 0, never a darker day, a full-strength night between night and noon,
/// stars that never dim, and eyes that add no dark boost on top.
/// </summary>
public sealed class NightSightRulesTests
{
    private static readonly float[] Levels = { 0f, 0.1f, 0.25f, 0.4f, 0.5f, 0.6f, 0.65f, 0.75f, 0.9f, 1f };

    /// <summary>The Sky's light brightness for an eased light level: 0.35 at night, 1 at noon (<c>Sky.ApplyLighting</c>).</summary>
    private static float Brightness(float dayLit) => 0.35f + (0.65f * dayLit);

    [Fact]
    public void StrengthZero_ChangesNothing()
    {
        foreach (float day in Levels)
        {
            Assert.Equal(day, NightSightRules.SeenDay(day, 0f));
            Assert.Equal(day, NightSightRules.LightDay(day, 0f));
            Assert.Equal(day, NightSightRules.SeenDay(day, -0.5f)); // a stray negative strength is no strength
        }

        Assert.Equal(0f, NightSightRules.CaveFill(0f));
        Assert.Equal(1f, NightSightRules.StarBoost(0f));
        Assert.Equal(0f, NightSightRules.AdaptedLight(0f));
    }

    [Fact]
    public void ADay_IsNeverDarkened()
    {
        foreach (float day in Levels)
        {
            foreach (float sight in Levels)
            {
                Assert.True(NightSightRules.SeenDay(day, sight) >= day, $"seen day {day} at strength {sight}");
                Assert.True(NightSightRules.LightDay(day, sight) >= day, $"light day {day} at strength {sight}");
            }
        }

        // Noon stays noon, and a bright afternoon already above the lift keeps its own level.
        Assert.Equal(1f, NightSightRules.SeenDay(1f, 1f));
        Assert.Equal(1f, NightSightRules.LightDay(1f, 1f));
        Assert.Equal(0.9f, NightSightRules.SeenDay(0.9f, 1f));
    }

    [Fact]
    public void FullStrength_SitsBetweenNightAndNoon()
    {
        float seen = NightSightRules.SeenDay(0f, 1f);
        Assert.True(seen > 0.5f && seen < 1f, $"a midnight under full night sight is seen at day {seen}: the sun up, not noon");

        float light = Brightness(NightSightRules.LightDay(0f, 1f));
        Assert.True(light > Brightness(0f) && light < Brightness(1f), $"light {light}");
        Assert.Equal(0.74f, light, 3); // the light floor night sight had before #3414 — a late afternoon

        float fill = NightSightRules.CaveFill(1f);
        Assert.True(fill > 0f && fill < 1f, $"a cave is lifted like a faint cabin light, not a lit one: {fill}");
    }

    [Fact]
    public void TheLift_GrowsWithTheStrength_AndStopsAtFull()
    {
        float previousDay = 0f, previousLight = 0f;
        foreach (float sight in Levels)
        {
            float seen = NightSightRules.SeenDay(0f, sight);
            float light = NightSightRules.LightDay(0f, sight);
            Assert.True(seen >= previousDay && light >= previousLight, $"strength {sight}");
            previousDay = seen;
            previousLight = light;
        }

        Assert.Equal(NightSightRules.SeenDay(0f, 1f), NightSightRules.SeenDay(0f, 3f));
        Assert.Equal(NightSightRules.LightDay(0f, 1f), NightSightRules.LightDay(0f, 3f));
        Assert.Equal(NightSightRules.StarBoost(1f), NightSightRules.StarBoost(3f));
    }

    [Fact]
    public void TheStars_AreNeverDimmed()
    {
        float previous = 1f;
        foreach (float sight in Levels)
        {
            float boost = NightSightRules.StarBoost(sight);
            Assert.True(boost >= previous, $"star boost {boost} at strength {sight}");
            previous = boost;
        }

        Assert.Equal(1f, NightSightRules.StarBoost(-1f));
        Assert.True(NightSightRules.StarBoost(1f) > 1f);
        Assert.True(NightSightRules.StarBoost(1f) * 1.3f <= 2f, "the starfield's 1.3 cap × the boost must stay in the shader's 0..2 range");
    }

    [Fact]
    public void TheEyes_AddNoDarkBoost_UnderFullNightSight()
    {
        float scene = NightSightRules.AdaptedLight(1f);
        Assert.True(scene >= EyeAdaptationRules.LitBrightness, $"night sight counts as {scene}");
        Assert.Equal(0f, EyeAdaptationRules.Target(scene, false, underground: true, day: 0f, darkVisibility: 1f, reducedEffects: false));
        Assert.Equal(0f, EyeAdaptationRules.Target(scene, true, underground: false, day: 0f, darkVisibility: 0f, reducedEffects: false));
    }
}
