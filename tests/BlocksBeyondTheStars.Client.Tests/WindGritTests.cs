// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3203: gale grit rendered as one-metre brown squares (the size rule took the long axis as the width), flew toward
/// world +X whatever the wind did (and squared the wind speed), had the same brown on every world and popped in and out.
/// <see cref="WindGrit"/> holds the rules; <c>WeatherFx3D</c> draws them.
/// </summary>
public sealed class WindGritTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void Grit_IsAThinStreak_AndRainKeepsItsShape()
    {
        // The style proportions WeatherFx3D uses: dust (0.04, 0.04, 0.36), sandstorm (0.05, 0.05, 0.30).
        var dust = WindGrit.Shape(0.04f, 0.04f, 0.36f, flake: false);
        Assert.Equal(0.12, dust.Size, 3);
        Assert.Equal(0.54, dust.Size * dust.LengthScale, 3);

        var sand = WindGrit.Shape(0.05f, 0.05f, 0.30f, flake: false);
        Assert.Equal(0.15, sand.Size, 3);
        Assert.Equal(0.45, sand.Size * sand.LengthScale, 3);

        // Rain and storm rain: the old rule (width = max(x, z) × 3, length scale = y / x × 0.5) still holds.
        var rain = WindGrit.Shape(0.03f, 0.5f, 0.03f, flake: false);
        Assert.Equal(0.09, rain.Size, 4);
        Assert.Equal(0.5 / 0.03 * 0.5, rain.LengthScale, 3);
        var storm = WindGrit.Shape(0.03f, 0.75f, 0.03f, flake: false);
        Assert.Equal(0.75 / 0.03 * 0.5, storm.LengthScale, 3);

        // Flakes stay round billboards.
        var snow = WindGrit.Shape(0.09f, 0.09f, 0.09f, flake: true);
        Assert.Equal(0.144, snow.Size, 4);
        Assert.Equal(1f, snow.LengthScale);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.2f)]
    [InlineData(3.9f)]
    public void Drive_FollowsTheWindDirection_LikeTheGrassAndTheCloudShadows(float direction)
    {
        var (x, z) = WindGrit.Drive(direction, 0.8f, 12f);
        float speed = MathF.Sqrt((x * x) + (z * z));
        Assert.Equal(12f + (0.8f * WindGrit.WindGain), speed, 3);
        // The grass sway and the cloud shadows move along (cos, sin) of the wind direction (GameBootstrap.WindVector).
        Assert.Equal(MathF.Cos(direction), x / speed, 4);
        Assert.Equal(MathF.Sin(direction), z / speed, 4);
    }

    [Fact]
    public void Drive_AppliesTheWindSpeedOnce()
    {
        float Speed(float wind)
        {
            var (x, z) = WindGrit.Drive(0.5f, wind, 2f);
            return MathF.Sqrt((x * x) + (z * z));
        }

        // Linear in the wind: the step from 0.25 → 0.5 equals the step from 0.5 → 0.75 (squared it would not).
        Assert.Equal(Speed(0.5f) - Speed(0.25f), Speed(0.75f) - Speed(0.5f), 3);
        Assert.Equal(2f, Speed(0f), 4);
    }

    [Fact]
    public void FallingWeather_KeepsItsOldStrength_ButFollowsTheWind()
    {
        // Rain and snow keep the historical push (slant + 12 × wind²) — only its direction now follows the wind.
        var (x, z) = WindGrit.FallDrive(1.2f, 0.4f, 2f);
        float speed = MathF.Sqrt((x * x) + (z * z));
        Assert.Equal(2f + (WindGrit.WindGain * 0.4f * 0.4f), speed, 3);
        Assert.Equal(MathF.Cos(1.2f), x / speed, 4);
        Assert.Equal(MathF.Sin(1.2f), z / speed, 4);

        // Calm air: just the style's own slant.
        var (cx, cz) = WindGrit.FallDrive(0f, 0f, 0.8f);
        Assert.Equal(0.8f, MathF.Sqrt((cx * cx) + (cz * cz)), 4);
    }

    [Theory]
    [InlineData("ice")]
    [InlineData("glacier")]
    [InlineData("tundra")]
    [InlineData("icecap")]
    [InlineData("frozen_ocean")]
    public void AFrozenWorld_BlowsWhiteSpindrift(string planetKey)
    {
        var planet = Content.GetPlanet(planetKey)!;
        var look = WindGrit.LookFor(planetKey, planet.FloraTheme, (float)planet.BaseTemperature, true, 0.4f, 0.4f, 0.4f);
        Assert.Equal("spindrift", look.Name);
        Assert.True(look.R > 0.8f && look.G > 0.8f && look.B > 0.9f, "spindrift is white");
    }

    [Fact]
    public void FreezingAir_OnAnyWorld_BlowsSpindrift()
    {
        Assert.Equal("spindrift", WindGrit.LookFor("rocky", string.Empty, -12f, true, 0.5f, 0.5f, 0.5f).Name);
        Assert.NotEqual("spindrift", WindGrit.LookFor("rocky", string.Empty, 12f, true, 0.5f, 0.5f, 0.5f).Name);
    }

    [Fact]
    public void TheRedDesert_BlowsReddishGrit()
    {
        var look = WindGrit.LookFor("red_desert", "desert", 38f, true, 0.78f, 0.5f, 0.36f);
        Assert.Equal("rust_grit", look.Name);
        Assert.True(look.R > look.G + 0.15f && look.G > look.B, $"rust: {look.R:0.00} {look.G:0.00} {look.B:0.00}");
    }

    [Fact]
    public void OtherWorlds_TakeTheGroundColour_ReadablyLifted()
    {
        // Pale sand throws pale sand-coloured grit.
        var desert = WindGrit.LookFor("desert", "desert", 44f, true, 0.88f, 0.80f, 0.58f);
        Assert.Equal("ground_grit", desert.Name);
        Assert.True(desert.R > desert.B + 0.1f, "sand grit is warm");

        // A grey stone world throws grey grit, not the old fixed brown.
        var stone = WindGrit.LookFor("rocky", string.Empty, 12f, true, 0.5f, 0.5f, 0.5f);
        Assert.True(Math.Abs(stone.R - stone.B) < 0.15f, "grey ground, greyish grit");

        // Black sand still reads against the land; an unknown ground falls back to plain grit.
        var black = WindGrit.LookFor("arena_nigra", "desert", 58f, true, 0.05f, 0.05f, 0.06f);
        Assert.True((0.299f * black.R) + (0.587f * black.G) + (0.114f * black.B) >= 0.27f);
        Assert.Equal(WindGrit.Plain.Name, WindGrit.LookFor("desert", "desert", 44f, false, 0f, 0f, 0f).Name);

        // The second colour is a lighter variant of the first.
        Assert.True(desert.R2 >= desert.R && desert.G2 >= desert.G && desert.B2 >= desert.B);
    }

    [Fact]
    public void Grains_Vary_InSizeAlphaAndTint()
    {
        var sizes = Enumerable.Range(0, 280).Select(WindGrit.GrainSize).ToList();
        var alphas = Enumerable.Range(0, 280).Select(WindGrit.GrainAlpha).ToList();
        var mixes = Enumerable.Range(0, 280).Select(WindGrit.GrainMix).ToList();
        Assert.All(sizes, s => Assert.InRange(s, 0.6f, 1.4f));
        Assert.All(alphas, a => Assert.InRange(a, 0.3f, 0.8f));
        Assert.All(mixes, m => Assert.InRange(m, 0f, 1f));
        Assert.True(sizes.Max() - sizes.Min() > 0.6f, "sizes spread over the range");
        Assert.True(alphas.Max() - alphas.Min() > 0.35f, "alphas spread over the range");
        Assert.Equal(WindGrit.GrainSize(17), WindGrit.GrainSize(17)); // fixed per pool slot
    }

    [Fact]
    public void AGrain_FadesIn_FadesAtTheEdge_AndNearTheCamera()
    {
        const float Kill = 28.8f;
        Assert.Equal(0f, WindGrit.GrainFade(0f, 10f, Kill));
        Assert.Equal(1f, WindGrit.GrainFade(1f, 10f, Kill));
        Assert.Equal(0f, WindGrit.GrainFade(1f, Kill, Kill));
        Assert.InRange(WindGrit.GrainFade(1f, Kill - (WindGrit.EdgeBand / 2f), Kill), 0.4f, 0.6f);
        Assert.Equal(0f, WindGrit.GrainFade(1f, 0.2f, Kill));
        Assert.Equal(1f, WindGrit.GrainFade(1f, WindGrit.NearFadeStart, Kill));
        Assert.InRange(WindGrit.GrainFade(1f, 0.9f, Kill), 0.01f, 0.99f);
    }

    [Fact]
    public void TheWholeEffect_EasesInAndOut_InsteadOfPopping()
    {
        float e = 0f;
        float t = 0f;
        while (e < 1f)
        {
            float next = WindGrit.Envelope(e, active: true, 1f / 60f);
            Assert.True(next - e <= (1f / 60f / WindGrit.EnvelopeInSeconds) + 1e-6f);
            e = next;
            t += 1f / 60f;
        }

        Assert.InRange(t, WindGrit.EnvelopeInSeconds - 0.05f, WindGrit.EnvelopeInSeconds + 0.05f);
        float half = WindGrit.Envelope(1f, active: false, WindGrit.EnvelopeOutSeconds / 2f);
        Assert.Equal(0.5, half, 3);
        Assert.Equal(0f, WindGrit.Envelope(half, active: false, WindGrit.EnvelopeOutSeconds));
        Assert.Equal(0.4f, WindGrit.Envelope(0.4f, active: true, 0f));
    }

    [Fact]
    public void OnlyTheBlownForms_AreGrit()
    {
        Assert.True(WindGrit.IsGrit("dust"));
        Assert.True(WindGrit.IsGrit("sandstorm"));
        Assert.False(WindGrit.IsGrit("rain"));
        Assert.False(WindGrit.IsGrit("snow"));
        Assert.False(WindGrit.IsGrit(null));
    }
}
