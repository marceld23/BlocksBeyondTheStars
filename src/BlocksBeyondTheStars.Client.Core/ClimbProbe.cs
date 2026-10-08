// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The wall a climber holds (#2188): the axis-aligned direction from the climber INTO the wall, and how the
    /// wall holds them.</summary>
    public readonly struct WallHold
    {
        public WallHold(int dirX, int dirZ, ClimbSurface surface)
        {
            DirX = dirX;
            DirZ = dirZ;
            Surface = surface;
        }

        /// <summary>−1, 0 or 1: the wall lies this way along X (exactly one of <see cref="DirX"/>/<see cref="DirZ"/> is set).</summary>
        public int DirX { get; }

        /// <summary>−1, 0 or 1: the wall lies this way along Z.</summary>
        public int DirZ { get; }

        /// <summary>How the wall holds right now (the worse of the hand and knee holds).</summary>
        public ClimbSurface Surface { get; }

        /// <summary>The heading that faces the wall, in degrees (0 = +Z, 90 = +X — the engine's yaw convention).</summary>
        public float FacingYaw => (float)(Math.Atan2(DirX, DirZ) * 180.0 / Math.PI);

        public WallHold WithSurface(ClimbSurface surface) => new WallHold(DirX, DirZ, surface);
    }

    /// <summary>What is in front of a hanging climber this frame.</summary>
    public enum WallAhead : byte
    {
        /// <summary>Nothing to hold any more (climbed off the side, the block was mined, …) — the climber lets go.</summary>
        Lost = 0,

        /// <summary>A wall at hand height — keep climbing.</summary>
        Wall = 1,

        /// <summary>Wall at the knees but open at the hands: the top edge is within reach (pull up if there is room).</summary>
        Ledge = 2,
    }

    /// <summary>
    /// The wall-climbing probe (#2188, #2190): reads the cells around a climber and answers "is there a wall to grab",
    /// "what is in front of me now", "does the wall go on sideways", "is my head under an overhang" and "where would a
    /// pull-up over this ledge end". Pure and Unity-free, so the rules are unit-tested headless; the player controller
    /// feeds it two cell lookups — how a cell holds THIS climber (block data + gear + "has a collider") and whether a
    /// cell has a collider at all (for headroom). Positions are the capsule's feet (bottom centre), in world units.
    /// </summary>
    public sealed class ClimbProbe
    {
        /// <summary>Knee sample height above the feet.</summary>
        public const float KneeHeight = 0.4f;

        /// <summary>Hand sample height above the feet (where a climber grips).</summary>
        public const float HandHeight = 1.35f;

        /// <summary>From the capsule's centre line to the wall sample: radius (0.35) + skin (0.03) + a little slack, so a
        /// climber pressed against the wall reads the wall cell, not their own.</summary>
        public const float WallReach = 0.6f;

        /// <summary>A grab needs the push to point at the wall within ~60° (cos 60° = 0.5).</summary>
        public const float MinApproach = 0.5f;

        /// <summary>The standing capsule's height.</summary>
        public const float StandHeight = 1.8f;

        /// <summary>The capsule's radius plus a hair, for "does the body fit there".</summary>
        public const float BodyRadius = 0.37f;

        /// <summary>A ledge no higher than this above the feet is a step — the auto-step and an ordinary jump take it.</summary>
        public const float StepHeight = 0.6f;

        /// <summary>The highest ledge a climber's hands can still pull the body over.</summary>
        public const float MaxPullUp = 1.75f;

        /// <summary>A jump still rising faster than this (m/s) grabs no wall yet (#2385): it carries the climber to its
        /// top first, and the grab happens there or on the way down.</summary>
        public const float GrabRiseLimit = 0.5f;

        /// <summary>How far a ledge must lie above the top of a jump before the jump counts as falling short of it
        /// (#2385) — a hair, so float noise at the very top never turns a clearing jump into a pull-up.</summary>
        public const float JumpShortfall = 0.05f;

        private readonly Func<int, int, int, ClimbSurface> _holdAt;
        private readonly Func<int, int, int, bool> _solidAt;

        /// <param name="holdAt">How the cell holds this climber (<see cref="ClimbSurface.None"/> = no hold).</param>
        /// <param name="solidAt">Whether the cell has a collider (anything the body cannot pass through).</param>
        public ClimbProbe(Func<int, int, int, ClimbSurface> holdAt, Func<int, int, int, bool> solidAt)
        {
            _holdAt = holdAt;
            _solidAt = solidAt;
        }

        private static int Floor(float v) => (int)Math.Floor(v);

        /// <summary>How the wall cell <see cref="WallReach"/> along (<paramref name="dirX"/>, <paramref name="dirZ"/>) at
        /// <paramref name="height"/> above the feet holds the climber.</summary>
        public ClimbSurface HoldAt(float x, float y, float z, int dirX, int dirZ, float height)
            => _holdAt(Floor(x + (dirX * WallReach)), Floor(y + height), Floor(z + (dirZ * WallReach)));

        /// <summary>The worse of two holds (a slippery knee hold makes the whole grip slippery).</summary>
        public static ClimbSurface Worse(ClimbSurface a, ClimbSurface b) => a < b ? a : b;

        /// <summary>
        /// The wall a climber pushing along (<paramref name="wishX"/>, <paramref name="wishZ"/>) can grab: the axis the
        /// push points at most (then the other one), within <see cref="MinApproach"/>, with a hold at the knees AND the
        /// hands. Two cells are required on purpose — a one-block step is never a wall, it is jumped onto as before.
        /// </summary>
        public bool TryFindWall(float x, float y, float z, float wishX, float wishZ, out WallHold hold)
        {
            hold = default;
            for (int pass = 0; pass < 2; pass++)
            {
                if (!Facing(pass, wishX, wishZ, out int dx, out int dz))
                {
                    continue;
                }

                var knee = HoldAt(x, y, z, dx, dz, KneeHeight);
                var hands = HoldAt(x, y, z, dx, dz, HandHeight);
                if (knee != ClimbSurface.None && hands != ClimbSurface.None)
                {
                    hold = new WallHold(dx, dz, Worse(knee, hands));
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a climber moving up at <paramref name="verticalSpeed"/> may grab a wall now (#2385): not while a
        /// jump is still rising fast.</summary>
        public static bool MayGrab(float verticalSpeed) => verticalSpeed <= GrabRiseLimit;

        /// <summary>
        /// The lowest ledge a jump rising at <paramref name="verticalSpeed"/> under <paramref name="gravity"/> pulls the
        /// climber over (#2385): higher than a step, and higher than what the jump still rises by itself
        /// (v² / 2g, plus <see cref="JumpShortfall"/>). A ledge the jump clears is landed on, not pulled over; one it falls
        /// short of is pulled over as soon as the hands reach it, on the way up — not only at the top of the jump.
        /// </summary>
        public static float JumpPullUpMinRise(float verticalSpeed, float gravity)
        {
            float rest = verticalSpeed > 0f && gravity > 0f ? verticalSpeed * verticalSpeed / (2f * gravity) : 0f;
            return Math.Max(StepHeight, rest + JumpShortfall);
        }

        /// <summary>A pull-up straight from a jump (#2190, #2385): <see cref="TryFindLedgeAhead"/> with the rise that
        /// <see cref="JumpPullUpMinRise"/> gives for the jump's vertical speed — a jump that falls short of a two-block
        /// wall pulls over it on the way up, a one-block step stays an ordinary jump.</summary>
        public bool TryPullUpFromJump(float x, float y, float z, float wishX, float wishZ, float verticalSpeed, float gravity,
            out WallHold hold, out float targetX, out float targetY, out float targetZ)
            => TryFindLedgeAhead(x, y, z, wishX, wishZ, JumpPullUpMinRise(verticalSpeed, gravity),
                out hold, out targetX, out targetY, out targetZ);

        /// <summary>A pull-up straight from a jump (#2190): the ledge the push points at, if
        /// <see cref="TryFindLedge"/> accepts it — what makes a two-block wall crossable with a jump.</summary>
        public bool TryFindLedgeAhead(float x, float y, float z, float wishX, float wishZ, float minRise,
            out WallHold hold, out float targetX, out float targetY, out float targetZ)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (Facing(pass, wishX, wishZ, out int dx, out int dz))
                {
                    hold = new WallHold(dx, dz, ClimbSurface.Normal);
                    if (TryFindLedge(x, y, z, hold, minRise, out targetX, out targetY, out targetZ))
                    {
                        return true;
                    }
                }
            }

            hold = default;
            targetX = x;
            targetY = y;
            targetZ = z;
            return false;
        }

        /// <summary>The axis the push points at most (pass 0), then the other one (pass 1) — each only when the push
        /// points at it within <see cref="MinApproach"/>.</summary>
        private static bool Facing(int pass, float wishX, float wishZ, out int dx, out int dz)
        {
            dx = 0;
            dz = 0;
            float len = (float)Math.Sqrt((wishX * wishX) + (wishZ * wishZ));
            if (len < 0.1f)
            {
                return false;
            }

            float wx = wishX / len;
            float wz = wishZ / len;
            bool alongX = (pass == 0) == (Math.Abs(wx) >= Math.Abs(wz));
            if ((alongX ? Math.Abs(wx) : Math.Abs(wz)) < MinApproach)
            {
                return false;
            }

            if (alongX)
            {
                dx = wx >= 0f ? 1 : -1;
            }
            else
            {
                dz = wz >= 0f ? 1 : -1;
            }

            return true;
        }

        /// <summary>What a hanging climber has in front of them now (and how it holds them, via <paramref name="surface"/>).</summary>
        public WallAhead Ahead(float x, float y, float z, WallHold hold, out ClimbSurface surface)
        {
            var hands = HoldAt(x, y, z, hold.DirX, hold.DirZ, HandHeight);
            var knee = HoldAt(x, y, z, hold.DirX, hold.DirZ, KneeHeight);
            if (hands != ClimbSurface.None)
            {
                surface = knee != ClimbSurface.None ? Worse(hands, knee) : hands;
                return WallAhead.Wall;
            }

            surface = knee;
            return knee != ClimbSurface.None ? WallAhead.Ledge : WallAhead.Lost;
        }

        /// <summary>Whether the wall still holds the climber after a sideways step of (<paramref name="stepX"/>,
        /// <paramref name="stepZ"/>) — the climber stops at the edge of a wall instead of hanging on to thin air.</summary>
        public bool WallContinues(float x, float y, float z, WallHold hold, float stepX, float stepZ)
        {
            // Look one body-width past the step, so the hand reaching sideways must find wall before the body follows.
            float len = (float)Math.Sqrt((stepX * stepX) + (stepZ * stepZ));
            if (len < 1e-5f)
            {
                return true;
            }

            float ax = x + stepX + (stepX / len * (BodyRadius * 0.5f));
            float az = z + stepZ + (stepZ / len * (BodyRadius * 0.5f));
            return HoldAt(ax, y, az, hold.DirX, hold.DirZ, HandHeight) != ClimbSurface.None
                || HoldAt(ax, y, az, hold.DirX, hold.DirZ, KneeHeight) != ClimbSurface.None;
        }

        /// <summary>A solid block right above the climber's head — an overhang: no higher, sideways still works.</summary>
        public bool BlockedAbove(float x, float y, float z) => _solidAt(Floor(x), Floor(y + StandHeight + 0.1f), Floor(z));

        /// <summary>
        /// Where a pull-up over the ledge in front ends (#2190): the top of the wall column must lie more than
        /// <paramref name="minRise"/> and at most <see cref="MaxPullUp"/> above the feet, its top block must hold
        /// (glass never gives a pull-up either), the body must fit standing on top, and the climber's own column must
        /// be open all the way up. The target is the feet position on top, on the wall column's centre line.
        /// </summary>
        public bool TryFindLedge(float x, float y, float z, WallHold hold, float minRise,
            out float targetX, out float targetY, out float targetZ)
        {
            targetX = x;
            targetY = y;
            targetZ = z;
            int cx = Floor(x + (hold.DirX * WallReach));
            int cz = Floor(z + (hold.DirZ * WallReach));
            int knee = Floor(y + KneeHeight);
            if (!_solidAt(cx, knee, cz))
            {
                return false; // no lip at the knees
            }

            int top = knee + 1;
            int highest = Floor(y + MaxPullUp) + 1;
            while (top <= highest && _solidAt(cx, top, cz))
            {
                top++;
            }

            float rise = top - y;
            if (rise <= minRise || rise > MaxPullUp || _holdAt(cx, top - 1, cz) == ClimbSurface.None)
            {
                return false;
            }

            // The climber's own column must stay open while the body rises to stand height on the new floor.
            int fromY = Floor(y + StandHeight);
            int toY = Floor(top + StandHeight - 0.05f);
            for (int cy = fromY; cy <= toY; cy++)
            {
                if (_solidAt(Floor(x), cy, Floor(z)))
                {
                    return false;
                }
            }

            // On top: centred on the wall column across the wall, sideways where the climber is — or, if the body would
            // brush a neighbouring block there, centred on the column both ways.
            float tx = hold.DirX != 0 ? cx + 0.5f : x;
            float tz = hold.DirZ != 0 ? cz + 0.5f : z;
            if (!BodyFits(tx, top, tz))
            {
                tx = cx + 0.5f;
                tz = cz + 0.5f;
                if (!BodyFits(tx, top, tz))
                {
                    return false;
                }
            }

            targetX = tx;
            targetY = top;
            targetZ = tz;
            return true;
        }

        /// <summary>Whether a standing body with its feet at (x, y, z) touches no collider.</summary>
        public bool BodyFits(float x, float y, float z)
        {
            // The body is under one block wide, so its four footprint corners name every column it overlaps; feet,
            // middle and head heights name every cell row of a 1.8-high body.
            float r = BodyRadius;
            int x0 = Floor(x - r), x1 = Floor(x + r), z0 = Floor(z - r), z1 = Floor(z + r);
            foreach (float h in BodySamples)
            {
                int cy = Floor(y + h);
                if (_solidAt(x0, cy, z0) || _solidAt(x1, cy, z0) || _solidAt(x0, cy, z1) || _solidAt(x1, cy, z1))
                {
                    return false;
                }
            }

            return true;
        }

        private static readonly float[] BodySamples = { 0.05f, 0.9f, StandHeight - 0.05f };
    }
}
