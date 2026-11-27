// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4046 part b, guarded as source text (scripts and shaders cannot load headless): the near drawers and the ground detail
/// draw from instance buffers on the graphics card. A buffer draw selects each pass's procedural variant, so EVERY pass of
/// such a shader that takes instances must name its procedural function and have one — a pass without would draw all
/// instances of the batch at the world's origin, and only in that pass (a hole in the depth prepass, a shadow in the
/// wrong place). The vectors a drawer uploads must arrive in the order the shader hands them to its properties, the
/// two sides must agree on the layout of an instance, the arrays must stay as the fallback, and a buffer must go when its
/// chunk goes.
/// </summary>
public sealed class GpuInstancesWiringTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    private static string Shader(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Shaders", file));

    private static List<Match> All(string text, string pattern, RegexOptions options = RegexOptions.None)
        => Regex.Matches(text, pattern, options | RegexOptions.ExplicitCapture, RegexTimeout).ToList();

    /// <summary>The <c>v</c> group of every match.</summary>
    private static List<string> Values(string text, string pattern, RegexOptions options = RegexOptions.None)
        => All(text, pattern, options).Select(m => m.Groups["v"].Value).ToList();

    /// <summary>The shaders drawn with instances, the file that holds their procedural function when it is not the shader
    /// itself, the function, and the instanced properties in the order the function must fill them (a, b, c, d).</summary>
    public static TheoryData<string, string, string, string[]> InstancedShaders() => new()
    {
        { "BlockAtlas.shader", "BlockAtlas.shader", "BbtsCrownInstance", new[] { "_CcTile", "_CcTint", "_CcLight", "_CcLightDir" } },
        { "PlantBody.shader", "PlantBody.shader", "BbtsPlantBodyInstance", new[] { "_PbTile", "_PbTint", "_PbLight", "_PbAccent" } },
        { "GrassClump.shader", "GrassClump.shader", "BbtsGrassClumpInstance", new[] { "_GcTile", "_GcLight", "_GcAccent" } },
        { "GrassShell.shader", "GrassShellCommon.hlsl", "BbtsGrassShellInstance", new[] { "_ShellTile", "_ShellInfo", "_ShellLight" } },
        { "MeadowFlower.shader", "MeadowFlowerCommon.hlsl", "BbtsMeadowFlowerInstance", new[] { "_FlowerColor", "_FlowerInfo" } },
        { "ScatterLit.shader", "ScatterLit.shader", "BbtsScatterInstance", new[] { "_ScatterLight" } },
    };

    [Theory]
    [MemberData(nameof(InstancedShaders))]
    public void EveryInstancedPass_NamesItsProceduralFunction_AndHasIt(string shaderFile, string functionFile, string function, string[] properties)
    {
        string shader = Shader(shaderFile).Replace("\r\n", "\n", StringComparison.Ordinal);
        string shared = string.Concat(Values(shader, @"HLSLINCLUDE(?<v>.*?)ENDHLSL", RegexOptions.Singleline));
        string external = functionFile == shaderFile ? string.Empty : Shader(functionFile).Replace("\r\n", "\n", StringComparison.Ordinal);
        int instanced = 0;
        foreach (string program in Values(shader, @"HLSLPROGRAM(?<v>.*?)ENDHLSL", RegexOptions.Singleline))
        {
            if (!program.Contains("#pragma multi_compile_instancing", StringComparison.Ordinal))
            {
                continue;
            }

            instanced++;
            Assert.Contains($"#pragma instancing_options procedural:{function}\n", program, StringComparison.Ordinal);

            // The function is in the pass, in the shader's shared block, or in the common file the pass includes.
            string reach = program + shared + (program.Contains($"#include \"{functionFile}\"", StringComparison.Ordinal) ? external : string.Empty);
            Assert.Single(All(reach, $@"void {function}\(\)"));
            Assert.Contains("#include \"GpuInstances.hlsl\"", reach, StringComparison.Ordinal);
            Assert.Contains("BBTS_GPU_INSTANCE(inst)", reach, StringComparison.Ordinal);
        }

        Assert.True(instanced >= 2, $"{shaderFile}: only {instanced} instanced passes found");

        // Where the properties are declared, the function fills them in the drawer's order: a, b, c, d. (A depth or shadow
        // pass that reads no instanced property has a function that sets the matrices only.)
        var bodies = Values(functionFile == shaderFile ? shader : external, $@"void {function}\(\)\s*\{{(?<v>.*?)\n\s*\}}", RegexOptions.Singleline);
        Assert.NotEmpty(bodies);
        string body = bodies.OrderByDescending(b => All(b, @"= inst\.").Count).First();
        string[] slots = { "a", "b", "c", "d" };
        for (int i = 0; i < properties.Length; i++)
        {
            Assert.Contains($"{properties[i]} = inst.{slots[i]};", body, StringComparison.Ordinal);
        }

        Assert.Equal(properties.Length, All(body, @"= inst\.[abcd];").Count);
    }

    [Theory]
    [InlineData("CrownClusters.cs", "Upload(_matrices, _tile, _tint, _light, _lightDir)", new[] { "TileId, _tile", "TintId, _tint", "LightId, _light", "LightDirId, _lightDir" })]
    [InlineData("PlantBodies.cs", "Upload(_matrices, _tile, _tint, _light, _accent)", new[] { "TileId, _tile", "TintId, _tint", "LightId, _light", "AccentId, _accent" })]
    [InlineData("GrassClumps.cs", "Upload(_matrices, _tile, _light, _accent)", new[] { "TileId, _tile", "LightId, _light", "AccentId, _accent" })]
    [InlineData("GrassShells.cs", "Upload(_matrices, Count, A, B, cId.HasValue ? C : null)", new[] { "aId, A", "bId, B", "cId.Value, C" })]
    [InlineData("GroundScatter.cs", "Upload(_tufts, _tufts.Length, lights)", new[] { "ScatterLightId, lights" })]
    public void EveryDrawer_UploadsOnce_KeepsItsArrays_AndGivesTheBufferBack(string file, string upload, string[] arrays)
    {
        string script = Script(file);
        Assert.Contains(upload, script, StringComparison.Ordinal);
        Assert.Contains("GpuInstances.Active", script, StringComparison.Ordinal);
        Assert.Contains("_gpu.Draw(", script, StringComparison.Ordinal);

        // The arrays stay, in the same order as the upload: the fallback, and what the buffer is checked against.
        Assert.Contains("Graphics.DrawMeshInstanced(", script, StringComparison.Ordinal);
        int at = 0;
        foreach (string array in arrays)
        {
            int found = script.IndexOf($"SetVectorArray({array})", at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"{file}: SetVectorArray({array}) missing or out of order");
            at = found;
        }

        // A buffer is not garbage collected: it goes when the chunk's component is destroyed.
        int destroy = script.IndexOf("private void OnDestroy()", StringComparison.Ordinal);
        Assert.True(destroy >= 0, $"{file}: no OnDestroy");
        Assert.Contains("Release", script.Substring(destroy), StringComparison.Ordinal);
    }

    [Fact]
    public void TheShaderAndTheScript_AgreeOnAnInstance()
    {
        string include = Shader("GpuInstances.hlsl");
        string fields = Values(include, @"struct BbtsGpuInstance\s*\{(?<v>.*?)\};", RegexOptions.Singleline).Single();
        var declared = All(fields, @"(?<type>float4x4|float4)\s+(?<name>\w+);").Select(m => (Type: m.Groups["type"].Value, Name: m.Groups["name"].Value)).ToArray();
        Assert.Equal(new[] { "objectToWorld", "worldToObject", "a", "b", "c", "d" }, declared.Select(f => f.Name));
        int bytes = declared.Sum(f => f.Type == "float4x4" ? 64 : 16);

        string script = Script("GpuInstances.cs").Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"public const int Stride = {bytes};", script, StringComparison.Ordinal);
        string managed = Values(script, @"private struct Instance\s*\{(?<v>.*?)\n        \}", RegexOptions.Singleline).Single();
        Assert.Equal(new[] { "ObjectToWorld", "WorldToObject", "A", "B", "C", "D" }, Values(managed, @"public (Matrix4x4|Vector4) (?<v>\w+);"));
        Assert.Contains("[StructLayout(LayoutKind.Sequential)]", script, StringComparison.Ordinal);

        // Desktop graphics APIs only; the browser's targets compile the procedural variant to an empty function.
        Assert.Contains("defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && (defined(SHADER_API_D3D11)", include, StringComparison.Ordinal);
        Assert.DoesNotContain("SHADER_API_GLES3)", include.Substring(include.IndexOf("#ifndef", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("RuntimePlatform.WebGLPlayer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCrownVariant_IsBehindBothInstancingPaths()
    {
        // BlockAtlas draws the chunk meshes too; what the crown clusters add sits behind the instancing variants. A gate
        // on the array variant alone would leave the buffer draw with the chunk meshes' code.
        string shader = Shader("BlockAtlas.shader");
        Assert.Empty(All(shader, @"#if !?defined\(UNITY_INSTANCING_ENABLED\)\s*\n"));
        Assert.True(All(shader, @"#if defined\(UNITY_INSTANCING_ENABLED\) \|\| defined\(UNITY_PROCEDURAL_INSTANCING_ENABLED\)").Count >= 10);
    }

    [Fact]
    public void TheProbe_CanRunTheOldWay_AndComparesTheTwo()
    {
        Assert.Contains("key == \"instances\"", Script("PerfProbe.Features.cs"), StringComparison.Ordinal);
        Assert.Contains("CompareInstancePaths()", Script("PerfProbe.cs"), StringComparison.Ordinal);
        Assert.Contains("-instanceArrays", Script("GpuInstances.cs"), StringComparison.Ordinal);
    }
}
