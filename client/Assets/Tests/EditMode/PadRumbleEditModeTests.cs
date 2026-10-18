// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The Unity half of the gamepad rumble (#2746): which frames may rumble, and that the effects reach the motors. The
    /// build machine has no pad, so the motors are a recording backend (<see cref="PadRumble.BackendOverrideForTest"/>),
    /// the device is pinned through <see cref="InputMap.DeviceOverrideForTest"/> and the focus and the connected pad
    /// through their seams; <see cref="PadRumble.TickForTest"/> runs the very frame <c>Update</c> runs. How the pulses
    /// mix and fade is covered headlessly (RumbleMixerTests); how it FEELS on a real pad stays a manual pass.
    /// </summary>
    public sealed class PadRumbleEditModeTests
    {
        private GameObject _go;
        private PadRumble _rumble;
        private float _low, _high, _maxLow, _maxHigh;

        [SetUp]
        public void SetUp()
        {
            PadRumble.ResetForTest();
            FxCamera.Reset();
            _low = _high = _maxLow = _maxHigh = 0f;
            PadRumble.BackendOverrideForTest = (low, high) =>
            {
                _low = low;
                _high = high;
                _maxLow = Mathf.Max(_maxLow, low);
                _maxHigh = Mathf.Max(_maxHigh, high);
            };

            // Everything allowed; each test takes one thing away.
            PadRumble.SettingOverrideForTest = true;
            PadRumble.FocusOverrideForTest = true;
            PadRumble.PadConnectedOverrideForTest = true;
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;

            _go = new GameObject("PadRumbleTest");
            _rumble = _go.AddComponent<PadRumble>();
            _rumble.TickForTest(0f); // the ticking instance takes the pulses
        }

        [TearDown]
        public void TearDown()
        {
            FxCamera.Reset();
            InputMap.DeviceOverrideForTest = null;
            PadRumble.ResetForTest();
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void ADamageHit_OnAPad_RumblesTheMotors()
        {
            PadRumble.Damage(0.2f);
            _rumble.TickForTest(1f / 60f);

            Assert.That(_low, Is.GreaterThan(0f), "the heavy motor");
            Assert.That(_high, Is.GreaterThan(0f), "the light motor");
            Assert.IsTrue(PadRumble.Supported, "the recording backend counts as a pad that can rumble");

            // …and it fades out on its own.
            for (int i = 0; i < 120; i++)
            {
                _rumble.TickForTest(1f / 60f);
            }

            Assert.AreEqual(0f, _low);
            Assert.AreEqual(0f, _high);
        }

        [Test]
        public void TheVibrationSwitchOff_TheMotorsGetNothing()
        {
            PadRumble.SettingOverrideForTest = false;
            PadRumble.Damage(0.3f);
            FxCamera.AddTrauma(0.8f);
            _rumble.TickForTest(1f / 60f);

            Assert.AreEqual(0f, _maxLow);
            Assert.AreEqual(0f, _maxHigh);
        }

        [Test]
        public void PlayingOnKeyboard_TheMotorsGetNothing()
        {
            InputMap.DeviceOverrideForTest = InputDeviceKind.KeyboardMouse;
            PadRumble.Damage(0.3f);
            FxCamera.Kick(2.2f);
            _rumble.TickForTest(1f / 60f);

            Assert.AreEqual(0f, _maxLow);
            Assert.AreEqual(0f, _maxHigh);

            // A pulse from a frame that could not rumble does not buzz later, once the pad is back in hand.
            InputMap.DeviceOverrideForTest = InputDeviceKind.Gamepad;
            _rumble.TickForTest(1f / 60f);
            Assert.AreEqual(0f, _maxLow);
        }

        [Test]
        public void LosingTheFocus_StopsTheMotorsAtOnce()
        {
            FxCamera.AddTrauma(0.9f); // an explosion close by
            _rumble.TickForTest(1f / 60f);
            Assert.That(_low, Is.GreaterThan(0f));

            PadRumble.FocusOverrideForTest = false;
            _rumble.TickForTest(1f / 60f);
            Assert.AreEqual(0f, _low);
            Assert.AreEqual(0f, _high);
        }

        [Test]
        public void TheWorldsTeardown_StopsTheMotors_AndTheHeatShimmerNeverRumbles()
        {
            FxCamera.AddTrauma(0.02f); // a hot hull, every frame
            _rumble.TickForTest(1f / 60f);
            Assert.AreEqual(0f, _maxLow, "trauma under the threshold must not buzz");

            FxCamera.Kick(1.8f);
            _rumble.TickForTest(1f / 60f);
            Assert.That(_high, Is.GreaterThan(0f), "the own shot buzzes the light motor");

            FxCamera.Reset();
            Assert.AreEqual(0f, _low);
            Assert.AreEqual(0f, _high);
        }

        [Test]
        public void TheGate_NeedsEveryCondition()
        {
            Assert.IsTrue(PadRumble.Allowed(true, InputDeviceKind.Gamepad, true, true, false, false));
            Assert.IsFalse(PadRumble.Allowed(false, InputDeviceKind.Gamepad, true, true, false, false), "setting off");
            Assert.IsFalse(PadRumble.Allowed(true, InputDeviceKind.Touch, true, true, false, false), "touch");
            Assert.IsFalse(PadRumble.Allowed(true, InputDeviceKind.Gamepad, false, true, false, false), "no pad connected");
            Assert.IsFalse(PadRumble.Allowed(true, InputDeviceKind.Gamepad, true, false, false, false), "no focus");
            Assert.IsFalse(PadRumble.Allowed(true, InputDeviceKind.Gamepad, true, true, true, false), "a menu is open");
            Assert.IsFalse(PadRumble.Allowed(true, InputDeviceKind.Gamepad, true, true, false, true), "dead");
        }
    }
}
