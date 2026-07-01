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
    /// Attach with <see cref="UiNav.Enable"/> on a menu's root object. Full coverage across every panel is
    /// tracked as follow-up (issue #195) and wants on-device validation — CI can't test a pad.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiNavFocus : MonoBehaviour
    {
        private void Update()
        {
            if (InputMap.ActiveDevice != InputDeviceKind.Gamepad)
            {
                return; // keyboard/mouse in hand — leave selection entirely to the pointer.
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
                    return; // a valid control is focused — nothing to do.
                }
            }

            var first = FirstInteractable();
            if (first != null)
            {
                es.SetSelectedGameObject(first.gameObject);
            }
        }

        private Selectable FirstInteractable()
        {
            var all = GetComponentsInChildren<Selectable>(includeInactive: false);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].IsInteractable() && all[i].gameObject.activeInHierarchy)
                {
                    return all[i];
                }
            }

            return null;
        }
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
    }
}
