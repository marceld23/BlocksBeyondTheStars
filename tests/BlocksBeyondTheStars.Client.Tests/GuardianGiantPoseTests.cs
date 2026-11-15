// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3676: the guardian giant's model geometry. The model that rises must start exactly on the block body the server just
/// removed (and the one that falls must end exactly where it stamps it): the client's lying rotation — Unity's
/// <c>Quaternion.Euler(0, facing·90, 0) * Quaternion.Euler(-90, 0, 0)</c> about the anchor cell's corner — has to put every
/// standing cell onto the cell <see cref="GuardianGiantBody.Lying"/> gives. Plus the arcs, the shake falloff and the footfalls.
/// </summary>
public sealed class GuardianGiantPoseTests
{
    private const int AnchorX = 117, AnchorY = 42, AnchorZ = -9;

    public static IEnumerable<object[]> SeedsAndFacings()
    {
        foreach (int seed in new[] { 0, 7, 123456 })
        {
            for (int facing = 0; facing < 4; facing++)
            {
                yield return new object[] { seed, facing };
            }
        }
    }

    [Fact]
    public void Rotations_FollowUnitysEulerConvention()
    {
        Near(new Vector3f(0f, 0f, 1f), GuardianGiantPose.RotateX(new Vector3f(0f, 1f, 0f), 90f));  // Euler(90,0,0): up → forward
        Near(new Vector3f(0f, -1f, 0f), GuardianGiantPose.RotateX(new Vector3f(0f, 0f, 1f), 90f)); // … forward → down
        Near(new Vector3f(1f, 0f, 0f), GuardianGiantPose.RotateY(new Vector3f(0f, 0f, 1f), 90f));  // Euler(0,90,0): forward → right
        Assert.Equal(-90f, GuardianGiantPose.LyingPitch);
        Assert.Equal(270f, GuardianGiantPose.LyingYaw(-1));
        Assert.Equal(90f, GuardianGiantPose.LyingYaw(5));
    }

    [Fact]
    public void ThePoint_StandingXYZ_LiesAtXZMinusY_ThenTurnsWithTheFacing()
    {
        Near(new Vector3f(2f, 3f, -14f), GuardianGiantPose.ToLying(new Vector3f(2f, 14f, 3f), 0)); // (x, y, z) → (x, z, −y)
        Near(new Vector3f(-14f, 0f, 0f), GuardianGiantPose.ToLying(new Vector3f(0f, 14f, 0f), 1)); // the head toward −Z, turned to −X
    }

    [Theory]
    [MemberData(nameof(SeedsAndFacings))]
    public void TheLyingModel_CoversEveryCellOfTheStampedBody(int seed, int facing)
    {
        var standing = GuardianGiantBody.Standing(seed, lit: true);
        var lying = GuardianGiantBody.Lying(seed, lit: true, facing);
        Assert.Equal(standing.Count, lying.Count);
        var root = GuardianGiantPose.LyingRoot(AnchorX, AnchorY, AnchorZ);
        for (int i = 0; i < standing.Count; i++)
        {
            var c = standing[i];
            var (dx, dy, dz, block, isLight) = lying[i];
            Assert.Equal(c.Block, block);
            Assert.Equal(c.IsLight, isLight);

            // Both opposite corners of the standing cell land on the lying cell's corners: the cube coincides exactly.
            var a = Add(root, GuardianGiantPose.ToLying(new Vector3f(c.X, c.Y, c.Z), facing));
            var b = Add(root, GuardianGiantPose.ToLying(new Vector3f(c.X + 1, c.Y + 1, c.Z + 1), facing));
            AssertSpan(AnchorX + dx, a.X, b.X, i);
            AssertSpan(AnchorY + dy, a.Y, b.Y, i);
            AssertSpan(AnchorZ + dz, a.Z, b.Z, i);
        }
    }

    [Fact]
    public void TheRootGuess_StandsOnTheRowTheLyingBackRestsIn()
    {
        var guess = GuardianGiantPose.StandingRootGuess(AnchorX, AnchorY, AnchorZ);
        Near(new Vector3f(AnchorX, AnchorY + GuardianGiantBody.LyingBottom, AnchorZ), guess);
        int lowest = int.MaxValue;
        foreach (var (_, dy, _, _, _) in GuardianGiantBody.Lying(0, lit: false, facing: 2))
        {
            lowest = Math.Min(lowest, dy);
        }

        Assert.Equal(guess.Y, AnchorY + lowest, 4); // the lowest lying cell's floor is the ground it stands on
    }

    [Fact]
    public void TheArc_RunsFromStartToEnd_AndPeaksHalfway()
    {
        var from = new Vector3f(0f, 10f, 0f);
        var to = new Vector3f(20f, 6f, -4f);
        Near(from, GuardianGiantPose.Arc(from, to, 0f, 5f));
        Near(to, GuardianGiantPose.Arc(from, to, 1f, 5f));
        Near(new Vector3f(10f, 13f, -2f), GuardianGiantPose.Arc(from, to, 0.5f, 5f)); // the middle of the line plus the peak
        Near(to, GuardianGiantPose.Arc(from, to, 3f, 5f));                            // clamped
        Assert.Equal(4f, GuardianGiantPose.ArcPeak(2f, 4f), 4);
        Assert.Equal(7f, GuardianGiantPose.ArcPeak(20f, 4f), 4);
    }

    [Fact]
    public void TheShake_IsFullAtTheCentre_AndGoneAtTwiceTheRadius()
    {
        float r = GuardianGiantRules.StompRadius;
        Assert.Equal(GuardianGiantRules.ShakeStrength, GuardianGiantPose.Shake(0f, r), 4);
        Assert.Equal(GuardianGiantRules.ShakeStrength * 0.5f, GuardianGiantPose.Shake(r, r), 4);
        Assert.Equal(0f, GuardianGiantPose.Shake(2f * r, r), 4);
        Assert.Equal(0f, GuardianGiantPose.Shake(50f, r), 4);
        Assert.Equal(0f, GuardianGiantPose.Shake(1f, 0f), 4);
        Assert.Equal(0f, GuardianGiantPose.Shake(float.NaN, r), 4);
    }

    [Fact]
    public void AShockWave_PushesAwayFromItsCentre_FallingOffLikeTheDamage()
    {
        float r = GuardianGiantRules.JumpRadius;
        var near = GuardianGiantPose.WavePush(new Vector3f(0f, 0.5f, -1f), r);
        Assert.Equal(-GuardianGiantRules.KnockSpeed * GuardianGiantRules.WaveDamage(1f, r, 1f), near.Z, 3); // away: toward −Z
        Assert.Equal(0f, near.X, 3);
        Assert.True(near.Y > 0f);
        var edge = GuardianGiantPose.WavePush(new Vector3f(r, 0f, 0f), r);
        Assert.Equal(GuardianGiantRules.KnockSpeed * GuardianGiantRules.EdgeShare, edge.X, 3);
        Assert.Equal(GuardianGiantRules.KnockLift * GuardianGiantRules.EdgeShare, edge.Y, 3);
        Near(new Vector3f(0f, 0f, 0f), GuardianGiantPose.WavePush(new Vector3f(r + 0.5f, 0f, 0f), r));      // beyond its reach
        Near(new Vector3f(0f, 0f, 0f), GuardianGiantPose.WavePush(new Vector3f(1f, GuardianGiantRules.WaveHeight + 1f, 0f), r)); // on a ledge
        Assert.Equal(GuardianGiantRules.KnockSpeed, GuardianGiantPose.WavePush(new Vector3f(0f, 0f, 0f), r).X, 3); // right at the centre: +X
    }

    [Theory]
    [InlineData(0.9f, 1.1f, 1)]  // the left foot plants as its phase wraps
    [InlineData(0.4f, 0.6f, 2)]  // the right one half a stride later
    [InlineData(0.1f, 0.2f, 0)]
    [InlineData(0.2f, 1.3f, 3)]  // a whole stride in one frame
    [InlineData(0.5f, 0.5f, 0)]
    [InlineData(1.2f, 0.8f, 0)]  // never backwards
    public void Footfalls_ComeWhenAFootsPhaseWraps(float prev, float walk, int expected)
        => Assert.Equal(expected, GuardianGiantPose.Footfalls(prev, walk));

    private static Vector3f Add(Vector3f a, Vector3f b) => new Vector3f(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static void AssertSpan(int cell, float a, float b, int index)
    {
        Assert.True(Math.Abs(Math.Min(a, b) - cell) < 1e-3f, $"cell {index}: {Math.Min(a, b)} is not the lying cell's floor {cell}");
        Assert.True(Math.Abs(Math.Max(a, b) - (cell + 1)) < 1e-3f, $"cell {index}: {Math.Max(a, b)} is not the lying cell's top {cell + 1}");
    }

    private static void Near(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }
}
