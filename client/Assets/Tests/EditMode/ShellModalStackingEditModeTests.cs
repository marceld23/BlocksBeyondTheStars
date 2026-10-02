// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The shell's own modals — "What's new?" and the update notice — live on canvases of their own over the
    /// main menu. Every shell screen is a <c>UiKit.CreateCanvas</c> at sort order 0, and between equal sort
    /// orders Unity draws and raycasts in creation order. The regression these pin is #2163: in the browser the
    /// menu was rebuilt after the late content load (#377) while the auto-opened release notes were up, and the
    /// newer menu canvas landed ON TOP of the dialog. So each test builds the real dialog FIRST and a menu-style
    /// canvas after it — the rebuild order that broke it — and requires the dialog to still sort above.
    /// </summary>
    public sealed class ShellModalStackingEditModeTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp() => _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;

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

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private T Track<T>(T go) where T : Object
        {
            _spawned.Add(go is Component c ? c.gameObject : go as GameObject);
            return go;
        }

        /// <summary>An AppShell that never ran Awake (edit mode): no localizer, so texts come back as their keys —
        /// all the dialog builders need from it here.</summary>
        private AppShell NewShell() => Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());

        /// <summary>Stand-in for a menu rebuilt while the dialog is open: a shell-screen canvas created AFTER it.</summary>
        private Canvas MenuBuiltLater() => Track(UiKit.CreateCanvas("MainMenuUI"));

        [Test]
        public void WhatsNewDialog_StaysAboveAMenuRebuiltAfterIt()
        {
            var dialog = Track(UiWhatsNew.Build(NewShell())).GetComponent<Canvas>();
            var menu = MenuBuiltLater();

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, dialog.renderMode);
            Assert.Greater(dialog.sortingOrder, menu.sortingOrder,
                "the What's-new canvas must outrank a shell screen created after it, or a menu rebuild buries it (#2163)");
            Assert.AreEqual(UiKit.ShellModalSortingOrder, dialog.sortingOrder);
        }

        [Test]
        public void UpdateNotice_StaysAboveAMenuRebuiltAfterIt()
        {
            var dialog = Track(UiUpdateNotice.Build(NewShell())).GetComponent<Canvas>();
            var menu = MenuBuiltLater();

            Assert.Greater(dialog.sortingOrder, menu.sortingOrder,
                "the update notice must stack above the menu by rule, not by creation order (#2164)");
            Assert.AreEqual(UiKit.ShellModalSortingOrder, dialog.sortingOrder);
        }
    }
}
