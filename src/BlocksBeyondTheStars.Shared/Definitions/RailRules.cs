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
    /// blocks and the line would not bend more than <see cref="AutoLinkMaxAngleDeg"/> against the line so far — and when
    /// that one does not take it (none, out of range, refused), to the best free line end in range (#3256).</summary>
    public const float AutoLinkRange = 32f;
    public const float AutoLinkMaxAngleDeg = 30f;

    /// <summary>The fallback auto-link's ranking (#3256): every degree a free line end's line would bend to reach the new
    /// pylon counts as this many blocks of extra distance — so the straight continuation of a line wins over the nearer end
    /// of a parallel line that would have to swerve (the full 30° costs as much as 15 blocks).</summary>
    public const float AutoLinkBendCostPerDegree = 0.5f;

    /// <summary>The linker gadget couples any two pylons within this many blocks (a branch the auto-link refused). 64 since
    /// #3207, the game's one design radius.</summary>
    public const float LinkerRange = 64f;

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

    /// <summary>The three speed settings' <b>top speeds</b> (blocks per second along the line). Since #3444 a train does
    /// not run at its setting's speed: it eases in, cruises by the length of its leg and brakes into each stop
    /// (<see cref="RailMotion"/>) — the setting is the most it ever goes.</summary>
    public static readonly float[] SpeedTable = { 8f, 16f, 24f };

    /// <summary>The speed setting a freshly placed cab starts at (#3444): the middle one, top speed 16 blocks/s.</summary>
    public const int NewCabSpeed = 2;

    /// <summary>How long an autopilot train waits at a stop before it moves on (a signal on the stop's port departs it early).</summary>
    public const double StopHaltSeconds = 8.0;

    /// <summary>The owner id of a <b>public</b> train (#2125): the generated intercity line's train belongs to nobody. No
    /// player id is ever empty (a player id is the player's name), so the empty owner is an unambiguous mark on the wire
    /// (<c>NetTrain.OwnerId</c>) and in the save — no new field, no protocol change.</summary>
    public const string PublicOwnerId = "";

    /// <summary>Whether a train with this owner is a public one (#2125): anyone may ride it, nobody may steer, halt, stow
    /// or couple it.</summary>
    public static bool IsPublic(string? ownerId) => string.IsNullOrEmpty(ownerId);

    /// <summary>How long a public train waits at each station (#2125) — long enough for a player to walk up and board.</summary>
    public const double PublicStopHaltSeconds = 30.0;

    /// <summary>The public train's speed setting (1..3, see <see cref="SpeedTable"/>): the middle one, top speed 16
    /// blocks/s (#3444 — it runs the same speed profile as every train).</summary>
    public const int PublicTrainSpeed = 2;

    /// <summary>The public train's wagons from the cab backwards (#2125): the cab and one passenger wagon with six seats.</summary>
    public static readonly string[] PublicTrainWagons = { CabItemKey, "wagon_seats" };

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

    /// <summary>How many degrees the line bends (horizontally) when it runs on from <paramref name="from"/> to
    /// <paramref name="to"/> after arriving along <paramref name="prevDir"/>: 0 for the first link of a line (no previous
    /// direction) or a step straight up. Both points must be unwrapped against each other (the caller's frame).</summary>
    public static float AutoLinkBendDegrees(Vector3f from, Vector3f to, Vector3f prevDir)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        float pl = (float)Math.Sqrt(prevDir.X * prevDir.X + prevDir.Z * prevDir.Z);
        float hl = (float)Math.Sqrt(dx * dx + dz * dz);
        if (pl < 1e-3f || hl < 1e-3f)
        {
            return 0f;
        }

        float cos = Math.Clamp((prevDir.X * dx + prevDir.Z * dz) / (pl * hl), -1f, 1f);
        return (float)(Math.Acos(cos) * 180.0 / Math.PI);
    }

    /// <summary>The fallback auto-link's cost of a free line end (#3256): its distance plus its bend at
    /// <see cref="AutoLinkBendCostPerDegree"/> — the lowest cost that also passes every link rule wins.</summary>
    public static float AutoLinkCost(float distance, float bendDegrees) => distance + bendDegrees * AutoLinkBendCostPerDegree;

    /// <summary>The seat spots of a wagon kind (wagon-local: X across, Y up from the floor, Z along, +Z toward the cab) —
    /// each on its own bench or berth cell of <see cref="RailWagonLayout"/> (#3471): 8 in a seat wagon, 4 berths in the
    /// sleeper, 4 at the bar, the cab's driver's place.</summary>
    public static IReadOnlyList<Vector3f> SeatOffsets(string wagonItem) => RailWagonLayout.Seats(wagonItem);

    /// <summary>The wagon-local point of a rider clamped into the wagon's box (the walls and the roof are the limits).</summary>
    public static Vector3f ClampLocal(Vector3f local)
        => new(Math.Clamp(local.X, -RiderHalfWidth, RiderHalfWidth), Math.Clamp(local.Y, 0f, RiderMaxHeight), Math.Clamp(local.Z, -RiderHalfLength, RiderHalfLength));

    /// <summary>Whether a rider's reported local point has left the wagon — walked out of the open side or the end.</summary>
    public static bool OutsideWagon(Vector3f local)
        => Math.Abs(local.X) > RiderHalfWidth + LeaveMargin || Math.Abs(local.Z) > RiderHalfLength + LeaveMargin || local.Y < -1.5f || local.Y > RiderMaxHeight + 1.5f;

    /// <summary>The arc position of wagon <paramref name="index"/> (0 = the cab) behind the train's own arc (the cab's).
    /// "Behind" is the train's <b>facing</b> (#2741) — the way the cab points, fixed when the train is placed — not its
    /// direction of travel: a train that reverses at a line end keeps every wagon where it is and pushes from the back.</summary>
    public static float WagonArc(float trainArc, int index, int facing)
        => trainArc - Sign(facing) * index * (WagonLength + WagonGap);

    /// <summary>The arc distance from the cab's centre to the last wagon's centre (0 for a lone cab).</summary>
    public static float TrainSpan(int wagonCount) => Math.Max(0, wagonCount - 1) * (WagonLength + WagonGap);

    /// <summary>The range the train's own arc (the cab's) may take on an open line of length <paramref name="total"/> so
    /// every wagon stays on the line (#2741): a train facing +1 hangs its wagons toward arc 0, so the cab runs between
    /// the train's span and the end; one facing −1 the other way round. A train longer than its line pins the cab at
    /// the far end (the wagons behind are clamped onto the line's start).</summary>
    public static (float Lo, float Hi) CabRange(float total, float span, int facing)
    {
        total = Math.Max(0f, total);
        span = Math.Max(0f, span);
        float lo = facing >= 0 ? span : 0f;
        float hi = facing >= 0 ? total : total - span;
        if (hi < lo)
        {
            float pin = facing >= 0 ? total : 0f;
            return (pin, pin);
        }

        return (lo, hi);
    }

    /// <summary>The arc of the wagon at the head of the train in its direction of travel (#2741): the cab when it runs the
    /// way it faces, the last wagon (<paramref name="span"/> behind the cab) when it runs backwards. The autopilot halts
    /// this wagon at a stop.</summary>
    public static float LeadArc(float arc, int direction, int facing, float span)
        => Sign(direction) == Sign(facing) ? arc : arc - Sign(facing) * span;

    /// <summary>The train's arc a halt at a stop puts it on (#2741): the leading wagon (see <see cref="LeadArc"/>)
    /// stands at <paramref name="stopArc"/>. Since #3463 the autopilot centres the train on a stop instead
    /// (<see cref="MiddleArc"/>, <see cref="HaltMiddleArc"/>).</summary>
    public static float ArcForLeadAt(float stopArc, int direction, int facing, float span)
        => Sign(direction) == Sign(facing) ? stopArc : stopArc + Sign(facing) * span;

    /// <summary>#3463: the arc of the train's middle — half its span behind the cab, whichever way it runs. A halt at a stop
    /// centres the train on the stop (Lyxette: "with the stop at the end of the station the train is all in or all out by
    /// its direction; in the middle it only ever pulls in halfway").</summary>
    public static float MiddleArc(float arc, int facing, float span) => arc - Sign(facing) * Math.Max(0f, span) * 0.5f;

    /// <summary>#3463: the train's (the cab's) arc that puts its middle at <paramref name="middleArc"/>.</summary>
    public static float ArcForMiddleAt(float middleArc, int facing, float span) => middleArc + Sign(facing) * Math.Max(0f, span) * 0.5f;

    /// <summary>#3463: where a halt at the stop at <paramref name="stopArc"/> puts the train's middle — the stop itself, but
    /// on an open line never so near an end that a wagon would hang past it (the middle stays half the train and half a
    /// wagon in from each end; the line's middle when the train is longer than that). A stop a few blocks from the line's
    /// end — the intercity station's, a station at a dead end — therefore holds the whole train next to it. A loop takes the
    /// stop's arc as it is.</summary>
    public static float HaltMiddleArc(float stopArc, float span, float total, bool closed)
    {
        if (closed)
        {
            return stopArc;
        }

        float inset = (Math.Max(0f, span) * 0.5f) + (WagonLength * 0.5f);
        float lo = inset, hi = total - inset;
        return hi < lo ? total * 0.5f : Math.Clamp(stopArc, lo, hi);
    }

    /// <summary>Runs a train's arc <paramref name="distance"/> along its <paramref name="direction"/> (#2741). A loop
    /// wraps. On an open line the arc stays inside <see cref="CabRange"/>: reaching a bound flips the direction and the
    /// rest of the step runs back the other way — the arc never jumps and the wagons never change places or turn, so
    /// riders ride through the reversal without a jump. An arc found outside the range (a coupled wagon, a shortened
    /// line) is first brought back into it. Returns the new arc and direction; non-finite or negative distances move
    /// nothing.</summary>
    public static (float Arc, int Direction) Advance(float arc, int direction, int facing, float distance, float total, float span, bool closed)
    {
        int dir = direction < 0 ? -1 : 1;
        if (float.IsNaN(arc) || float.IsInfinity(arc))
        {
            arc = 0f;
        }

        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
        {
            distance = 0f;
        }

        if (total <= 0f)
        {
            return (0f, dir);
        }

        if (closed)
        {
            float s = (arc + dir * distance) % total;
            return (s < 0f ? s + total : s, dir);
        }

        var (lo, hi) = CabRange(total, span, facing);
        arc = Math.Clamp(arc, lo, hi);
        float len = hi - lo;
        if (len <= 0f)
        {
            return (lo, dir); // no room to run: the train stands at its one spot
        }

        // Fold whole round trips away, then bounce at most twice.
        distance %= 2f * len;
        for (int bounce = 0; bounce < 3 && distance > 0f; bounce++)
        {
            float room = dir > 0 ? hi - arc : arc - lo;
            if (distance < room)
            {
                arc += dir * distance;
                distance = 0f;
            }
            else
            {
                distance -= room;
                arc = dir > 0 ? hi : lo;
                dir = -dir;
            }
        }

        return (Math.Clamp(arc, lo, hi), dir);
    }

    /// <summary>The facing a client poses a train's wagons by (#2741): the server's facing, or — 0 from an older server
    /// that sent none, whose wagons followed the direction of travel — the direction.</summary>
    public static int PoseFacing(int facing, int direction) => facing != 0 ? Sign(facing) : Sign(direction);

    private static int Sign(int value) => value < 0 ? -1 : 1;

    /// <summary>A world point from a wagon pose (its centre on the line, its heading in radians about +Y) and a local offset:
    /// local X across (right of the way the wagon faces), Y up, Z along (toward the cab — the train's facing, which is the
    /// direction of travel unless the train is running backwards after a reversal).</summary>
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
