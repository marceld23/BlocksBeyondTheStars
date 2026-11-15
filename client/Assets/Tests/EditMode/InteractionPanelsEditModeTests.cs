// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The in-world interaction panels on the menu template (#3662, UI layout 2026-11): an opaque dialog on a modal scrim (so
    /// the pad's focus stays inside it), Close drawn with the cross and built last (a pad never lands on it first), Confirm as
    /// the one primary action, every control a finger's 44 reference px high, a Close of its own on the train cab (a tablet's
    /// touch layer hides under a menu), keyboard-only hints only with a keyboard in hand. Each panel here opens without a
    /// <c>Game</c> (their Open paths are null-safe), and Awake/OnDestroy never run in edit mode, so the teardown removes the
    /// canvases itself.
    /// </summary>
    public sealed class InteractionPanelsEditModeTests
    {
        private readonly List<UiPanel> _panels = new();
        private bool _hadEventSystem;
        private bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            _reducedMotion = UiKit.ReducedMotion;
            UiKit.ReducedMotion = true; // UiKit.OpenModal then only sets the alpha — no transition component to drive
        }

        [TearDown]
        public void TearDown()
        {
            InputMap.DeviceOverrideForTest = null;
            UiKit.ReducedMotion = _reducedMotion;
            foreach (var panel in _panels)
            {
                if (panel == null)
                {
                    continue;
                }

                if (panel.CanvasForTest != null)
                {
                    Object.DestroyImmediate(panel.CanvasForTest.gameObject);
                }

                Object.DestroyImmediate(panel.gameObject);
            }

            _panels.Clear();

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private T NewPanel<T>(string name) where T : UiPanel
        {
            var panel = new GameObject(name).AddComponent<T>();
            _panels.Add(panel);
            return panel;
        }

        [Test]
        public void BeamPad_IsAnOpaqueModalDialogWithTheCrossOnCloseBuiltLast()
        {
            var p = NewPanel<BeamPadUi>("BeamPad");
            p.Open(1);

            AssertOpaqueModal(p.CanvasForTest);
            var buttons = p.CanvasForTest.GetComponentsInChildren<Button>(true);
            var close = buttons[buttons.Length - 1];
            Assert.AreEqual("ui.beam.close", Label(close), "Close is the last control — a pad's first focus is never Close");
            AssertIcon(close, "btn_close");
            AssertTapTargets(p.CanvasForTest);
        }

        [Test]
        public void BeamPad_RenameIsOffOnAPadThePlayerDoesNotOwn()
        {
            var p = NewPanel<BeamPadUi>("BeamPad");
            p.Open(1); // no world: the pad is nobody's the player knows

            var rename = Find(p.CanvasForTest, "ui.beam.rename");
            Assert.IsFalse(rename.interactable, "a Rename that did nothing on a click is greyed out (and skipped by the pad)");
        }

        [Test]
        public void TrainCab_HasACloseOfItsOwn_ForATabletWhoseTouchLayerHidesUnderTheMenu()
        {
            var p = NewPanel<TrainCabUi>("TrainCab");
            p.Open("t1"); // no train in the list: the panel still opens with its title — and now its way out

            var close = Find(p.CanvasForTest, "ui.action.close");
            AssertIcon(close, "btn_close");
            Assert.AreEqual(1, p.CanvasForTest.transform.childCount, "still exactly one modal overlay (shape B2)");
            AssertTapTargets(p.CanvasForTest);
        }

        [Test]
        public void StoryReader_IsAnOpaquePageWithTheCrossOnClose()
        {
            var p = NewPanel<StoryReaderUi>("StoryReader");
            p.Open("A title", "A label", "A text to read.");

            AssertOpaqueModal(p.CanvasForTest);
            AssertIcon(Find(p.CanvasForTest, "ui.reader.close"), "btn_close");
            AssertTapTargets(p.CanvasForTest);
        }

        [Test]
        public void BeaconLabel_PadLandsOnConfirm_AndTheKeyboardHintHidesWithoutAKeyboard()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            var p = NewPanel<BeaconLabelUi>("BeaconLabel");
            p.Open("Name it", "Iron Lake", null);

            AssertOpaqueModal(p.CanvasForTest);
            var first = p.CanvasForTest.GetComponent<UiNavFocus>().FocusTarget();
            Assert.IsNotNull(first);
            Assert.AreEqual("ui.beacon.confirm", Label(first), "the pad's first stop is Confirm — not the field, not Cancel");
            Assert.AreSame(UiKit.PrimarySprite, ((Image)first.targetGraphic).sprite, "Confirm is the dialog's one primary action");
            AssertIcon(Find(p.CanvasForTest, "ui.beacon.cancel"), "btn_back");
            Assert.IsFalse(KeysHint(p.CanvasForTest).gameObject.activeSelf, "\"— Enter · — Esc\" names keys a pad does not have");
            AssertTapTargets(p.CanvasForTest);
        }

        [Test]
        public void BeaconLabel_KeyboardHintShowsWithAKeyboard()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.KeyboardMouse;
            var p = NewPanel<BeaconLabelUi>("BeaconLabel");
            p.Open("Name it", string.Empty, null);

            Assert.IsTrue(KeysHint(p.CanvasForTest).gameObject.activeSelf);
        }

        [Test]
        public void AnswerKeyPrefix_OnlyWithAKeyboardInHand()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.KeyboardMouse;
            Assert.AreEqual("[2] ", NpcDialogUi.KeyPrefix(2));
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            Assert.AreEqual(string.Empty, NpcDialogUi.KeyPrefix(2), "a pad picks with A — no bracketed digits");
            InputMap.DeviceOverrideForTest = InputDeviceKind.Touch;
            Assert.AreEqual(string.Empty, NpcDialogUi.KeyPrefix(2), "a tablet taps — no bracketed digits");
        }

        [Test]
        public void NpcDialogue_SitsAboveVegasCommsRowEvenAtItsTallest()
        {
            // The tallest conversation: four answers under a four-line speech (~100 px of wrapped italic text).
            const float tallest = 130f + 56f + (4f * (44f + 8f));
            float bottom = 540f - NpcDialogUi.PanelRaise + (tallest / 2f);
            float vegaTop = 546f * (1080f / UiKit.HudRefH); // VEGA's speech panel, HUD → menu reference
            Assert.Less(bottom, vegaTop, "VEGA's lesson on answering (tutorial c11.2) comes while this panel is open");
        }

        // ---------------- helpers ----------------

        /// <summary>The panel hangs off a modal scrim (the pad's focus stays inside it) and has an opaque dialog panel.</summary>
        private static void AssertOpaqueModal(Canvas canvas)
        {
            Assert.IsNotNull(canvas.GetComponentInChildren<UiModalScope>(true), "a modal scrim (UiKit.AddModalDim)");
            bool opaque = false;
            foreach (var image in canvas.GetComponentsInChildren<Image>(true))
            {
                opaque |= image.gameObject.name == "DialogPanel" && image.sprite == UiKit.DialogSprite;
            }

            Assert.IsTrue(opaque, "an opaque dialog panel (UiKit.AddDialogPanel) — no world shining through the text");
        }

        private static void AssertIcon(Selectable button, string icon)
        {
            var sprite = UiKit.Icon(icon);
            Assert.IsNotNull(sprite, $"the icon {icon} exists");
            bool found = false;
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                found |= image.sprite == sprite;
            }

            Assert.IsTrue(found, $"'{Label(button)}' carries {icon}");
        }

        /// <summary>Every interactable control is at least <see cref="UiKit.MinTapSize"/> high — itself or its hit area.</summary>
        private static void AssertTapTargets(Canvas canvas)
        {
            foreach (var control in canvas.GetComponentsInChildren<Selectable>(false))
            {
                if (!control.IsInteractable() || control.navigation.mode == Navigation.Mode.None || control is Scrollbar)
                {
                    continue; // a list's scrollbar, the arrows of a cycle
                }

                float h = ((RectTransform)control.transform).rect.height;
                foreach (Transform child in control.transform)
                {
                    if (child.name == "HitArea")
                    {
                        h = Mathf.Max(h, ((RectTransform)child).rect.height);
                    }
                }

                Assert.GreaterOrEqual(h, UiKit.MinTapSize, $"'{Label(control)}' is a finger's target");
            }
        }

        private static Button Find(Canvas canvas, string label)
        {
            foreach (var button in canvas.GetComponentsInChildren<Button>(true))
            {
                if (Label(button) == label)
                {
                    return button;
                }
            }

            Assert.Fail($"no button '{label}'");
            return null;
        }

        private static string Label(Selectable control)
        {
            var text = control.GetComponentInChildren<Text>(true);
            return text != null ? text.text : control.gameObject.name;
        }

        private static Text KeysHint(Canvas canvas)
        {
            foreach (var text in canvas.GetComponentsInChildren<Text>(true))
            {
                if (text.text.Contains("— Enter"))
                {
                    return text;
                }
            }

            Assert.Fail("no keyboard hint line");
            return null;
        }
    }
}
