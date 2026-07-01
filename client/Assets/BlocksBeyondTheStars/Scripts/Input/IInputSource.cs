// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which family of hardware last drove input — used only to pick which button glyphs to show
    /// (keyboard letters vs pad face buttons). It never gates input: every source stays live at once
    /// (see <see cref="InputMap"/>), so a player can mix mouse + pad freely.</summary>
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Gamepad,
        Touch,
    }

    /// <summary>
    /// One hardware input backend, expressed as the game's control verbs rather than raw keys/axes. The
    /// gameplay code (<see cref="PlayerController"/>, <see cref="SpaceView"/>) reads these through
    /// <see cref="InputMap"/> instead of polling <c>UnityEngine.Input</c> directly, so a new backend
    /// (gamepad, touch) is added by implementing this interface — no gameplay changes.
    ///
    /// Continuous getters return the SAME units the legacy call sites expected, so the keyboard+mouse
    /// implementation is behaviour-preserving: move axes are −1..1 (like <c>GetAxis("Horizontal")</c>),
    /// and the look deltas are raw per-frame deltas (like <c>GetAxis("Mouse X"/"Mouse Y")</c>) that the
    /// caller still multiplies by its own sensitivity. A gamepad backend scales its stick into the same
    /// space (rate × <c>Time.deltaTime</c>) so the caller math is untouched.
    /// </summary>
    public interface IInputSource
    {
        InputDeviceKind Kind { get; }

        /// <summary>True if this backend saw ANY input this frame — drives the active-device glyph choice.</summary>
        bool HadActivityThisFrame();

        // Continuous — locomotion + camera.
        float MoveX();        // strafe, −1 (left) .. +1 (right)   — mirrors GetAxis("Horizontal")
        float MoveY();        // forward, −1 (back) .. +1 (fwd)    — mirrors GetAxis("Vertical")
        float LookX();        // yaw delta, caller multiplies by sensitivity   — mirrors GetAxis("Mouse X")
        float LookY();        // pitch delta, caller multiplies by sensitivity — mirrors GetAxis("Mouse Y")
        float HotbarScroll(); // >0 = previous slot, <0 = next slot — mirrors GetAxis("Mouse ScrollWheel")

        // Standard action buttons — the continuous/pointer core that was NOT rebindable before.
        bool JumpHeld();
        bool JumpDown();
        bool CrouchHeld();    // descend / climb-down (Ctrl/C on keyboard)
        bool PrimaryDown();   // mine / attack / scan tap (left mouse)
        bool PrimaryHeld();   // keep-mining hold (left mouse held)
        bool SecondaryDown(); // place block / use held item (right mouse)

        /// <summary>The hotbar slot 0..8 selected THIS frame by a direct pick (number keys / d-pad), or −1.</summary>
        int HotbarSlotDown();

        // Discrete, rebindable actions (the pre-existing InputMap set: Interact, PrimaryFire, …).
        bool ActionDown(InputAction action);
        bool ActionHeld(InputAction action);
        bool ActionUp(InputAction action);
    }
}
