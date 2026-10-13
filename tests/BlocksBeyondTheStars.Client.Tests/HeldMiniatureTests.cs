// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2534: everything is held as a real miniature — a vehicle as its hull, a plant as its in-world look, every
/// other item without a model as its extruded icon. These pin the Unity-free halves: which item gets which miniature,
/// the shared vehicle designs, and the icon → voxel maths.</summary>
public sealed class HeldMiniatureTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static HeldItemShapes.Miniature MiniatureOf(string itemKey)
    {
        var item = Content.GetItem(itemKey);
        Assert.NotNull(item);
        var placed = string.IsNullOrEmpty(item!.PlacesBlock) ? null : Content.GetBlock(item.PlacesBlock!);
        return HeldItemShapes.MiniatureFor(item, placed);
    }

    [Theory]
    [InlineData("speeder")]
    [InlineData("boat")]
    public void Vehicles_AreHeldAsTheirVehicle(string item)
        => Assert.Equal(HeldItemShapes.Miniature.Vehicle, MiniatureOf(item));

    [Theory]
    [InlineData("plant_seed")]
    [InlineData("sapling")]
    [InlineData("seedling")]
    [InlineData("berry_seed")]
    [InlineData("tree_leaves")]
    [InlineData("torch")]
    [InlineData("lantern")]
    public void PlantsAndSlimProps_AreHeldAsTheirBlock(string item)
        => Assert.Equal(HeldItemShapes.Miniature.Plant, MiniatureOf(item));

    [Theory]
    [InlineData("stone")]
    [InlineData("glass")]
    [InlineData("basic_drill")]
    [InlineData("scrap_pistol")]
    public void BlocksAndTools_KeepTheirModel(string item)
        => Assert.Equal(HeldItemShapes.Miniature.None, MiniatureOf(item));

    [Fact]
    public void Gloves_KeepTheirTwoHandedModel()
    {
        var gloves = Content.Items.Values.Where(i => HeldGrips.IsGloves(i)).ToList();
        Assert.NotEmpty(gloves);
        Assert.All(gloves, g => Assert.Equal(HeldItemShapes.Miniature.None, HeldItemShapes.MiniatureFor(g, null)));
    }

    [Fact]
    public void EveryItemWithoutBlockToolOrVehicle_IsHeldAsItsIcon()
    {
        var plain = Content.Items.Values
            .Where(i => i.Vehicle == null && string.IsNullOrEmpty(i.PlacesBlock) && i.Tool == null
                        && !HeldGrips.IsGloves(i))
            .ToList();
        Assert.True(plain.Count > 50, "the materials, components and food");
        Assert.All(plain, i => Assert.Equal(HeldItemShapes.Miniature.Icon, HeldItemShapes.MiniatureFor(i, null)));
        Assert.Equal(HeldItemShapes.Miniature.None, HeldItemShapes.MiniatureFor(null, null));
    }

    private static BlockId? Resolve(string key) => Content.GetBlock(key)?.NumericId;

    [Fact]
    public void TheVehicleDesigns_FillTheHullBox()
    {
        var speeder = VehicleVoxels.Cells("speeder", Resolve);
        var boat = VehicleVoxels.Cells(VehicleVoxels.BoatKind, Resolve);
        Assert.Equal(27, speeder.Count); // floor 15 + rails 6 + backrest 3 + windscreen 3
        Assert.Equal(23, boat.Count);    // floor 12 + bow 1 + gunwales 6 + bow post 1 + transom 3
        foreach (var p in speeder.Keys.Concat(boat.Keys))
        {
            Assert.InRange(p.X, 0, (int)VehicleHull.Width - 1);
            Assert.InRange(p.Y, 0, (int)VehicleHull.Height - 1);
            Assert.InRange(p.Z, 0, (int)VehicleHull.Length - 1);
        }

        Assert.Equal(Resolve("glass"), speeder[new Vector3i(1, 1, 4)]); // the windscreen
        Assert.Equal(Resolve("iron_wall"), boat[new Vector3i(1, 1, 0)]); // the outboard motor
        Assert.Equal(Resolve("wood_log"), boat[new Vector3i(1, 0, 4)]);  // the bow tip
    }

    [Fact]
    public void TheVehicleDesigns_FallBackAlongTheirChains()
    {
        var stone = new BlockId(7);
        var speeder = VehicleVoxels.SpeederCells(k => k == "stone" ? stone : null);
        Assert.Equal(27, speeder.Count);
        Assert.All(speeder.Values, v => Assert.Equal(stone, v)); // no glass → the windscreen is hull

        Assert.Empty(VehicleVoxels.SpeederCells(_ => null));
        Assert.Empty(VehicleVoxels.BoatCells(_ => null));
    }

    // ---- icon → voxels ----

    private static byte[] Icon(int w, int h, System.Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = pixel(x, y);
                int i = ((y * w) + x) * 4;
                rgba[i] = p.R;
                rgba[i + 1] = p.G;
                rgba[i + 2] = p.B;
                rgba[i + 3] = p.A;
            }
        }

        return rgba;
    }

    [Fact]
    public void AnEmptyIcon_HasNoGrid()
    {
        Assert.Null(HeldIconVoxels.Downsample(Icon(16, 16, (_, _) => (255, 0, 0, 0)), 16, 16));
        Assert.Null(HeldIconVoxels.Downsample(new byte[3], 16, 16)); // a buffer that does not match
    }

    [Fact]
    public void ThePaddingAroundTheArt_IsCroppedAway()
    {
        // A 64² opaque square in the middle of a 128² icon fills the whole held grid.
        var rgba = Icon(128, 128, (x, y) => x >= 32 && x < 96 && y >= 32 && y < 96 ? ((byte)200, (byte)100, (byte)50, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var grid = HeldIconVoxels.Downsample(rgba, 128, 128, 16)!;
        Assert.Equal(16, grid.Size);
        Assert.Equal(16 * 16, grid.OpaqueCount);
        Assert.Equal(new HeldIconVoxels.Rgba(200, 100, 50, 255), grid[5, 9]);
    }

    [Fact]
    public void TheTransparentSurround_NeverDarkensAnEdge()
    {
        // A white bar three pixels wide on a 4-wide icon whose clear pixels are black: the cells that keep enough
        // coverage stay pure white, because colour is averaged by alpha.
        var rgba = Icon(8, 8, (x, y) => x < 6 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var grid = HeldIconVoxels.Downsample(rgba, 8, 8, 4)!;
        foreach (var c in grid.Cells.Where(c => c.A == 255))
        {
            Assert.Equal(new HeldIconVoxels.Rgba(255, 255, 255, 255), c);
        }
    }

    [Fact]
    public void ASmallCrop_KeepsItsOwnResolution()
    {
        var rgba = Icon(32, 32, (x, y) => x >= 10 && x < 18 && y >= 4 && y < 12 ? ((byte)10, (byte)20, (byte)30, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var grid = HeldIconVoxels.Downsample(rgba, 32, 32, 24)!;
        Assert.Equal(8, grid.Size);
        Assert.Equal(64, grid.OpaqueCount); // no holes between stretched pixels
    }

    [Fact]
    public void OnePixel_IsAClosedVoxel()
    {
        var grid = new HeldIconVoxels.Grid(1);
        grid.Cells[0] = new HeldIconVoxels.Rgba(255, 0, 0, 255);
        var mesh = HeldIconVoxels.BuildMesh(grid, 0.1f, 0.05f);
        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(36, mesh.Triangles.Count);
        Assert.Equal(0.05f, mesh.Positions.Max(p => p.X), 4);
        Assert.Equal(-0.025f, mesh.Positions.Min(p => p.Z), 4);
    }

    [Fact]
    public void ARunOfOneColour_SharesItsFrontAndBack_AndOnlyOuterWallsAreBuilt()
    {
        var grid = new HeldIconVoxels.Grid(3);
        for (int x = 0; x < 3; x++)
        {
            grid.Cells[(1 * 3) + x] = new HeldIconVoxels.Rgba(10, 200, 10, 255); // the middle row
        }

        var mesh = HeldIconVoxels.BuildMesh(grid, 1f, 0.2f);
        // front 1 + back 1 + top 3 + bottom 3 + left 1 + right 1
        Assert.Equal(10, mesh.QuadCount);
    }

    [Fact]
    public void EveryTriangle_FacesOutward()
    {
        var rgba = Icon(20, 20, (x, y) => (x - 10) * (x - 10) + (y - 10) * (y - 10) < 64
            ? ((byte)(x * 12), (byte)(y * 12), (byte)90, (byte)255)
            : ((byte)0, (byte)0, (byte)0, (byte)0));
        var grid = HeldIconVoxels.Downsample(rgba, 20, 20, 12)!;
        var mesh = HeldIconVoxels.BuildMesh(grid, 0.02f, 0.02f);
        Assert.True(mesh.QuadCount > 12);
        for (int t = 0; t < mesh.Triangles.Count; t += 3)
        {
            var a = mesh.Positions[mesh.Triangles[t]];
            var b = mesh.Positions[mesh.Triangles[t + 1]];
            var c = mesh.Positions[mesh.Triangles[t + 2]];
            var n = mesh.Normals[mesh.Triangles[t]];
            // Unity's front face: cross(b - a, c - a) points along the outward normal.
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f, $"triangle {t / 3} is wound inward");
        }

        // The front faces carry the pixel colour unshaded; the underside is darker.
        Assert.Contains(mesh.Colors, c => grid.Cells.Contains(c));
    }

    [Fact]
    public void AFullHeldGrid_StaysSmall()
    {
        var rgba = Icon(128, 128, (x, y) => ((byte)(x * 2), (byte)(y * 2), (byte)((x + y) & 255), (byte)255));
        var grid = HeldIconVoxels.Downsample(rgba, 128, 128)!;
        Assert.Equal(HeldIconVoxels.DefaultResolution, grid.Size);
        var mesh = HeldIconVoxels.BuildMesh(grid, 0.0125f, 0.0125f);
        Assert.True(mesh.Positions.Count < 65535 / 4, $"{mesh.Positions.Count} vertices");
    }
}
