// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Whether the attacker of a hit is clearly above or below the player (#2564).</summary>
    public enum HitHeight
    {
        /// <summary>About level with the player: the marker shows the direction only.</summary>
        Level,

        /// <summary>Clearly overhead (a flier, a bandit on a ledge): an up chevron joins the marker.</summary>
        Above,

        /// <summary>Clearly underneath (a creature in a pit below a ledge): a down chevron joins the marker.</summary>
        Below,
    }

    /// <summary>Where a hit came from, as the HUD's damage marker shows it (#2564).</summary>
    public readonly struct HitBearing
    {
        public HitBearing(float angleDegrees, HitHeight height)
        {
            AngleDegrees = angleDegrees;
            Height = height;
        }

        /// <summary>The attacker's direction around the view axis, in degrees: 0 ahead (the marker at the top of its ring),
        /// 90 to the right, −90 to the left, ±180 behind. 0 when the attacker is straight overhead or underneath.</summary>
        public float AngleDegrees { get; }

        /// <summary>Whether the attacker is clearly above or below the player.</summary>
        public HitHeight Height { get; }
    }

    /// <summary>
    /// The damage marker's math (#2564). The marker on the ring around the crosshair used to turn toward the nearest
    /// hostile in the horizontal plane only, so a flier biting from overhead read as "ahead". It now turns toward the
    /// attacker the server names and adds an up or down chevron when that attacker is clearly above or below: more than
    /// <see cref="MinHeightDifference"/> blocks AND steeper than <see cref="MinElevationDegrees"/> — a creature on a gentle
    /// slope a few blocks away stays "level", and a bird gliding past at head height over a long distance too.
    /// </summary>
    public static class HitDirection
    {
        /// <summary>Height difference (blocks) the attacker must exceed before it counts as above or below.</summary>
        public const float MinHeightDifference = 2.5f;

        /// <summary>Elevation angle (degrees above or below the horizon) the attacker must exceed as well.</summary>
        public const float MinElevationDegrees = 30f;

        /// <summary>Closer than this on the ground (blocks), the attacker is straight overhead or underneath: no direction.</summary>
        private const float OverheadHorizontal = 0.05f;

        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>Classifies a hit from the attacker's offset (<paramref name="dx"/>, <paramref name="dy"/>,
        /// <paramref name="dz"/> = attacker − viewer, Y up) against the view heading on the ground
        /// (<paramref name="headingX"/>, <paramref name="headingZ"/>; any length, see <see cref="ScreenHeading"/>).</summary>
        public static HitBearing Classify(float dx, float dy, float dz, float headingX, float headingZ)
        {
            float fx = 0f, fz = 1f;
            float headingLength = (float)Math.Sqrt((headingX * headingX) + (headingZ * headingZ));
            if (headingLength > 1e-6f)
            {
                fx = headingX / headingLength;
                fz = headingZ / headingLength;
            }

            float horizontal = (float)Math.Sqrt((dx * dx) + (dz * dz));
            float angle = 0f;
            if (horizontal > OverheadHorizontal)
            {
                float right = (dx * fz) - (dz * fx); // the view's right on the ground is (fz, −fx)
                float ahead = (dx * fx) + (dz * fz);
                angle = (float)(Math.Atan2(right, ahead) * RadToDeg);
            }

            float elevation = (float)(Math.Atan2(dy, horizontal) * RadToDeg);
            var height = HitHeight.Level;
            if (dy > MinHeightDifference && elevation > MinElevationDegrees)
            {
                height = HitHeight.Above;
            }
            else if (dy < -MinHeightDifference && elevation < -MinElevationDegrees)
            {
                height = HitHeight.Below;
            }

            return new HitBearing(angle, height);
        }

        /// <summary>The view's heading on the ground — the world direction that sits at the top of the screen. That is the
        /// camera's forward laid flat; looking (almost) straight down or up the flat forward vanishes, and the camera's up
        /// vector laid flat takes over (it leans forward while looking down and backward while looking up, so it is
        /// flipped then). Not normalised.</summary>
        public static void ScreenHeading(float forwardX, float forwardY, float forwardZ, float upX, float upZ,
            out float headingX, out float headingZ)
        {
            if ((forwardX * forwardX) + (forwardZ * forwardZ) > 0.01f)
            {
                headingX = forwardX;
                headingZ = forwardZ;
                return;
            }

            float sign = forwardY < 0f ? 1f : -1f;
            headingX = upX * sign;
            headingZ = upZ * sign;
        }
    }

    /// <summary>
    /// The last blow the server reported on the local player (#2564): <c>PlayerStateUpdate.LastHit*</c>. Every update
    /// carries the last attacker again, so only a CHANGED sequence number is a new blow; 0 means none yet (a new session —
    /// a reconnect or another server starts counting again, and any change counts, not only a rise).
    /// </summary>
    public sealed class HitSourceTracker
    {
        /// <summary>A blow counts as "just now" for this long by default (seconds): about one merged damage pulse of the
        /// HUD plus network jitter. Continuous damage reports a blow every server tick, so it stays fresh throughout.</summary>
        public const double FreshSeconds = 1.0;

        /// <summary>The sequence number of the last update seen.</summary>
        public int Seq { get; private set; }

        /// <summary>Entity id of the last attacker (empty = none known).</summary>
        public string EntityId { get; private set; } = string.Empty;

        /// <summary>Where the last blow came from (canonical world position).</summary>
        public float X { get; private set; }

        public float Y { get; private set; }

        public float Z { get; private set; }

        /// <summary>The caller's clock when the last blow was reported; negative infinity = never.</summary>
        public double HitAt { get; private set; } = double.NegativeInfinity;

        /// <summary>Feeds one state update. Returns true when it reports a new blow.</summary>
        public bool Observe(int seq, string? entityId, float x, float y, float z, double now)
        {
            if (seq == Seq)
            {
                return false;
            }

            Seq = seq;
            if (seq == 0)
            {
                Forget(); // a new session: nothing has hit this player there yet
                return false;
            }

            EntityId = entityId ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
            HitAt = now;
            return true;
        }

        /// <summary>True when a blow was reported within <paramref name="maxAge"/> seconds before <paramref name="now"/>.</summary>
        public bool IsFresh(double now, double maxAge = FreshSeconds) => now >= HitAt && now - HitAt <= maxAge;

        /// <summary>True when <paramref name="entityId"/> landed a blow within <paramref name="maxAge"/> seconds.</summary>
        public bool IsFreshFrom(string? entityId, double now, double maxAge = FreshSeconds)
            => !string.IsNullOrEmpty(entityId) && string.Equals(entityId, EntityId, StringComparison.Ordinal) && IsFresh(now, maxAge);

        /// <summary>Drops the remembered attacker (the sequence number stays, so a repeated report is not new).</summary>
        public void Forget()
        {
            EntityId = string.Empty;
            X = Y = Z = 0f;
            HitAt = double.NegativeInfinity;
        }
    }
}
