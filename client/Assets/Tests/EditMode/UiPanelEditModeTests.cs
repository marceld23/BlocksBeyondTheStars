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
    /// Pins the <see cref="UiPanel"/> base contract (#2913) on its three pilot panels — one per shape: the beam pad
    /// (build once, toggle active), the train cab (canvas once, overlay per open) and the crate filter (build on open) —
    /// and the <see cref="ClientContext"/> registry that replaced their static Instance singletons. Every panel runs
    /// without a <c>Game</c> (each Open path is null-safe on it), and Awake/OnDestroy never run in edit mode, so the
    /// teardown removes the canvases itself.
    /// </summary>
    public sealed class UiPanelEditModeTests
    {
        private readonly List<UiPanel> _panels = new();
        private bool _hadEventSystem;
        private bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            _reducedMotion = UiKit.ReducedMotion;
            UiKit.ReducedMotion = true; // UiKit.OpenModal then only sets the alpha — no transition component to drive
        }

        [TearDown]
        public void TearDown()
        {
            UiKit.ReducedMotion = _reducedMotion;
            foreach (var panel in _panels)
            {
                if (panel == null)
                {
                    continue;
                }

                if (panel.CanvasForTest != null)
                {
                    Object.DestroyImmediate(panel.CanvasForTest.gameObject);
                }

                Object.DestroyImmediate(panel.gameObject);
            }

            _panels.Clear();

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private T Track<T>(T panel) where T : UiPanel
        {
            _panels.Add(panel);
            return panel;
        }

        private T NewPanel<T>(string name) where T : UiPanel => Track(new GameObject(name).AddComponent<T>());

        [Test]
        public void BeamPad_OpenBuildsItsCanvasOnceWithSortOrderAndPadFocus()
        {
            var p = NewPanel<BeamPadUi>("BeamPad");
            p.Open(1);

            Assert.IsTrue(p.IsOpen);
            Assert.IsTrue(p.IsBuilt);
            Assert.AreEqual(59, p.CanvasForTest.sortingOrder);
            Assert.IsNotNull(p.CanvasForTest.GetComponent<UiNavFocus>(), "the base enables pad navigation on the canvas");
            Assert.IsTrue(p.CanvasForTest.gameObject.activeSelf);

            var first = p.CanvasForTest;
            // The second open rebuilds the destination list, whose Object.Destroy logs outside play mode.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                p.Open(2);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreSame(first, p.CanvasForTest, "shape B builds its canvas once and only toggles it");
        }

        [Test]
        public void TrainCab_OpenWithoutTrainStillRaisesOneOverlay()
        {
            var p = NewPanel<TrainCabUi>("TrainCab");
            p.Open("t1");

            Assert.IsTrue(p.IsOpen);
            Assert.AreEqual(59, p.CanvasForTest.sortingOrder);
            Assert.AreEqual(1, p.CanvasForTest.transform.childCount, "shape B2 hangs exactly one modal overlay on its canvas");
        }

        [Test]
        public void ContainerFilter_StaysClosedWithoutGame()
        {
            var p = NewPanel<ContainerFilterUi>("ContainerFilter");
            p.Open("c", null);

            Assert.IsFalse(p.IsOpen);
            Assert.IsFalse(p.IsBuilt);
            Assert.IsNull(p.CanvasForTest);
        }

        [Test]
        public void ClientContext_PanelFindsRegisteredAndForgetsUnregistered()
        {
            var ctx = new ClientContext();
            var a = NewPanel<BeamPadUi>("BeamPadA");

            ctx.Register(a);
            Assert.AreSame(a, ctx.Panel<BeamPadUi>());
            Assert.IsNull(ctx.Panel<TrainCabUi>());

            ctx.Unregister(a);
            Assert.IsNull(ctx.Panel<BeamPadUi>());
        }

        [Test]
        public void ClientContext_UnregisterIgnoresAnotherInstanceOfTheSameType()
        {
            var ctx = new ClientContext();
            var a = NewPanel<BeamPadUi>("BeamPadA");
            var b = NewPanel<BeamPadUi>("BeamPadB");

            ctx.Register(a);
            ctx.Unregister(b);

            Assert.AreSame(a, ctx.Panel<BeamPadUi>(), "only the registered panel itself may clear its slot");
        }
    }
}
