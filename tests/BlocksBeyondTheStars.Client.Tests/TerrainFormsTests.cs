// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3964: the terrain forms — the same cell gives the same form on every client, the merged faces cover exactly the
/// micro faces the rule asks for (no see-through hole, no face twice), two formed neighbours draw their shared border
/// once, tops stay walkable, the face budget holds, and only the terrain families take forms.
/// </summary>
public sealed class TerrainFormsTests
{
    private const int G = TerrainForms.G;
    private const long Salt = 424242;
    private static readonly byte[] Families = { TerrainForms.FamilyRock, TerrainForms.FamilySoft, TerrainForms.FamilyGrass };
    private static readonly int[] Dx = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] Dy = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };

    /// <summary>Every (family, open mask, cell) the sweeps visit that yields a form.</summary>
    private static IEnumerable<(byte Family, int Open, int X, int Y, int Z, bool[] Vox)> Formed(int cellsPerMask = 60)
    {
        foreach (byte family in Families)
        {
            for (int open = 0; open < 64; open++)
            {
                for (int c = 0; c < cellsPerMask; c++)
                {
                    int x = (c * 37) - 900, y = (c % 11) + 40, z = (c * 13) - 300;
                    var vox = new bool[TerrainForms.Cells];
                    if (TerrainForms.TrySelect(family, open, x, y, z, Salt, 2f, vox, out _))
                    {
                        yield return (family, open, x, y, z, vox);
                    }
                }
            }
        }
    }

    [Fact]
    public void SameCell_GivesTheSameForm()
    {
        int checkedForms = 0;
        foreach (var f in Formed(20))
        {
            var again = new bool[TerrainForms.Cells];
            Assert.True(TerrainForms.TrySelect(f.Family, f.Open, f.X, f.Y, f.Z, Salt, 2f, again, out _));
            Assert.Equal(f.Vox, again);
            checkedForms++;
        }

        Assert.True(checkedForms > 100, $"only {checkedForms} forms in the sweep");
    }

    [Fact]
    public void Faces_CoverExactlyTheMicroFaces_ForOpenAndSolidSides()
    {
        var rects = new List<TerrainForms.Rect3>();
        var side = EmptySides();
        var kinds = new byte[6];
        int count = 0;
        foreach (var f in Formed())
        {
            for (int d = 0; d < 6; d++)
            {
                kinds[d] = (f.Open & (1 << d)) != 0 ? TerrainForms.SideOpen : TerrainForms.SideSolid;
            }

            Assert.True(TerrainForms.BuildFaces(f.Vox, kinds, side, rects, 256));
            Assert.Equal(Expected(f.Vox, kinds, side), Rasterise(rects));
            count++;
        }

        Assert.True(count > 1000, $"only {count} forms checked");
    }

    [Fact]
    public void TwoFormedNeighbours_DrawTheirSharedBorderOnce()
    {
        var rects = new List<TerrainForms.Rect3>();
        int pairs = 0;
        for (int x = -2000; x < 2000 && pairs < 40; x++)
        {
            // Two wall cells side by side along X, both open toward +Z (a cliff face), closed toward each other.
            const int open = 1 << 4;
            var a = new bool[TerrainForms.Cells];
            var b = new bool[TerrainForms.Cells];
            if (!TerrainForms.TrySelect(TerrainForms.FamilyRock, open, x, 50, 7, Salt, 2f, a, out _)
                || !TerrainForms.TrySelect(TerrainForms.FamilyRock, open, x + 1, 50, 7, Salt, 2f, b, out _))
            {
                continue;
            }

            var aFaces = FacesWith(a, open, b, null, rects);
            var bFaces = FacesWith(b, open, null, a, rects);
            for (int v = 0; v < G; v++)
            {
                for (int u = 0; u < G; u++)
                {
                    bool aDraws = aFaces.Contains((2, G, u, v));
                    bool bDraws = bFaces.Contains((3, 0, u, v));
                    bool aFilled = a[TerrainForms.Index(G - 1, v, u)], bFilled = b[TerrainForms.Index(0, v, u)];
                    Assert.Equal(aFilled && !bFilled, aDraws);
                    Assert.Equal(bFilled && !aFilled, bDraws);
                }
            }

            pairs++;
        }

        Assert.True(pairs >= 10, $"only {pairs} neighbour pairs found");
    }

    [Fact]
    public void Tops_StayWalkable()
    {
        foreach (var f in Formed())
        {
            bool topOpen = (f.Open & 1) != 0;
            if (!topOpen)
            {
                continue;
            }

            int sides = (f.Open >> 2) & 0xF;
            for (int z = 0; z < G; z++)
            {
                for (int x = 0; x < G; x++)
                {
                    if (sides == 0)
                    {
                        // Flat ground: only the top micro layer may go.
                        for (int y = 0; y < G - 1; y++)
                        {
                            Assert.True(f.Vox[TerrainForms.Index(x, y, z)], $"flat form carved below the top layer at {x},{y},{z}");
                        }
                    }
                    else if (DistanceToOpenSide(x, z, sides) >= 4)
                    {
                        // An edge: the half away from the drop keeps its full top.
                        Assert.True(f.Vox[TerrainForms.Index(x, G - 1, z)], $"edge form carved the inner top at {x},{z}");
                    }
                }
            }
        }
    }

    [Fact]
    public void FaceBudget_Holds()
    {
        var rects = new List<TerrainForms.Rect3>();
        var kinds = new byte[6];
        int max = 0;
        foreach (var f in Formed())
        {
            for (int d = 0; d < 6; d++)
            {
                kinds[d] = (f.Open & (1 << d)) != 0 ? TerrainForms.SideOpen : TerrainForms.SideSolid;
            }

            Assert.True(TerrainForms.BuildFaces(f.Vox, kinds, EmptySides(), rects, 256));
            max = System.Math.Max(max, rects.Count);
        }

        Assert.InRange(max, 1, 64);
    }

    [Theory]
    [InlineData("stone", TerrainForms.FamilyRock)]
    [InlineData("ice", TerrainForms.FamilyRock)]
    [InlineData("sand", TerrainForms.FamilySoft)]
    [InlineData("snow", TerrainForms.FamilySoft)]
    [InlineData("grass", TerrainForms.FamilyGrass)]
    [InlineData("iron_ore", TerrainForms.FamilyNone)]
    [InlineData("glass", TerrainForms.FamilyNone)]
    [InlineData("water", TerrainForms.FamilyNone)]
    public void Families_AreTheTerrainMaterials(string key, byte family)
    {
        Assert.Equal(family, TerrainForms.FamilyOf(key));
    }

    [Fact]
    public void NoForm_WithoutDensityOrFamilyOrWhereNothingIsOpen()
    {
        var vox = new bool[TerrainForms.Cells];
        for (int x = 0; x < 500; x++)
        {
            Assert.False(TerrainForms.TrySelect(TerrainForms.FamilyRock, 1 | (1 << 2), x, 10, 3, Salt, 0f, vox, out _));
            Assert.False(TerrainForms.TrySelect(TerrainForms.FamilyNone, 1 | (1 << 2), x, 10, 3, Salt, 2f, vox, out _));
            Assert.False(TerrainForms.TrySelect(TerrainForms.FamilyRock, 0, x, 10, 3, Salt, 2f, vox, out _));
            Assert.False(TerrainForms.TrySelect(TerrainForms.FamilyRock, 1 | 2, x, 10, 3, Salt, 2f, vox, out _));
        }
    }

    [Fact]
    public void Regions_MixRoughAndCalm()
    {
        var vox = new bool[TerrainForms.Cells];
        int formed = 0, total = 0;
        for (int x = 0; x < 200; x++)
        {
            for (int z = 0; z < 200; z += 5)
            {
                total++;
                if (TerrainForms.TrySelect(TerrainForms.FamilyRock, 1 | (1 << 2), x, 30, z, Salt, 1f, vox, out _))
                {
                    formed++;
                }
            }
        }

        double share = formed / (double)total;
        Assert.InRange(share, 0.1, 0.8);
    }

    // ------------------------------------------------------------------ helpers

    private static bool[][] EmptySides()
    {
        var side = new bool[6][];
        for (int i = 0; i < 6; i++)
        {
            side[i] = new bool[TerrainForms.Cells];
        }

        return side;
    }

    private static int DistanceToOpenSide(int x, int z, int sides)
    {
        int best = int.MaxValue;
        if ((sides & 1) != 0)
        {
            best = System.Math.Min(best, G - 1 - x); // +X
        }

        if ((sides & 2) != 0)
        {
            best = System.Math.Min(best, x); // −X
        }

        if ((sides & 4) != 0)
        {
            best = System.Math.Min(best, G - 1 - z); // +Z
        }

        if ((sides & 8) != 0)
        {
            best = System.Math.Min(best, z); // −Z
        }

        return best;
    }

    /// <summary>The unit faces of a wall cell whose ±X neighbour is a formed cell (the other one left solid or open by
    /// <paramref name="open"/>).</summary>
    private static HashSet<(int Dir, int Plane, int U, int V)> FacesWith(bool[] vox, int open, bool[]? plusX, bool[]? minusX,
        List<TerrainForms.Rect3> rects)
    {
        var kinds = new byte[6];
        var side = EmptySides();
        for (int d = 0; d < 6; d++)
        {
            kinds[d] = (open & (1 << d)) != 0 ? TerrainForms.SideOpen : TerrainForms.SideSolid;
        }

        if (plusX != null)
        {
            kinds[2] = TerrainForms.SideFormed;
            side[2] = plusX;
        }

        if (minusX != null)
        {
            kinds[3] = TerrainForms.SideFormed;
            side[3] = minusX;
        }

        Assert.True(TerrainForms.BuildFaces(vox, kinds, side, rects, 256));
        return Rasterise(rects);
    }

    /// <summary>The unit micro faces a rect set covers, as (dir, plane, u, v) — u, v the two other axes in x, y, z order
    /// with the plane's axis left out (+Y/−Y: u = x, v = z; ±X: u = z, v = y; ±Z: u = x, v = y). A face covered twice fails.</summary>
    private static HashSet<(int Dir, int Plane, int U, int V)> Rasterise(List<TerrainForms.Rect3> rects)
    {
        var set = new HashSet<(int, int, int, int)>();
        foreach (var r in rects)
        {
            int axis = Axis(r.Dir);
            int plane = axis == 0 ? r.X0 : axis == 1 ? r.Y0 : r.Z0;
            for (int x = r.X0; x < System.Math.Max(r.X1, r.X0 + 1); x++)
            {
                for (int y = r.Y0; y < System.Math.Max(r.Y1, r.Y0 + 1); y++)
                {
                    for (int z = r.Z0; z < System.Math.Max(r.Z1, r.Z0 + 1); z++)
                    {
                        var key = axis switch
                        {
                            0 => (r.Dir, plane, z, y),
                            1 => (r.Dir, plane, x, z),
                            _ => (r.Dir, plane, x, y),
                        };
                        Assert.True(set.Add(key), $"micro face {key} drawn twice");
                    }
                }
            }
        }

        return set;
    }

    /// <summary>The rule, unit face by unit face: a face points from a filled micro cell to an empty one; this cell draws
    /// it when the filled one is its own (toward its own space, open air or a formed neighbour's empty cell), or when the
    /// filled one lies in a solid unformed neighbour and the empty one is its own (the back wall).</summary>
    private static HashSet<(int Dir, int Plane, int U, int V)> Expected(bool[] vox, byte[] kinds, bool[][] side)
    {
        var set = new HashSet<(int, int, int, int)>();
        for (int dir = 0; dir < 6; dir++)
        {
            for (int y = -1; y <= G; y++)
            {
                for (int z = -1; z <= G; z++)
                {
                    for (int x = -1; x <= G; x++)
                    {
                        int bx = x + Dx[dir], by = y + Dy[dir], bz = z + Dz[dir];
                        bool aIn = Inside(x, y, z), bIn = Inside(bx, by, bz);
                        if (!aIn && !bIn)
                        {
                            continue;
                        }

                        if (!Filled(vox, kinds, side, x, y, z, out int aSide) || Filled(vox, kinds, side, bx, by, bz, out int bSide))
                        {
                            continue;
                        }

                        bool draws = aIn || kinds[aSide] == TerrainForms.SideSolid;
                        if (!draws || (!aIn && !bIn))
                        {
                            continue;
                        }

                        int plane = (dir & 1) == 0 ? Coord(Axis(dir), bx, by, bz) : Coord(Axis(dir), x, y, z);
                        set.Add(Axis(dir) switch
                        {
                            0 => (dir, plane, z, y),
                            1 => (dir, plane, x, z),
                            _ => (dir, plane, x, y),
                        });
                    }
                }
            }
        }

        return set;
    }

    private static bool Filled(bool[] vox, byte[] kinds, bool[][] side, int x, int y, int z, out int sideIndex)
    {
        sideIndex = -1;
        if (Inside(x, y, z))
        {
            return vox[TerrainForms.Index(x, y, z)];
        }

        sideIndex = y >= G ? 0 : y < 0 ? 1 : x >= G ? 2 : x < 0 ? 3 : z >= G ? 4 : 5;
        int outside = (x < 0 || x >= G ? 1 : 0) + (y < 0 || y >= G ? 1 : 0) + (z < 0 || z >= G ? 1 : 0);
        if (outside > 1)
        {
            return false; // a diagonal cell never meets an inner one along one axis
        }

        return kinds[sideIndex] switch
        {
            TerrainForms.SideOpen => false,
            TerrainForms.SideSolid => true,
            _ => side[sideIndex][TerrainForms.Index((x + G) % G, (y + G) % G, (z + G) % G)],
        };
    }

    private static bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < G && y < G && z < G;

    private static int Axis(int dir) => dir <= 1 ? 1 : dir <= 3 ? 0 : 2;

    private static int Coord(int axis, int x, int y, int z) => axis == 0 ? x : axis == 1 ? y : z;
}
