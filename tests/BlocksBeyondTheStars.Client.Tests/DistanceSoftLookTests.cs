// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3461/#3462: the distance softness — the block textures fade toward their tile's average colour inside the haze, and
/// on Medium+ desktop clients a slight blur softens the far third of the haze. Both only where the world has air, both
/// barely noticeable (<see cref="DistanceSoftLook"/>); the shader side is pinned as source text (it cannot run headless).
/// </summary>
public sealed class DistanceSoftLookTests
{
    private const float Thin = 0.2f, Thick = 0.8f;

    [Fact]
    public void AnAirlessWorld_StaysSharp()
    {
        // Moons, asteroids and the void arrive with density 0 — and so does the crystal world, which keeps a haze.
        Assert.Equal(0f, DistanceSoftLook.TextureFade(0f, hazeOn: true));
        Assert.Equal(0f, DistanceSoftLook.BlurBlend(0f, enabled: true, hazeOn: true, reducedEffects: false));
        Assert.False(DistanceSoftLook.HasAir(0f));
        Assert.True(DistanceSoftLook.HasAir(Thin));
    }

    [Fact]
    public void ThickAir_SoftensMoreThanThinAir_AndBothStayBarelyNoticeable()
    {
        float thinFade = DistanceSoftLook.TextureFade(Thin, true), thickFade = DistanceSoftLook.TextureFade(Thick, true);
        Assert.True(thinFade > 0f && thinFade < thickFade);
        Assert.InRange(thickFade, DistanceSoftLook.TextureFadeThin, DistanceSoftLook.TextureFadeThick);

        float thinBlur = DistanceSoftLook.BlurBlend(Thin, true, true, false), thickBlur = DistanceSoftLook.BlurBlend(Thick, true, true, false);
        Assert.True(thinBlur > 0f && thinBlur < thickBlur);
        Assert.InRange(thickBlur, DistanceSoftLook.BlurBlendThin, DistanceSoftLook.BlurBlendThick);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(0.001f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(5f)]
    public void TheCapsHold_ForAnyDensity(float density)
    {
        Assert.InRange(DistanceSoftLook.TextureFade(density, true), 0f, 0.35f);
        Assert.InRange(DistanceSoftLook.BlurBlend(density, true, true, false), 0f, 0.5f);
    }

    [Fact]
    public void EveryOffReason_TurnsItOff()
    {
        Assert.Equal(0f, DistanceSoftLook.TextureFade(Thick, hazeOn: false));
        Assert.Equal(0f, DistanceSoftLook.BlurBlend(Thick, enabled: false, hazeOn: true, reducedEffects: false));
        Assert.Equal(0f, DistanceSoftLook.BlurBlend(Thick, enabled: true, hazeOn: false, reducedEffects: false));
        Assert.Equal(0f, DistanceSoftLook.BlurBlend(Thick, enabled: true, hazeOn: true, reducedEffects: true));
    }

    [Fact]
    public void TheBlur_RunsOnlyOnMediumAndBetter_AndNeverInTheBrowser()
    {
        Assert.False(DistanceSoftLook.BlurAllowed(0, false)); // Potato: no depth texture
        Assert.False(DistanceSoftLook.BlurAllowed(1, false)); // Low
        Assert.True(DistanceSoftLook.BlurAllowed(2, false));
        Assert.True(DistanceSoftLook.BlurAllowed(3, false));
        Assert.False(DistanceSoftLook.BlurAllowed(3, true));  // WebGL/WebGPU, whatever the preset
    }

    [Fact]
    public void TheBand_LiesInsideTheHazeRange_AndIsFullAtItsEnd()
    {
        const float start = 120f, end = 300f;
        float band = DistanceSoftLook.BandStart(start, end);
        Assert.True(band > start && band < end);
        Assert.Equal(start + (DistanceSoftLook.BandStartShare * (end - start)), band, 3);

        Assert.Equal(0f, DistanceSoftLook.BandWeight(50f, band, end));
        Assert.Equal(0f, DistanceSoftLook.BandWeight(band, band, end));
        Assert.Equal(1f, DistanceSoftLook.BandWeight(end, band, end));
        Assert.Equal(1f, DistanceSoftLook.BandWeight(end + 500f, band, end)); // the far terrain beyond the haze end

        float last = 0f;
        for (float d = band; d <= end; d += 5f)
        {
            float w = DistanceSoftLook.BandWeight(d, band, end);
            Assert.True(w >= last, $"the band weight fell at {d} m");
            last = w;
        }
    }

    [Fact]
    public void AFogPeaSouper_PullsTheBandIn_WithTheHaze()
    {
        // Fog weather crushes the haze range; the band follows it without a weather rule of its own.
        float clear = DistanceSoftLook.BandStart(200f, 400f);
        float fog = DistanceSoftLook.BandStart(30f, 60f);
        Assert.True(fog < clear);
        Assert.Equal(60f, DistanceSoftLook.BandStart(80f, 60f)); // degenerate range: at the end
    }

    [Theory]
    [InlineData(64, 0, 6)] // the 64-px tiles in the full 2048² atlas: one texel per tile at mip 6
    [InlineData(64, 1, 5)] // Potato's global mip limit halves the atlas: the same texel is its mip 5
    [InlineData(64, 9, 0)] // never below 0
    [InlineData(32, 0, 5)]
    public void TheTileMean_IsTheMipWithOneTexelPerTile(int tile, int mipLimit, int mip)
    {
        Assert.Equal(mip, DistanceSoftLook.TileMeanMip(tile, mipLimit));
    }

    [Fact]
    public void TheShaders_ReadTheGlobal_AndKeepTheSkySharp()
    {
        string common = ShaderText("AtmosphereCommon.hlsl");
        Assert.Contains("float4 _Sc_DistSoft;", common, StringComparison.Ordinal);
        Assert.Contains("float BbtsDistSoftTexture(float3 wp)", common, StringComparison.Ordinal);

        // A: the tile's average colour from the mip Sky sends, the alpha (the leaves' cutout) untouched.
        string atlas = ShaderText("BlockAtlas.shader");
        Assert.Contains("float soft = BbtsDistSoftTexture(i.wp);", atlas, StringComparison.Ordinal);
        Assert.Contains("SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, cellUv, _Sc_DistSoft.w)", atlas, StringComparison.Ordinal);
        Assert.Contains("texel.rgb = lerp(texel.rgb, meanRgb, soft);", atlas, StringComparison.Ordinal);

        // B: the sky bodies mark themselves in the stencil, the composite skips them and the far plane.
        string bodies = ShaderText("SkyBodyPhase.shader");
        Assert.Contains("Ref 64", bodies, StringComparison.Ordinal);
        Assert.Contains("Pass Replace", bodies, StringComparison.Ordinal);
        string soft = ShaderText("DistanceSoft.shader");
        Assert.Contains("Shader \"BlocksBeyondTheStars/DistanceSoft\"", soft, StringComparison.Ordinal);
        Assert.Contains("Comp NotEqual", soft, StringComparison.Ordinal);
        Assert.Contains("if (DsIsSky(raw))", soft, StringComparison.Ordinal);
        // The composite's band weight is DistanceSoftLook.BandWeight (a smoothstep from the band start to the haze end).
        Assert.Contains("_Sc_DistSoft.y * smoothstep(_Sc_DistSoft.z, bandEnd, d0)", soft, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSkyPublishesTheGlobal_AndClearsItWithTheMist()
    {
        string mist = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "Sky.Mist.cs"));
        Assert.Contains("Shader.PropertyToID(\"_Sc_DistSoft\")", mist, StringComparison.Ordinal);
        Assert.Contains("DistanceSoftLook.TextureFade(", mist, StringComparison.Ordinal);
        Assert.Contains("DistanceSoftLook.BlurBlend(", mist, StringComparison.Ordinal);

        int clear = mist.IndexOf("private void ClearMist()", StringComparison.Ordinal);
        Assert.True(clear > 0, "ClearMist not found");
        int end = mist.IndexOf("RenderSettings.fog = false;", clear, StringComparison.Ordinal);
        Assert.Contains("Shader.SetGlobalVector(DistSoftId, Vector4.zero);", mist.Substring(clear, end - clear), StringComparison.Ordinal);
    }

    private static string ShaderText(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Shaders", file));
}
