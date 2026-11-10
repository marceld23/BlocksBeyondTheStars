// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3458 — where a rider who presses F is set down. The wagon floor hovers off the block grid (the pylon's top cell
/// + <see cref="RailRules.LineRise"/> + <see cref="RailRules.HoverHeight"/>), so a platform one block above the floor a
/// player sees is ~1.3 blocks up — too high to step, and the door is only two blocks tall, so nobody could jump out onto
/// it. F used to drop the rider <see cref="RailRules.ExitSide"/> beside the wagon at floor height whatever stood there.
/// Now <see cref="RailExitSpot"/> looks for a real standing spot (support below, two cells of headroom) outside the door:
/// on the halt stop's side first (else the side the rider stood on), at the floor's height or the nearest one up to two
/// above or one below, a few spots along the door; then the other side; then one ring farther out on either side — never
/// behind the train, where the wagons would run into the rider. The old point is the last resort. No chunk is loaded (a
/// rider's surroundings are streamed anyway).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The door opening spans this far along the wagon either side of its middle, less a hand's breadth off the
    /// jambs (the wagon's door is the grid's z 2..3, local −1 … +1).</summary>
    private const float RailExitDoorHalf = 0.8f;

    /// <summary>How far out from the wagon's middle a set-down spot is tried (the wall stands at 1.5) — the first two on both
    /// sides before the third, so the other side's door beats a long step on this one.</summary>
    private static readonly float[] RailExitOutward = { 2.3f, 3.1f };

    /// <summary>The last ring tried on either side when both doors are blocked close by (a cutting, a narrow platform).
    /// Never behind the train: on the line's axis a rider would stand in the wagons' path.</summary>
    private const float RailExitFarOutward = 4.1f;

    /// <summary>A standing spot keeps at least this far off its cell's edges, so the body does not poke into a neighbour.</summary>
    private const float RailExitCellInset = 0.3f;

    /// <summary>The spot a rider leaving by F is set down at (see the class summary).</summary>
    private Vector3f RailExitSpot(ServerTrain t, RailLine line, RailRider rider)
    {
        var (pos, yaw) = WagonPose(t, line, rider.Wagon);
        float side = RailExitPreferredSide(t, line, rider);
        float zc = Math.Clamp(rider.Local.Z, -RailExitDoorHalf, RailExitDoorHalf);
        var along = new List<float> { zc, 0f, -RailExitDoorHalf, RailExitDoorHalf }.Distinct().ToList();
        foreach (float s in new[] { side, -side })
        {
            foreach (float outward in RailExitOutward)
            {
                if (RailStandSpotAlong(pos, yaw, s * outward, along) is { } spot)
                {
                    return spot;
                }
            }
        }

        // Both doors blocked close by: one ring farther out on either side.
        foreach (float s in new[] { side, -side })
        {
            if (RailStandSpotAlong(pos, yaw, s * RailExitFarOutward, along) is { } spot)
            {
                return spot;
            }
        }

        // Nothing free: today's point beside the wagon, on the side the rider stood, at the floor.
        float old = rider.Local.X >= 0f ? RailRules.ExitSide : -RailRules.ExitSide;
        return RailWrap(RailRules.LocalToWorld(pos, yaw, new Vector3f(old, 0f, Math.Clamp(rider.Local.Z, -RailRules.RiderHalfLength, RailRules.RiderHalfLength))));
    }

    /// <summary>The first standing spot at wagon-local <paramref name="x"/> along the door (<paramref name="along"/>), or null.</summary>
    private Vector3f? RailStandSpotAlong(Vector3f pos, float yaw, float x, List<float> along)
    {
        foreach (float z in along)
        {
            if (RailStandSpotNear(pos, yaw, new Vector3f(x, 0f, z)) is { } spot)
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>+1 or −1: the side of the halt stop when the train is halted at one, else the side the rider stood on. The
    /// stop's side is read in the line's own frame at the stop (its pose facing the train's way) — every wagon of the train
    /// shares that sense of "right", while a rear wagon's own frame can turn away from a stop on a curve.</summary>
    private float RailExitPreferredSide(ServerTrain t, RailLine line, RailRider rider)
    {
        if (t.Halted && t.HaltStop is { } stop && line.Stops.FirstOrDefault(s => s.Cell == stop) is var st && st.Cell == stop)
        {
            var (pos, yaw) = line.Spline.PoseAt(line.Spline.NormalizeArc(st.Arc), t.Facing);
            var at = Unwrapped(pos, new Vector3f(stop.X + 0.5f, pos.Y, stop.Z + 0.5f));
            float x = RailRules.WorldToLocal(pos, yaw, at).X;
            if (Math.Abs(x) > 0.25f)
            {
                return x >= 0f ? 1f : -1f;
            }
        }

        return rider.Local.X >= 0f ? 1f : -1f;
    }

    /// <summary>A standing spot in the cell under a wagon-local point: feet at the floor's height or the nearest one from
    /// one below to two above it, support below and two cells of headroom. Null when none of them stands.</summary>
    private Vector3f? RailStandSpotNear(Vector3f wagonPos, float yaw, Vector3f local)
    {
        var w = RailWrap(RailRules.LocalToWorld(wagonPos, yaw, local));
        int cx = (int)Math.Floor(w.X), cz = (int)Math.Floor(w.Z);
        float floorY = wagonPos.Y;
        int baseY = (int)Math.Floor(floorY);
        foreach (int fy in Enumerable.Range(baseY - 1, 4).OrderBy(y => Math.Abs(y - floorY)))
        {
            if (WithinBuildHeight(fy) && StandableAt(cx, fy, cz, headroom: 2))
            {
                float x = cx + Math.Clamp(w.X - cx, RailExitCellInset, 1f - RailExitCellInset);
                float z = cz + Math.Clamp(w.Z - cz, RailExitCellInset, 1f - RailExitCellInset);
                return new Vector3f(x, fy, z);
            }
        }

        return null;
    }

    private Vector3f RailWrap(Vector3f w)
        => new((float)WorldConstants.WrapX((double)w.X, _world.Circumference), w.Y, (float)WorldConstants.WrapZ((double)w.Z, _world.Circumference));
}
