// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Headless (no graphics, no hardware) tests for the input abstraction. They pin the two guarantees that
    /// CAN be checked without a physical pad: (1) the gamepad source is fully inert when no joystick is
    /// connected — the property that makes the keyboard/mouse path behaviour-preserving — and (2) the pure
    /// mapping tables (button-for-action, key resolution, glyphs) are correct. The actual on-hardware feel of
    /// sticks/buttons is a manual pass (issue #195); CI cannot exercise a controller. See
    /// docs/developer/CLIENT_TESTING.md.
    /// </summary>
    public sealed class InputAbstractionEditModeTests
    {
        // ---- Gamepad source is inert with no pad connected (behaviour-preservation guarantee) -------------

        [Test]
        public void GamepadSource_WithNoPad_ProducesNoMovementOrLook()
        {
            // The test runner has no joystick, so Connected() is false and every continuous getter must be 0.
            Assume.That(GamepadInputSource.Connected(), Is.False, "test host unexpectedly has a joystick");
            var pad = new GamepadInputSource();

            Assert.AreEqual(0f, pad.MoveX());
            Assert.AreEqual(0f, pad.MoveY());
            Assert.AreEqual(0f, pad.LookX());
            Assert.AreEqual(0f, pad.LookY());
            Assert.AreEqual(0f, pad.HotbarScroll());
        }

        [Test]
        public void GamepadSource_WithNoPad_ReportsNoButtonsAndNoActivity()
        {
            Assume.That(GamepadInputSource.Connected(), Is.False);
            var pad = new GamepadInputSource();

            Assert.IsFalse(pad.JumpHeld());
            Assert.IsFalse(pad.JumpDown());
            Assert.IsFalse(pad.CrouchHeld());
            Assert.IsFalse(pad.PrimaryDown());
            Assert.IsFalse(pad.PrimaryHeld());
            Assert.IsFalse(pad.SecondaryDown());
            Assert.AreEqual(-1, pad.HotbarSlotDown());
            Assert.IsFalse(pad.HadActivityThisFrame());
            Assert.IsFalse(pad.ActionDown(InputAction.Interact));
        }

        // ---- Pure mapping tables --------------------------------------------------------------------------

        [Test]
        public void ButtonFor_MapsInteractToX_AndLeavesUnmappedActionsUnbound()
        {
            Assert.AreEqual(KeyCode.JoystickButton2, GamepadInputSource.ButtonFor(InputAction.Interact));
            Assert.AreEqual(KeyCode.JoystickButton3, GamepadInputSource.ButtonFor(InputAction.ToggleThirdPerson));
            // An action with no pad binding stays keyboard-only (KeyCode.None) — the combined map keeps it usable.
            Assert.AreEqual(KeyCode.None, GamepadInputSource.ButtonFor(InputAction.LootContainer));
        }

        [Test]
        public void Key_UsesDefault_WhenUnbound_AndOverride_WhenBound()
        {
            var settings = new ClientSettings();
            InputMap.Use(settings);
            Assert.AreEqual(KeyCode.E, InputMap.Key(InputAction.Interact), "default should hold when unbound");

            settings.SetBoundKey(InputAction.Interact.ToString(), KeyCode.Q.ToString());
            InputMap.Use(settings);
            Assert.AreEqual(KeyCode.Q, InputMap.Key(InputAction.Interact), "player override should win");

            // Reset so later tests / play sessions see stock bindings.
            settings.SetBoundKey(InputAction.Interact.ToString(), "");
            InputMap.Use(settings);
        }

        [Test]
        public void Glyph_ShowsBoundKey_OnKeyboardMouse()
        {
            InputMap.Use(new ClientSettings());
            // No hardware activity in the headless runner, so the active device stays keyboard/mouse and the
            // glyph is the bound key name rather than a pad face-button label.
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, InputMap.ActiveDevice);
            Assert.AreEqual("E", InputMap.Glyph(InputAction.Interact));
        }
    }
}
