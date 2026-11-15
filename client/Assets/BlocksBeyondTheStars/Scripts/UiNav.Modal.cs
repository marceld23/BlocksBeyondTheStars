// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Marks a modal scrim (<see cref="UiKit.AddModalDim"/>, and so every <see cref="UiKit.AddModalOverlay"/>): while it
    /// is active and holds controls, the <see cref="UiNavFocus"/> of its screen treats it as an open dialog — the pad focus
    /// moves into it, cannot leave it for the controls under the scrim, and goes back to the control that opened it when
    /// it closes. A scrim without controls of its own is a backdrop (the settings screen's, the on-screen keyboard's) and
    /// changes nothing. The component itself only counts openings and closings, so the focus rule re-checks the selection
    /// exactly when a dialog came or went.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiModalScope : MonoBehaviour
    {
        /// <summary>Bumped whenever a scrim is shown, hidden or gets its dialog panel.</summary>
        public static int Generation { get; private set; }

        private void OnEnable() => Generation++;

        private void OnDisable() => Generation++;

        private void OnTransformChildrenChanged() => Generation++;
    }

    /// <summary>
    /// The dialog part of the pad focus (#3674 follow-up). Before it a modal never took the focus: the selection stayed on
    /// the button that opened it, behind the scrim, and the stick reached every control under the scrim — on "My worlds"
    /// Delete → confirmation → stick up to Play → A launched the world. Closing a dialog restored the focus by its clamped
    /// position, which on screens with Back last was the header's Back.
    /// <para>
    /// Now, while a dialog (<see cref="UiModalScope"/>) is open on this screen, only its controls — and whatever the screen
    /// draws above it — are offered (<see cref="DialogStart"/>); a selection that lands under the scrim is pulled back into
    /// the dialog, a destructive button is never the first focus, and the control the dialog covered is remembered and
    /// selected again once it closes. A dialog rebuilt in place (the festival list rebuilds on every switch) keeps the
    /// focus where it was.
    /// </para>
    /// </summary>
    public sealed partial class UiNavFocus
    {
        private int _modalIndex = -1;       // position of the last selection inside the open dialog
        private UiModalScope _modalScope;   // the dialog _modalIndex was read from
        private int _seenModalGeneration = -1;
        private bool _recheck;              // a dialog came or went since the selection was last checked
        private readonly List<GameObject> _openers = new List<GameObject>(2); // what each open dialog covered, oldest first
        private Selectable _preferred;      // the first focus the screen asked for (UiNav.Prefer)
        private GameObject _autoClaimed;    // what this menu selected by itself — "untouched" while it is still selected

        /// <summary>True when <paramref name="control"/> lies under the scrim of a dialog open on this screen.</summary>
        public bool IsCovered(Selectable control) => control != null && CoveredByDialog(control.gameObject);

        private bool CoveredByDialog(GameObject current)
        {
            var all = AllInteractables();
            int index = IndexOf(all, current);
            return index >= 0 && index < DialogStart(all, out _);
        }

        /// <summary>Where the controls the pad may reach begin in <paramref name="all"/> (hierarchy = draw order): the
        /// first control of the topmost open dialog, or -1 when none is open. Controls drawn after the dialog stay
        /// reachable — they sit on top of its scrim.</summary>
        private int DialogStart(Selectable[] all, out UiModalScope dialog)
        {
            dialog = null;
            int best = -1;
            foreach (var scope in GetComponentsInChildren<UiModalScope>(includeInactive: false))
            {
                if (!scope.enabled)
                {
                    continue;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].transform.IsChildOf(scope.transform))
                    {
                        if (i > best)
                        {
                            best = i;
                            dialog = scope;
                        }

                        break;
                    }
                }
            }

            return best;
        }

        /// <summary>The focus inside an open dialog: where it was in this dialog (or in the one it replaced), else the
        /// control the screen asked for when it lies in the dialog (a panel that is one big dialog, like the admin menu),
        /// else its first safe control (<see cref="FirstChoice"/>).</summary>
        private Selectable DialogSelectable(Selectable[] all, int start, UiModalScope dialog)
        {
            bool replaced = !ReferenceEquals(_modalScope, null) && _modalScope == null; // destroyed and rebuilt in place
            if (_modalIndex >= 0 && (_modalScope == dialog || replaced))
            {
                var remembered = all[Mathf.Clamp(start + _modalIndex, start, all.Length - 1)];
                if (!IsTextField(remembered))
                {
                    return remembered;
                }
            }

            if (_preferred != null && IndexOf(all, _preferred.gameObject) >= start)
            {
                return _preferred;
            }

            return FirstChoice(all, start);
        }

        /// <summary>What <see cref="Update"/> selects when the selection is gone or covered: the control a dialog covered,
        /// once nothing covers it any more (the dialog closed); else <see cref="FocusTarget"/>.</summary>
        private Selectable RestoreTarget()
        {
            var all = AllInteractables();
            int start = DialogStart(all, out var dialog);
            for (int i = _openers.Count - 1; i >= 0; i--)
            {
                var opener = _openers[i];
                var sel = opener != null && opener.activeInHierarchy ? opener.GetComponent<Selectable>() : null;
                int index = sel != null && sel.IsInteractable() ? IndexOf(all, opener) : -1;
                if (index < 0)
                {
                    _openers.RemoveAt(i); // gone with a rebuild — fall back to the remembered position
                    continue;
                }

                if (start >= 0 && index < start)
                {
                    break; // still covered: its dialog is open, the focus belongs inside it
                }

                _openers.RemoveRange(i, _openers.Count - i);
                return sel;
            }

            if (start < 0)
            {
                _modalIndex = -1; // no dialog any more: the next one starts on its own first control
                _modalScope = null;
            }

            return PreferredSelectable(all, start, dialog);
        }

        /// <summary>See <see cref="UiNav.Prefer"/>.</summary>
        internal void SetPreferred(Selectable target, bool takeOver)
        {
            _preferred = target;
            var es = EventSystem.current;
            if (!takeOver || es == null || InputMap.ActiveDevice != InputDeviceKind.Gamepad || !WantsFocus
                || !target.gameObject.activeInHierarchy || !target.IsInteractable() || IsCovered(target))
            {
                return;
            }

            // Untouched: nothing selected yet, what this menu picked by itself, or a leftover of a screen that is gone — but
            // never a control the player moved to, nor a screen above this one (the on-screen keyboard).
            var current = es.currentSelectedGameObject;
            bool untouched = current == null || !current.activeInHierarchy || current == _autoClaimed
                || (!current.transform.IsChildOf(transform) && StaleForeignSelection(current));
            if (untouched)
            {
                es.SetSelectedGameObject(target.gameObject);
                _autoClaimed = target.gameObject; // still the menu's own choice: a later Prefer may move it again
            }
        }
    }
}
