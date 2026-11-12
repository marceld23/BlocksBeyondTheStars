// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Admin
{
    /// <summary>What <see cref="AdminFollowTracker.Observe"/> tells the observer to do this frame.</summary>
    public enum AdminFollowStep
    {
        /// <summary>Nobody is followed.</summary>
        Idle,

        /// <summary>Following started, but the player has not been seen yet (the jump to them is still on its way): hold still.</summary>
        Waiting,

        /// <summary>The player is seen: move to the chase point behind them.</summary>
        Following,

        /// <summary>The player is gone (left the body, or never showed up): following has ended.</summary>
        Lost,
    }

    /// <summary>
    /// "Follow" in the admin view's Players tab (#3505), Unity-free: the invisible admin keeps itself behind a player whose
    /// presence streams in on the same body. The client steers its own observer there each frame (the server only learns the
    /// admin's position like any observer move). Following ends when the player has not been seen for
    /// <see cref="LostAfterSeconds"/> after they were seen (they left the body), or was never seen within
    /// <see cref="FirstSightSeconds"/> (the jump to them failed); any manual movement ends it too (the caller's
    /// <see cref="Stop"/>).
    /// </summary>
    public sealed class AdminFollowTracker
    {
        /// <summary>Seconds without the player's presence after which following ends.</summary>
        public const double LostAfterSeconds = 2.0;

        /// <summary>Seconds the first sighting may take — the jump to the player (perhaps to another body) comes first.</summary>
        public const double FirstSightSeconds = 15.0;

        /// <summary>How far behind the player the observer keeps itself, in blocks.</summary>
        public const float BehindBlocks = 6f;

        /// <summary>How far above the player's feet the observer keeps itself, in blocks.</summary>
        public const float AboveBlocks = 3f;

        /// <summary>A gap between two <see cref="Observe"/> calls longer than this is a pause (the admin panel reopened, a world
        /// loading after the jump) — its time counts neither against the first sighting nor as the player being gone.</summary>
        public const double PauseGapSeconds = 0.5;

        private double _startedAt;
        private double _lastSeenAt = -1d;
        private double _lastObservedAt = -1d;

        /// <summary>True while a player is followed.</summary>
        public bool Active => TargetId.Length > 0;

        /// <summary>The followed player's id, or empty.</summary>
        public string TargetId { get; private set; } = string.Empty;

        /// <summary>The followed player's name (for the log line when following ends).</summary>
        public string TargetName { get; private set; } = string.Empty;

        /// <summary>True once the followed player has been seen at least once.</summary>
        public bool Seen => _lastSeenAt >= 0d;

        /// <summary>Starts following <paramref name="playerId"/> at <paramref name="now"/> (seconds, any steady clock).</summary>
        public void Start(string playerId, string playerName, double now)
        {
            TargetId = playerId ?? string.Empty;
            TargetName = string.IsNullOrEmpty(playerName) ? TargetId : playerName;
            _startedAt = now;
            _lastSeenAt = -1d;
            _lastObservedAt = now;
        }

        /// <summary>Ends following (manual movement, admin mode left, the panel's Stop).</summary>
        public void Stop()
        {
            TargetId = string.Empty;
            TargetName = string.Empty;
            _lastSeenAt = -1d;
        }

        /// <summary>One frame: whether the followed player is drawn here (<paramref name="seen"/>) at <paramref name="now"/>.
        /// <see cref="AdminFollowStep.Lost"/> does not stop the tracker by itself — the caller reads <see cref="TargetName"/>
        /// for its note and then calls <see cref="Stop"/>.</summary>
        public AdminFollowStep Observe(double now, bool seen)
        {
            if (!Active)
            {
                return AdminFollowStep.Idle;
            }

            double gap = _lastObservedAt >= 0d ? now - _lastObservedAt : 0d;
            if (gap > PauseGapSeconds)
            {
                // Not observed meanwhile: the timers resume where they stood.
                _startedAt += gap;
                if (_lastSeenAt >= 0d)
                {
                    _lastSeenAt += gap;
                }
            }

            _lastObservedAt = now;
            if (seen)
            {
                _lastSeenAt = now;
                return AdminFollowStep.Following;
            }

            if (!Seen)
            {
                return now - _startedAt > FirstSightSeconds ? AdminFollowStep.Lost : AdminFollowStep.Waiting;
            }

            return now - _lastSeenAt > LostAfterSeconds ? AdminFollowStep.Lost : AdminFollowStep.Following;
        }

        /// <summary>The point the observer steers to: <see cref="BehindBlocks"/> behind the player along their facing
        /// (<paramref name="yawDegrees"/>, Unity's convention: 0 looks along +Z, 90 along +X) and <see cref="AboveBlocks"/>
        /// above their feet.</summary>
        public static (float X, float Y, float Z) ChasePoint(float x, float y, float z, float yawDegrees)
        {
            double rad = yawDegrees * Math.PI / 180d;
            float fx = (float)Math.Sin(rad), fz = (float)Math.Cos(rad);
            return (x - (fx * BehindBlocks), y + AboveBlocks, z - (fz * BehindBlocks));
        }

        /// <summary>The yaw and pitch (degrees, Unity's convention: a positive pitch looks down) that look from
        /// (<paramref name="fromX"/>, <paramref name="fromY"/>, <paramref name="fromZ"/>) at the other point.</summary>
        public static (float Yaw, float Pitch) LookAt(float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            double dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
            double flat = Math.Sqrt((dx * dx) + (dz * dz));
            float yaw = (float)(Math.Atan2(dx, dz) * 180d / Math.PI);
            float pitch = (float)(-Math.Atan2(dy, Math.Max(flat, 1e-4)) * 180d / Math.PI);
            return (yaw, pitch);
        }
    }
}
