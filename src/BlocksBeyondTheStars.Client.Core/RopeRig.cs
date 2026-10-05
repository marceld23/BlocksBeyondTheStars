// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The energy rope gun's numbers (#2317), in one place. Client-only like every on-foot movement rule: the
    /// server validates the anchor and charges the shot (<c>GameServerGadgets</c>); the pull itself is the client's.</summary>
    public static class RopeRules
    {
        /// <summary>The item the rules belong to.</summary>
        public const string ItemKey = "energy_rope_gun";

        /// <summary>Rope length (blocks) when the tool data carries no range.</summary>
        public const float DefaultLength = 24f;

        /// <summary>The reel's speed once it has wound up, m/s — brisk, well under the safe landing speed on a normal world.</summary>
        public const float PullSpeed = 10f;

        /// <summary>Seconds the reel takes to reach <see cref="PullSpeed"/> from a standstill.</summary>
        public const float EaseInSeconds = 0.25f;

        /// <summary>How much of the walk input steers sideways while reeling (across a gap you can lean left or right).</summary>
        public const float SteerShare = 0.3f;

        /// <summary>Within this of the target (feet to feet) the pull has arrived.</summary>
        public const float ArriveRadius = 0.35f;

        /// <summary>The rope snaps once the anchor is further than the length plus this.</summary>
        public const float SnapSlack = 2f;

        /// <summary>Seconds terrain may block the line of sight before the rope is cut.</summary>
        public const float SightGraceSeconds = 0.5f;

        /// <summary>A pull that moved less than <see cref="StuckProgress"/> in this many seconds has hit something: the winch holds.</summary>
        public const float StuckSeconds = 0.4f;

        /// <summary>See <see cref="StuckSeconds"/>.</summary>
        public const float StuckProgress = 0.2f;

        /// <summary>A slack rope (shot, never pulled) catches a fall this far below the take-off, m — a jump stays a jump.</summary>
        public const float CatchDrop = 1.5f;

        /// <summary>Letting go leaves at most this share of the safe landing speed downward: the rope never causes fall damage.</summary>
        public const float ReleaseFallShare = 0.5f;

        /// <summary>The hop upward when Jump lets go of the rope, m/s — enough for climbing gloves to grab, or for a low ledge.</summary>
        public const float HopSpeed = 4.5f;

        /// <summary>How far off a wall the body hangs (feet position from the face), blocks.</summary>
        public const float StandOff = 0.6f;

        /// <summary>The hands above the feet while hanging — the climber's hand height.</summary>
        public const float HandHeight = 1.35f;

        /// <summary>The chest above the feet: where the rope leaves the body and what the length is measured from.</summary>
        public const float ChestHeight = 1.0f;

        /// <summary>Hanging under a ceiling, the feet sit this far below it (the head just under the block).</summary>
        public const float HeadRoom = 1.9f;

        /// <summary>Landing on a top face, the feet are moved this share of the way from the hit point to the block's centre.</summary>
        public const float TopInset = 0.6f;

        /// <summary>Reaching a ledge, the hands are wound up to this far under the top edge before the pull-up.</summary>
        public const float LedgeGrip = 0.1f;
    }

    /// <summary>Which face of a block the rope stuck to.</summary>
    public enum RopeFace : byte
    {
        Top = 0,
        Side = 1,
        Bottom = 2,
    }

    /// <summary>What happens when the pull arrives.</summary>
    public enum RopeArrival : byte
    {
        /// <summary>A top face: the feet land on the block.</summary>
        Land = 0,

        /// <summary>A side face with a free top: the rope winds the hands to the edge, then the pull-up takes over.</summary>
        PullUp = 1,

        /// <summary>Mid-wall or under a ceiling: the body hangs at the anchor.</summary>
        Hang = 2,
    }

    /// <summary>The rig's state.</summary>
    public enum RopeState : byte
    {
        Idle = 0,

        /// <summary>Shot and stuck, never pulled: slack — the player walks and jumps as usual, a real fall is caught.</summary>
        Attached = 1,

        /// <summary>The secondary button is held: the winch reels the body in.</summary>
        Pulling = 2,

        /// <summary>The winch holds: the body hangs where it is (the button was let go, or the pull arrived at a wall).</summary>
        Hanging = 3,
    }

    /// <summary>What a step reports beyond the velocity.</summary>
    public enum RopeEvent : byte
    {
        None = 0,
        Landed = 1,
        PullUp = 2,
        Hang = 3,
        Snapped = 4,
    }

    /// <summary>One frame of the rig: whether it owns the body's movement, the velocity to apply then, and an event.</summary>
    public readonly struct RopeStep
    {
        public RopeStep(bool owns, Vector3f velocity, RopeEvent evt)
        {
            Owns = owns;
            Velocity = velocity;
            Event = evt;
        }

        /// <summary>True when the rope moves (or holds) the body this frame — the walk and the gravity are replaced.</summary>
        public bool Owns { get; }

        /// <summary>The body's velocity while <see cref="Owns"/> (feet, m/s).</summary>
        public Vector3f Velocity { get; }

        public RopeEvent Event { get; }

        public static readonly RopeStep NotOwned = new RopeStep(false, Vector3f.Zero, RopeEvent.None);
    }

    /// <summary>
    /// The energy rope (#2317/#2320): shot at a block face it sticks there; holding the secondary button reels the
    /// body straight toward a target in front of the anchor, letting go makes the winch hold, and the arrival depends
    /// on the face — a top face is landed on, a side face with a free top is climbed over with the pull-up, anything
    /// else is hung from. Pure: positions in and a velocity out, no Unity, so it is tested headless (like
    /// <see cref="ClimbProbe"/>). All positions are the FEET (the controller's transform); the rope itself leaves the
    /// chest, which is where the length is measured from.
    /// </summary>
    public sealed class RopeRig
    {
        private float _pullTime;
        private float _sightLost;
        private float _stuckTime;
        private Vector3f _stuckFrom;
        private float _startDistance = 1f;
        private bool _pullLatched; // the hold that ended in a hang is spent — a new pull needs a fresh press

        public RopeState State { get; private set; }

        public bool Attached => State != RopeState.Idle;

        public bool Pulling => State == RopeState.Pulling;

        public bool Hanging => State == RopeState.Hanging;

        /// <summary>The point on the block face the rope stuck to (kept after a release, for the fading line).</summary>
        public Vector3f Anchor { get; private set; }

        /// <summary>The face's outward normal.</summary>
        public Vector3f Normal { get; private set; }

        /// <summary>The block the rope holds on to.</summary>
        public Vector3i Cell { get; private set; }

        public RopeFace Face { get; private set; }

        public RopeArrival Arrival { get; private set; }

        /// <summary>Where the feet are pulled to.</summary>
        public Vector3f Target { get; private set; }

        /// <summary>Where the feet end up after the pull-up (<see cref="RopeArrival.PullUp"/>): the top of the block.</summary>
        public Vector3f PullUpTarget { get; private set; }

        /// <summary>The rope's length, blocks (the tool's range).</summary>
        public float Length { get; private set; } = RopeRules.DefaultLength;

        /// <summary>How far along the pull the body is, 0..1 (for the poses).</summary>
        public float Progress { get; private set; }

        /// <summary>The face a normal points away from.</summary>
        public static RopeFace FaceOf(Vector3f normal)
            => normal.Y > 0.5f ? RopeFace.Top : normal.Y < -0.5f ? RopeFace.Bottom : RopeFace.Side;

        /// <summary>
        /// The rope stuck at <paramref name="anchor"/> on the face of <paramref name="cell"/> whose outward normal is
        /// <paramref name="normal"/>. Decides the target and the arrival: a top face is stood on (a little in from the
        /// edge); a side face whose two cells above are free is a ledge — the hands are wound up to just under the top
        /// edge and the pull-up finishes it; a side face without room above, or a ceiling, is hung from.
        /// </summary>
        public void Attach(Vector3f anchor, Vector3f normal, Vector3i cell, float length, Func<int, int, int, bool> solidAt)
        {
            if (solidAt == null)
            {
                throw new ArgumentNullException(nameof(solidAt));
            }

            Anchor = anchor;
            Normal = normal;
            Cell = cell;
            Length = length > 0f ? length : RopeRules.DefaultLength;
            Face = FaceOf(normal);
            switch (Face)
            {
                case RopeFace.Top:
                    {
                        float cx = cell.X + 0.5f, cz = cell.Z + 0.5f;
                        Target = new Vector3f(
                            anchor.X + ((cx - anchor.X) * RopeRules.TopInset),
                            cell.Y + 1.02f,
                            anchor.Z + ((cz - anchor.Z) * RopeRules.TopInset));
                        Arrival = RopeArrival.Land;
                        break;
                    }

                case RopeFace.Bottom:
                    Target = new Vector3f(anchor.X, anchor.Y - RopeRules.HeadRoom, anchor.Z);
                    Arrival = RopeArrival.Hang;
                    break;
                default:
                    {
                        bool ledge = !solidAt(cell.X, cell.Y + 1, cell.Z) && !solidAt(cell.X, cell.Y + 2, cell.Z);
                        float handsY = ledge ? cell.Y + 1f - RopeRules.LedgeGrip : anchor.Y;
                        Target = new Vector3f(
                            anchor.X + (normal.X * RopeRules.StandOff),
                            handsY - RopeRules.HandHeight,
                            anchor.Z + (normal.Z * RopeRules.StandOff));
                        if (ledge)
                        {
                            Arrival = RopeArrival.PullUp;
                            PullUpTarget = new Vector3f(cell.X + 0.5f, cell.Y + 1f, cell.Z + 0.5f);
                        }
                        else
                        {
                            Arrival = RopeArrival.Hang;
                        }

                        break;
                    }
            }

            State = RopeState.Attached;
            _pullTime = 0f;
            _sightLost = 0f;
            _stuckTime = 0f;
            _pullLatched = false;
            _startDistance = 1f;
            Progress = 0f;
        }

        /// <summary>
        /// One frame. <paramref name="feet"/> is the body's position, <paramref name="pullHeld"/> the secondary button,
        /// <paramref name="grounded"/> whether the feet stand on something, <paramref name="fallDrop"/> how far the body
        /// has fallen below its take-off (0 on the ground), <paramref name="sightClear"/> whether nothing solid lies
        /// between the chest and the anchor, and <paramref name="steer"/> the walk input as a world-space vector (m/s).
        /// A pull that ended in a hang stays a hang while the button is held; a fresh press pulls again.
        /// </summary>
        public RopeStep Step(Vector3f feet, float dt, bool pullHeld, bool grounded, float fallDrop, bool sightClear, Vector3f steer)
        {
            if (State == RopeState.Idle)
            {
                return RopeStep.NotOwned;
            }

            var chest = new Vector3f(feet.X, feet.Y + RopeRules.ChestHeight, feet.Z);
            if (Len(Anchor - chest) > Length + RopeRules.SnapSlack)
            {
                Release();
                return new RopeStep(false, Vector3f.Zero, RopeEvent.Snapped);
            }

            if (sightClear)
            {
                _sightLost = 0f;
            }
            else
            {
                _sightLost += dt;
                if (_sightLost > RopeRules.SightGraceSeconds)
                {
                    Release();
                    return new RopeStep(false, Vector3f.Zero, RopeEvent.Snapped);
                }
            }

            var to = Target - feet;
            float d = Len(to);
            Progress = _startDistance > 0.01f ? Clamp01(1f - (d / _startDistance)) : 1f;

            if (pullHeld)
            {
                if (_pullLatched)
                {
                    // The pull that got us here ended in a hang (arrived at a wall, ran into something): holding on
                    // keeps hanging — otherwise the rig would re-enter the pull every frame and shove the body into
                    // the obstacle again and again. Let go and press again to pull once more.
                    if (grounded)
                    {
                        State = RopeState.Attached;
                        return RopeStep.NotOwned;
                    }

                    return new RopeStep(true, Vector3f.Zero, RopeEvent.None);
                }

                if (State != RopeState.Pulling)
                {
                    State = RopeState.Pulling;
                    _pullTime = 0f;
                    _stuckTime = 0f;
                    _stuckFrom = feet;
                    _startDistance = Math.Max(d, 0.01f);
                }

                _pullTime += dt;
                if (d <= RopeRules.ArriveRadius)
                {
                    return Arrive();
                }

                _stuckTime += dt;
                if (_stuckTime >= RopeRules.StuckSeconds)
                {
                    if (Len(feet - _stuckFrom) < RopeRules.StuckProgress)
                    {
                        State = RopeState.Hanging; // the body ran into something: the winch holds it there
                        _pullLatched = true;
                        return new RopeStep(true, Vector3f.Zero, RopeEvent.Hang);
                    }

                    _stuckTime = 0f;
                    _stuckFrom = feet;
                }

                float ease = Smooth(Math.Min(1f, _pullTime / RopeRules.EaseInSeconds));
                float speed = Math.Min(RopeRules.PullSpeed * ease, Math.Max(2f, d * 6f)); // no overshoot on the last metre
                var dir = Scale(to, 1f / d);
                var velocity = Scale(dir, speed);
                var side = steer - Scale(dir, Dot(steer, dir));
                velocity += Scale(side, RopeRules.SteerShare);
                return new RopeStep(true, velocity, RopeEvent.None);
            }

            _pullTime = 0f;
            _stuckTime = 0f;
            _pullLatched = false; // the button is up: the next press pulls again
            if (State == RopeState.Pulling)
            {
                State = RopeState.Hanging; // the button went up mid-pull: the winch holds
            }

            if (State == RopeState.Hanging)
            {
                if (grounded)
                {
                    State = RopeState.Attached; // a floor under the feet: walk, the rope goes slack
                    return RopeStep.NotOwned;
                }

                return new RopeStep(true, Vector3f.Zero, RopeEvent.None);
            }

            // Slack: the player walks and jumps as usual — but a real fall is caught by the rope, as long as the anchor
            // is above the chest (a rope to the floor of the pit cannot hold anyone up).
            if (!grounded && fallDrop > RopeRules.CatchDrop && Anchor.Y > chest.Y)
            {
                State = RopeState.Hanging;
                return new RopeStep(true, Vector3f.Zero, RopeEvent.Hang);
            }

            return RopeStep.NotOwned;
        }

        /// <summary>Lets go of the rope (a crouch, a hop, a hotbar change, water, a vehicle). The anchor stays readable
        /// for the fading line.</summary>
        public void Release()
        {
            State = RopeState.Idle;
            _pullTime = 0f;
            _sightLost = 0f;
            _stuckTime = 0f;
            _pullLatched = false;
            Progress = 0f;
        }

        /// <summary>The downward speed letting go may leave: the lesser of the current fall and half the safe landing
        /// speed, so the rope never hands the player a fall that hurts.</summary>
        public static float CappedFall(float verticalSpeed, float safeFallSpeed)
            => Math.Max(verticalSpeed, -RopeRules.ReleaseFallShare * Math.Max(0f, safeFallSpeed));

        private RopeStep Arrive()
        {
            Progress = 1f;
            switch (Arrival)
            {
                case RopeArrival.Land:
                    Release();
                    return new RopeStep(true, Vector3f.Zero, RopeEvent.Landed);
                case RopeArrival.PullUp:
                    Release();
                    return new RopeStep(true, Vector3f.Zero, RopeEvent.PullUp);
                default:
                    State = RopeState.Hanging;
                    _pullLatched = true;
                    return new RopeStep(true, Vector3f.Zero, RopeEvent.Hang);
            }
        }

        private static float Len(Vector3f v) => (float)Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

        private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        private static Vector3f Scale(Vector3f v, float k) => new Vector3f(v.X * k, v.Y * k, v.Z * k);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Smooth(float t) => t * t * (3f - (2f * t));
    }

    /// <summary>The drawn rope (#2322): a few points from the hand to the anchor — straight and humming under load, with a
    /// slight droop when it is slack. Pure, so the shape is the same for the player's own rope and another player's.</summary>
    public static class RopeLine
    {
        public const int PointCount = 7;

        /// <summary>Fills <paramref name="into"/> (length <see cref="PointCount"/>) with the points from <paramref name="from"/>
        /// (the hand) to <paramref name="to"/> (the anchor). <paramref name="slack"/> droops the middle; <paramref name="phase"/>
        /// (seconds) drives a small wobble. The endpoints are exact.</summary>
        public static void Points(Vector3f from, Vector3f to, bool slack, float phase, Vector3f[] into)
        {
            if (into == null || into.Length < PointCount)
            {
                throw new ArgumentException("needs " + PointCount + " points", nameof(into));
            }

            var span = to - from;
            float len = (float)Math.Sqrt((span.X * span.X) + (span.Y * span.Y) + (span.Z * span.Z));
            float sag = slack ? Math.Min(0.6f, 0.08f * len) : 0f;
            float wobble = slack ? 0.015f : 0.03f;
            for (int i = 0; i < PointCount; i++)
            {
                float f = i / (float)(PointCount - 1);
                float bulge = 4f * f * (1f - f); // 0 at both ends, 1 in the middle
                float x = from.X + (span.X * f) + ((float)Math.Sin((phase * 9f) + (i * 1.3f)) * wobble * bulge);
                float y = from.Y + (span.Y * f) - (sag * bulge) + ((float)Math.Cos((phase * 7f) + (i * 0.9f)) * wobble * bulge * 0.5f);
                float z = from.Z + (span.Z * f) + ((float)Math.Cos((phase * 11f) + (i * 1.7f)) * wobble * bulge);
                into[i] = new Vector3f(x, y, z);
            }

            into[0] = from;
            into[PointCount - 1] = to;
        }
    }
}
