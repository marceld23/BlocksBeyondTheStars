// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The Unity half of the gamepad on-screen keyboard (#1211): that every text field the toolkit builds is
    /// wired to it, that A on a field opens the grid instead of typing into it, and that the whole thing
    /// stays invisible on keyboard/mouse. What the keys DO is covered headlessly in the Client.Core suite
    /// (OnScreenKeyboardLayoutTests).
    ///
    /// The build machine has no pad, so the pad paths are reached through
    /// <see cref="InputMap.DeviceOverrideForTest"/> and <see cref="OnScreenKeyboardUi.PressForTest"/> —
    /// which is the same entry point the on-screen buttons use, so these exercise the real wiring rather
    /// than a copy of it. How it FEELS on a controller stays the manual pass in #1227.
    /// </summary>
    public sealed class OnScreenKeyboardEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("KeyboardTestRoot", typeof(RectTransform));

        [TearDown]
        public void TearDown()
        {
            InputMap.DeviceOverrideForTest = null;
            OnScreenKeyboardUi.Dismiss();
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void EveryFieldTheToolkitBuilds_IsWiredToTheKeyboard()
        {
            // 63 AddInput call sites across the menus, the world dialogs and the editors — none of them
            // should have to remember to opt in, so the bridge is attached by the builder itself.
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, "hello", null, "Name", maxLength: 12);

            Assert.IsNotNull(field.GetComponent<PadTextEntryBridge>());
            Assert.AreEqual(12, field.characterLimit, "the keyboard takes its limit from the field itself");
        }

        [Test]
        public void OnKeyboardAndMouse_TheKeyboardIsNeverWanted_AndTypingIsUnaffected()
        {
            Assume.That(InputMap.ActiveDevice, Is.EqualTo(InputDeviceKind.KeyboardMouse),
                "the test host has no pad, so the desktop path is what must stay untouched");

            Assert.IsFalse(OnScreenKeyboardUi.WantedFor());
            Assert.IsFalse(OnScreenKeyboardUi.IsOpen);
            Assert.IsFalse(UiKit.TextFieldFocused(), "nothing is focused and no keyboard is up");
        }

        [Test]
        public void WhileTheKeyboardIsOpen_TheGameCountsThePlayerAsTyping_AndTheMenuVerbsGoQuiet()
        {
            string result = null;
            OnScreenKeyboardUi.Open("Title", "abc", 24, text => result = text);

            Assert.IsTrue(OnScreenKeyboardUi.IsOpen);

            // The one gate the rest of the game already uses for "the player is typing" — so movement,
            // hotkeys and the Esc/Tab handlers all do the right thing without knowing this class exists.
            Assert.IsTrue(UiKit.TextFieldFocused());

            // …and B must close the keyboard rather than the screen behind it.
            Assert.IsTrue(InputMap.ModalCaptures(InputAction.UiCancel));
            Assert.IsFalse(InputMap.Down(InputAction.UiCancel));

            OnScreenKeyboardUi.Dismiss();
            Assert.IsFalse(OnScreenKeyboardUi.IsOpen);
            Assert.IsFalse(UiKit.TextFieldFocused());
            Assert.IsFalse(InputMap.ModalCapture, "dismissing must hand the buttons back");
            Assert.IsNull(result, "dismiss is not accept — the callback must not fire");
        }

        [Test]
        public void OnAPad_SubmittingAFieldOpensTheKeyboardInsteadOfTypingIntoIt_AndDoneWritesItBack()
        {
            // The acceptance criterion of #1211: with a pad in hand, A on a text field must NOT drop the
            // player into the field (a focused InputField swallows the navigation axes) but open the grid.
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            Assert.IsTrue(OnScreenKeyboardUi.WantedFor());

            string written = null;
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, "hi", v => written = v, "Name", maxLength: 4);
            var bridge = field.GetComponent<PadTextEntryBridge>();

            bridge.OnSubmit(null);

            Assert.IsTrue(OnScreenKeyboardUi.IsOpen, "the field must hand over to the keyboard");
            Assert.IsFalse(field.isFocused, "…and must not have taken the pad captive itself");
            Assert.AreEqual("hi", OnScreenKeyboardUi.TextForTest, "it starts on what the field already held");

            OnScreenKeyboardUi.PressForTest("!");
            OnScreenKeyboardUi.PressForTest("?");
            OnScreenKeyboardUi.PressForTest("x"); // over the field's 4-character limit — dropped
            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Done);

            Assert.IsFalse(OnScreenKeyboardUi.IsOpen);
            Assert.AreEqual("hi!?", field.text);
            Assert.AreEqual("hi!?", written, "writing through field.text must fire the field's own listeners");
        }

        [Test]
        public void OnAPad_CancellingLeavesTheFieldExactlyAsItWas()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, "keep", null, "Name");

            field.GetComponent<PadTextEntryBridge>().OnSubmit(null);
            OnScreenKeyboardUi.PressForTest("z");
            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Cancel);

            Assert.IsFalse(OnScreenKeyboardUi.IsOpen);
            Assert.AreEqual("keep", field.text);
        }

        [Test]
        public void ShiftAndThePageSwitch_ChangeTheKeyboard_NotTheText()
        {
            OnScreenKeyboardUi.Open("Title", "abc", 0, _ => { });

            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Shift);
            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Page);

            Assert.IsTrue(OnScreenKeyboardUi.IsOpen, "neither key may close the keyboard");
            Assert.AreEqual("abc", OnScreenKeyboardUi.TextForTest);
        }

        [Test]
        public void Dismiss_IsSafe_WhenNoKeyboardWasEverOpened()
        {
            OnScreenKeyboardUi.Dismiss();
            OnScreenKeyboardUi.Dismiss();

            Assert.IsFalse(OnScreenKeyboardUi.IsOpen);
            Assert.IsFalse(InputMap.ModalCapture);
        }

        [Test]
        public void OpeningTwice_ReplacesTheFirstKeyboard_RatherThanStackingTwo()
        {
            OnScreenKeyboardUi.Open("First", string.Empty, 0, _ => { });
            OnScreenKeyboardUi.Open("Second", string.Empty, 0, _ => { });

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            int keyboards = 0;
            foreach (var canvas in canvases)
            {
                if (canvas != null && canvas.name == "OnScreenKeyboardCanvas")
                {
                    keyboards++;
                }
            }

            Assert.AreEqual(1, keyboards, "the last caller wins; two stacked keyboards would trap the pad");
        }

        [Test]
        public void OnAPad_APasswordField_ShowsBulletsOnThePreview_ButWritesTheRealTextBack()
        {
            // The portal / server password dialogs (#1289): the keyboard's preview line is big and readable
            // from the couch, so it must mask exactly like the field itself does.
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, string.Empty, null, "Password");
            field.contentType = InputField.ContentType.Password;

            field.GetComponent<PadTextEntryBridge>().OnSubmit(null);
            OnScreenKeyboardUi.PressForTest("a");
            OnScreenKeyboardUi.PressForTest("b");
            OnScreenKeyboardUi.PressForTest("c");

            Assert.AreEqual("abc", OnScreenKeyboardUi.TextForTest);
            Assert.AreEqual("•••_", OnScreenKeyboardUi.PreviewForTest, "bullets plus the caret, never the letters");

            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Done);
            Assert.AreEqual("abc", field.text);
        }

        [Test]
        public void OnAPad_AnIntegerField_DropsLettersAndKeepsDigits()
        {
            // The text setter bypasses uGUI's characterValidation, so the keyboard filters itself (#1289).
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, string.Empty, null, "Port");
            field.contentType = InputField.ContentType.IntegerNumber;

            field.GetComponent<PadTextEntryBridge>().OnSubmit(null);
            OnScreenKeyboardUi.PressForTest("2");
            OnScreenKeyboardUi.PressForTest("x");
            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Space);
            OnScreenKeyboardUi.PressForTest("5");
            OnScreenKeyboardUi.PressForTest(".");

            Assert.AreEqual("25", OnScreenKeyboardUi.TextForTest);
            Assert.AreEqual("25_", OnScreenKeyboardUi.PreviewForTest, "a number field is not a password — no masking");

            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Done);
            Assert.AreEqual("25", field.text);
        }

        [Test]
        public void APlainTextField_IsNeitherMaskedNorFiltered()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            var field = UiKit.AddInput(_root.transform, 0f, 0f, 300f, 40f, string.Empty, null, "Name");

            field.GetComponent<PadTextEntryBridge>().OnSubmit(null);
            OnScreenKeyboardUi.PressForTest("a");
            OnScreenKeyboardUi.PressForTest("1");

            Assert.AreEqual("a1_", OnScreenKeyboardUi.PreviewForTest);
            OnScreenKeyboardUi.PressForTest(OnScreenKeyboardLayout.Cancel);
        }
    }
}
