// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3414 (the Ressaw lamp made only a lit bubble in a night-black world): the Unity half of night sight, guarded as source
/// text (the Unity scripts cannot load headless — the wiring guards' pattern). <c>BioSenses</c> only eases the strength and
/// hands it over; the <c>Sky</c> reads it and draws the light, the models' light, the sky, haze and mist colours and the
/// cave fill from <see cref="NightSightRules"/> — and never moves the time of day or the sun, so the moon and the stars stay.
/// The starfield, the clouds and the eye adaptation follow.
/// </summary>
public sealed class NightSightWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    private static readonly string[] SkyParts = { "Sky.cs", "Sky.Dark.cs", "Sky.Mist.cs", "Sky.Seasons.cs", "Sky.StarLight.cs", "Sky.HighDetails.cs" };

    [Fact]
    public void BioSenses_HandsTheEasedStrengthOver_AndNoLongerWritesTheGlobals()
    {
        string src = Script("BioSenses.cs");
        Assert.Contains("public static float NightSight { get; private set; }", src);
        Assert.Contains("NightSight = _sight;", src);
        Assert.Contains("NightSight = 0f;", src); // gone with the component: the Sky draws a plain night again
        Assert.DoesNotContain("Shader.SetGlobal", src);
        Assert.DoesNotContain("Shader.GetGlobal", src);
    }

    [Fact]
    public void TheSky_ReadsTheNightSightInput_AndLightsTheWorldWithIt()
    {
        Assert.Contains("BioSenses.NightSight", Script("Sky.Dark.cs"));
        string sky = Script("Sky.cs");
        Assert.Contains("float sight = NightSightInput();", sky);
        Assert.Contains("NightSightRules.SeenDay(day, sight)", sky);
        Assert.Contains("NightSightRules.LightDay(dayLit, sight)", sky);

        // The world light, and with it the models' light and the ambient, take the raised level.
        Assert.Contains("litSun * (seenBrightness * weatherDim)", sky);
        Assert.Contains("PublishDarkGlobals(ShaderColor.Srgb(tint), interiorFill);", sky);
        Assert.Contains("RenderSettings.ambientLight = Color.Lerp(sky, tint, 0.35f);", sky);
        Assert.Contains("InteriorFill(litInterior, home, sight)", sky);
        Assert.Contains("NightSightRules.CaveFill(nightSight)", Script("Sky.Dark.cs"));
    }

    [Fact]
    public void TheSkyColour_TakesTheSeenDay_BeforeTheWeatherTint()
    {
        string sky = Script("Sky.cs");
        int lerp = sky.IndexOf("sky = Color.Lerp(nightSky, daySky, seenDay);", StringComparison.Ordinal);
        int haze = sky.IndexOf("sky = WeatherHazeSky(sky, weatherIntensity);", StringComparison.Ordinal);
        Assert.True(lerp > 0 && haze > lerp, "the night-sight day must lift the sky BEFORE the weather tints it (a sandstorm stays sandy)");
        Assert.Contains("ApplyFog(sky, warmSun, weatherIntensity, day, seenDay, spaceSky);", sky);

        string mist = Script("Sky.Mist.cs");
        Assert.Contains("DistanceFogEnd(env, renderDist, airDensity, weatherIntensity, seenDay, tod)", mist);
        Assert.Contains("ApplyUnderwater(sky, seenDay, airDensity,", mist);
        Assert.Contains("ApplyMistColours(env, sky, sun, seenDay, weatherIntensity)", mist);
        // What the sun does keeps the real day: the height fog's time of day and the cloud shadows.
        Assert.Contains("ApplyHeightFog(env, tod, day, exposure)", mist);
        Assert.Contains("ApplyCloudShadows(env, weatherIntensity, day, exposure)", mist);
    }

    [Fact]
    public void TheSky_NeverMovesTheTimeOfDayOrTheSun_FromNightSight()
    {
        var assignment = new Regex(@"(\b_time\b|\bDayFraction\b|\bLocalTimeOfDay\b|_sun\.intensity|_sun\.transform\.rotation)\s*=[^=]",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        foreach (string part in SkyParts)
        {
            foreach (string line in Script(part).Split('\n'))
            {
                if (!assignment.IsMatch(line))
                {
                    continue;
                }

                Assert.DoesNotContain("sight", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("seen", line, StringComparison.OrdinalIgnoreCase);
            }
        }

        string sky = Script("Sky.cs");
        Assert.Contains("_sun.intensity = brightness;", sky);
        Assert.Contains("float brightness = constantLight ? stationDim : Mathf.Lerp(0.35f, 1f, dayLit);", sky);
        Assert.Contains("_sun.transform.rotation = Quaternion.Euler(time * 360f - 90f, 160f, 0f);", sky);
        Assert.Contains("public float DayFraction => _time;", sky);
    }

    [Fact]
    public void TheStars_StayOutByTheSunsHeight_WithABoost()
    {
        string src = Script("Starfield.cs");
        Assert.Contains("NightSightRules.StarBoost(BioSenses.NightSight)", src);
        Assert.Contains("Game.SpaceViewActive ? 1f : NightSightRules.StarBoost(", src); // the flight view stays as it is

        // When the stars come out is still the sun's height alone.
        int start = src.IndexOf("private float TargetBrightness()", StringComparison.Ordinal);
        int end = src.IndexOf("private static Texture2D BuildDotTexture()", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.DoesNotContain("NightSight", src.Substring(start, end - start));
    }

    [Fact]
    public void TheClouds_AndTheEyes_FollowNightSight()
    {
        Assert.Contains("NightSightRules.SeenDay(", Script("Clouds.cs"));
        Assert.Contains("NightSightRules.SeenDay(day, BioSenses.NightSight)", Script("CloudSea.cs"));
        Assert.Contains("NightSightRules.AdaptedLight(BioSenses.NightSight)", Script("AtmosphereProbe.cs"));
    }
}
