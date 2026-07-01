// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Touch backend (tablet / touch browser). It is a thin reader over <see cref="TouchControlsUi"/>: the
    /// on-screen joystick, look pad and buttons live in that MonoBehaviour, and this maps them onto the
    /// <see cref="IInputSource"/> verbs. When no touch UI is active (desktop, or the controls are hidden
    /// during flight/menus) every getter returns zero/false, so <see cref="InputMap"/>'s combined read is
    /// unaffected — the abstraction stays behaviour-preserving on non-touch devices.
    ///
    /// Look conversion: the pad reports a pixel drag delta; <see cref="LookYawScale"/> maps it into the same
    /// space as a mouse delta (the caller still multiplies by MouseSensitivity, so the sensitivity slider
    /// scales touch look too). The exact scale is a feel tunable that needs an on-device pass (issue #196).
    /// </summary>
    public sealed class TouchInputSource : IInputSource
    {
        // Pixels → mouse-delta-equivalent. Roughly matches the InputManager's 0.1 mouse sensitivity so a drag
        // feels similar to a mouse move after the caller applies MouseSensitivity.
        private const float LookYawScale = 0.08f;
        private const float LookPitchScale = 0.08f;

        public InputDeviceKind Kind => InputDeviceKind.Touch;

        private static TouchControlsUi Ui => TouchControlsUi.Active;
        private static bool Live => Ui != null && Ui.Visible;

        public float MoveX() => Live ? Ui.Move.x : 0f;
        public float MoveY() => Live ? Ui.Move.y : 0f;
        public float LookX() => Live ? Ui.LookDelta.x * LookYawScale : 0f;
        public float LookY() => Live ? Ui.LookDelta.y * LookPitchScale : 0f;
        public float HotbarScroll() => Live ? Ui.HotbarStep() : 0f;

        public bool JumpHeld() => Live && Ui.JumpHeld;
        public bool JumpDown() => Live && Ui.JumpDown;
        public bool CrouchHeld() => Live && Ui.DescendHeld;
        public bool PrimaryDown() => Live && Ui.MineDown;
        public bool PrimaryHeld() => Live && Ui.MineHeld;
        public bool SecondaryDown() => Live && Ui.PlaceDown;

        // No 1..9 pick on touch — the hotbar is cycled via the ◄ ► buttons (HotbarScroll).
        public int HotbarSlotDown() => -1;

        // Discrete actions come from the per-context button clusters (USE / LAND / SHIP / AUTO / VIEW /
        // EXIT / FUEL, and BOOST as a hold). Anything without a touch button stays keyboard/pad-only —
        // harmless, since sources are combined.
        public bool ActionDown(InputAction action) => Live && Ui.ActionDownFor(action);
        public bool ActionHeld(InputAction action) => Live && Ui.ActionHeldFor(action);
        public bool ActionUp(InputAction action) => false;

        public bool HadActivityThisFrame()
        {
            if (!Live)
            {
                return false;
            }

            return Ui.Move.sqrMagnitude > 0.0001f
                || Ui.LookDelta.sqrMagnitude > 0.0001f
                || Ui.JumpHeld || Ui.MineHeld || Ui.DescendHeld;
        }
    }
}
