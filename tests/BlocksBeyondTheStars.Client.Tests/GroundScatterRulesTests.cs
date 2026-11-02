// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The ground details after a tester's six reports on v2026.11.2 (the first build that drew them): tufts only on natural
/// soft ground (#3120, no pebbles), never on built cells (#3118, the server's edit runs reach ClientWorld), only under the
/// open sky (#3119), lit like the ground (#3120) and switchable (#3121). The Unity side is guarded as source text.
/// </summary>
public sealed class GroundScatterRulesTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot() }.Concat(parts).ToArray()));

    private static string Script(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Scripts", file);

    private static string Shader(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Shaders", file);

    [Theory]
    [InlineData("grass", GroundScatterRules.Tuft)]
    [InlineData("dirt", GroundScatterRules.Tuft)]
    [InlineData("mud", GroundScatterRules.Tuft)]
    [InlineData("sand", GroundScatterRules.None)]
    [InlineData("snow", GroundScatterRules.None)]
    [InlineData("stone", GroundScatterRules.None)]
    [InlineData("basalt", GroundScatterRules.None)]
    [InlineData("concrete", GroundScatterRules.None)]
    [InlineData(null, GroundScatterRules.None)]
    public void OnlySoftNaturalGround_GrowsTufts_ThePebblesAreGone(string? key, int expected)
        => Assert.Equal(expected, GroundScatterRules.TypeOf(key));

    [Theory]
    [InlineData(1f, false, true)]     // open sky, natural ground
    [InlineData(1f, true, false)]     // a built cell (#3118)
    [InlineData(0.55f, false, false)] // the shade floor up to six blocks under a roof (#3119)
    [InlineData(0.96f, false, false)] // a tunnel mouth: 24 of 25 columns open, its own covered
    [InlineData(0f, false, false)]    // a deep cave
    public void Accepts_OnlyNaturalCellsUnderTheOpenSky(float skylight, bool edited, bool expected)
        => Assert.Equal(expected, GroundScatterRules.Accepts(skylight, edited));

    private static ClientWorld World(ushort fill)
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        var blocks = new ushort[WorldConstants.BlocksPerChunk];
        Array.Fill(blocks, fill);
        var built = WorldConstants.LocalIndex(4, 5, 6);
        w.StoreChunk(new ChunkCoord(0, 0, 0), blocks, editRuns: new ushort[] { (ushort)built, 2 });
        return w;
    }

    [Fact]
    public void ClientWorld_RestoresTheEditRuns_AndMarksEveryLiveChange()
    {
        var world = World(3);
        Assert.True(world.TryGetChunk(new ChunkCoord(0, 0, 0), out var chunk));
        Assert.True(chunk.IsEditedLocal(WorldConstants.LocalIndex(4, 5, 6)));
        Assert.True(chunk.IsEditedLocal(WorldConstants.LocalIndex(5, 5, 6)));
        Assert.False(chunk.IsEditedLocal(WorldConstants.LocalIndex(6, 5, 6)));

        Assert.True(world.ApplyBlockChange(9, 9, 9, 7, out _)); // a placed block (or fallen snow)
        Assert.True(chunk.IsEditedLocal(WorldConstants.LocalIndex(9, 9, 9)));

        Assert.True(world.ApplyBlockChange(4, 5, 6, BlockId.AirValue, out _)); // mined
        Assert.False(chunk.IsEditedLocal(WorldConstants.LocalIndex(4, 5, 6)));
    }

    [Fact]
    public void TheEditRuns_TravelFromTheChunkMessage_ToTheMesher()
    {
        Assert.Contains("m.ShapeIndex, m.ShapeData, m.EditRuns);", Script("GameBootstrap.ChunkStreaming.cs"));
        Assert.Contains("copy.CopyEditsFrom(src);", Script("GameBootstrap.ChunkStreaming.cs")); // the worker's chunk copy
        Assert.Contains("chunk.IsEditedLocal(WorldConstants.LocalIndex(x, y, z)));", Script("ChunkMesher.cs"));
        Assert.Contains("GroundScatterRules.Accepts(skylight, edited)", Script("ChunkMesher.HighDetails.cs"));
        Assert.DoesNotContain("ScatterMinSkylight", Script("ChunkMesher.HighDetails.cs"));
    }

    [Fact]
    public void Tufts_AreLitLikeTheGround_AndThePebbleMeshIsGone()
    {
        string scatter = Script("GroundScatter.cs");
        Assert.DoesNotContain("BuildPebble", scatter);
        Assert.Contains("_props.SetVectorArray(ScatterLightId, lights);", scatter);
        Assert.Contains("data.ScatterLight.Add(", Script("ChunkMesher.HighDetails.cs"));
        Assert.Contains("scatter.Setup(data.Scatter, data.ScatterLight);", Script("GameBootstrap.Meadow.cs"));

        string shader = Shader("ScatterLit.shader");
        Assert.Contains("#include \"MeadowLight.hlsl\"", shader);
        Assert.Contains("BbtsMeadowShade(ScatterBase(i.color), i.wp, i.light.a, i.light.rgb,", shader);
        Assert.Equal(2, shader.Split("UNITY_DEFINE_INSTANCED_PROP(float4, _ScatterLight)").Length - 1); // both pipelines
    }

    [Fact]
    public void TheGroundDetailsSwitch_IsSaved_AppliedLive_AndLocalized()
    {
        Assert.Contains("public bool GroundDetails = true;", Script("ClientSettings.cs"));
        Assert.Contains("GroundScatter.Enabled = GroundDetails;", Script("ClientSettings.cs"));
        Assert.Contains("L(\"ui.settings.ground_details\"), S.GroundDetails", Script("UiSettings.cs"));
        Assert.Contains("\"ui.settings.ground_details\":", Read("data", "locales", "en.json"));
        Assert.Contains("\"ui.settings.ground_details\":", Read("data", "locales", "de.json"));
    }
}
