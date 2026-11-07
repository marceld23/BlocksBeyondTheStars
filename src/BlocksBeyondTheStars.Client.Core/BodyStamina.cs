// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The body's stamina (#3373): one 0..1 value that sprinting (#3374) and wall climbing (#2189) both spend. It comes back
    /// only at rest — on the ground or a ladder rung, while not sprinting and not hanging on a wall — and only slowly: after a
    /// short pause (<see cref="RestDelay"/>) it fills from empty to full in <see cref="RefillSeconds"/>. A spent stamina never
    /// hurts: a sprinter walks on, a climber slides down slowly (<see cref="ClimbGrip.SlideSpeedFor"/>). Once it ran dry a new
    /// sprint waits until <see cref="SprintRecoverAt"/> is back, so the run does not flicker on and off at the edge.
    /// The HUD shows it as a small bar under the crosshair while it is not full (#3375). Client-only like all on-foot
    /// movement: nothing is saved or sent.
    /// </summary>
    public sealed class BodyStamina
    {
        /// <summary>Below this a climb gets slower.</summary>
        public const float SlowBelow = 0.4f;

        /// <summary>Below this the body strains (a tremble on the wall, a breath).</summary>
        public const float StrainBelow = 0.25f;

        /// <summary>Seconds of rest before the stamina starts to come back.</summary>
        public const float RestDelay = 1f;

        /// <summary>Seconds of rest (after <see cref="RestDelay"/>) from empty to full.</summary>
        public const float RefillSeconds = 6f;

        /// <summary>After running dry, a new sprint needs this much back.</summary>
        public const float SprintRecoverAt = 0.25f;

        private float _restedFor = RestDelay;

        /// <summary>The stamina, 0 (spent) … 1 (fresh).</summary>
        public float Value { get; private set; } = 1f;

        /// <summary>Nothing used — the HUD bar hides.</summary>
        public bool Full => Value >= 1f;

        /// <summary>Nothing left: a climber slides.</summary>
        public bool Exhausted => Value <= 0f;

        /// <summary>Ran dry and not yet back to <see cref="SprintRecoverAt"/> — no new sprint.</summary>
        public bool Spent { get; private set; }

        /// <summary>A sprint may run.</summary>
        public bool CanSprint => !Spent && Value > 0f;

        /// <summary>Enough left for a fresh grab of a wall.</summary>
        public bool CanGrab => Value > ClimbGrip.GrabMinimum;

        /// <summary>The climb-speed multiplier: 1 down to <see cref="SlowBelow"/>, then easing to 0.5 at empty.</summary>
        public float SpeedFactor => Value >= SlowBelow ? 1f : 0.5f + (0.5f * (Value / SlowBelow));

        /// <summary>How hard the body strains, 0 (fine) … 1 (spent) — below <see cref="StrainBelow"/> only.</summary>
        public float Strain => Value >= StrainBelow ? 0f : 1f - (Value / StrainBelow);

        /// <summary>Spends <paramref name="amount"/> of stamina (a frame of sprinting or hanging on) and restarts the pause
        /// before a refill.</summary>
        public void Spend(float amount)
        {
            if (!(amount > 0f))
            {
                return;
            }

            Value = Math.Max(0f, Value - amount);
            _restedFor = 0f;
            if (Value <= 0f)
            {
                Spent = true;
            }
        }

        /// <summary>Rests for <paramref name="dt"/> seconds (standing or walking on the ground, a ladder rung): after
        /// <see cref="RestDelay"/> the stamina comes back at 1 / <see cref="RefillSeconds"/> per second. Not called in the
        /// air, while sprinting or while hanging on a wall.</summary>
        public void Rest(float dt)
        {
            if (!(dt > 0f))
            {
                return;
            }

            _restedFor += dt;
            if (_restedFor < RestDelay)
            {
                return;
            }

            Value = Math.Min(1f, Value + (dt / RefillSeconds));
            if (Spent && Value >= SprintRecoverAt)
            {
                Spent = false;
            }
        }

        /// <summary>A fresh body (a new world, a respawn, a teleport).</summary>
        public void Reset()
        {
            Value = 1f;
            Spent = false;
            _restedFor = RestDelay;
        }
    }
}
