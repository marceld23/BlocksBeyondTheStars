// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Text entry on touch devices. Native tablets (Android/iOS) need nothing: uGUI's InputField opens the
    /// OS soft keyboard by itself. The gap is the **WebGL build on a touch device** — TouchScreenKeyboard is
    /// unsupported in the browser, so a tapped field would be dead. There, <see cref="Prompt"/> falls back to
    /// the browser's own <c>window.prompt()</c> (which opens the OS keyboard on every mobile browser), via
    /// the <c>BbsTextPrompt.jslib</c> plugin. On every other platform <see cref="NeedsPrompt"/> is false and
    /// this class is inert — the shipped desktop/keyboard flow is untouched.
    /// </summary>
    public static class TouchTextEntry
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string BbsPromptText(string label, string current);
#endif

        /// <summary>True when text entry must go through the browser prompt (WebGL on a touch device).</summary>
        public static bool NeedsPrompt
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return TouchControlsUi.ShouldShow();
#else
                return false;
#endif
            }
        }

        /// <summary>Shows the blocking browser prompt and returns the entered text (the previous value when
        /// cancelled). Only meaningful when <see cref="NeedsPrompt"/>; returns <paramref name="current"/> otherwise.</summary>
        public static string Prompt(string label, string current)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return BbsPromptText(label ?? string.Empty, current ?? string.Empty);
#else
            return current;
#endif
        }

        /// <summary>Wires the fallback onto an InputField: when <see cref="NeedsPrompt"/>, a tap opens the
        /// browser prompt and writes the result back through <c>field.text</c> (so the field's onValueChanged
        /// callbacks fire as usual). No-op — not even a component — on all other platforms.</summary>
        public static void Attach(InputField field, string label)
        {
            if (field == null || !NeedsPrompt)
            {
                return;
            }

            var bridge = field.gameObject.AddComponent<TouchTextPromptBridge>();
            bridge.Init(field, label);
        }
    }

    /// <summary>Per-field click hook for <see cref="TouchTextEntry.Attach"/> (WebGL-touch only).</summary>
    public sealed class TouchTextPromptBridge : MonoBehaviour, IPointerClickHandler
    {
        private InputField _field;
        private string _label;

        public void Init(InputField field, string label)
        {
            _field = field;
            _label = label ?? string.Empty;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_field == null || !TouchTextEntry.NeedsPrompt)
            {
                return;
            }

            _field.text = TouchTextEntry.Prompt(_label, _field.text);
            _field.DeactivateInputField();
        }
    }
}
