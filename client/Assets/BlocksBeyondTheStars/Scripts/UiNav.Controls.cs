// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Left / Right on a focused value control step the value instead of moving the focus: the value of a
    /// <c>‹ value ›</c> cycle (<see cref="UiKit.AddCycle"/>) and of a − / + stepper. Before it a cycle was three focus
    /// stops and Left / Right only walked between them, so changing a setting with the pad took a walk to the arrow and
    /// an A press per step. The control navigates vertically only (Up / Down still leave the row), so uGUI's own move
    /// handler has nothing to do on Left / Right; the arrows themselves are no focus stops (mouse and touch keep them).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StepOnMove : MonoBehaviour, IMoveHandler
    {
        private System.Action<int> _step;

        /// <summary>Makes <paramref name="control"/> step with Left (-1) / Right (+1) through <paramref name="step"/>.</summary>
        public static StepOnMove Attach(Selectable control, System.Action<int> step)
        {
            if (control == null)
            {
                return null;
            }

            control.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            var handler = control.GetComponent<StepOnMove>();
            if (handler == null)
            {
                handler = control.gameObject.AddComponent<StepOnMove>();
            }

            handler._step = step;
            return handler;
        }

        /// <summary>uGUI's move event on the focused control (stick, d-pad, arrow keys).</summary>
        public void OnMove(AxisEventData eventData)
        {
            if (eventData == null || eventData.used)
            {
                return;
            }

            int dir = eventData.moveDir == MoveDirection.Left ? -1 : eventData.moveDir == MoveDirection.Right ? 1 : 0;
            if (dir == 0 || _step == null)
            {
                return;
            }

            eventData.Use();
            UiSound.Click(); // the same feedback as a click on the arrow
            _step(dir);
        }
    }

    /// <summary>
    /// LB / RB step through a <see cref="UiKit.Segmented"/> tab bar (<see cref="UiKit.Segmented.StepWithShoulders"/>), the
    /// way they step the console's main tabs (#1409): the step is a click on the neighbouring tab, and the pad's focus
    /// lands on it. Not while a text field has the keys, while the screen is hidden or suspended, while another screen
    /// (the on-screen keyboard) holds the selection, or while a dialog covers the bar.
    /// </summary>
    public sealed class SegmentedShoulderTabs : MonoBehaviour
    {
        /// <summary>The bar the shoulder buttons step.</summary>
        public UiKit.Segmented Bar;

        private UiNavFocus _nav;

        private void Update()
        {
            if (Bar == null || Bar.Buttons == null || Bar.Buttons.Length < 2 || UiKit.TextFieldFocused())
            {
                return;
            }

            int dir = InputMap.PadDown(PadButton.Rb) ? 1 : InputMap.PadDown(PadButton.Lb) ? -1 : 0;
            if (dir == 0 || !OwnsThePad())
            {
                return;
            }

            int n = Bar.Buttons.Length;
            var next = Bar.Buttons[((Bar.Selected + dir) % n + n) % n];
            if (next == null || !next.IsInteractable())
            {
                return;
            }

            next.onClick.Invoke(); // the same path as a click: the look, the screen's callback, the click sound
            var es = EventSystem.current;
            if (es != null && InputMap.ActiveDevice == InputDeviceKind.Gamepad)
            {
                es.SetSelectedGameObject(next.gameObject); // the focus follows the tab, as on the console
                _nav?.NoteSelection(next.gameObject);
            }
        }

        private bool OwnsThePad()
        {
            if (_nav == null)
            {
                _nav = GetComponentInParent<UiNavFocus>();
            }

            if (_nav == null)
            {
                return true;
            }

            if (!_nav.isActiveAndEnabled || !_nav.WantsFocus || _nav.IsCovered(Bar.Buttons[Mathf.Clamp(Bar.Selected, 0, Bar.Buttons.Length - 1)]))
            {
                return false;
            }

            var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return current == null || current.transform.IsChildOf(_nav.transform);
        }
    }
}
