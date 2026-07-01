// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Gamepad backend (legacy Input Manager). Reads the joystick axes added to <c>InputManager.asset</c>
    /// and the standard <see cref="KeyCode.JoystickButton0"/>… buttons. Mapping targets the common case —
    /// an <b>Xbox / XInput</b> pad on Windows; the axis numbers (right stick = 4th/5th axis, d-pad = 6th/7th)
    /// and the A/B/X/Y/LB/RB button order follow that layout. Other pad families (DirectInput / PlayStation
    /// via some drivers) report different numbers, so the axis names and this button table are the single
    /// place to retune — see issue #195 (needs a pass on real hardware; not verifiable in CI).
    ///
    /// The left stick is NOT read here: the project's InputManager already maps it onto the shared
    /// "Horizontal"/"Vertical" axes, so movement flows through <see cref="DesktopInputSource.MoveX"/> for
    /// free. This source therefore contributes the RIGHT stick (look), the face/shoulder buttons, and the
    /// d-pad (hotbar). <see cref="InputMap"/> combines it with the keyboard/mouse source, so both are always
    /// live; with no pad connected every getter here returns zero/false.
    /// </summary>
    public sealed class GamepadInputSource : IInputSource
    {
        // Axis names — must match the entries appended to client/ProjectSettings/InputManager.asset.
        private const string AxisRightStickX = "RightStickX";
        private const string AxisRightStickY = "RightStickY"; // inverted in the asset so up = look up (like Mouse Y)
        private const string AxisDpadX = "DPadX";

        // XInput button layout (Windows).
        private const KeyCode BtnA = KeyCode.JoystickButton0;   // jump / submit
        private const KeyCode BtnB = KeyCode.JoystickButton1;   // crouch / cancel
        private const KeyCode BtnX = KeyCode.JoystickButton2;   // interact
        private const KeyCode BtnY = KeyCode.JoystickButton3;   // toggle third-person
        private const KeyCode BtnLb = KeyCode.JoystickButton4;  // place block
        private const KeyCode BtnRb = KeyCode.JoystickButton5;  // mine / attack

        // Tunables (see issue #195). Look is a rate (deg/sec at sensitivity 1) turned into a per-frame delta
        // so it lands in the same space as a mouse delta — the caller still multiplies by MouseSensitivity,
        // so pad turn speed also scales with that slider. Deadzone rejects stick drift at rest.
        private const float StickDeadzone = 0.2f;
        private const float LookYawSpeed = 75f;
        private const float LookPitchSpeed = 60f;
        private const float DpadRepeatSeconds = 0.25f;

        private float _dpadCooldownUntil;

        public InputDeviceKind Kind => InputDeviceKind.Gamepad;

        /// <summary>Whether at least one joystick is connected (a non-empty name). Recomputed cheaply per
        /// call; when false the source stays fully inert so an unplugged pad costs nothing.</summary>
        public static bool Connected()
        {
            var names = Input.GetJoystickNames();
            if (names == null)
            {
                return false;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrEmpty(names[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static float Deadzoned(float v) => Mathf.Abs(v) < StickDeadzone ? 0f : v;

        // Left stick already feeds the shared Horizontal/Vertical axes — don't double-count it here.
        public float MoveX() => 0f;
        public float MoveY() => 0f;

        public float LookX()
        {
            if (!Connected())
            {
                return 0f;
            }

            return Deadzoned(Input.GetAxis(AxisRightStickX)) * LookYawSpeed * Time.deltaTime;
        }

        public float LookY()
        {
            if (!Connected())
            {
                return 0f;
            }

            return Deadzoned(Input.GetAxis(AxisRightStickY)) * LookPitchSpeed * Time.deltaTime;
        }

        /// <summary>D-pad left/right cycles the hotbar, edge-/repeat-gated so a held press steps at a steady
        /// rate rather than flying through all nine slots in one frame. Sign matches the mouse wheel:
        /// &gt;0 = previous slot, &lt;0 = next slot.</summary>
        public float HotbarScroll()
        {
            if (!Connected())
            {
                return 0f;
            }

            float dx = Deadzoned(Input.GetAxis(AxisDpadX));
            if (Mathf.Abs(dx) < 0.5f)
            {
                _dpadCooldownUntil = 0f; // released → ready to fire immediately on next press
                return 0f;
            }

            if (Time.unscaledTime < _dpadCooldownUntil)
            {
                return 0f;
            }

            _dpadCooldownUntil = Time.unscaledTime + DpadRepeatSeconds;
            return dx > 0f ? -1f : 1f; // right = next (<0), left = previous (>0)
        }

        public bool JumpHeld() => Connected() && Input.GetKey(BtnA);
        public bool JumpDown() => Connected() && Input.GetKeyDown(BtnA);
        public bool CrouchHeld() => Connected() && Input.GetKey(BtnB);
        public bool PrimaryDown() => Connected() && Input.GetKeyDown(BtnRb);
        public bool PrimaryHeld() => Connected() && Input.GetKey(BtnRb);
        public bool SecondaryDown() => Connected() && Input.GetKeyDown(BtnLb);

        // No direct 1..9 pick on a pad — the hotbar is cycled via HotbarScroll (d-pad) instead.
        public int HotbarSlotDown() => -1;

        /// <summary>The pad button bound to a discrete action, or <see cref="KeyCode.None"/> if this action
        /// isn't on the pad yet (it then stays keyboard-only — sources are combined, so nothing is lost).
        /// A fuller binding set is follow-up work (issue #195).</summary>
        public static KeyCode ButtonFor(InputAction action) => action switch
        {
            InputAction.Interact => BtnX,
            InputAction.ToggleThirdPerson => BtnY,
            InputAction.FlightEnterInterior => BtnY,
            _ => KeyCode.None,
        };

        public bool ActionDown(InputAction action)
        {
            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKeyDown(b);
        }

        public bool ActionHeld(InputAction action)
        {
            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKey(b);
        }

        public bool ActionUp(InputAction action)
        {
            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKeyUp(b);
        }

        public bool HadActivityThisFrame()
        {
            if (!Connected())
            {
                return false;
            }

            // Any of our mapped buttons, or a stick/d-pad pushed past the deadzone.
            if (Input.GetKey(BtnA) || Input.GetKey(BtnB) || Input.GetKey(BtnX) || Input.GetKey(BtnY)
                || Input.GetKey(BtnLb) || Input.GetKey(BtnRb))
            {
                return true;
            }

            // Left stick shows up on the shared Horizontal/Vertical axes; sample those too so simply
            // steering with the stick flips the glyphs to the pad set.
            bool moved = Mathf.Abs(Deadzoned(Input.GetAxis("Horizontal"))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis("Vertical"))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisRightStickX))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisRightStickY))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisDpadX))) > 0f;
            return moved;
        }
    }
}
