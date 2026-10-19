// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The creature part cube (#2831): even texel density by whole tile repeats per face, the unit cube's shape, outward
/// winding the way Unity culls, and the pinned variant the eye mask needs.
/// </summary>
public sealed class TiledCubeTests
{
    [Theory]
    [InlineData(0.05f, 1)]
    [InlineData(0.6f, 1)]
    [InlineData(0.95f, 2)]
    [InlineData(1.2f, 2)]
    [InlineData(3f, 5)]
    [InlineData(50f, TiledCube.MaxRepeats)]
    [InlineData(0f, 1)]
    [InlineData(-2f, 1)]
    [InlineData(float.NaN, 1)]
    public void Repeats_FollowTheSize_Clamped(float size, int expected)
        => Assert.Equal(expected, TiledCube.Repeats(size));

    [Fact]
    public void TheCube_IsTheUnitCube_WithOutwardFacesWoundForUnity()
    {
        var d = TiledCube.Build(2, 3, 4, false);
        Assert.Equal(24 * 3, d.Positions.Length);
        Assert.Equal(36, d.Triangles.Length);
        Assert.All(d.Positions, p => Assert.Equal(0.5f, Math.Abs(p)));

        for (int t = 0; t < 36; t += 3)
        {
            int a = d.Triangles[t], b = d.Triangles[t + 1], c = d.Triangles[t + 2];
            var pa = V(d.Positions, a);
            var ab = Sub(V(d.Positions, b), pa);
            var ac = Sub(V(d.Positions, c), pa);
            var n = Cross(ab, ac);
            var normal = V(d.Normals, a);
            Assert.True(Dot(n, normal) > 0f, $"triangle {t / 3} faces inward");
            Assert.True(Dot(normal, pa) > 0f, "the normal points out of the cube"); // the vertex sits on the face's outer side
        }
    }

    [Fact]
    public void TheUvs_RepeatPerAxis_AlongTheLongAxisOnTheLongFaces()
    {
        var d = TiledCube.Build(2, 3, 4, false);
        // Face order: +X, −X, +Y, −Y, +Z, −Z; 4 vertices each.
        (float MaxU, float MaxV) Range(int face)
        {
            var us = Enumerable.Range(face * 4, 4).Select(i => d.Uvs[i * 2]).ToList();
            var vs = Enumerable.Range(face * 4, 4).Select(i => d.Uvs[i * 2 + 1]).ToList();
            Assert.Equal(0f, us.Min());
            Assert.Equal(0f, vs.Min());
            return (us.Max(), vs.Max());
        }

        Assert.Equal((4f, 3f), Range(0)); // flank: u along Z, v up
        Assert.Equal((4f, 3f), Range(1));
        Assert.Equal((4f, 2f), Range(2)); // top: u along Z, v across X
        Assert.Equal((4f, 2f), Range(3));
        Assert.Equal((2f, 3f), Range(4)); // end caps: u along X, v up
        Assert.Equal((2f, 3f), Range(5));
    }

    [Fact]
    public void APinnedCube_MapsVOnce_AndKeepsTheTopAndBottomToTheirRows()
    {
        var d = TiledCube.Build(3, 5, 2, true);
        for (int face = 0; face < 6; face++)
        {
            var vs = Enumerable.Range(face * 4, 4).Select(i => d.Uvs[i * 2 + 1]).ToList();
            switch (face)
            {
                case 2:
                    Assert.Equal(1f - TiledCube.PinnedCapRows, vs.Min(), 4);
                    Assert.Equal(1f, vs.Max(), 4);
                    break;
                case 3:
                    Assert.Equal(0f, vs.Min(), 4);
                    Assert.Equal(TiledCube.PinnedCapRows, vs.Max(), 4);
                    break;
                default:
                    Assert.Equal(0f, vs.Min(), 4);
                    Assert.Equal(1f, vs.Max(), 4);
                    break;
            }
        }
    }

    [Fact]
    public void Keys_AreDistinctPerVariant()
    {
        var keys = (from x in Enumerable.Range(1, TiledCube.MaxRepeats)
                    from y in Enumerable.Range(1, TiledCube.MaxRepeats)
                    from z in Enumerable.Range(1, TiledCube.MaxRepeats)
                    from p in new[] { false, true }
                    select TiledCube.Key(x, y, z, p)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    private static (float X, float Y, float Z) V(float[] a, int i) => (a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);

    private static (float X, float Y, float Z) Sub((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b)
        => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
