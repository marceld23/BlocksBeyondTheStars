// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Makes a menu navigable by gamepad. uGUI's <c>StandaloneInputModule</c> already turns the pad into
    /// directional navigation + Submit(A)/Cancel(B) via the project's InputManager axes, and buttons built by
    /// <see cref="UiKit.AddButton"/> default to <c>Navigation.Automatic</c> — so the ONE missing piece is that
    /// a mouse-built menu has nothing selected, leaving the stick with no cursor to move. This component fixes
    /// exactly that: while a gamepad is the active device and this menu has no valid selection, it selects the
    /// first interactable control in its own subtree. It is completely inert on keyboard/mouse (so it never
    /// steals the pointer), and self-healing (re-focuses if the selected control is hidden or destroyed).
    ///
    /// Attach with <see cref="UiNav.Enable"/> on a menu's root object — for a screen built on its own
    /// <see cref="UiKit.CreateCanvas"/> root that is the CANVAS object, not the owning MonoBehaviour's
    /// GameObject: <c>CreateCanvas</c> returns a scene-root GameObject that is never reparented, so a
    /// component sitting on the owner sees none of the screen's controls (#1198).
    ///
    /// Three details keep it from doing harm on screens that are built but not currently shown:
    /// <list type="bullet">
    /// <item>It respects <see cref="Canvas.enabled"/>. Screens hide themselves two different ways —
    /// <c>gameObject.SetActive(false)</c> (which disables this component too) and <c>canvas.enabled = false</c>
    /// (which does NOT: the GameObject stays active). Without the canvas check a hidden screen keeps pulling
    /// the selection away from whatever is actually on screen (#1198).</item>
    /// <item>It prefers not to auto-select a text field: a form's first control is usually a field, and
    /// landing there means the player's first stick flick edits text instead of moving. Since the on-screen
    /// keyboard (#1211) a field is no longer a TRAP — <see cref="PadTextEntryBridge"/> keeps it deactivated
    /// while a pad is in hand — so a screen made of nothing but fields now focuses its first one rather than
    /// being left with no selection at all.</item>
    /// <item>It remembers WHERE the selection was and restores that position when the controls are rebuilt.
    /// Data-driven panes (the crafting list rebuilds all three panes on every pick) would otherwise snap the
    /// focus back to control #1 on every input.</item>
    /// </list>
    ///
    /// Full coverage across every panel is tracked as follow-up (issue #195) and wants on-device validation —
    /// CI can't test a pad.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiNavFocus : MonoBehaviour
    {
        /// <summary>While true this menu neither claims nor restores the selection. Screens that hand the
        /// sticks to a 3D viewport (the ship editor's fly-cam, the face editor's paint canvas) suspend their
        /// panel navigation while the viewport has focus, so the pad drives exactly one thing at a time.</summary>
        public bool Suspended;

        private Canvas _canvas;
        private int _lastIndex = -1;        // position of the last valid selection among the interactables
        private GameObject _lastSelected;   // what that position was read from, so Update can skip re-walking

        private void Awake() => _canvas = GetComponentInParent<Canvas>();

        /// <summary>Whether this menu may claim the selection at all: not suspended, and not hidden behind a
        /// disabled <see cref="Canvas"/> (see the class summary — a canvas hidden that way keeps its
        /// GameObject active, so this component would otherwise keep running).</summary>
        public bool WantsFocus
        {
            get
            {
                if (_canvas == null)
                {
                    _canvas = GetComponentInParent<Canvas>(); // a canvas added after Awake still counts
                }

                return !Suspended && (_canvas == null || _canvas.enabled);
            }
        }

        /// <summary>The control this menu would focus right now, or null if it has none to offer. Split out of
        /// <see cref="Update"/> so the rule can be checked without a pad, an EventSystem or a running
        /// player loop — none of which CI has.</summary>
        public Selectable FocusTarget() => PreferredSelectable(Interactables());

        /// <summary>Records where <paramref name="current"/> sits among this menu's controls, so the same
        /// position can be restored after a rebuild. Ignores controls that belong to another menu.</summary>
        public void NoteSelection(GameObject current)
        {
            if (current == null)
            {
                return;
            }

            var all = Interactables();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject == current)
                {
                    _lastIndex = i;
                    return;
                }
            }
        }

        private void Update()
        {
            if (InputMap.ActiveDevice != InputDeviceKind.Gamepad || !WantsFocus)
            {
                return; // keyboard/mouse in hand, or this screen is hidden — leave the selection alone.
            }

            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var current = es.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy)
            {
                var sel = current.GetComponent<Selectable>();
                if (sel != null && sel.IsInteractable())
                {
                    // Only walk the subtree when the selection actually MOVED. GetComponentsInChildren
                    // allocates, and a pad resting on one control must not cost an array per frame — the
                    // crafting pane alone holds hundreds of Selectables (WebGL / Raspberry-Pi budgets).
                    if (current != _lastSelected)
                    {
                        _lastSelected = current;
                        NoteSelection(current);
                    }

                    return; // a valid control is focused — nothing to do.
                }
            }

            _lastSelected = null;

            var restore = FocusTarget();
            if (restore != null)
            {
                es.SetSelectedGameObject(restore.gameObject);
            }
        }

        /// <summary>The control to focus: the one sitting where the selection was before the panel was
        /// rebuilt (clamped — a filtered list can get shorter), else the first control that is not a text
        /// field, and only failing that a field (see the class summary).</summary>
        private Selectable PreferredSelectable(Selectable[] all)
        {
            if (all.Length == 0)
            {
                return null;
            }

            if (_lastIndex >= 0)
            {
                var remembered = all[Mathf.Clamp(_lastIndex, 0, all.Length - 1)];
                if (!IsTextField(remembered))
                {
                    return remembered;
                }
            }

            for (int i = 0; i < all.Length; i++)
            {
                if (!IsTextField(all[i]))
                {
                    return all[i];
                }
            }

            return all[0]; // nothing but fields: focus one anyway — with the on-screen keyboard it is usable
        }

        /// <summary>This menu's currently interactable controls, in hierarchy order.</summary>
        private Selectable[] Interactables()
        {
            var found = GetComponentsInChildren<Selectable>(includeInactive: false);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].IsInteractable() && found[i].gameObject.activeInHierarchy)
                {
                    found[n++] = found[i];
                }
            }

            if (n == found.Length)
            {
                return found;
            }

            var kept = new Selectable[n];
            System.Array.Copy(found, kept, n);
            return kept;
        }

        /// <summary>True for controls that swallow the navigation axes once focused (see the class summary).</summary>
        private static bool IsTextField(Selectable s) => s is InputField;
    }

    /// <summary>Helpers for wiring gamepad menu navigation onto a menu root.</summary>
    public static class UiNav
    {
        /// <summary>Ensures <paramref name="menuRoot"/> auto-focuses its first control for a gamepad. Idempotent.</summary>
        public static void Enable(GameObject menuRoot)
        {
            if (menuRoot != null && menuRoot.GetComponent<UiNavFocus>() == null)
            {
                menuRoot.AddComponent<UiNavFocus>();
            }
        }

        /// <summary>Hands the pad to (or takes it back from) a screen's 3D viewport — see
        /// <see cref="UiNavFocus.Suspended"/>. Clears the selection on suspend, so the panel's highlight does
        /// not sit there looking focused while the sticks drive the world. No-op if the root carries no
        /// <see cref="UiNavFocus"/>.</summary>
        public static void SetSuspended(GameObject menuRoot, bool suspended)
        {
            var nav = menuRoot != null ? menuRoot.GetComponent<UiNavFocus>() : null;
            if (nav == null || nav.Suspended == suspended)
            {
                return;
            }

            nav.Suspended = suspended;
            if (suspended && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
