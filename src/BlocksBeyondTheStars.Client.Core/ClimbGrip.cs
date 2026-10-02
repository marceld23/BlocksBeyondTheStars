// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a hanging climber is doing this frame — each tires the grip at its own rate.</summary>
    public enum ClimbMotion : byte
    {
        Hang = 0,
        Up = 1,
        Side = 2,
        Down = 3,
    }

    /// <summary>
    /// The wall climber's grip (#2189): a hidden 0..1 value that is never drawn on the HUD (Marcel's decision) — the
    /// player feels it instead. Below <see cref="SlowBelow"/> the climb slows (<see cref="SpeedFactor"/>), below
    /// <see cref="StrainBelow"/> the body strains (<see cref="Strain"/> drives a tremble and a breath), and at 0 the
    /// climber slides down slowly instead of falling. Client-only like all on-foot movement: nothing is saved or sent.
    /// <para>Tiring scales with the world's gravity, so an asteroid lets you climb several times further than a heavy
    /// planet; slippery walls tire twice as fast, and worn climbing gear takes a share of the drain away. The grip
    /// refills on the ground and on a ladder — never in the air, so letting go and grabbing again is no trick.</para>
    /// </summary>
    public sealed class ClimbGrip
    {
        /// <summary>A fresh grab needs more grip left than this.</summary>
        public const float GrabMinimum = 0.2f;

        /// <summary>Below this the climb gets slower.</summary>
        public const float SlowBelow = 0.4f;

        /// <summary>Below this the body strains (tremble, breath).</summary>
        public const float StrainBelow = 0.25f;

        /// <summary>Seconds on the ground (or a ladder) from empty to full.</summary>
        public const float RefillSeconds = 1.5f;

        /// <summary>A slippery wall tires the grip this many times as fast.</summary>
        public const float SlipperyFactor = 2f;

        /// <summary>Climbing speeds at full grip, m/s. The ladder stays at its own 4 m/s.</summary>
        public const float UpSpeed = 2.0f;
        public const float DownSpeed = 2.5f;
        public const float SideSpeed = 1.8f;

        /// <summary>The slide of an empty grip, m/s — far below the safe-landing speed, so it never hurts.</summary>
        public const float SlideSpeed = 2.5f;

        /// <summary>Seconds after letting go before a new grab — so a let-go is not caught again at once.</summary>
        public const float RegrabCooldown = 0.4f;

        /// <summary>How long a pull-up over a ledge takes.</summary>
        public const float PullUpSeconds = 0.35f;

        /// <summary>The grip, 0 (spent) … 1 (fresh).</summary>
        public float Value { get; private set; } = 1f;

        /// <summary>Spent — the climber slides.</summary>
        public bool Exhausted => Value <= 0f;

        /// <summary>Enough left for a fresh grab.</summary>
        public bool CanGrab => Value > GrabMinimum;

        /// <summary>The climb-speed multiplier: 1 down to <see cref="SlowBelow"/>, then easing to 0.5 at empty.</summary>
        public float SpeedFactor => Value >= SlowBelow ? 1f : 0.5f + (0.5f * (Value / SlowBelow));

        /// <summary>How hard the body strains, 0 (fine) … 1 (spent) — below <see cref="StrainBelow"/> only.</summary>
        public float Strain => Value >= StrainBelow ? 0f : 1f - (Value / StrainBelow);

        /// <summary>Grip spent per second at 1 g on a normal wall without gear.</summary>
        public static float BaseDrain(ClimbMotion motion) => motion switch
        {
            ClimbMotion.Up => 0.10f,
            ClimbMotion.Side => 0.07f,
            ClimbMotion.Down => 0.03f,
            _ => 0.04f,
        };

        /// <summary>Grip spent per second: <see cref="BaseDrain"/> × the world's gravity factor × slippery × (1 − gear).</summary>
        public static float DrainPerSecond(ClimbMotion motion, float gravityFactor, ClimbSurface surface, float gearGrip)
        {
            float g = Math.Max(0.2f, Math.Min(2.5f, gravityFactor));
            float slip = surface == ClimbSurface.Slippery ? SlipperyFactor : 1f;
            float gear = 1f - Math.Max(0f, Math.Min(SuitEquipment.MaxClimbGrip, gearGrip));
            return BaseDrain(motion) * g * slip * gear;
        }

        /// <summary>Seconds of <paramref name="motion"/> a fresh grip lasts (what the tuning and the manual quote).</summary>
        public static float SecondsFromFull(ClimbMotion motion, float gravityFactor, ClimbSurface surface, float gearGrip)
            => 1f / DrainPerSecond(motion, gravityFactor, surface, gearGrip);

        /// <summary>Tires the grip for <paramref name="dt"/> seconds of hanging on.</summary>
        public void Drain(float dt, ClimbMotion motion, float gravityFactor, ClimbSurface surface, float gearGrip)
            => Value = Math.Max(0f, Value - (dt * DrainPerSecond(motion, gravityFactor, surface, gearGrip)));

        /// <summary>Recovers the grip for <paramref name="dt"/> seconds of standing (ground or ladder).</summary>
        public void Refill(float dt) => Value = Math.Min(1f, Value + (dt / RefillSeconds));

        /// <summary>A fresh grip (a new world, a respawn).</summary>
        public void Reset() => Value = 1f;
    }
}
