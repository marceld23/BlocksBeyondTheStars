// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3335 and #3337 on the client: Ressaw is a liquid you swim in like water (the probe reads the block data), it takes its
/// own milky look everywhere the client draws a liquid (<see cref="LiquidLook"/> — the mesher's shader mode, the screen wash,
/// the underwater fog), and the Ressaw diving suit draws its own pieces. #3644: the Grand Technicians' red fluid shares the
/// look (in red) and nothing else. The Unity side is guarded as source text.
/// </summary>
public sealed class RessawLookTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot() }.Concat(parts).ToArray()));

    private static string Script(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Scripts", file);

    private static string Shader(string file) => Read("client", "Assets", "BlocksBeyondTheStars", "Shaders", file);

    private static int Index(int x, int y, int z) => (y * WorldConstants.ChunkSize + z) * WorldConstants.ChunkSize + x;

    [Theory]
    [InlineData("water", LiquidLookKind.Water)]
    [InlineData("oil", LiquidLookKind.Water)]
    [InlineData("gas", LiquidLookKind.Gas)]
    [InlineData("gas_dense", LiquidLookKind.DenseGas)]
    [InlineData("ressaw", LiquidLookKind.Ressaw)]
    [InlineData("gt_fluid", LiquidLookKind.RedFluid)]
    [InlineData(null, LiquidLookKind.Water)]
    public void EachLiquid_TakesItsLook(string? key, LiquidLookKind expected) => Assert.Equal(expected, LiquidLook.Of(key));

    [Theory]
    [InlineData("ressaw", true)]
    [InlineData("gt_fluid", true)]
    [InlineData("water", false)]
    [InlineData("oil", false)]
    [InlineData("gas", false)]
    [InlineData("gas_dense", false)]
    [InlineData(null, false)]
    public void OnlyRessawAndTheRedFluid_GlowStill(string? key, bool expected) => Assert.Equal(expected, LiquidLook.GlowsStill(key));

    [Fact]
    public void TheRedFluid_IsAStillGlowingDecoration_InTheShippedData()
    {
        // #3644: you swim through it like water, it glows and lights its surface like Ressaw — and it is decoration only:
        // never mined, never pumped (no drops), no item, and none of Ressaw's mechanics (the server keys those to "ressaw").
        var fluid = Content.GetBlock(LiquidLook.RedFluidKey);
        Assert.NotNull(fluid);
        Assert.True(fluid!.Liquid);
        Assert.False(fluid.Solid);
        Assert.False(fluid.Mineable);
        Assert.Empty(fluid.Drops);
        Assert.Null(fluid.RandomDrops);
        Assert.False(fluid.Flammable);
        Assert.True(fluid.Emission > 0f, "the red fluid glows");
        Assert.True(fluid.LightSurfaceOnly, "a tank lights from its surface, not its depth");
        Assert.Equal("grosstechniker", fluid.Civilization);
        Assert.DoesNotContain(Content.Items.Values, i => i.PlacesBlock == LiquidLook.RedFluidKey || i.Key == LiquidLook.RedFluidKey);
        Assert.Equal(LiquidLook.RessawKey, BlocksBeyondTheStars.Shared.Definitions.RessawCrackRules.LiquidKey);
    }

    [Fact]
    public void Ressaw_IsAStillGlowingLiquid_InTheShippedData()
    {
        var ressaw = Content.GetBlock(LiquidLook.RessawKey);
        Assert.NotNull(ressaw);
        Assert.True(ressaw!.Liquid);
        Assert.False(ressaw.Solid);
        Assert.True(ressaw.Emission > 0f, "Ressaw glows");
        Assert.NotNull(ressaw.LightColor);
        Assert.True(ressaw.LightSurfaceOnly, "a basin lights from its surface, not its depth");
    }

    [Fact]
    public void AHeadInRessaw_IsUnderTheLiquid_ForTheWashTheMuffleAndSwimming()
    {
        ushort ressaw = Content.GetBlock(LiquidLook.RessawKey)!.NumericId.Value;
        ushort stone = Content.GetBlock("stone")!.NumericId.Value;
        int n = WorldConstants.ChunkSize;
        var blocks = new ushort[n * n * n];
        for (int x = 2; x <= 6; x++)
        {
            for (int z = 2; z <= 6; z++)
            {
                blocks[Index(x, 1, z)] = stone;
                blocks[Index(x, 2, z)] = ressaw;
                blocks[Index(x, 3, z)] = ressaw;
            }
        }

        var world = new ClientWorld();
        world.StoreChunk(new ChunkCoord(0, 0, 0), blocks);
        var probe = new WaterProbe(world, Content);

        Assert.True(probe.IsWet(4, 3, 4), "a Ressaw cell");
        Assert.False(probe.IsWet(4, 4, 4), "the air above the basin");
        Assert.Contains("if (def.Liquid)", ClientTestPaths.PlayerControllerSources()); // the footing's liquid keys come from the data
    }

    [Fact]
    public void TheMesher_DrawsRessawSeeThrough_LikeAWaterSurface_InItsOwnShaderMode()
    {
        string mesher = Script("ChunkMesher.cs");
        // #3644: the red fluid takes every one of these steps through LiquidLook.GlowsStill.
        Assert.Contains("or \"phase_block_open\" or \"force_field_off\" || LiquidLook.GlowsStill(def?.Key);", mesher); // see-through
        Assert.Contains("if (key is \"water\" or \"gas\" or \"gas_dense\" || LiquidLook.GlowsStill(key)) f |= TraitWater;", mesher);
        Assert.Contains("if (isGas || (isWater && traits.GlowsStill(id)))", mesher);
        Assert.Contains("isGas ? (isDenseGas ? 6f : 5f) : MilkyLiquidMode", mesher);
        string still = Script("ChunkMesher.Ressaw.cs");
        Assert.Contains("public const float MilkyLiquidMode = 7f;", still);
        Assert.Contains("IdPlusOne(LiquidLook.RessawKey) | (IdPlusOne(LiquidLook.RedFluidKey) << 15)", still);
    }

    [Fact]
    public void TheTransparentShader_DrawsModeSeven_MilkyAndGlowing_InBothPipelines()
    {
        string shader = Shader("BlockAtlasTransparent.shader");
        // Mode 7 is tested BEFORE the dense gas (> 5.5), or Ressaw would be drawn as the dark gas.
        Assert.Equal(2, shader.Split("if (i.water.x > 6.5)").Length - 1);
        Assert.Equal(2, shader.Split("else if (i.water.x > 5.5)").Length - 1);
        Assert.True(shader.IndexOf("if (i.water.x > 6.5)", StringComparison.Ordinal)
                    < shader.IndexOf("else if (i.water.x > 5.5)", StringComparison.Ordinal));
        // The vertex waves are gated to the water modes, so the still liquid never bobs.
        Assert.Equal(2, shader.Split("(v.water.x > 0.5 && v.water.x < 3.5)").Length - 1);
    }

    [Fact]
    public void UnderRessaw_TheWashAndTheFog_AreMilkyWhite()
    {
        string weather = Script("WeatherFx.cs");
        Assert.Contains("LiquidLookKind.Ressaw => EyeIn.Ressaw,", weather);
        Assert.Contains("LiquidLookKind.RedFluid => EyeIn.RedFluid,", weather); // #3644: the same veil, in red
        Assert.Contains("bool glowing = eye is EyeIn.Ressaw or EyeIn.RedFluid;", weather);
        Assert.Contains("GUI.color = _veilRed ? new Color(", weather);
        Assert.Contains("_milkyWash > 0.01f", weather); // keeps the overlay alive while it fades
        Assert.Contains("DrawRessawWash(); // #3335", weather);

        string mist = Script("Sky.Mist.cs");
        Assert.Contains("_murkLook = LiquidLookAt(Camera.transform.position);", mist);
        Assert.Contains("bool glowing = LiquidLook.GlowsStill(_murkLook);", mist);
        Assert.Contains("Color water = glowing ? (_murkLook == LiquidLookKind.RedFluid ? RedFluidMurk : RessawMurk)", mist);
    }

    [Fact]
    public void TheRessawSuit_DrawsItsOwnPieces_BootsIncluded()
    {
        string sets = Script("PlayerAvatar.GearSets.cs");
        Assert.Contains("BuildRessawHelmet();", sets);
        Assert.Contains("BuildRessawChest();", sets);
        Assert.Contains("BuildRessawLegs();", sets);
        Assert.Contains("private bool BuildBootsSet(int style)", sets);
        Assert.Contains("!BuildBootsSet(GearLook.BootsStyle(mask))", Script("PlayerAvatar.cs"));
    }
}
