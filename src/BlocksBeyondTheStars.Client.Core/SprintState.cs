// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Sprinting on foot (#3374, #3376): when a run may start, keep going and stop. The keyboard HOLDS its key (Left Shift by
    /// default); the pad's left-stick click and the touch RUN button SWITCH it on while moving (<see cref="Latched"/>) — it
    /// stays on until the player stops, the stamina runs out, the place forbids it, or the control is pressed again. A run
    /// needs forward input and starts only with ground under the feet; a jump out of a run keeps the speed. The caller spends
    /// <see cref="DrainPerSecond"/> of the shared <see cref="BodyStamina"/> while <see cref="Active"/>.
    /// Client-only like all on-foot movement: the server trusts the reported position.
    /// </summary>
    public sealed class SprintState
    {
        /// <summary>A run is this many times as fast as the walk (on top of gravity, gear and bio-lab factors).</summary>
        public const float SpeedFactor = 1.5f;

        /// <summary>Seconds a full stamina lasts at a run.</summary>
        public const float SecondsFromFull = 8f;

        /// <summary>Stamina spent per second of running.</summary>
        public const float DrainPerSecond = 1f / SecondsFromFull;

        /// <summary>Forward input (−1..1) a run needs — no sideways or backwards running.</summary>
        public const float ForwardMinimum = 0.3f;

        /// <summary>Summed stick/key input above which the body counts as moving (the controller's own walking test).</summary>
        public const float MoveMinimum = 0.1f;

        /// <summary>Running this frame.</summary>
        public bool Active { get; private set; }

        /// <summary>Switched on by the pad or touch control (no key held needed).</summary>
        public bool Latched { get; private set; }

        /// <summary>The movement input counts as moving.</summary>
        public static bool IsMoving(float strafe, float forward) => Math.Abs(strafe) + Math.Abs(forward) > MoveMinimum;

        /// <summary>
        /// #3376: the stock pad layout shares the left-stick click between the sprint switch (while moving) and the
        /// context-actions list (while standing). True when this frame's press of the context-actions control belongs to the
        /// sprint instead: both actions sit on the same pad button, the press came from the pad (not from a key or the touch
        /// ACT button), running is possible here (<paramref name="runPossible"/> — not in a speeder, in flight or an EVA,
        /// swimming, crouched, on a ladder or a wall, where the same stick steers but nothing would run), and the body is moving.
        /// </summary>
        public static bool PadPressIsSprint(bool sameButton, bool padPressed, bool otherSourcePressed, bool runPossible, float strafe, float forward)
            => sameButton && padPressed && !otherSourcePressed && runPossible && IsMoving(strafe, forward);

        /// <summary>
        /// One frame: <paramref name="keyHeld"/> is the held keyboard key, <paramref name="togglePressed"/> a press of the
        /// pad/touch switch, <paramref name="strafe"/>/<paramref name="forward"/> the movement input, <paramref name="allowed"/>
        /// whether running is possible here at all (on foot, not crouched, swimming, climbing, flying, …), and
        /// <paramref name="canSprint"/> whether the stamina allows it. Returns <see cref="Active"/>.
        /// </summary>
        public bool Step(bool keyHeld, bool togglePressed, float strafe, float forward, bool grounded, bool allowed, bool canSprint)
        {
            bool moving = IsMoving(strafe, forward);
            if (togglePressed)
            {
                // A second press switches it off; a press while standing does nothing (the pad's click opens the list then).
                Latched = !Latched && moving;
            }

            if (!moving || !allowed || !canSprint)
            {
                Latched = false; // stopping, a place without running or an empty stamina switch the run off
            }

            bool wants = keyHeld || Latched;
            bool start = Active || grounded; // a run starts on the ground; a jump out of it keeps going
            Active = wants && forward > ForwardMinimum && allowed && canSprint && start;
            return Active;
        }

        /// <summary>Ends any run at once (a teleport, a respawn, a vehicle).</summary>
        public void Stop()
        {
            Active = false;
            Latched = false;
        }
    }

    /// <summary>
    /// The stamina bar's visibility (#3375): shown while the stamina is not full, kept a moment (<see cref="HoldFull"/>) after
    /// it is full again, then faded out over <see cref="FadeSeconds"/>. Under reduced motion it appears and disappears at once.
    /// Starts hidden.
    /// </summary>
    public sealed class StaminaBarFade
    {
        /// <summary>Seconds a full bar stays before it fades.</summary>
        public const float HoldFull = 1f;

        /// <summary>Seconds of a fade in or out.</summary>
        public const float FadeSeconds = 0.35f;

        private float _fullFor = HoldFull;

        /// <summary>The bar's opacity, 0 (hidden) … 1.</summary>
        public float Alpha { get; private set; }

        /// <summary>Advances the fade by <paramref name="dt"/> for a stamina of <paramref name="value"/>; returns <see cref="Alpha"/>.</summary>
        public float Step(float dt, float value, bool reducedMotion)
        {
            bool full = value >= 1f;
            _fullFor = full ? _fullFor + Math.Max(0f, dt) : 0f;
            float target = !full || _fullFor < HoldFull ? 1f : 0f;
            if (reducedMotion)
            {
                Alpha = target;
                return Alpha;
            }

            float step = Math.Max(0f, dt) / FadeSeconds;
            Alpha = Alpha < target ? Math.Min(target, Alpha + step) : Math.Max(target, Alpha - step);
            return Alpha;
        }
    }
}
