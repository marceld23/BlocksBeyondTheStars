// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>One frame of raw gamepad state, already deadzoned — the engine-free input the mapper consumes.
    /// The Unity host fills it from <c>InputMap</c>; tests fill it by hand.</summary>
    public struct PadFrame
    {
        /// <summary>Left stick, −1..1 each. Y is UP-positive (Unity's axis convention).</summary>
        public float StickX;
        public float StickY;

        /// <summary>D-pad as −1/0/1 axes. Y is UP-positive.</summary>
        public float DpadX;
        public float DpadY;

        public bool A;      // bottom face button — Confirm / Primary / pointer click
        public bool B;      // right face button — Cancel
        public bool X;      // left face button — Secondary
        public bool Y;      // top face button — Help
        public bool Start;  // Pause
        public bool Back;   // Restart
    }

    /// <summary>
    /// The gamepad → <see cref="MinigameAction"/> + virtual-cursor bridge for the Arcade host (#1218) — pure
    /// C#, no engine, so the whole mapping (edges, repeat, cursor math, drag) is unit-tested headless.
    ///
    /// <para>Directional: the D-pad always feeds Left/Right/Up/Down with an edge press, a first repeat after
    /// <see cref="RepeatDelay"/> and further repeats every <see cref="RepeatInterval"/> (the HotbarScroll
    /// feel). The left STICK does the same for key-driven games — but for a game that wants the pointer
    /// (<c>wantsPointer</c>) the stick becomes the <b>virtual cursor</b> instead: it glides a reticle across
    /// the game's canvas, A presses/releases the pointer at the reticle (drags included), and the D-pad keeps
    /// serving the arrow actions. Sending arrow presses a pointer-only game never bound is harmless — the
    /// host's dispatch drops them — so nothing needs per-game switches.</para>
    ///
    /// <para>Buttons: A → Confirm then Primary (two presses, the two names games actually bind for "do it"),
    /// B → Cancel, X → Secondary, Y → Help, Start → Pause, Back → Restart. Releases mirror presses so
    /// <c>api.Held</c> works from the pad exactly like from a key.</para>
    /// </summary>
    public sealed class MinigamePadMapper
    {
        public const float RepeatDelay = 0.35f;     // seconds until a held direction repeats
        public const float RepeatInterval = 0.12f;  // seconds between repeats after that
        public const float StickThreshold = 0.5f;   // deflection that counts as a direction
        public const float CursorSpeedFactor = 0.9f; // cursor speed = factor * min(canvas w, h) per second

        private readonly Action<MinigameAction> _press;
        private readonly Action<MinigameAction> _release;
        private readonly Action<PointerPhase, float, float> _pointer;

        private PadFrame _prev;
        private readonly float[] _dirHeldFor = new float[4]; // Left, Right, Up, Down
        private readonly bool[] _dirActive = new bool[4];

        private float _cursorX = -1f;  // <0 = not initialised yet (centred on first pointer use)
        private float _cursorY = -1f;
        private bool _cursorDown;

        public MinigamePadMapper(Action<MinigameAction> press, Action<MinigameAction> release, Action<PointerPhase, float, float> pointer)
        {
            _press = press ?? throw new ArgumentNullException(nameof(press));
            _release = release ?? throw new ArgumentNullException(nameof(release));
            _pointer = pointer ?? throw new ArgumentNullException(nameof(pointer));
        }

        /// <summary>Reticle position in canvas pixels (valid once <see cref="CursorVisible"/>).</summary>
        public float CursorX => _cursorX;
        public float CursorY => _cursorY;

        /// <summary>True once the cursor has been placed (first frame of a pointer game with a pad).</summary>
        public bool CursorVisible { get; private set; }

        /// <summary>True while A holds the virtual pointer down (the host tints the reticle).</summary>
        public bool CursorPressed => _cursorDown;

        /// <summary>Forget everything held (game changed / host reset), releasing latched actions.</summary>
        public void Reset()
        {
            var released = _prev;
            _prev = default;
            for (int i = 0; i < 4; i++)
            {
                _dirActive[i] = false;
                _dirHeldFor[i] = 0f;
            }

            if (_cursorDown)
            {
                _cursorDown = false;
                _pointer(PointerPhase.Up, _cursorX, _cursorY);
            }

            _cursorX = _cursorY = -1f;
            CursorVisible = false;

            // Mirror releases for anything the previous frame held, so api.Held never sticks.
            ReleaseIf(released.A, MinigameAction.Confirm);
            ReleaseIf(released.A, MinigameAction.Primary);
            ReleaseIf(released.B, MinigameAction.Cancel);
            ReleaseIf(released.X, MinigameAction.Secondary);
            ReleaseIf(released.Y, MinigameAction.Help);
            ReleaseIf(released.Start, MinigameAction.Pause);
            ReleaseIf(released.Back, MinigameAction.Restart);
        }

        private void ReleaseIf(bool wasHeld, MinigameAction a)
        {
            if (wasHeld)
            {
                _release(a);
            }
        }

        /// <summary>Advance one frame. <paramref name="wantsPointer"/> = the running game registered a pointer
        /// callback (the stick then drives the cursor, not the arrows); <paramref name="canvasW"/>/<paramref name="canvasH"/>
        /// give the cursor its space.</summary>
        public void Update(in PadFrame f, float dt, bool wantsPointer, int canvasW, int canvasH)
        {
            if (dt < 0f)
            {
                dt = 0f;
            }

            // ---- directional actions (D-pad always; the stick only when it is not the cursor) ----
            float dx = f.DpadX;
            float dy = f.DpadY;
            if (!wantsPointer)
            {
                if (Math.Abs(f.StickX) >= StickThreshold && Math.Abs(f.StickX) > Math.Abs(dx))
                {
                    dx = f.StickX;
                }

                if (Math.Abs(f.StickY) >= StickThreshold && Math.Abs(f.StickY) > Math.Abs(dy))
                {
                    dy = f.StickY;
                }
            }

            Direction(0, MinigameAction.Left, dx <= -StickThreshold, dt);
            Direction(1, MinigameAction.Right, dx >= StickThreshold, dt);
            Direction(2, MinigameAction.Up, dy >= StickThreshold, dt);
            Direction(3, MinigameAction.Down, dy <= -StickThreshold, dt);

            // ---- face + shell buttons (edge press / release) ----
            Button(f.A, _prev.A, MinigameAction.Confirm);
            Button(f.A, _prev.A, MinigameAction.Primary);
            Button(f.B, _prev.B, MinigameAction.Cancel);
            Button(f.X, _prev.X, MinigameAction.Secondary);
            Button(f.Y, _prev.Y, MinigameAction.Help);
            Button(f.Start, _prev.Start, MinigameAction.Pause);
            Button(f.Back, _prev.Back, MinigameAction.Restart);

            // ---- virtual cursor ----
            if (wantsPointer && canvasW > 0 && canvasH > 0)
            {
                if (_cursorX < 0f)
                {
                    _cursorX = canvasW * 0.5f;
                    _cursorY = canvasH * 0.5f;
                }

                CursorVisible = true;
                float speed = CursorSpeedFactor * Math.Min(canvasW, canvasH);
                float nx = Clamp(_cursorX + f.StickX * speed * dt, 0f, canvasW - 1);
                // Stick up = cursor up = SMALLER canvas y (canvas row 0 is the top).
                float ny = Clamp(_cursorY - f.StickY * speed * dt, 0f, canvasH - 1);
                bool moved = Math.Abs(nx - _cursorX) > 0.0001f || Math.Abs(ny - _cursorY) > 0.0001f;
                _cursorX = nx;
                _cursorY = ny;

                if (f.A && !_prev.A)
                {
                    _cursorDown = true;
                    _pointer(PointerPhase.Down, _cursorX, _cursorY);
                }
                else if (!f.A && _prev.A && _cursorDown)
                {
                    _cursorDown = false;
                    _pointer(PointerPhase.Up, _cursorX, _cursorY);
                }
                else if (moved)
                {
                    _pointer(PointerPhase.Move, _cursorX, _cursorY); // drags included — the pointer is down or roaming
                }
            }
            else if (CursorVisible)
            {
                if (_cursorDown)
                {
                    _cursorDown = false;
                    _pointer(PointerPhase.Up, _cursorX, _cursorY);
                }

                CursorVisible = false;
                _cursorX = _cursorY = -1f;
            }

            _prev = f;
        }

        private void Button(bool now, bool before, MinigameAction a)
        {
            if (now && !before)
            {
                _press(a);
            }
            else if (!now && before)
            {
                _release(a);
            }
        }

        private void Direction(int slot, MinigameAction a, bool active, float dt)
        {
            if (!active)
            {
                if (_dirActive[slot])
                {
                    _dirActive[slot] = false;
                    _dirHeldFor[slot] = 0f;
                    _release(a);
                }

                return;
            }

            if (!_dirActive[slot])
            {
                _dirActive[slot] = true;
                _dirHeldFor[slot] = 0f;
                _press(a);
                return;
            }

            // Held: repeat like the hotbar scroll — a release+press pair per repeat, so edge-driven games
            // (press handlers) step along while api.Held stays true across the whole hold for loop-driven ones…
            // except the instant of the repeat itself, which no game samples mid-dispatch.
            float before = _dirHeldFor[slot];
            _dirHeldFor[slot] = before + dt;
            float sinceRepeatZone = before - RepeatDelay;
            if (_dirHeldFor[slot] >= RepeatDelay
                && (sinceRepeatZone < 0f || (int)(sinceRepeatZone / RepeatInterval) != (int)((_dirHeldFor[slot] - RepeatDelay) / RepeatInterval)))
            {
                _release(a);
                _press(a);
            }
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
