// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #3046: quitting the game while a cutscene was on screen threw out of <c>CutsceneDirector.OnDestroy</c> — Unity had
    /// already destroyed the screen's root-level canvas, and the director's teardown reset the continue button on it. The
    /// screen now resets itself through <see cref="CutsceneScreen.ResetForClose"/>, which touches no widget once the
    /// canvas is gone. Awake/OnDestroy never run in edit mode, so each test removes the frame and its canvas itself.
    /// </summary>
    public sealed class CutsceneScreenTeardownEditModeTests
    {
        private CutsceneScreen _screen;
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            _screen = CutsceneScreen.Create("TestCutsceneFrame", 67, skipButton: true);
        }

        [TearDown]
        public void TearDown()
        {
            var frame = _screen.Frame;
            if (frame != null)
            {
                if (frame.Root != null && frame.Root != frame.transform)
                {
                    Object.DestroyImmediate(frame.Root.gameObject);
                }

                Object.DestroyImmediate(frame.gameObject);
            }

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        [Test]
        public void ALiveScreen_ResetsForClose_AndHidesTheContinueButton()
        {
            _screen.ShowContinue("Continue", true);
            var button = _screen.Frame.Root.Find("CutsceneContinue");
            Assert.That(button.gameObject.activeSelf, Is.True);

            Assert.That(_screen.Alive, Is.True);
            Assert.That(_screen.ResetForClose(), Is.True, "a live screen eases out");
            Assert.That(button.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void AScreenWhoseCanvasWentFirst_ResetsWithoutTouchingAWidget()
        {
            // The order of an application quit: the root-level canvas and its frame are gone before the director's OnDestroy.
            Object.DestroyImmediate(_screen.Frame.Root.gameObject);
            Object.DestroyImmediate(_screen.Frame.gameObject);

            Assert.That(_screen.Alive, Is.False);
            Assert.That(_screen.ResetForClose(), Is.False, "nothing is left to ease out");
            _screen.SetWorldDim(0.5f); // a closing screen's tick: never builds a new dim canvas for a dead screen
            LogAssert.NoUnexpectedReceived();
        }
    }
}
