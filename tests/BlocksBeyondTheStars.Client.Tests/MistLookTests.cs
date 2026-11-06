// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3270–#3272: the mist rules (<see cref="MistLook"/>) the Sky turns into the haze globals — the height fog from the world's
/// mist amount, the wisps, the lit mist and cloud colours, the cloud band and the weather's colour — as properties (no golden
/// floats), plus the noise sheets the wisps read (<see cref="CloudNoiseSheet"/>) and the shader constants MistLook shares with
/// <c>AtmosphereCommon.hlsl</c> (source text: the shader cannot run headless).
/// </summary>
public sealed class MistLookTests
{
    private static readonly string[] Weathers = { "clear", "clouds", "rain", "fog", "ground_fog", "storm", "blizzard", "ion_storm" };
    private static readonly MistRgb NoonSky = new MistRgb(0.55f, 0.75f, 0.95f);
    private static readonly MistRgb NightSky = new MistRgb(0.03f, 0.04f, 0.09f);
    private static readonly MistRgb Sun = new MistRgb(1f, 0.96f, 0.88f);
    private static readonly string[] OnlyFogWriter = { "Sky.Mist.cs" };
    private static readonly int[] MistColours = { 0xFFFFFF, 0xF2E27A, 0x9BE38C, 0xA8D8FF, 0xC7A8F0, 0xF5B8C8 };

    /// <summary>The Sky's daylight for a local time of day (sun height, 0 at midnight … 1 at noon).</summary>
    private static float Day(float tod) => MistLook.Clamp01((MathF.Sin((tod - 0.25f) * MathF.PI * 2f) * 0.5f) + 0.5f);

    [Fact]
    public void AnAirlessWorld_HasNoMist_AtAnyHourInAnyWeather()
    {
        foreach (string weather in Weathers)
        {
            for (float tod = 0f; tod <= 1f; tod += 0.05f)
            {
                Assert.Equal(0f, MistLook.HeightFogStrength(0f, tod, Day(tod), weather));
            }
        }
    }

    [Fact]
    public void Dawn_IsMistierThanNoon()
    {
        float dawn = MistLook.HeightFogStrength(0.45f, 0.27f, Day(0.27f), "clear");
        float noon = MistLook.HeightFogStrength(0.45f, 0.5f, Day(0.5f), "clear");
        Assert.True(dawn > noon * 2f, $"dawn {dawn} vs noon {noon}");
    }

    [Theory]
    [InlineData(0.27f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    public void FogWeather_IsMistierThanClear_AStormBlowsItAway(float tod)
    {
        float clear = MistLook.HeightFogStrength(0.4f, tod, Day(tod), "clear");
        Assert.True(MistLook.HeightFogStrength(0.4f, tod, Day(tod), "fog") > clear);
        Assert.True(MistLook.HeightFogStrength(0.4f, tod, Day(tod), "ground_fog") > clear);
        Assert.True(MistLook.HeightFogStrength(0.4f, tod, Day(tod), "storm") < clear);
        Assert.True(MistLook.FogFloor(10f, "fog") > MistLook.FogFloor(10f, "clear"));
    }

    [Fact]
    public void TheMistStrength_StaysWithinZeroAndOne_AndThickerMistReachesHigher()
    {
        for (float amount = 0f; amount <= 1f; amount += 0.1f)
        {
            foreach (string weather in Weathers)
            {
                float s = MistLook.HeightFogStrength(amount, 0.27f, Day(0.27f), weather);
                Assert.InRange(s, 0f, 1f);
            }
        }

        Assert.True(MistLook.Falloff(1f) < MistLook.Falloff(0f)); // a smaller 1/falloff = the density reaches higher
    }

    [Fact]
    public void WispsOff_LeaveTheSmoothBlanketExactly()
    {
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            float r = (float)rng.NextDouble(), g = (float)rng.NextDouble(), morph = (float)rng.NextDouble();
            Assert.Equal(1f, MistLook.WispFactor(0f, r, g, morph, MistLook.MorphContrast(morph)));
        }

        Assert.Equal(0f, MistLook.WispStrength(0.9f, enabled: false));
        Assert.Equal(0.9f, MistLook.WispStrength(0.9f, enabled: true), 5);
        Assert.True(MistLook.WispFrequency(1f) > MistLook.WispFrequency(0f)); // torn worlds: smaller, closer banks
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void FullWisps_KeepTheAverageDensity_AndOpenRealGapsBetweenBanks(float morph)
    {
        byte[] a = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedA);
        byte[] b = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedB);
        float contrast = MistLook.MorphContrast(morph);
        double sum = 0;
        int gaps = 0, banks = 0, n = 0;
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                // Between the texel centres too: what the bilinear sample returns across the sheet.
                float u = (x + 0.25f) / 128f, v = (y + 0.25f) / 128f;
                float f = MistLook.WispFactor(1f, CloudNoiseSheet.Sample(a, u, v), CloudNoiseSheet.Sample(b, u, v), morph, contrast);
                Assert.InRange(f, 0f, 2f);
                sum += f;
                gaps += f < 0.1f ? 1 : 0;
                banks += f > 1.9f ? 1 : 0;
                n++;
            }
        }

        double mean = sum / n;
        Assert.InRange(mean, 0.9, 1.1); // the banks hold about the mist the smooth blanket had
        Assert.True(gaps > n / 8, $"gaps {gaps} of {n}");   // real gaps, nearly clear between the banks
        Assert.True(banks > n / 8, $"banks {banks} of {n}");
    }

    [Fact]
    public void TheNoiseSheets_AreDeterministic_TheEvenOnesUniformAndIndependent()
    {
        byte[] shadow = CloudNoiseSheet.Bake(CloudNoiseSheet.ShadowSeed);
        Assert.Equal(shadow, CloudNoiseSheet.Bake(CloudNoiseSheet.ShadowSeed));
        Assert.Equal(CloudNoiseSheet.Size * CloudNoiseSheet.Size, shadow.Length);

        byte[] a = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedA);
        byte[] b = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedB);
        Assert.Equal(a, CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedA));
        foreach (byte[] sheet in new[] { a, b })
        {
            // Even: every quarter of the value range covers a quarter of the area (a threshold t leaves the share t below).
            for (int q = 1; q < 4; q++)
            {
                double below = sheet.Count(v => v < q * 64) / (double)sheet.Length;
                Assert.InRange(below, (q / 4.0) - 0.01, (q / 4.0) + 0.01);
            }

            Assert.Equal(0, sheet.Min());
            Assert.Equal(255, sheet.Max());
        }

        double cov = 0, va = 0, vb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double da = (a[i] / 255.0) - 0.5, db = (b[i] / 255.0) - 0.5;
            cov += da * db;
            va += da * da;
            vb += db * db;
        }

        Assert.InRange(cov / Math.Sqrt(va * vb), -0.5, 0.5); // the morph blends two different patterns
    }

    [Fact]
    public void TheSheet_SamplesLikeTheGpu_AtTexelCentres_AndWraps()
    {
        byte[] r = CloudNoiseSheet.Bake(CloudNoiseSheet.ShadowSeed);
        const int size = CloudNoiseSheet.Size;
        foreach (var (x, y) in new[] { (0, 0), (5, 9), (63, 63), (31, 2) })
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            Assert.Equal(r[(y * size) + x] / 255f, CloudNoiseSheet.Sample(r, u, v), 4);
            Assert.Equal(CloudNoiseSheet.Sample(r, u, v), CloudNoiseSheet.Sample(r, u + 3f, v - 2f), 3);
        }

        Assert.Equal(0.5f, CloudNoiseSheet.Sample(null, 0.3f, 0.7f)); // no sheet yet: the middle value
    }

    [Fact]
    public void AWorldWithoutAMistColour_TakesTheSkyColour()
    {
        foreach (var sky in new[] { NoonSky, NightSky, new MistRgb(0.9f, 0.5f, 0.3f) })
        {
            for (float day = 0f; day <= 1f; day += 0.25f)
            {
                MistRgb lit = MistLook.LitMist(-1, sky, Sun, day, 0.3f);
                Assert.Equal(sky.R, lit.R);
                Assert.Equal(sky.G, lit.G);
                Assert.Equal(sky.B, lit.B);
            }
        }
    }

    [Fact]
    public void TheNightMist_IsDarkerThanTheDayMist_ButNeverBlackAndNeverGlowing()
    {
        foreach (int colour in MistColours)
        {
            MistRgb day = MistLook.LitMist(colour, NoonSky, Sun, 1f, 0f);
            MistRgb night = MistLook.LitMist(colour, NightSky, Sun, 0f, 0f);
            Assert.True(night.Luma < day.Luma, $"{colour:X6}: night {night.Luma} vs day {day.Luma}");
            Assert.True(night.Luma < 0.25f, $"{colour:X6}: the night mist glows ({night.Luma})");
            Assert.True(night.R >= MistLook.NightFloor.R && night.G >= MistLook.NightFloor.G && night.B >= MistLook.NightFloor.B);
            Assert.True(day.Luma > 0.55f, $"{colour:X6}: the day mist is not bright ({day.Luma})");
        }

        // A tinted world keeps its hue by day: sulphur-yellow mist stays yellow, not sky-blue.
        MistRgb yellow = MistLook.LitMist(0xF2E27A, NoonSky, Sun, 1f, 0f);
        Assert.True(yellow.R > yellow.B && yellow.G > yellow.B);
    }

    [Fact]
    public void TheCloudBand_IsLitLikeTheMist()
    {
        MistRgb day = MistLook.LitCloud(0xEDEFF2, -1, NoonSky, Sun, 1f, 0f);
        MistRgb night = MistLook.LitCloud(0xEDEFF2, -1, NightSky, Sun, 0f, 0f);
        Assert.True(night.Luma < day.Luma && night.Luma < 0.25f && day.Luma > 0.6f);
        Assert.True(night.R >= MistLook.NightFloor.R);
    }

    [Fact]
    public void ClearWeather_BringsNoBand_CloudsAndRainDo()
    {
        Assert.Equal(0f, MistLook.BandCover("clear", 1f, 1f, 100f, 140f, enabled: true));
        Assert.True(MistLook.BandCover("clouds", 0.5f, 0.6f, 100f, 140f, enabled: true) > 0f);
        Assert.True(MistLook.BandCover("rain", 0.5f, 0.6f, 100f, 140f, enabled: true) > 0.6f);
        Assert.Equal(0f, MistLook.BandCover("rain", 1f, 1f, 100f, 140f, enabled: false)); // "Cloud fog" off
        Assert.Equal(0f, MistLook.BandCover("rain", 1f, 1f, 0f, 0f, enabled: true));      // no band (airless, older server)
        Assert.Equal(0f, MistLook.BandCover("rain", 1f, 1f, 100f, 102f, enabled: true));  // too thin to count
    }

    [Fact]
    public void TheBand_IsDensestInItsMiddle_WithSoftEdges()
    {
        const float baseY = 120f, topY = 160f;
        float edge = MistLook.BandEdge(baseY, topY);
        Assert.Equal(0f, MistLook.BandDensityAt(baseY - 5f, baseY, topY));
        Assert.Equal(0f, MistLook.BandDensityAt(baseY, baseY, topY));
        Assert.Equal(1f, MistLook.BandDensityAt((baseY + topY) / 2f, baseY, topY));
        Assert.Equal(0.5f, MistLook.BandDensityAt(baseY + (edge / 2f), baseY, topY), 4);
        Assert.Equal(0.5f, MistLook.BandDensityAt(topY - (edge / 2f), baseY, topY), 4);
        Assert.Equal(0f, MistLook.BandDensityAt(topY + 1f, baseY, topY));
        Assert.True(edge * 2f <= topY - baseY);
        Assert.Equal(0f, MistLook.BandDensityAt(130f, 0f, 0f)); // a world without a band
    }

    [Fact]
    public void InsideACloud_TheNearestMetresStayReadable_AndFortyMetresFade()
    {
        for (float cover = 0f; cover <= 1f; cover += 0.1f)
        {
            Assert.Equal(0f, MistLook.BandAmount(MistLook.BandClearMetres, cover));
            Assert.True(MistLook.BandAmount(MistLook.MinVisibleMetres, cover) <= 0.5f, $"cover {cover}");
        }

        Assert.True(MistLook.BandAmount(40f, 1f) >= 0.85f);
        Assert.True(MistLook.BandAmount(40f, 0.5f) >= 0.75f);
    }

    [Fact]
    public void AFullCover_FillsTheBand_APatchyOneLeavesHoles()
    {
        byte[] holes = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedA);
        double FillShare(float cover) => holes.Average(b => MistLook.BandFill(b / 255f, cover));
        Assert.Equal(1.0, FillShare(1f), 3);                  // a closed band: no holes
        Assert.Equal(0.0, FillShare(0f), 3);
        for (float cover = 0.2f; cover <= 0.8f; cover += 0.15f)
        {
            Assert.InRange(FillShare(cover), cover - 0.12, cover + 0.12); // about `cover` of the area is cloud
        }
    }

    [Fact]
    public void FogDustAndIonStorms_TintTheHaze_ClearWeatherDoesNot()
    {
        var grit = new MistRgb(0.74f, 0.62f, 0.40f);
        HazeTint fog = MistLook.WeatherTint("fog", "none", 1f, grit);
        Assert.True(fog.SkyWeight > 0f && fog.MistWeight > 0f);
        HazeTint ground = MistLook.WeatherTint("ground_fog", "none", 1f, grit);
        Assert.True(ground.SkyWeight == 0f && ground.MistWeight > 0f); // from a hilltop you look out over ground fog
        HazeTint dust = MistLook.WeatherTint("gale", "dust", 0.8f, grit);
        Assert.Equal(grit.R, dust.Color.R);
        Assert.True(dust.SkyWeight > 0f);
        Assert.True(MistLook.WeatherTint("ion_storm", "none", 1f, grit).SkyWeight > 0f);
        Assert.Equal(0f, MistLook.WeatherTint("clear", "none", 1f, grit).SkyWeight);
        Assert.Equal(0f, MistLook.WeatherTint("rain", "rain", 1f, grit).MistWeight);
        Assert.Equal(0f, MistLook.WeatherTint("fog", "none", 0f, grit).SkyWeight); // no episode yet: no tint
    }

    [Fact]
    public void ATint_KeepsTheBrightness_SoTheNightNeverGlows()
    {
        var fogGrey = new MistRgb(0.80f, 0.83f, 0.86f);
        foreach (var sky in new[] { NoonSky, NightSky })
        {
            MistRgb tinted = MistLook.ApplyTint(sky, fogGrey, 0.8f);
            Assert.Equal(sky.Luma, tinted.Luma, 3);
        }

        Assert.Equal(NoonSky.R, MistLook.ApplyTint(NoonSky, fogGrey, 0f).R);
    }

    [Fact]
    public void TheShader_SharesMistLooksConstants()
    {
        string shader = Read("client", "Assets", "BlocksBeyondTheStars", "Shaders", "AtmosphereCommon.hlsl");
        Assert.Equal(MistLook.WispLow, Define(shader, "BBTS_WISP_LOW"), 5);
        Assert.Equal(MistLook.WispHigh, Define(shader, "BBTS_WISP_HIGH"), 5);
        Assert.Equal(MistLook.BandClearMetres, Define(shader, "BBTS_BAND_CLEAR"), 5);
        Assert.Equal(MistLook.BandExtinctionBase, Define(shader, "BBTS_BAND_K0"), 5);
        Assert.Equal(MistLook.BandExtinctionCover, Define(shader, "BBTS_BAND_K1"), 5);
        Assert.Equal(MistLook.BandHoleTile, Define(shader, "BBTS_BAND_HOLE_TILE"), 5);
        string shear = string.Create(CultureInfo.InvariantCulture, $"#define BBTS_WISP_SHEAR float2({MistLook.WispShearX}, {MistLook.WispShearZ})");
        Assert.Contains(shear, shader);
        // The band fill and the wisp morph are repeated in the shader as formulas (on the even green/blue sheets): pin them too.
        Assert.Contains("float t = 1.1 - 1.2 * cover;", shader);
        Assert.Contains("fill = smoothstep(t - 0.08, t + 0.08, n);", shader);
        Assert.Contains("float n = 0.5 + (lerp(gb.x, gb.y, _Sc_MistCam.y) - 0.5) * _Sc_MistCam.z;", shader);
        Assert.Contains("SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, uv, 0).gb;", shader);
    }

    [Fact]
    public void TheDeadUnityFogPath_IsGone_AndTheSkyIsItsOnlyWriter()
    {
        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        var writers = Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("RenderSettings.fogColor", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToArray();
        Assert.Equal(OnlyFogWriter, writers);
        Assert.DoesNotContain("RenderSettings.fog", File.ReadAllText(Path.Combine(scripts, "WeatherFx3D.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("MistFor(", File.ReadAllText(Path.Combine(scripts, "Sky.cs")), StringComparison.Ordinal);
        Assert.Contains("env.MistAmount", File.ReadAllText(Path.Combine(scripts, "Sky.Mist.cs")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BlockAtlas.shader")]
    [InlineData("BlockAtlasTransparent.shader")]
    [InlineData("FarTerrain.shader")]
    [InlineData("ParticleAlpha.shader")]
    public void TheMainSurfaceShaders_TakeAmountAndColourFromOnePass(string file)
    {
        string shader = Read("client", "Assets", "BlocksBeyondTheStars", "Shaders", file);
        Assert.Contains("BbtsHaze(", shader, StringComparison.Ordinal);
        Assert.DoesNotContain("BbtsHazeColor(", shader, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot() }.Concat(parts).ToArray()));

    private static float Define(string source, string name)
    {
        var m = Regex.Match(source, @"#define " + name + @" (?<v>[0-9.]+)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(2));
        Assert.True(m.Success, $"{name} is gone from AtmosphereCommon.hlsl");
        return float.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
    }
}
