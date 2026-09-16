// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// The caret crash (<c>NullReferenceException</c> in <c>InputField.GenerateCaret</c>): a focused field whose canvas
    /// is switched off with <c>canvas.enabled = false</c>. uGUI logs an exception from the canvas rebuild, which fails a
    /// PlayMode test on its own — so every case here simply has to run through a few caret blinks cleanly and leave
    /// the field released.
    /// </summary>
    public sealed class InputFocusGuardPlayModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator FocusedField_CanvasSwitchedOff_IsReleasedWithoutCaretException()
        {
            var (canvas, field) = BuildScreen();
            field.ActivateInputField();
            yield return null;
            yield return null;
            Assert.IsTrue(field.isFocused, "precondition: the field took focus");

            canvas.enabled = false;
            yield return WaitCaretBlinks();

            Assert.IsFalse(field.isFocused, "a field under a switched-off canvas must be released");
            Assert.IsFalse(InputFocusGuard.HasLiveCanvas(field));
        }

        [UnityTest]
        public IEnumerator FocusRequested_AndCanvasSwitchedOffInTheSameFrame_NeverFocusesUnderTheHiddenCanvas()
        {
            // ActivateInputField only raises a flag; uGUI focuses the field in its LateUpdate. A screen that hides in
            // the same frame (a hyperjump closing the menu while a field was clicked) left no message to react to.
            var (canvas, field) = BuildScreen();
            yield return null;

            field.ActivateInputField();
            canvas.enabled = false;
            yield return WaitCaretBlinks();

            Assert.IsFalse(field.isFocused);
        }

        [UnityTest]
        public IEnumerator FieldFocusedWhileItsCanvasIsAlreadyHidden_IsReleased()
        {
            // The chat box in the flight view: its canvas is off, Enter wakes the row and focuses the field.
            var (canvas, field) = BuildScreen();
            canvas.enabled = false;
            yield return null;

            field.ActivateInputField();
            yield return WaitCaretBlinks();

            Assert.IsFalse(field.isFocused);
        }

        [UnityTest]
        public IEnumerator NestedCanvasSwitchedOff_UnderALiveRoot_KeepsTheFieldFocused()
        {
            var (root, field) = BuildScreen();
            var nestedGo = new GameObject("Nested", typeof(RectTransform), typeof(Canvas));
            nestedGo.transform.SetParent(root.transform, false);
            field.transform.SetParent(nestedGo.transform, false);
            field.ActivateInputField();
            yield return null;
            yield return null;

            nestedGo.GetComponent<Canvas>().enabled = false;
            yield return WaitCaretBlinks();

            Assert.IsTrue(InputFocusGuard.HasLiveCanvas(field), "the root canvas above still draws the field");
            Assert.IsTrue(field.isFocused, "a nested canvas toggle must not steal the player's typing");
        }

        private static IEnumerator WaitCaretBlinks()
        {
            // The caret blinks at 0.85 Hz by default; a few rebuild cycles cover both the "on" and "off" phase.
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        private (Canvas canvas, InputField field) BuildScreen()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                _spawned.Add(es);
            }

            var canvasGo = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _spawned.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var fieldGo = new GameObject("Field", typeof(RectTransform), typeof(Image));
            fieldGo.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)fieldGo.transform).sizeDelta = new Vector2(300f, 40f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(fieldGo.transform, false);
            ((RectTransform)textGo.transform).sizeDelta = new Vector2(280f, 36f);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.supportRichText = false;

            var field = fieldGo.AddComponent<InputField>();
            field.textComponent = text;
            field.text = "caret";
            fieldGo.AddComponent<InputFocusGuard>().Init(field);
            return (canvas, field);
        }
    }
}
