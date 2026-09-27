// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The monorail hover train (#2113, Justus' idea, Marcel's decisions 2026-09-27): pylons the player places, a
/// glowing <b>energy line</b> the game spawns between linked pylons (data, not blocks — a Catmull-Rom spline through the
/// pylon tops, see <see cref="RailSpline"/>), and a train of authored wagons that hovers along the line, on autopilot
/// with no driver. Riders <b>walk inside the moving train</b> — the wagon is their reference frame — or sit for the view.
/// Every number both sides must agree on lives here, Unity-free: the server drives the train and derives every rider's
/// world position from the wagon pose plus their local offset; every client draws the same wagons at the same poses.
/// </summary>
public static class RailRules
{
    /// <summary>The blocks and items of the system.</summary>
    public const string PylonBlockKey = "rail_pylon";
    public const string StopBlockKey = "rail_stop";
    public const string LinkerItemKey = "rail_linker";
    public const string CabItemKey = "rail_cab";
    public const string PostBlockKey = "rail_post";

    /// <summary>The wagon items, in the order the dealer sells them; the cab is the head of every train.</summary>
    public static readonly string[] WagonItems = { CabItemKey, "wagon_seats", "wagon_sleeper", "wagon_bar" };

    /// <summary>A pylon links itself to the previously placed pylon of the same builder when it stands within this many
    /// blocks and the line would not bend more than <see cref="AutoLinkMaxAngleDeg"/> against the line so far.</summary>
    public const float AutoLinkRange = 32f;
    public const float AutoLinkMaxAngleDeg = 30f;

    /// <summary>The linker gadget couples any two pylons within this many blocks (a branch the auto-link refused).</summary>
    public const float LinkerRange = 48f;

    /// <summary>A pylon carries at most two links: a line is a path or a loop, never a fork (a fork would need a switch).</summary>
    public const int MaxLinksPerPylon = 2;

    /// <summary>A line holds at most this many pylons.</summary>
    public const int MaxPylonsPerLine = 64;

    /// <summary>How high over the line's centre the wagon floor hovers, and the wagon's box (length along the line,
    /// width across it, height over the floor) — the clearance check and the rider's walking room use the same box.</summary>
    public const float HoverHeight = 0.5f;
    public const float WagonLength = 6f;
    public const float WagonGap = 0.6f;
    public const float WagonWidth = 3f;
    public const float WagonHeight = 3.2f;

    /// <summary>The line runs this far over the pylon's top cell (the tube's centre); the wagon floor sits <see cref="HoverHeight"/> above it.</summary>
    public const float LineRise = 1.2f;

    /// <summary>The three speed settings (blocks per second along the line).</summary>
    public static readonly float[] SpeedTable = { 4f, 8f, 12f };

    /// <summary>How long an autopilot train waits at a stop before it moves on (a signal on the stop's port departs it early).</summary>
    public const double StopHaltSeconds = 8.0;

    /// <summary>A stop block counts for the line when it stands within this many blocks of the line.</summary>
    public const float StopReach = 4f;

    /// <summary>Boarding reach from any wagon, and the owner's stow reach.</summary>
    public const float BoardRange = 4f;
    public const float StowRange = 5f;

    /// <summary>A rider's local offset is clamped inside the wagon: half the width less the wall, half the length, the height.</summary>
    public const float RiderHalfWidth = 1.1f;
    public const float RiderHalfLength = WagonLength * 0.5f - 0.3f;
    public const float RiderMaxHeight = WagonHeight - 0.6f;

    /// <summary>A rider more than this far outside the wagon's box has walked off (a halted train at a stop): they are on foot.</summary>
    public const float LeaveMargin = 0.6f;

    /// <summary>How far beside the wagon a leaving rider is set down.</summary>
    public const float ExitSide = 2.6f;

    /// <summary>The speed of a train's clock: how often the server tells everyone where it is (seconds).</summary>
    public const double BroadcastSeconds = 0.2;

    /// <summary>Whether two pylon tops may be auto-linked: near enough, and — given the previous direction of the line
    /// (<paramref name="prevDir"/>, zero when the line has one pylon) — not a sharper turn than the rule allows.</summary>
    public static bool AutoLinkFits(Vector3f from, Vector3f to, Vector3f prevDir)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float d2 = dx * dx + dy * dy + dz * dz;
        if (d2 < 4f || d2 > AutoLinkRange * AutoLinkRange)
        {
            return false;
        }

        float pl = (float)Math.Sqrt(prevDir.X * prevDir.X + prevDir.Z * prevDir.Z);
        if (pl < 1e-3f)
        {
            return true; // the first link of a line
        }

        float hl = (float)Math.Sqrt(dx * dx + dz * dz);
        if (hl < 1e-3f)
        {
            return false; // straight up: no line
        }

        float cos = (prevDir.X * dx + prevDir.Z * dz) / (pl * hl);
        return cos >= Math.Cos(AutoLinkMaxAngleDeg * Math.PI / 180.0);
    }

    /// <summary>The seat spots of a wagon kind (wagon-local: X across, Y up from the floor, Z along, +Z toward the cab).</summary>
    public static IReadOnlyList<Vector3f> SeatOffsets(string wagonItem) => wagonItem switch
    {
        "wagon_seats" => new[] { new Vector3f(-0.9f, 0f, 1.6f), new Vector3f(0.9f, 0f, 1.6f), new Vector3f(-0.9f, 0f, -0.4f), new Vector3f(0.9f, 0f, -0.4f), new Vector3f(-0.9f, 0f, -2.2f), new Vector3f(0.9f, 0f, -2.2f) },
        "wagon_sleeper" => new[] { new Vector3f(-0.9f, 0f, 1.2f), new Vector3f(0.9f, 0f, 1.2f), new Vector3f(-0.9f, 0f, -1.4f), new Vector3f(0.9f, 0f, -1.4f) },
        "wagon_bar" => new[] { new Vector3f(-0.9f, 0f, -1.8f), new Vector3f(0.9f, 0f, -1.8f) },
        CabItemKey => new[] { new Vector3f(0f, 0f, 1.8f) }, // the driver's seat
        _ => Array.Empty<Vector3f>(),
    };

    /// <summary>The wagon-local point of a rider clamped into the wagon's box (the walls and the roof are the limits).</summary>
    public static Vector3f ClampLocal(Vector3f local)
        => new(Math.Clamp(local.X, -RiderHalfWidth, RiderHalfWidth), Math.Clamp(local.Y, 0f, RiderMaxHeight), Math.Clamp(local.Z, -RiderHalfLength, RiderHalfLength));

    /// <summary>Whether a rider's reported local point has left the wagon — walked out of the open side or the end.</summary>
    public static bool OutsideWagon(Vector3f local)
        => Math.Abs(local.X) > RiderHalfWidth + LeaveMargin || Math.Abs(local.Z) > RiderHalfLength + LeaveMargin || local.Y < -1.5f || local.Y > RiderMaxHeight + 1.5f;

    /// <summary>The arc position of wagon <paramref name="index"/> (0 = the cab) behind the train's own arc.</summary>
    public static float WagonArc(float trainArc, int index, int direction)
        => trainArc - direction * index * (WagonLength + WagonGap);

    /// <summary>A world point from a wagon pose (its centre on the line, its heading in radians about +Y) and a local offset:
    /// local X across (right of travel), Y up, Z along (toward the cab / the direction of travel).</summary>
    public static Vector3f LocalToWorld(Vector3f wagonPos, float yaw, Vector3f local)
    {
        float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
        // The heading's forward is (c, 0, s); right is (s, 0, -c).
        return new Vector3f(
            wagonPos.X + local.Z * c + local.X * s,
            wagonPos.Y + local.Y,
            wagonPos.Z + local.Z * s - local.X * c);
    }

    /// <summary>The inverse of <see cref="LocalToWorld"/>: a world point (unwrapped beside the wagon) into the wagon's frame.</summary>
    public static Vector3f WorldToLocal(Vector3f wagonPos, float yaw, Vector3f world)
    {
        float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
        float dx = world.X - wagonPos.X, dz = world.Z - wagonPos.Z;
        return new Vector3f(dx * s - dz * c, world.Y - wagonPos.Y, dx * c + dz * s);
    }

    /// <summary>The frame id a rider's pose is reported in: the train and the wagon they are in.</summary>
    public static string FrameId(string trainId, int wagon) => trainId + ":" + wagon;

    /// <summary>Splits a frame id; false for an empty id or one that is not a train frame.</summary>
    public static bool TryParseFrame(string frameId, out string trainId, out int wagon)
    {
        trainId = string.Empty;
        wagon = 0;
        if (string.IsNullOrEmpty(frameId))
        {
            return false;
        }

        int colon = frameId.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(frameId.Substring(colon + 1), out wagon) || wagon < 0)
        {
            return false;
        }

        trainId = frameId.Substring(0, colon);
        return true;
    }
}
