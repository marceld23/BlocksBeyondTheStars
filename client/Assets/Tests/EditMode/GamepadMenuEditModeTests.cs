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
    /// Headless pins for the gamepad menu fixes of the 2026-08-30 controller review (#1404–#1411). CI has no
    /// pad, so these cover the parts that are checkable without one: the explicit ring wiring of the
    /// slot-action pie, the scroll-into-view arithmetic, the hint-strip wording, and the two UiKit widgets
    /// whose navigation / focus setup was wrong. The feel stays a manual pass (protocol #1227).
    /// </summary>
    public sealed class GamepadMenuEditModeTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            InputMap.DeviceOverrideForTest = null;
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        private Button NewButton(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _spawned.Add(go);
            go.AddComponent<Image>();
            return go.AddComponent<Button>();
        }

        // ---- #1405: the pie wedges are concentric, so navigation must be explicit ---------------------------

        [Test]
        public void WireRing_WalksAllFourWedgesInBothDirections()
        {
            var top = NewButton("Swap");
            var right = NewButton("Form");
            var bottom = NewButton("Close");
            var left = NewButton("Colour");

            HotbarActionUi.WireRing(top, right, bottom, left);

            Assert.AreEqual(Navigation.Mode.Explicit, top.navigation.mode);
            Assert.AreSame(right, top.navigation.selectOnRight);
            Assert.AreSame(left, top.navigation.selectOnLeft);
            Assert.AreSame(bottom, top.navigation.selectOnDown);
            Assert.AreSame(top, bottom.navigation.selectOnUp);
            Assert.AreSame(bottom, left.navigation.selectOnDown);
            Assert.AreSame(top, right.navigation.selectOnUp);
            Assert.AreSame(right, left.navigation.selectOnRight, "left → right crosses the ring");
            Assert.AreSame(left, right.navigation.selectOnLeft);
        }

        [Test]
        public void WireRing_FallsThroughToTheOppositeWedge_WhenASideIsDimmed()
        {
            // A non-material item has no Colour / Form wedge (null). Left/right from Swap must still land
            // somewhere — on Close — and Close must lead back up, so the pie never dead-ends.
            var top = NewButton("Swap");
            var bottom = NewButton("Close");

            HotbarActionUi.WireRing(top, null, bottom, null);

            Assert.AreSame(bottom, top.navigation.selectOnLeft);
            Assert.AreSame(bottom, top.navigation.selectOnRight);
            Assert.AreSame(top, bottom.navigation.selectOnLeft);
            Assert.AreSame(top, bottom.navigation.selectOnRight);
            Assert.AreSame(top, bottom.navigation.selectOnUp);
        }

        // ---- #1407: scroll the selection into view -----------------------------------------------------------

        [Test]
        public void ScrollDelta_IsZero_WhileTheRowIsInsideTheViewport()
        {
            Assert.AreEqual(0f, UiNavFocus.ScrollDelta(selTop: -100f, selBottom: -140f, viewTop: 0f, viewBottom: -742f, margin: 8f));
        }

        [Test]
        public void ScrollDelta_ScrollsDown_WhenTheRowHangsBelowTheViewport()
        {
            // Row bottom 20 px under the viewport's bottom edge → scroll down by that plus the margin.
            float d = UiNavFocus.ScrollDelta(selTop: -730f, selBottom: -762f, viewTop: 0f, viewBottom: -742f, margin: 8f);
            Assert.AreEqual(28f, d, 0.001f);
        }

        [Test]
        public void ScrollDelta_ScrollsUp_WhenTheRowSitsAboveTheViewport()
        {
            float d = UiNavFocus.ScrollDelta(selTop: 30f, selBottom: -10f, viewTop: 0f, viewBottom: -742f, margin: 8f);
            Assert.AreEqual(-38f, d, 0.001f);
        }

        [Test]
        public void ScrollDelta_AlignsTheTop_ForARowTallerThanTheViewport()
        {
            float d = UiNavFocus.ScrollDelta(selTop: 50f, selBottom: -900f, viewTop: 0f, viewBottom: -742f, margin: 8f);
            Assert.Less(d, 0f, "a row taller than the viewport shows its top, not its bottom");
        }

        // ---- #1408: the hint strip names the controls ----------------------------------------------------------

        [Test]
        public void ComposeHint_NamesAAndB_AndTheScreensExtraLine()
        {
            string text = UiNavFocus.ComposeHint(false,
                new List<(PadButton[] Buttons, string VerbKey)> { (new[] { PadButton.Lb, PadButton.Rb }, "ui.pad.tabs") });

            StringAssert.StartsWith("(A) ", text);
            StringAssert.Contains("(B) ", text);
            StringAssert.Contains("LB/RB ", text);
            StringAssert.Contains("ui.pad.tabs", text); // no localizer in EditMode → the key itself is shown
        }

        [Test]
        public void ComposeHint_SaysType_WhileATextFieldIsSelected()
        {
            StringAssert.Contains("ui.pad.type", UiNavFocus.ComposeHint(true, null));
            StringAssert.Contains("ui.pad.choose", UiNavFocus.ComposeHint(false, null));
        }

        // ---- #1411: scrollbars are not navigation stops ----------------------------------------------------------

        [Test]
        public void InlineScrollbar_OptsOutOfNavigation_AndUiNavDoesNotOfferIt()
        {
            var root = new GameObject("Screen", typeof(RectTransform));
            _spawned.Add(root);
            root.AddComponent<Canvas>();
            var viewGo = new GameObject("Viewport", typeof(RectTransform));
            viewGo.transform.SetParent(root.transform, false);
            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewGo.transform;

            var bar = UiKit.AddInlineScrollbar(scroll);

            Assert.AreEqual(Navigation.Mode.None, bar.navigation.mode);
            var nav = root.AddComponent<UiNavFocus>();
            Assert.IsNull(nav.FocusTarget(), "a screen whose only Selectable is a scrollbar offers the pad nothing");
        }
    }
}
