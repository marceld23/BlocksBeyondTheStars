// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Keyboard + mouse backend. This is a 1:1 wrapper over the exact legacy <c>UnityEngine.Input</c> calls
    /// the gameplay code used before the abstraction existed, so routing a call site through
    /// <see cref="InputMap"/> onto this source changes nothing: <see cref="MoveX"/> is
    /// <c>GetAxis("Horizontal")</c> (which already merges WASD and, via the project's InputManager, the
    /// gamepad left stick), the look deltas are the raw mouse axes the caller still scales by sensitivity,
    /// and the standard buttons resolve to the same space / Ctrl / mouse buttons as before. Discrete actions
    /// resolve through <see cref="InputMap.Key"/> so the rebinding UI keeps working unchanged.
    /// </summary>
    public sealed class DesktopInputSource : IInputSource
    {
        public InputDeviceKind Kind => InputDeviceKind.KeyboardMouse;

        public float MoveX() => Input.GetAxis("Horizontal");
        public float MoveY() => Input.GetAxis("Vertical");
        public float LookX() => Input.GetAxis("Mouse X");
        public float LookY() => Input.GetAxis("Mouse Y");
        public float HotbarScroll() => Input.GetAxis("Mouse ScrollWheel");

        public bool JumpHeld() => Input.GetButton("Jump");
        public bool JumpDown() => Input.GetButtonDown("Jump");
        public bool CrouchHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        public bool PrimaryDown() => Input.GetMouseButtonDown(0);
        public bool PrimaryHeld() => Input.GetMouseButton(0);
        public bool SecondaryDown() => Input.GetMouseButtonDown(1);

        public int HotbarSlotDown()
        {
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool ActionDown(InputAction action) => Input.GetKeyDown(InputMap.Key(action));
        public bool ActionHeld(InputAction action) => Input.GetKey(InputMap.Key(action));
        public bool ActionUp(InputAction action) => Input.GetKeyUp(InputMap.Key(action));

        /// <summary>Keyboard/mouse activity this frame — any key held or the mouse moved/clicked/scrolled.
        /// Used only to decide whether to show keyboard glyphs; a tiny mouse jitter threshold avoids
        /// flip-flopping the glyph set while the player rests a hand on the mouse.</summary>
        public bool HadActivityThisFrame()
        {
            if (Input.anyKey || Input.GetMouseButton(0) || Input.GetMouseButton(1))
            {
                return true;
            }

            const float MouseMoveEps = 0.5f;
            return Mathf.Abs(Input.GetAxis("Mouse X")) > MouseMoveEps
                || Mathf.Abs(Input.GetAxis("Mouse Y")) > MouseMoveEps
                || Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.01f;
        }
    }
}
