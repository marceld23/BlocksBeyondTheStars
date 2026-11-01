// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The ground detail's Unity wiring, guarded as source text (the scripts and shaders cannot load headless): the tufts take the
/// sun's shadow and sit in the depth prepass (#3063), the High preset's meadow is drawn by GrassShells with its own shaders,
/// ranges that agree between C# and HLSL, and no keyword beyond the block shader's shadow pair (#3056).
/// </summary>
public sealed class MeadowWiringTests
{
    private static readonly string[] AllowedKeywords =
    {
        "_", "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_SHADOWS_SOFT",
    };

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    private static string Shader(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Shaders", file));

    /// <summary>The URP SubShader of a dual-pipeline shader (everything before the Built-in RP SubShader).</summary>
    private static string UrpPart(string shader)
    {
        int builtIn = shader.IndexOf("// ---------------- Built-in RP", StringComparison.Ordinal);
        return builtIn < 0 ? shader : shader.Substring(0, builtIn);
    }

    [Fact]
    public void Tufts_ReceiveTheSunShadow_AndJoinTheDepthPrepass()
    {
        string urp = UrpPart(Shader("ScatterLit.shader"));
        Assert.Contains("MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp))", urp);
        Assert.Contains("GetMainLightShadowFade(i.wp)", urp);
        Assert.Contains("\"LightMode\" = \"DepthOnly\"", urp);
        Assert.DoesNotContain("ShadowCaster", urp); // tufts stay non-casters
        Assert.Contains("ShadowCastingMode.Off", Script("GroundScatter.cs"));
    }

    [Theory]
    [InlineData("GrassShell.shader")]
    [InlineData("MeadowFlower.shader")]
    public void MeadowShaders_HaveADepthPass_NoCaster_AndTheBlockShadersShadow(string file)
    {
        string shader = Shader(file);
        Assert.Contains("\"LightMode\" = \"UniversalForward\"", shader);
        Assert.Contains("\"LightMode\" = \"DepthOnly\"", shader);
        Assert.DoesNotContain("ShadowCaster", shader);
        Assert.Contains("#pragma multi_compile_instancing", shader);
        Assert.Contains("#include \"MeadowLight.hlsl\"", shader);
        Assert.Contains("MainLightRealtimeShadow(TransformWorldToShadowCoord(wp))", Shader("MeadowLight.hlsl"));
    }

    [Theory]
    [InlineData("ScatterLit.shader")]
    [InlineData("GrassShell.shader")]
    [InlineData("MeadowFlower.shader")]
    public void GroundDetailShaders_AddNoNewKeywords(string file)
    {
        var lines = Regex.Matches(Shader(file), @"#pragma\s+multi_compile(?:_fragment|_vertex)?\s+(?<kw>[^\r\n]+)",
            RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));
        foreach (Match line in lines)
        {
            foreach (string keyword in line.Groups["kw"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Contains(keyword, AllowedKeywords);
            }
        }
    }

    [Fact]
    public void TheCarpet_SamplesTheAtlasAtAnExplicitLod()
    {
        string shader = Shader("GrassShell.shader");
        Assert.Contains("SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex", shader);
        Assert.DoesNotContain("SAMPLE_TEXTURE2D(_MainTex", shader);
    }

    [Fact]
    public void Ranges_AgreeBetweenCSharpAndHlsl()
    {
        string shells = Script("GrassShells.cs");
        Assert.Contains("public const float CarpetRange = 16f;", shells); // #3078: was 20
        Assert.Contains("public const float FlowerRange = 28f;", shells);
        Assert.Contains("#define BBTS_SHELL_FADE_END 16.0", Shader("GrassShellCommon.hlsl"));
        Assert.Contains("#define BBTS_FLOWER_FADE_END 28.0", Shader("MeadowFlowerCommon.hlsl"));
    }

    [Fact]
    public void ChunkUploads_BuildTheGroundDetail_AndTheWorldSetsTheFlowerColours()
    {
        Assert.Contains("ApplyGroundDetail(view.Go, coord, data);", Script("GameBootstrap.ChunkStreaming.cs"));
        string meadow = Script("GameBootstrap.Meadow.cs");
        Assert.Contains("shells.Setup(data.GrassTops,", meadow);
        Assert.Contains("HighDetails.DesktopClass", meadow); // the browser never gets a meadow component
        Assert.Contains("FloraTints.For(_worldSeed, LocationName, keys[i])", meadow);
        Assert.Contains("ApplyMeadowPalette();", Script("GameBootstrap.Environment.cs"));
        Assert.Contains("HighDetails.Changed += OnHighDetailsChanged;", Script("GrassShells.cs"));
    }
}
