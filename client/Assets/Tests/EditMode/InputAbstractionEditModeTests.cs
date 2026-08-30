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
            Assert.AreEqual(0, pad.DpadYStep());
            Assert.IsFalse(pad.HadActivityThisFrame());
            Assert.IsFalse(pad.ActionDown(InputAction.Interact));
            Assert.IsFalse(pad.ActionDown(InputAction.OpenChat), "the d-pad verbs must be inert too (#1220)");
            Assert.IsFalse(pad.ActionDown(InputAction.RotateShape));
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

        // ---- menu verbs (#1198) ---------------------------------------------------------------------------

        [Test]
        public void MenuVerbs_ResolveToTheKeysAndPadButtonsTheScreensUsedToPollRaw()
        {
            // Nine screens spelled out KeyCode.JoystickButton1 / JoystickButton7 next to their Escape / Tab
            // check. Routing them through actions must land on exactly the same controls.
            Assert.AreEqual(KeyCode.Escape, InputMap.DefaultKey(InputAction.UiCancel));
            Assert.AreEqual(KeyCode.Tab, InputMap.DefaultKey(InputAction.UiMenu));
            Assert.AreEqual(KeyCode.JoystickButton1, GamepadInputSource.ButtonFor(InputAction.UiCancel));
            Assert.AreEqual(KeyCode.JoystickButton7, GamepadInputSource.ButtonFor(InputAction.UiMenu));
        }

        [Test]
        public void MenuVerbs_KeepTheirKeyboardKey_EvenWhenAnOverrideIsStored()
        {
            // Escape and Tab must not be bindable away: a player who did could end up inside a modal with no
            // key that leaves it. The PAD column stays rebindable.
            var settings = new ClientSettings();
            InputMap.Use(settings);

            settings.SetBoundKey(InputAction.UiCancel.ToString(), KeyCode.Q.ToString());
            InputMap.Use(settings);
            Assert.IsTrue(InputMap.KeyboardLocked(InputAction.UiCancel));
            Assert.AreEqual(KeyCode.Escape, InputMap.Key(InputAction.UiCancel), "a stored override must not win here");

            settings.SetBoundPad(InputAction.UiCancel.ToString(), KeyCode.JoystickButton3.ToString());
            Assert.AreEqual(KeyCode.JoystickButton3, GamepadInputSource.ButtonFor(InputAction.UiCancel),
                "the pad column stays freely rebindable");

            settings.SetBoundKey(InputAction.UiCancel.ToString(), "");
            settings.SetBoundPad(InputAction.UiCancel.ToString(), "");
            InputMap.Use(new ClientSettings()); // leave stock settings for later tests
        }

        [Test]
        public void MenuVerbs_AreLabelled_AndOnlyTheyAreKeyboardLocked()
        {
            Assert.AreEqual("ui.key.ui_cancel", InputMap.LabelKey(InputAction.UiCancel));
            Assert.AreEqual("ui.key.ui_menu", InputMap.LabelKey(InputAction.UiMenu));
            CollectionAssert.AreEquivalent(
                new[] { InputAction.UiCancel, InputAction.UiMenu },
                InputMap.MenuRemappable,
                "the menu group is exactly the two locked-keyboard actions");

            foreach (var action in InputMap.Remappable)
            {
                Assert.IsFalse(InputMap.KeyboardLocked(action), $"{action} must stay rebindable");
            }
        }

        [Test]
        public void NamedPadButtons_MapOntoTheXInputLayout_AndAreInertWithNoPad()
        {
            Assert.AreEqual(KeyCode.JoystickButton0, GamepadInputSource.CodeOf(PadButton.A));
            Assert.AreEqual(KeyCode.JoystickButton1, GamepadInputSource.CodeOf(PadButton.B));
            Assert.AreEqual(KeyCode.JoystickButton2, GamepadInputSource.CodeOf(PadButton.X));
            Assert.AreEqual(KeyCode.JoystickButton3, GamepadInputSource.CodeOf(PadButton.Y));
            Assert.AreEqual(KeyCode.JoystickButton4, GamepadInputSource.CodeOf(PadButton.Lb));
            Assert.AreEqual(KeyCode.JoystickButton5, GamepadInputSource.CodeOf(PadButton.Rb));
            Assert.AreEqual(KeyCode.JoystickButton6, GamepadInputSource.CodeOf(PadButton.Back));
            Assert.AreEqual(KeyCode.JoystickButton7, GamepadInputSource.CodeOf(PadButton.Start));
            Assert.AreEqual(KeyCode.JoystickButton8, GamepadInputSource.CodeOf(PadButton.L3));
            Assert.AreEqual(KeyCode.JoystickButton9, GamepadInputSource.CodeOf(PadButton.R3));

            Assume.That(GamepadInputSource.Connected(), Is.False, "test host unexpectedly has a joystick");
            Assert.IsFalse(InputMap.PadDown(PadButton.A));
            Assert.IsFalse(InputMap.PadHeld(PadButton.Start));
            Assert.AreEqual(0f, InputMap.PadStickX());
            Assert.AreEqual(0f, InputMap.PadStickY());
            Assert.AreEqual(0f, InputMap.PadDpadStep());
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
        public void PadBinding_Override_Wins_AndClearRestoresDefault()
        {
            var settings = new ClientSettings();
            InputMap.Use(settings); // also hands the settings to the gamepad backend
            Assert.AreEqual(KeyCode.JoystickButton2, GamepadInputSource.ButtonFor(InputAction.Interact),
                "stock pad binding should hold when nothing is bound");

            settings.SetBoundPad(InputAction.Interact.ToString(), KeyCode.JoystickButton9.ToString());
            Assert.AreEqual(KeyCode.JoystickButton9, GamepadInputSource.ButtonFor(InputAction.Interact),
                "player pad override should win");

            settings.SetBoundPad(InputAction.Interact.ToString(), "");
            Assert.AreEqual(KeyCode.JoystickButton2, GamepadInputSource.ButtonFor(InputAction.Interact),
                "clearing the override should restore the default");

            // An action with no stock pad button can still be bound by the player.
            settings.SetBoundPad(InputAction.LootContainer.ToString(), KeyCode.JoystickButton6.ToString());
            Assert.AreEqual(KeyCode.JoystickButton6, GamepadInputSource.ButtonFor(InputAction.LootContainer));
            settings.SetBoundPad(InputAction.LootContainer.ToString(), "");
            InputMap.Use(new ClientSettings()); // leave stock settings for later tests
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

        // ---- Device-neutral verbs (#1041–#1043) --------------------------------------------------------------

        [Test]
        public void NewActions_KeepTheirHistoricalKeys_AndTakeTheFreePadButtons()
        {
            InputMap.Use(new ClientSettings());
            // VEGA's continue used to be the raw N key; the planet map polled M directly. Same letters, now rebindable.
            Assert.AreEqual(KeyCode.N, InputMap.Key(InputAction.VegaContinue));
            Assert.AreEqual(KeyCode.M, InputMap.Key(InputAction.PlanetMap));
            // The context-actions list has no keyboard default (every verb already has a key) — pad LS / touch ACT.
            Assert.AreEqual(KeyCode.None, InputMap.Key(InputAction.ContextActions));
            Assert.AreEqual(KeyCode.JoystickButton8, GamepadInputSource.ButtonFor(InputAction.ContextActions));
            Assert.AreEqual(KeyCode.JoystickButton6, GamepadInputSource.ButtonFor(InputAction.VegaContinue));
            // …and the two stick clicks / View have glyphs, so the VEGA hint and the settings rows can name them.
            // "View" (the Xbox One/Series name), not Unity's 360-era "Back" — nobody found "Back" on a modern pad.
            Assert.AreEqual("LS", InputMap.PadGlyph(KeyCode.JoystickButton8));
            Assert.AreEqual("View", InputMap.PadGlyph(KeyCode.JoystickButton6));
        }

        [Test]
        public void EveryAction_HasALabelKey_AndIsListedInARebindGroup()
        {
            foreach (InputAction action in System.Enum.GetValues(typeof(InputAction)))
            {
                Assert.IsNotEmpty(InputMap.LabelKey(action), $"{action} has no ui.key.* label");
                bool listed = System.Array.IndexOf(InputMap.Remappable, action) >= 0
                              || System.Array.IndexOf(InputMap.FlightRemappable, action) >= 0
                              || System.Array.IndexOf(InputMap.VehicleRemappable, action) >= 0
                              || System.Array.IndexOf(InputMap.MenuRemappable, action) >= 0; // #1198
                Assert.IsTrue(listed, $"{action} is missing from the settings rebind groups");
            }
        }

        [Test]
        public void UnboundAction_IsNeverDown_OnKeyboard()
        {
            InputMap.Use(new ClientSettings());
            // KeyCode.None must not be handed to Input.GetKeyDown — an unbound action simply never fires.
            Assert.IsFalse(InputMap.Down(InputAction.ContextActions));
            Assert.IsFalse(InputMap.Held(InputAction.ContextActions));
        }

        // ---- d-pad verbs, triggers and the controller page (#1211 / #1219 / #1220) ------------------------

        [Test]
        public void OpenChat_KeepsTheEnterKey_AndLivesOnTheDPadRatherThanAFaceButton()
        {
            InputMap.Use(new ClientSettings());
            // Chat opened on a raw Return before it was an action; the key must not move.
            Assert.AreEqual(KeyCode.Return, InputMap.DefaultKey(InputAction.OpenChat));
            Assert.AreEqual("ui.key.open_chat", InputMap.LabelKey(InputAction.OpenChat));

            // No STOCK pad button: the d-pad is an axis and cannot be expressed as a KeyCode, so d-pad up
            // fires this action from inside the gamepad source instead of through the binding table.
            Assert.AreEqual(KeyCode.None, GamepadInputSource.ButtonFor(InputAction.OpenChat));

            // …and a player can still bind it to a free face button like any other action.
            var settings = new ClientSettings();
            InputMap.Use(settings);
            settings.SetBoundPad(InputAction.OpenChat.ToString(), KeyCode.JoystickButton3.ToString());
            Assert.AreEqual(KeyCode.JoystickButton3, GamepadInputSource.ButtonFor(InputAction.OpenChat));
            InputMap.Use(new ClientSettings());
        }

        [Test]
        public void ModalCapture_BlanksTheMenuVerbsOnly_AndOnlyWhileItIsUp()
        {
            Assert.IsFalse(InputMap.ModalCapture, "nothing may leave a capture standing");
            Assert.IsFalse(InputMap.ModalCaptures(InputAction.UiCancel));

            InputMap.ModalCapture = true;
            try
            {
                // One press of B must close the on-screen keyboard and NOT the screen behind it; the
                // keyboard reads the physical button, everyone else reads the action and gets nothing.
                Assert.IsTrue(InputMap.ModalCaptures(InputAction.UiCancel));
                Assert.IsTrue(InputMap.ModalCaptures(InputAction.UiMenu));
                Assert.IsFalse(InputMap.Down(InputAction.UiCancel));
                Assert.IsFalse(InputMap.Held(InputAction.UiMenu));
                Assert.IsFalse(InputMap.Up(InputAction.UiCancel));

                // Gameplay verbs are untouched — a modal takes the two menu buttons, not the input system.
                Assert.IsFalse(InputMap.ModalCaptures(InputAction.Interact));
                Assert.IsFalse(InputMap.ModalCaptures(InputAction.OpenChat));
            }
            finally
            {
                InputMap.ModalCapture = false;
            }

            Assert.IsFalse(InputMap.ModalCaptures(InputAction.UiCancel));
        }

        [Test]
        public void ControllerSettings_ShipWithTheValuesTheCodeUsedBeforeTheyWereSettings()
        {
            var settings = new ClientSettings();

            // An existing client_settings.json has none of these fields, so JsonUtility leaves the defaults
            // in place — they must therefore BE the old hardcoded behaviour, or every player's pad changes
            // feel on update.
            Assert.AreEqual(0.2f, settings.PadDeadzone, 0.0001f);
            Assert.AreEqual(1f, settings.PadLookX, 0.0001f);
            Assert.AreEqual(1f, settings.PadLookY, 0.0001f);
            Assert.IsFalse(settings.PadInvertY);
            Assert.AreEqual(PadGlyphSet.Xbox, settings.PadGlyphs);
            Assert.IsTrue(settings.PadVibration, "stored only — nothing reads it yet");

            // Triggers stay OFF: the axis number is the one reading that differs between XInput, Proton and
            // the browser, and a pad that idles at full deflection would otherwise mine on its own.
            Assert.IsFalse(settings.PadTriggersMinePlace);

            Assert.That(ClientSettings.PadDeadzoneMin, Is.LessThan(ClientSettings.PadDeadzoneMax));
            Assert.That(ClientSettings.PadLookMin, Is.LessThan(ClientSettings.PadLookMax));
        }

        [Test]
        public void PadGlyphs_RenameTheSameButtonsPerLayout_MatchedByPhysicalPosition()
        {
            var settings = new ClientSettings();
            InputMap.Use(settings);

            Assert.AreEqual("(A)", InputMap.PadGlyph(KeyCode.JoystickButton0), "Xbox is the default");
            Assert.AreEqual("Menu", InputMap.PadGlyph(KeyCode.JoystickButton7), "the One/Series name, not the 360's Start");

            settings.PadGlyphs = PadGlyphSet.PlayStation;
            Assert.AreEqual("(Cross)", InputMap.PadGlyph(KeyCode.JoystickButton0));
            Assert.AreEqual("(Circle)", InputMap.PadGlyph(KeyCode.JoystickButton1));
            Assert.AreEqual("L1", InputMap.PadGlyph(KeyCode.JoystickButton4));

            // Nintendo swaps the two right-hand pairs: the BOTTOM button is B there, not A.
            settings.PadGlyphs = PadGlyphSet.Nintendo;
            Assert.AreEqual("(B)", InputMap.PadGlyph(KeyCode.JoystickButton0));
            Assert.AreEqual("(A)", InputMap.PadGlyph(KeyCode.JoystickButton1));
            Assert.AreEqual("(Y)", InputMap.PadGlyph(KeyCode.JoystickButton2));
            Assert.AreEqual("(X)", InputMap.PadGlyph(KeyCode.JoystickButton3));

            // The unnamed buttons keep their numeric fallback whatever the layout says.
            Assert.AreEqual("B12", InputMap.PadGlyph(KeyCode.JoystickButton12));
            Assert.IsNull(InputMap.PadGlyph(KeyCode.E));

            InputMap.Use(new ClientSettings());
        }

        [Test]
        public void InjectNextFrame_ArmsTheEdge_ForTheFollowingFrameOnly()
        {
            // The context-actions list fires a verb by injection; the edge is for the NEXT frame (the list has
            // closed and gameplay polls again), never the current one. In an EditMode test the frame counter
            // does not advance, so "next frame" is observable only as "not this frame".
            InputMap.InjectNextFrame(InputAction.ToggleLamp);
            Assert.IsFalse(InputMap.Down(InputAction.ToggleLamp));
        }
    }
}
