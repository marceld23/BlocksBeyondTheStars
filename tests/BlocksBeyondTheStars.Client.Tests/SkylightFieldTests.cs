// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The chunk mesher's skylight (#1608, #3265) on hand-built scenes: a tree crown and a one-block roof keep the shade floor,
/// two blocks of cover halve it, a tunnel dug twenty blocks into a hill is dark, its mouth fades in over eight blocks, and
/// a deep 1×1 shaft's floor is dim instead of open sky. Ground is stone up to y = 3 in every scene; the chunk sits at the
/// origin, so the cells around x, z = 8 are its middle.
/// </summary>
public sealed class SkylightFieldTests
{
    private static readonly BlockId Stone = new BlockId(1);
    private static readonly BlockId Leaves = new BlockId(2);
    private static readonly BlockId Water = new BlockId(3);
    private const int Ground = 3;

    /// <summary>A world of stone ground plus the cells set or dug by the test.</summary>
    private sealed class Scene
    {
        private readonly Dictionary<(int, int, int), BlockId> _cells = new Dictionary<(int, int, int), BlockId>();

        public void Fill(int x0, int x1, int y0, int y1, int z0, int z1, BlockId id)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        _cells[(x, y, z)] = id;
                    }
                }
            }
        }

        public BlockId Block(int x, int y, int z)
            => _cells.TryGetValue((x, y, z), out var b) ? b : y <= Ground && y > -40 ? Stone : BlockId.Air;

        public SkylightField Field()
        {
            var field = new SkylightField(WorldConstants.ChunkSize);
            field.Begin(Block, b => b.Value == Stone.Value, 0, 0, 0);
            return field;
        }
    }

    [Fact]
    public void OpenGround_SeesTheFullSky()
        => Assert.Equal(1f, new Scene().Field().At(8, Ground + 1, 8));

    [Fact]
    public void UnderWater_ACellBelowALogPillar_KeepsLightFromTheLakeAroundIt()
    {
        // #3478 (Lyxette): logs standing on a lake covered the water cells under them with three light-stopping blocks, and
        // under water no column counts as open sky — the faces looking into those cells drew black among the lit lake floor.
        var scene = new Scene();
        scene.Fill(-30, 46, Ground + 1, Ground + 3, -30, 46, Water); // a lake three deep
        scene.Fill(8, 8, Ground + 4, Ground + 6, 8, 8, Stone);       // three logs standing on it
        var field = scene.Field();

        Assert.Equal(SkylightRules.ShadeFloor, field.At(9, Ground + 1, 8), 4); // the lake floor beside it: as before
        Assert.Equal(SkylightRules.ShadeFloor * SkylightRules.ShadeFloor, field.At(8, Ground + 1, 8), 4); // shade, not black
    }

    [Fact]
    public void UnderWater_TheLakeFloorIsUnchanged()
    {
        var scene = new Scene();
        scene.Fill(-30, 46, Ground + 1, Ground + 3, -30, 46, Water);
        Assert.Equal(SkylightRules.ShadeFloor, scene.Field().At(8, Ground + 1, 8), 4);
    }

    [Fact]
    public void ATreeCrown_KeepsTheShadeFloor()
    {
        var scene = new Scene();
        scene.Fill(-30, 46, 8, 9, -30, 46, Leaves); // a closed canopy two leaves thick, five blocks overhead

        Assert.Equal(SkylightRules.ShadeFloor, scene.Field().At(8, Ground + 1, 8), 4);
    }

    [Fact]
    public void AOneBlockRoof_KeepsTheShadeFloor()
    {
        var scene = new Scene();
        scene.Fill(-30, 46, 7, 7, -30, 46, Stone);

        Assert.Equal(SkylightRules.ShadeFloor, scene.Field().At(8, Ground + 1, 8), 4);
    }

    [Fact]
    public void TwoBlocksOfCover_HalveTheShadeFloor()
    {
        var scene = new Scene();
        scene.Fill(-30, 46, 7, 8, -30, 46, Stone);

        Assert.Equal(SkylightRules.ShadeFloor * 0.5f, scene.Field().At(8, Ground + 1, 8), 4);
    }

    [Fact]
    public void ThreeBlocksOfCover_WithNoOpeningNear_AreDark()
    {
        var scene = new Scene();
        scene.Fill(-30, 46, 7, 9, -30, 46, Stone);

        Assert.Equal(0f, scene.Field().At(8, Ground + 1, 8), 4);
    }

    /// <summary>A hill (stone up to y = 9) east of x = 0 with a 1×2 tunnel dug east along z = 8 at y = 4..5: four blocks
    /// of cap over the tunnel, open ground west of the mouth.</summary>
    private static Scene TunnelHill()
    {
        var scene = new Scene();
        scene.Fill(0, 60, Ground + 1, 9, -30, 46, Stone);
        scene.Fill(0, 40, Ground + 1, Ground + 2, 8, 8, BlockId.Air);
        return scene;
    }

    [Fact]
    public void ATunnelTwentyBlocksIntoAFourBlockCap_IsDark()
    {
        var field = TunnelHill().Field();

        Assert.Equal(0f, field.At(20, Ground + 1, 8), 4);
        Assert.Equal(0f, field.At(20, Ground + 2, 8), 4);
    }

    [Fact]
    public void ATunnelMouth_FadesFromTheShadeFloorToDarkWithinEightBlocks()
    {
        var field = TunnelHill().Field();
        var values = new List<float>();
        for (int x = 0; x <= 9; x++)
        {
            values.Add(field.At(x, Ground + 1, 8));
        }

        Assert.Equal(SkylightRules.ShadeFloor, values[0], 4); // the first block under the cap: the mouth's shade
        Assert.Equal(SkylightRules.ShadeFloor, values[1], 4);
        for (int i = 2; i <= 7; i++)
        {
            Assert.True(values[i] < values[i - 1], $"no falloff at x = {i}: {string.Join(", ", values)}");
        }

        Assert.Equal(0f, values[7], 4); // eight blocks from the open column at x = -1
        Assert.Equal(0f, values[9], 4);
    }

    [Fact]
    public void ADeepNarrowShaft_HasADimFloor_AndTheTunnelAtItsFootIsNoBrighter()
    {
        var scene = new Scene();
        scene.Fill(8, 8, -6, Ground, 8, 8, BlockId.Air);   // a 1×1 shaft ten blocks deep
        scene.Fill(9, 20, -6, -5, 8, 8, BlockId.Air);      // a tunnel branching off its foot
        var field = scene.Field();

        float floor = field.At(8, -6, 8);
        Assert.Equal(SkylightRules.WellFloor, floor, 4);
        Assert.Equal(1f, field.At(8, Ground, 8), 4);        // the top block of the shaft is level with the ground
        Assert.True(field.At(8, -2, 8) < field.At(8, 1, 8)); // darker the deeper

        float foot = field.At(9, -6, 8);
        Assert.True(foot > 0f && foot <= floor, $"tunnel at the shaft's foot {foot} vs shaft floor {floor}");
        Assert.Equal(0f, field.At(18, -6, 8), 4);
    }

    [Fact]
    public void AOneBlockHole_AndGroundBesideAWall_StayFullyLit()
    {
        var scene = new Scene();
        scene.Fill(8, 8, Ground, Ground, 8, 8, BlockId.Air);   // one block dug out of flat ground
        scene.Fill(12, 14, Ground + 1, 20, -30, 46, Stone);    // a tall wall further east
        var field = scene.Field();

        Assert.Equal(1f, field.At(8, Ground, 8));
        Assert.Equal(1f, field.At(11, Ground + 1, 8));
    }

    [Fact]
    public void Begin_StartsAFreshBuild()
    {
        var scene = new Scene();
        var field = scene.Field();
        Assert.Equal(1f, field.At(8, Ground + 1, 8));

        scene.Fill(-30, 46, 7, 9, -30, 46, Stone);
        Assert.Equal(1f, field.At(8, Ground + 1, 8)); // cached for this build
        field.Begin(scene.Block, b => b.Value == Stone.Value, 0, 0, 0);
        Assert.Equal(0f, field.At(8, Ground + 1, 8), 4);
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(1, 1f)]
    [InlineData(2, 0.5f)]
    [InlineData(3, 0f)]
    [InlineData(9, 0f)]
    public void ThinCover_CountsTheLightStoppingBlocksOverhead(int blocks, float expected)
        => Assert.Equal(expected, SkylightRules.ThinCover(blocks));

    [Theory]
    [InlineData(1, 1f)]
    [InlineData(2, 1f)]
    [InlineData(5, 0.5f)]
    [InlineData(8, 0f)]
    [InlineData(int.MaxValue, 0f)]
    public void NearOpening_FadesLinearlyFromTwoToEightBlocks(int distance, float expected)
        => Assert.Equal(expected, SkylightRules.NearOpening(distance), 4);

    [Fact]
    public void WellLight_FallsFromFullSkyToTheWellFloor()
    {
        Assert.Equal(1f, SkylightRules.WellLight(1));
        Assert.Equal(SkylightRules.ShadeFloor, SkylightRules.WellLight(3), 4);
        Assert.Equal(SkylightRules.WellFloor, SkylightRules.WellLight(SkylightRules.WellDepthNone), 4);
        for (int d = 2; d <= SkylightRules.WellDepthNone; d++)
        {
            Assert.True(SkylightRules.WellLight(d) < SkylightRules.WellLight(d - 1));
        }
    }
}
