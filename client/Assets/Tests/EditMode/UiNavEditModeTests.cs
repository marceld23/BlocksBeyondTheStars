// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Headless tests for the gamepad menu-focus rule (<see cref="UiNavFocus"/>). CI has no pad, so these
    /// pin the part that IS checkable: WHICH control a menu offers the pad, and when it must offer none.
    /// The pad feel itself stays a manual pass (issue #195, protocol #1227).
    ///
    /// The regression they exist for is #1198: every screen built on its own <c>UiKit.CreateCanvas</c> root
    /// (the whole in-game Tab menu among them) had its UiNavFocus on the owning MonoBehaviour instead of on
    /// the canvas — and <c>CreateCanvas</c> returns a SCENE-ROOT GameObject, so the component saw none of the
    /// screen's controls and the pad could not reach a single tab.
    /// </summary>
    public sealed class UiNavEditModeTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        private GameObject NewRoot(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private static Button AddButton(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<Image>();
            return go.AddComponent<Button>();
        }

        private static InputField AddField(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<Image>();
            return go.AddComponent<InputField>();
        }

        // ---- #1198: the canvas-root case ------------------------------------------------------------------

        [Test]
        public void FocusTarget_OnOwnerObject_SeesNothingOfACanvasRootScreen()
        {
            // Exactly the shipped wiring before #1198: the screen's owner (GameMenu) carries the component,
            // while the controls live under a canvas that UiKit.CreateCanvas parented to the scene root.
            var owner = NewRoot("ScreenOwner");
            var canvasRoot = NewRoot("ScreenCanvas");
            canvasRoot.AddComponent<Canvas>();
            AddButton(canvasRoot, "Tab1");
            AddButton(canvasRoot, "Tab2");

            var nav = owner.AddComponent<UiNavFocus>();

            Assert.IsNull(nav.FocusTarget(), "a UiNavFocus on the owner must not be able to see a scene-root canvas");
        }

        [Test]
        public void FocusTarget_OnTheCanvasItself_FindsTheFirstControl()
        {
            var canvasRoot = NewRoot("ScreenCanvas");
            canvasRoot.AddComponent<Canvas>();
            var first = AddButton(canvasRoot, "Tab1");
            AddButton(canvasRoot, "Tab2");

            var nav = canvasRoot.AddComponent<UiNavFocus>();

            Assert.AreSame(first, nav.FocusTarget());
        }

        // ---- the hidden-canvas guard ----------------------------------------------------------------------

        [Test]
        public void WantsFocus_IsFalse_WhileTheCanvasIsHiddenViaEnabled()
        {
            // Screens hide two ways. SetActive(false) disables the component for us; canvas.enabled = false
            // does NOT (the GameObject stays active), and a screen hidden that way used to keep pulling the
            // selection off whatever was actually on screen.
            var canvasRoot = NewRoot("ScreenCanvas");
            var canvas = canvasRoot.AddComponent<Canvas>();
            AddButton(canvasRoot, "Ok");
            var nav = canvasRoot.AddComponent<UiNavFocus>();

            Assert.IsTrue(nav.WantsFocus, "a visible screen must claim the selection");

            canvas.enabled = false;
            Assert.IsFalse(nav.WantsFocus, "a screen hidden via Canvas.enabled must not claim the selection");

            canvas.enabled = true;
            Assert.IsTrue(nav.WantsFocus);
        }

        [Test]
        public void SetSuspended_StopsTheMenuClaimingTheSelection()
        {
            // The ship / face editors hand the sticks to their viewport; the panels must let go while that
            // lasts, or the same stick would walk a list and fly a camera at once.
            var canvasRoot = NewRoot("EditorCanvas");
            canvasRoot.AddComponent<Canvas>();
            AddButton(canvasRoot, "Palette");
            var nav = canvasRoot.AddComponent<UiNavFocus>();

            UiNav.SetSuspended(canvasRoot, true);
            Assert.IsTrue(nav.Suspended);
            Assert.IsFalse(nav.WantsFocus);

            UiNav.SetSuspended(canvasRoot, false);
            Assert.IsFalse(nav.Suspended);
            Assert.IsTrue(nav.WantsFocus);
        }

        // ---- text fields are never the auto-focus ---------------------------------------------------------

        [Test]
        public void FocusTarget_SkipsATextFieldAndPicksTheFirstButton()
        {
            // A focused InputField swallows the navigation axes, so auto-selecting one strands a pad player
            // in a field they can neither type into (until #1211) nor leave.
            var canvasRoot = NewRoot("NamingModal");
            canvasRoot.AddComponent<Canvas>();
            AddField(canvasRoot, "Name");
            var ok = AddButton(canvasRoot, "Ok");

            var nav = canvasRoot.AddComponent<UiNavFocus>();

            Assert.AreSame(ok, nav.FocusTarget());
        }

        [Test]
        public void FocusTarget_IsNull_WhenTheScreenIsNothingButTextFields()
        {
            var canvasRoot = NewRoot("FormOnly");
            canvasRoot.AddComponent<Canvas>();
            AddField(canvasRoot, "A");
            AddField(canvasRoot, "B");

            var nav = canvasRoot.AddComponent<UiNavFocus>();

            Assert.IsNull(nav.FocusTarget(), "with nothing safe to focus the selection must be left alone");
        }

        // ---- the selection survives a rebuild -------------------------------------------------------------

        [Test]
        public void FocusTarget_ReturnsToTheRememberedRow_AfterTheListIsRebuilt()
        {
            // The crafting pane rebuilds all three panels on every pick, which used to snap the focus back to
            // control #1 on every single input.
            var canvasRoot = NewRoot("ListScreen");
            canvasRoot.AddComponent<Canvas>();
            var rows = new List<Button>();
            for (int i = 0; i < 5; i++)
            {
                rows.Add(AddButton(canvasRoot, "Row" + i));
            }

            var nav = canvasRoot.AddComponent<UiNavFocus>();
            nav.NoteSelection(rows[3].gameObject);

            // Rebuild: the old rows are gone, five fresh ones take their place.
            foreach (var row in rows)
            {
                Object.DestroyImmediate(row.gameObject);
            }

            var rebuilt = new List<Button>();
            for (int i = 0; i < 5; i++)
            {
                rebuilt.Add(AddButton(canvasRoot, "Row" + i));
            }

            Assert.AreSame(rebuilt[3], nav.FocusTarget(), "focus must come back to the row the player was on");
        }

        [Test]
        public void FocusTarget_ClampsTheRememberedRow_WhenTheListGotShorter()
        {
            var canvasRoot = NewRoot("FilteredList");
            canvasRoot.AddComponent<Canvas>();
            var rows = new List<Button>();
            for (int i = 0; i < 5; i++)
            {
                rows.Add(AddButton(canvasRoot, "Row" + i));
            }

            var nav = canvasRoot.AddComponent<UiNavFocus>();
            nav.NoteSelection(rows[4].gameObject);

            foreach (var row in rows)
            {
                Object.DestroyImmediate(row.gameObject);
            }

            var shorter = new List<Button>();
            for (int i = 0; i < 2; i++)
            {
                shorter.Add(AddButton(canvasRoot, "Row" + i));
            }

            Assert.AreSame(shorter[1], nav.FocusTarget(), "a filtered-down list must clamp, not fall off the end");
        }

        [Test]
        public void FocusTarget_IgnoresAControlThatBelongsToAnotherMenu()
        {
            var mine = NewRoot("MyScreen");
            mine.AddComponent<Canvas>();
            var mineFirst = AddButton(mine, "Mine0");
            AddButton(mine, "Mine1");

            var other = NewRoot("OtherScreen");
            other.AddComponent<Canvas>();
            var stranger = AddButton(other, "Stranger");

            var nav = mine.AddComponent<UiNavFocus>();
            nav.NoteSelection(stranger.gameObject);

            Assert.AreSame(mineFirst, nav.FocusTarget(), "a foreign control must not move this menu's remembered row");
        }

        // ---- disabled controls are not offered ------------------------------------------------------------

        [Test]
        public void FocusTarget_SkipsControlsThatAreNotInteractable()
        {
            var canvasRoot = NewRoot("Screen");
            canvasRoot.AddComponent<Canvas>();
            var locked = AddButton(canvasRoot, "LockedKeyRow"); // e.g. the fixed Escape row in the settings
            locked.interactable = false;
            var usable = AddButton(canvasRoot, "PadColumn");

            var nav = canvasRoot.AddComponent<UiNavFocus>();

            Assert.AreSame(usable, nav.FocusTarget());
        }
    }
}
