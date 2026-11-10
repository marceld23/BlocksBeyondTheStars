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
/// above or one below, a few spots along the door; then the other side; then behind the train. The old point is the last
/// resort. No chunk is loaded (a rider's surroundings are streamed anyway).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The door opening spans this far along the wagon either side of its middle, less a hand's breadth off the
    /// jambs (the wagon's door is the grid's z 2..3, local −1 … +1).</summary>
    private const float RailExitDoorHalf = 0.8f;

    /// <summary>How far out from the wagon's middle a set-down spot is tried (the wall stands at 1.5).</summary>
    private static readonly float[] RailExitOutward = { 2.3f, 3.1f };

    /// <summary>A standing spot keeps at least this far off its cell's edges, so the body does not poke into a neighbour.</summary>
    private const float RailExitCellInset = 0.3f;

    /// <summary>The spot a rider leaving by F is set down at (see the class summary).</summary>
    private Vector3f RailExitSpot(ServerTrain t, RailLine line, RailRider rider)
    {
        var (pos, yaw) = WagonPose(t, line, rider.Wagon);
        float side = RailExitPreferredSide(t, pos, yaw, rider);
        float zc = Math.Clamp(rider.Local.Z, -RailExitDoorHalf, RailExitDoorHalf);
        var along = new List<float> { zc, 0f, -RailExitDoorHalf, RailExitDoorHalf }.Distinct().ToList();
        foreach (float s in new[] { side, -side })
        {
            foreach (float outward in RailExitOutward)
            {
                foreach (float z in along)
                {
                    if (RailStandSpotNear(pos, yaw, new Vector3f(s * outward, 0f, z)) is { } spot)
                    {
                        return spot;
                    }
                }
            }
        }

        // Behind the train: past the open end of the last wagon.
        var (lastPos, lastYaw) = WagonPose(t, line, Math.Max(0, t.Wagons.Count - 1));
        foreach (float back in new[] { 1.2f, 2.0f })
        {
            if (RailStandSpotNear(lastPos, lastYaw, new Vector3f(0f, 0f, -((RailRules.WagonLength * 0.5f) + back))) is { } spot)
            {
                return spot;
            }
        }

        // Nothing free: today's point beside the wagon, on the side the rider stood, at the floor.
        float old = rider.Local.X >= 0f ? RailRules.ExitSide : -RailRules.ExitSide;
        return RailWrap(RailRules.LocalToWorld(pos, yaw, new Vector3f(old, 0f, Math.Clamp(rider.Local.Z, -RailRules.RiderHalfLength, RailRules.RiderHalfLength))));
    }

    /// <summary>+1 or −1: the side of the halt stop when the train is halted at one, else the side the rider stood on.</summary>
    private float RailExitPreferredSide(ServerTrain t, Vector3f pos, float yaw, RailRider rider)
    {
        if (t.Halted && t.HaltStop is { } stop)
        {
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
