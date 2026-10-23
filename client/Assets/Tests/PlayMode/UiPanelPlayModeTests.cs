// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// The <see cref="UiPanel"/> canvas cleanup (#2303, #2925). A panel's canvas is a scene-root object of its own, so
    /// destroying the panel — the rig teardown on every leave of a world — must take the canvas with it, or every world
    /// join leaves one more hidden canvas behind. This lives in PlayMode because OnDestroy never runs in edit mode.
    /// </summary>
    public sealed class UiPanelPlayModeTests
    {
        private GameObject _panelGo;
        private GameObject _canvasGo;
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null)
            {
                Object.Destroy(_canvasGo);
            }

            if (_panelGo != null)
            {
                Object.Destroy(_panelGo);
            }

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.Destroy(es.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator InterviewUi_DestroyedPanel_TakesItsCanvasAlong()
        {
            _panelGo = new GameObject("InterviewPanel");
            var panel = _panelGo.AddComponent<InterviewUi>();
            BuildCanvas(panel);
            Assert.IsTrue(panel.IsBuilt, "precondition: the interview canvas exists");
            _canvasGo = panel.CanvasForTest.gameObject;
            Assert.IsTrue(_canvasGo.transform.parent == null, "precondition: the canvas is a scene root, not under the panel");

            Object.Destroy(_panelGo);
            yield return null;
            yield return null;

            Assert.IsTrue(_canvasGo == null, "the interview canvas must go with its panel (the rig teardown), not outlive it");
        }

        /// <summary>Open needs a <c>Game</c>; the canvas build it starts with does not — build it the way InterviewUi
        /// does (<c>EnsureCanvas(hidden: false)</c>: the canvas stays active, only the overlay comes and goes).</summary>
        private static void BuildCanvas(UiPanel panel)
        {
            var ensure = typeof(UiPanel).GetMethod("EnsureCanvas", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(ensure, "UiPanel.EnsureCanvas(bool) is the canvas builder this test drives");
            ensure.Invoke(panel, new object[] { false });
        }
    }
}
