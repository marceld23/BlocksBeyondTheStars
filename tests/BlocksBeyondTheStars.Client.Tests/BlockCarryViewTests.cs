// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Numerics;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3828 (the player's report on #3766: "lifting looks odd from first person"). The carry used to shift the one-handed
/// rest pose, which put the block ON the aim point with the open end of its forearm in the middle of the view. Now the
/// block is held up with both hands: at the top middle of the view, level and bigger, two arms under its lower corners.
/// The scripts cannot load headless, so the pose is rebuilt here from <see cref="BlockCarryPose"/>'s numbers with the
/// view's own maths (Unity's angle order, the 60° reference field of view the first-person hands are tuned at) and
/// what the player must see is checked on that; the Unity wiring is guarded as source text.
/// </summary>
public sealed class BlockCarryViewTests
{
    private const float ItemScale = 0.72f;      // Viewmodel.ItemScale
    private const float ReferenceFov = 60f;     // Viewmodel.ReferenceFov
    private const float Aspect = 16f / 9f;

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>Unity's <c>Quaternion.Euler(x, y, z) * v</c>: roll (Z) first, then pitch (X), then yaw (Y).</summary>
    private static Vector3 Turn(Vector3 euler, Vector3 v)
    {
        float x = euler.X * MathF.PI / 180f, y = euler.Y * MathF.PI / 180f, z = euler.Z * MathF.PI / 180f;
        v = new Vector3((v.X * MathF.Cos(z)) - (v.Y * MathF.Sin(z)), (v.X * MathF.Sin(z)) + (v.Y * MathF.Cos(z)), v.Z);
        v = new Vector3(v.X, (v.Y * MathF.Cos(x)) - (v.Z * MathF.Sin(x)), (v.Y * MathF.Sin(x)) + (v.Z * MathF.Cos(x)));
        return new Vector3((v.X * MathF.Cos(y)) + (v.Z * MathF.Sin(y)), v.Y, (-v.X * MathF.Sin(y)) + (v.Z * MathF.Cos(y)));
    }

    private static IEnumerable<Vector3> Corners(Vector3 centre, Vector3 size)
    {
        foreach (float sx in new[] { -0.5f, 0.5f })
        {
            foreach (float sy in new[] { -0.5f, 0.5f })
            {
                foreach (float sz in new[] { -0.5f, 0.5f })
                {
                    yield return centre + new Vector3(size.X * sx, size.Y * sy, size.Z * sz);
                }
            }
        }
    }

    /// <summary>A point of the camera's space on the screen: x and y in −1…1 (the middle is the aim point), y up.</summary>
    private static Vector2 OnScreen(Vector3 p)
    {
        float t = MathF.Tan(ReferenceFov * 0.5f * MathF.PI / 180f);
        return new Vector2(p.X / p.Z / (t * Aspect), p.Y / p.Z / t);
    }

    /// <summary>The carried block's corners in the camera's space: the palm cube of <c>HeldItem</c> (0.22 at z 0.16).</summary>
    private static List<Vector3> BlockCorners()
    {
        var pos = BlockCarryPose.ViewBlockPos;
        var turn = new Vector3(BlockCarryPose.ViewBlockPitch, 0f, 0f);
        float scale = ItemScale * BlockCarryPose.ViewBlockScale;
        return Corners(new Vector3(0f, 0f, 0.16f), new Vector3(0.22f)).Select(c => pos + Turn(turn, c * scale)).ToList();
    }

    /// <summary>One arm's corners in the camera's space (<paramref name="side"/> +1 right, −1 left).</summary>
    private static List<Vector3> ArmCorners(float side)
    {
        var p = BlockCarryPose.ViewArmPos;
        var e = BlockCarryPose.ViewArmEuler;
        var pos = new Vector3(side * p.X, p.Y, p.Z);
        var turn = new Vector3(e.X, side * e.Y, side * e.Z);
        var list = new List<Vector3>();
        foreach (var box in HeldHands.Fist(new Vector3(0f, 0f, 0.07f), BlockCarryPose.ViewForearm))
        {
            var centre = new Vector3(box.Position.X * side, box.Position.Y, box.Position.Z); // a left hand is the mirror image
            list.AddRange(Corners(centre, box.Size).Select(c => pos + Turn(turn, c * ItemScale)));
        }

        return list;
    }

    [Fact]
    public void TheCarriedBlock_SitsAtTheTopMiddle_AndLeavesTheAimPointFree()
    {
        var corners = BlockCorners();
        Assert.All(corners, c => Assert.True(c.Z > 0.3f, "the block is in front of the camera's near plane"));
        var screen = corners.Select(OnScreen).ToList();

        Assert.True(screen.Min(s => s.Y) > 0.25f, "the block's lower edge is well above the aim point: " + screen.Min(s => s.Y));
        Assert.True(screen.Max(s => s.Y) > 0.9f, "the block reaches the top of the view — it is over the head");
        Assert.Equal(0f, screen.Average(s => s.X), 3); // centred
        Assert.InRange(screen.Max(s => s.X) - screen.Min(s => s.X), 0.3f, 0.8f); // "that block", not a palm cube, not a wall

        // The old pose, for the record: the one-handed rest pose shifted by (−0.27, 0.25, 0.06) left the block ON the aim point.
        var old = new Vector3(0.26f - 0.27f, -0.22f + 0.25f, 0.52f + 0.06f);
        var oldScreen = Corners(new Vector3(0f, 0f, 0.16f), new Vector3(0.22f))
            .Select(c => OnScreen(old + Turn(new Vector3(6f, -10f, 4f), c * ItemScale))).ToList();
        Assert.True(oldScreen.Min(s => s.Y) < 0f && oldScreen.Max(s => s.Y) > 0f && oldScreen.Min(s => s.X) < 0f && oldScreen.Max(s => s.X) > 0f,
            "the old pose covered the aim point — the reason for #3828");
    }

    [Fact]
    public void BothArms_ComeUpFromBelowToTheBlocksLowerCorners_AndFrameTheAimPoint()
    {
        var block = BlockCorners().Select(OnScreen).ToList();
        float blockBottom = block.Min(s => s.Y), blockLeft = block.Min(s => s.X), blockRight = block.Max(s => s.X);

        foreach (float side in new[] { 1f, -1f })
        {
            var corners = ArmCorners(side);
            var visible = corners.Where(c => c.Z > 0.05f).Select(OnScreen).ToList();
            Assert.NotEmpty(visible);

            // No part of an arm crosses the middle of the view: the aim point and what is aimed at stay free.
            Assert.All(visible, s => Assert.True(s.X * side > 0.12f, $"an arm reaches into the middle of the view: {s}"));

            // The fist is up at the block's lower corner …
            var fist = HeldHands.Fist(new Vector3(0f, 0f, 0.07f), BlockCarryPose.ViewForearm)[2];
            var p = BlockCarryPose.ViewArmPos;
            var e = BlockCarryPose.ViewArmEuler;
            var fistAt = OnScreen(new Vector3(side * p.X, p.Y, p.Z)
                                  + Turn(new Vector3(e.X, side * e.Y, side * e.Z), new Vector3(fist.Position.X * side, fist.Position.Y, fist.Position.Z) * ItemScale));
            Assert.InRange(fistAt.Y, blockBottom - 0.25f, blockBottom + 0.25f);
            Assert.InRange(fistAt.X * side, (side > 0f ? blockRight : -blockLeft) - 0.15f, (side > 0f ? blockRight : -blockLeft) + 0.25f);

            // … and the forearm runs out of the view at its lower edge: its open end is never seen.
            Assert.True(visible.Min(s => s.Y) < -1f || corners.Any(c => c.Z <= 0.05f), "the forearm's end hangs in the view");
        }

        // The two arms are mirror images.
        var right = ArmCorners(1f);
        var left = ArmCorners(-1f);
        Assert.Equal(right.Count, left.Count);
        Assert.All(right, r => Assert.Contains(left, l => Vector3.Distance(l, new Vector3(-r.X, r.Y, r.Z)) < 1e-4f));
    }

    [Fact]
    public void TheView_UsesThePose_AndBuildsTheArmsOnlyWhileTheBlockIsUp()
    {
        string view = Script("Viewmodel.cs");
        Assert.Contains("var posOff = RestPos + _stanceShift + CarryShift() + bob;", view);
        Assert.Contains("rot += CarryTurn();", view);
        Assert.Contains("PoseCarry(shown: true);", view);
        Assert.Equal(2, view.Split("PoseCarry(shown: false);").Length - 1); // hidden hands, and the climb's hands

        string carry = Script("Viewmodel.Carry.cs");
        Assert.Contains("BlockCarryPose.ViewBlockPos", carry);
        Assert.Contains("BlockCarryPose.ViewBlockPitch", carry);
        Assert.Contains("ItemScale * Mathf.Lerp(1f, BlockCarryPose.ViewBlockScale, up)", carry);
        Assert.Contains("_grip.Hand.SetActive(up <= 0f);", carry);                  // the one hand under the block goes
        Assert.Contains("HeldItem.BuildCarryArm(transform, tint, left: false)", carry);
        Assert.Contains("HeldItem.BuildCarryArm(transform, tint, left: true)", carry);
        Assert.Contains("Destroy(_carryArmR.gameObject);", carry);                   // down again: no arms left in the view
        Assert.Contains("new Vector3(e.X, side * e.Y, side * e.Z)", carry);          // the left arm mirrors yaw and roll
        Assert.Contains("HeldHands.Fist(new System.Numerics.Vector3(0f, 0f, 0.07f), BlockCarryPose.ViewForearm)", Script("HeldItem.cs"));
    }
}
