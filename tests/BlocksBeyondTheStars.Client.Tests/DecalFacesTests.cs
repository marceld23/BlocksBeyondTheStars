// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3058: which cube faces a voxel-face decal lies on, and how its quads are laid out.</summary>
public sealed class DecalFacesTests
{
    private const float Cos80 = 0.17f;

    /// <summary>A flat field: every cell at or below y = 0 is a plain cube, the rest is air.</summary>
    private static DecalCell Ground(int x, int y, int z) => y <= 0 ? DecalCell.Surface : DecalCell.Open;

    private static List<DecalFace> Collect(Func<int, int, int, DecalCell> cell, DecalBox box)
    {
        var faces = new List<DecalFace>();
        DecalFaces.Collect(cell, box, faces);
        return faces;
    }

    private static DecalBox Small(float x, float y, float z, int dir, float radius)
    {
        var (nx, ny, nz) = DecalFaces.Normal(dir);
        return new DecalBox(x, y, z, nx, ny, nz, radius, 0.55f, Cos80, 0f);
    }

    [Fact]
    public void ASmallMarkInTheMiddleOfAFace_TakesOnlyThatFace()
    {
        var faces = Collect(Ground, Small(0.5f, 1f, 0.5f, dir: 2, radius: 0.3f));

        var face = Assert.Single(faces);
        Assert.Equal((0, 0, 0, 2), (face.X, face.Y, face.Z, face.Dir));
        Assert.Equal(0, face.OpenEdges); // a top face inside a flat field has no convex edge
    }

    [Fact]
    public void AMarkOnACorner_SpreadsOverTheFacesItTouches()
    {
        // Centred on the corner of four cells: each of the four top faces is within reach, nothing else.
        var faces = Collect(Ground, Small(1f, 1f, 1f, dir: 2, radius: 0.3f));

        Assert.Equal(4, faces.Count);
        Assert.All(faces, f => Assert.Equal(2, f.Dir));
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, faces.Select(f => (f.X, f.Z)).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void AMarkOnAWall_SkipsTheFloorAndTheWallsBackSide()
    {
        // A one-block-thick wall at x = 3 standing on the ground; a shot from −X struck its −X face at y = 1.5.
        DecalCell Cell(int x, int y, int z) => y <= 0 || (x == 3 && y <= 3) ? DecalCell.Surface : DecalCell.Open;
        var faces = Collect(Cell, Small(3f, 1.5f, 0.5f, dir: 1, radius: 0.5f));

        Assert.NotEmpty(faces);
        Assert.All(faces, f => Assert.Equal(1, f.Dir)); // only faces turned toward the shooter (the floor is 90° off)
        Assert.All(faces, f => Assert.Equal(3, f.X));
    }

    [Fact]
    public void AHiddenFace_IsNeverMarked()
    {
        // A block sits on top of the struck ground cell: its top face is gone, the block's own top is a metre off the plane.
        DecalCell Cell(int x, int y, int z) => y <= 0 || (x == 0 && y == 1 && z == 0) ? DecalCell.Surface : DecalCell.Open;
        var faces = Collect(Cell, Small(0.5f, 1f, 0.5f, dir: 2, radius: 0.3f));

        Assert.Empty(faces);
    }

    [Fact]
    public void AClosedCell_SealsTheFaceUnderIt_AndTakesNoMarkItself()
    {
        // Lava (or oil) lying on the ground: the ground's top face is hidden, and the lava's own top is no surface.
        DecalCell Lava(int x, int y, int z) => y <= 0 ? DecalCell.Surface : y == 1 ? DecalCell.Closed : DecalCell.Open;

        Assert.Empty(Collect(Lava, Small(0.5f, 1f, 0.5f, dir: 2, radius: 0.3f)));
        Assert.Empty(Collect(Lava, Small(0.5f, 2f, 0.5f, dir: 2, radius: 0.3f)));
    }

    [Fact]
    public void AFacingCrater_ScorchesTheBowlAndTheRim_ButNoFaceTurnedAway()
    {
        // Ground (top at y = 1) with a 3×3×2 pit carved at x,z ∈ [−1, 1], y ∈ [−1, 0], a cave pocket under it at y = −3,
        // and one block standing on the rim at (2, 1, 0).
        static DecalCell Cell(int x, int y, int z)
        {
            bool pit = x >= -1 && x <= 1 && z >= -1 && z <= 1 && y >= -1 && y <= 0;
            bool cave = x >= -2 && x <= 2 && z >= -2 && z <= 2 && y == -3;
            bool block = x == 2 && y == 1 && z == 0;
            return (y <= 0 && !pit && !cave) || block ? DecalCell.Surface : DecalCell.Open;
        }

        var box = new DecalBox(0.5f, 1f, 0.5f, 0f, 1f, 0f, radius: 3f, depth: 3.5f, minDot: -0.1f, facingLift: 1.5f);
        var faces = Collect(Cell, box);

        Assert.Contains(faces, f => f.Dir == 2 && f.Y == -2);              // the pit's floor
        Assert.Contains(faces, f => f.Dir == 0 && f.X == -2 && f.Y == -1); // a pit wall, facing in (+X)
        Assert.Contains(faces, f => f.Dir == 2 && f.Y == 0 && f.X == 2);   // the rim
        Assert.Contains(faces, f => (f.X, f.Y, f.Z, f.Dir) == (2, 1, 0, 1));         // the rim block's side toward the blast
        Assert.DoesNotContain(faces, f => (f.X, f.Y, f.Z, f.Dir) == (2, 1, 0, 0));   // …but not its far side
        Assert.DoesNotContain(faces, f => f.Dir == 3);                      // and never the cave ceiling (turned down)
    }

    [Fact]
    public void AnIsolatedBlock_HasConvexEdgesAllAround()
    {
        DecalCell Cell(int x, int y, int z) => x == 0 && y == 0 && z == 0 ? DecalCell.Surface : DecalCell.Open;
        var face = Assert.Single(Collect(Cell, Small(0.5f, 1f, 0.5f, dir: 2, radius: 0.3f)));

        Assert.Equal(0b1111, face.OpenEdges);
    }

    [Fact]
    public void ABlockOnAnEdgeOfAField_HasOneConvexEdge()
    {
        // Ground only at x ≤ 0: the top face of cell x = 0 has its +X edge open. A +Y face's tangent axes are Z (bits 0/1)
        // and X (bits 2/3), so that edge is bit 3.
        DecalCell Cell(int x, int y, int z) => y <= 0 && x <= 0 ? DecalCell.Surface : DecalCell.Open;
        var face = Assert.Single(Collect(Cell, Small(0.5f, 1f, 0.5f, dir: 2, radius: 0.3f)));

        Assert.Equal(0b1000, face.OpenEdges);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryQuad_FacesOutward_AndSitsLiftedOffItsFace(int dir)
    {
        var corners = new float[12];
        DecalFaces.Corners(new DecalFace(2, 3, 4, dir, 0), 0.005f, 0.06f, corners);
        var (nx, ny, nz) = DecalFaces.Normal(dir);

        float e1x = corners[3] - corners[0], e1y = corners[4] - corners[1], e1z = corners[5] - corners[2];
        float e2x = corners[6] - corners[0], e2y = corners[7] - corners[1], e2z = corners[8] - corners[2];
        float cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
        Assert.True(cx * nx + cy * ny + cz * nz > 0.99f, "the triangle (0, 1, 2) must face along the face normal");

        // Every corner lies on the face's plane, 5 mm out: the cell's centre plus half a block plus the lift.
        for (int k = 0; k < 4; k++)
        {
            float along = (corners[k * 3] - 2.5f) * nx + (corners[k * 3 + 1] - 3.5f) * ny + (corners[k * 3 + 2] - 4.5f) * nz;
            Assert.Equal(0.505f, along, 4);
        }
    }

    [Fact]
    public void AQuadIsPulledInOnlyAlongItsConvexEdges()
    {
        var corners = new float[12];
        // +Y face (tangent axes Z then X); only the high-Z edge (bit 1) is convex.
        DecalFaces.Corners(new DecalFace(0, 0, 0, 2, 0b0010), 0f, 0.06f, corners);

        float minX = Enumerable.Range(0, 4).Min(k => corners[k * 3]), maxX = Enumerable.Range(0, 4).Max(k => corners[k * 3]);
        float minZ = Enumerable.Range(0, 4).Min(k => corners[k * 3 + 2]), maxZ = Enumerable.Range(0, 4).Max(k => corners[k * 3 + 2]);
        Assert.Equal((0f, 1f), (minX, maxX));
        Assert.Equal(0f, minZ, 4);
        Assert.Equal(0.94f, maxZ, 4);
    }

    [Fact]
    public void TheBoxNeverReachesPastFourCells()
    {
        Assert.Equal(1, DecalFaces.Reach(0.3f, 0.55f));
        Assert.Equal(4, DecalFaces.Reach(4f, 4.5f));
        Assert.Equal(DecalFaces.MaxReach, DecalFaces.Reach(9f, 9f));

        int asked = 0;
        DecalCell Count(int x, int y, int z)
        {
            asked++;
            return Ground(x, y, z);
        }

        DecalFaces.Collect(Count, new DecalBox(0.5f, 1f, 0.5f, 0f, 1f, 0f, 9f, 9f, -2f, 0f), new List<DecalFace>());
        Assert.Equal(11 * 11 * 11, asked); // (2·4 + 1)³ cells plus the one-cell margin, each asked once
    }
}
