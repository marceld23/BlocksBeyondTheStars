// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Lyxette's reports of 2026-11-03 (evening) on the client: no snow cap or wet gloss on a roofed floor (#3164, the weather
/// surface measured past the mesher's shade floor — guarded as source text, the shader cannot run headless) and the HUD's
/// weather chip naming what actually falls (#3165, <see cref="WeatherLook.ChipState"/>).
/// </summary>
public sealed class Reports20261103WiringTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot() }.Concat(parts).ToArray()));

    private static string Script(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Scripts", file);

    private static string Shader(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Shaders", file);

    [Fact]
    public void TheWeatherSurface_MeasuresTheExposurePastTheMeshersShadeFloor()
    {
        var timeout = TimeSpan.FromSeconds(2);
        // #3265: the mesher's skylight rules moved to Client.Core (SkylightRules), so the constant is read directly.
        var shader = Regex.Match(Shader("AtmosphereCommon.hlsl"), @"#define BBTS_SHADE_FLOOR (?<v>[0-9.]+)", RegexOptions.ExplicitCapture, timeout);
        Assert.True(shader.Success, "AtmosphereCommon's BBTS_SHADE_FLOOR is gone");
        Assert.Equal(SkylightRules.ShadeFloor, float.Parse(shader.Groups["v"].Value, CultureInfo.InvariantCulture));
        Assert.Contains("SkylightField", Script("ChunkMesher.Skylight.cs")); // the mesher's skylight is the shared rule's

        // A roof's shade (skylight == the floor) must count as no exposure at all, not 55 %.
        Assert.Contains("float exposed = saturate(N.y) * saturate((skylight - BBTS_SHADE_FLOOR) / (1.0 - BBTS_SHADE_FLOOR));", Shader("AtmosphereCommon.hlsl"));
    }

    /// <summary>#3265: one sky-ambient ramp for every opaque world surface — unchanged from the shade floor up, falling to
    /// almost nothing below it (a cave is dark, not shade) — plus the flat dark floor every lit surface keeps. The Built-in
    /// passes (CG) cannot include AtmosphereCommon, so they repeat the formula; all four places are pinned here.</summary>
    [Fact]
    public void TheSkyAmbient_IsOneRampInEveryWorldShader()
    {
        const string ramp = "lerp(0.32, 0.78, sky) - 0.30 * saturate(1.0 - sky / BBTS_SHADE_FLOOR)";
        Assert.Contains("return " + ramp + ";", Shader("AtmosphereCommon.hlsl"));
        Assert.Contains("return _Sc_DarkFloor > 0.0 ? _Sc_DarkFloor : 0.045;", Shader("AtmosphereCommon.hlsl")); // unset = the default
        Assert.Equal(DarkLightRules.DarkFloorMin, 0.045f);

        string atlas = Shader("BlockAtlas.shader");
        Assert.Contains("float amb = BbtsSkyAmbient(sky);", atlas);                                              // URP
        Assert.Contains("float amb = lerp(0.32, 0.78, sky) - 0.30 * saturate(1.0 - sky / 0.55);", atlas);        // Built-in
        Assert.Contains("float amb = BbtsSkyAmbient(sky);", Shader("MeadowLight.hlsl"));
        Assert.Contains("lerp(0.32, 0.78, sky) - 0.30 * saturate(1.0 - sky / 0.55)", Shader("ScatterLit.shader")); // Built-in
        foreach (var file in new[] { "BlockAtlas.shader", "MeadowLight.hlsl", "ScatterLit.shader" })
        {
            Assert.DoesNotContain("+ 0.05)", Shader(file)); // the flat floor is the global now
        }

        // The skylight below the floor reaches the shaders (and the models follow the world light): see the summary.
        Assert.Contains("_Sc_ModelLight", Shader("LitColor.shader"));
        Assert.Contains("BbtsSkyLightScale(i.sky)", Shader("BlockAtlasTransparent.shader"));
    }

    [Theory]
    [InlineData("rain", "snow", "snow")]
    [InlineData("drizzle", "sleet", "sleet")]
    [InlineData("rain", "hail", "hail")]
    [InlineData("rain", "sandstorm", "sandstorm")]
    [InlineData("rain", "ash", "ash")]
    [InlineData("rain", "rain", "rain")]
    [InlineData("rain", "none", "rain")]
    [InlineData("storm", "snow", "storm")]       // a storm names itself — its wind and lightning are the news
    [InlineData("blizzard", "snow", "blizzard")]
    [InlineData("ion_storm", "snow", "ion_storm")]
    public void TheWeatherChip_NamesWhatARainStateActuallyDrops(string state, string precipitation, string expected)
        => Assert.Equal(expected, WeatherLook.ChipState(state, precipitation));

    [Fact]
    public void TheHud_UsesTheChipState_AndEveryFormHasANameInEnglishAndGerman()
    {
        Assert.Contains("WeatherLook.ChipState(state, env.Precipitation)", Script("HudUi.TimeOfDay.cs"));
        foreach (var code in new[] { "en", "de" })
        {
            using var doc = JsonDocument.Parse(Read("data", "locales", code + ".json"));
            foreach (var form in WeatherLook.ChipPrecipitations)
            {
                Assert.True(doc.RootElement.TryGetProperty("weather." + form, out var name) && name.GetString()!.Length > 0,
                    $"{code}.json lacks weather.{form}");
            }
        }
    }
}
