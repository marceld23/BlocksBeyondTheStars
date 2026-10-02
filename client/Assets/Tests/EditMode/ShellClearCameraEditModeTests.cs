// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The shell's fallback clear (#2185). The launcher scene has no camera, and in the browser the backdrop's camera
    /// only exists once the game data has downloaded — until then nothing cleared the frame and every shell canvas
    /// left ghost images. <see cref="ShellClearCamera"/> clears exactly while no other camera draws to the screen.
    /// </summary>
    public sealed class ShellClearCameraEditModeTests
    {
        private readonly List<GameObject> _spawned = new();
        private readonly List<Camera> _parked = new();

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

            foreach (var cam in _parked)
            {
                if (cam != null)
                {
                    cam.enabled = true;
                }
            }

            _parked.Clear();
        }

        /// <summary>Switches off every camera this test did not make (the open scene's, another test's leftover)
        /// so the hand-over can be checked against a known camera set; TearDown switches them back on.</summary>
        private void ParkForeignCameras()
        {
            foreach (var cam in Camera.allCameras)
            {
                if (!_spawned.Contains(cam.gameObject))
                {
                    cam.enabled = false;
                    _parked.Add(cam);
                }
            }
        }

        private Camera NewCamera(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Camera>();
        }

        [Test]
        public void TheClearCameraAlone_DoesNotCountAsAnotherCamera()
        {
            var self = NewCamera("Self");

            Assert.IsFalse(ShellClearCamera.AnyOtherScreenCamera(self, new[] { self }, 1));
            Assert.IsFalse(ShellClearCamera.AnyOtherScreenCamera(self, new Camera[] { null, self }, 2));
        }

        [Test]
        public void AScreenCamera_TakesTheScreenOver()
        {
            var self = NewCamera("Self");
            var backdrop = NewCamera("MenuCamera");

            Assert.IsTrue(ShellClearCamera.AnyOtherScreenCamera(self, new[] { self, backdrop }, 2),
                "the backdrop (or world, intro, editor) camera clears the screen itself — the fallback must step aside");
        }

        [Test]
        public void ACameraRenderingIntoATexture_DoesNotClearTheScreen()
        {
            var self = NewCamera("Self");
            var preview = NewCamera("AvatarPreview");
            var rt = new RenderTexture(16, 16, 16);
            try
            {
                preview.targetTexture = rt;
                Assert.IsFalse(ShellClearCamera.AnyOtherScreenCamera(self, new[] { self, preview }, 2),
                    "a preview rig draws into its texture, not onto the screen — the screen still needs the fallback clear");
            }
            finally
            {
                preview.targetTexture = null;
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void OnlyTheFirstCountEntries_AreConsidered()
        {
            var self = NewCamera("Self");
            var stale = NewCamera("StaleBufferEntry");

            Assert.IsFalse(ShellClearCamera.AnyOtherScreenCamera(self, new[] { self, stale }, 1),
                "entries past the live count are leftovers of a reused buffer");
        }

        [Test]
        public void Create_BuildsABlackClearThatRendersNothing()
        {
            var clear = ShellClearCamera.Create();
            _spawned.Add(clear.gameObject);
            var cam = clear.Camera;

            Assert.AreEqual(CameraClearFlags.SolidColor, cam.clearFlags);
            Assert.AreEqual(Color.black, cam.backgroundColor);
            Assert.AreEqual(0, cam.cullingMask, "the clear is the whole job — it must not render any layer");
            Assert.Less(cam.depth, 0f, "below every real camera");
        }

        [Test]
        public void Refresh_StepsAsideForAnotherCamera_AndComesBackWhenItIsGone()
        {
            var clear = ShellClearCamera.Create();
            _spawned.Add(clear.gameObject);
            var other = NewCamera("MenuCamera");
            ParkForeignCameras();

            clear.Refresh();
            Assert.IsFalse(clear.Camera.enabled, "another screen camera is up — the fallback is off");

            other.enabled = false;
            clear.Refresh();
            Assert.IsTrue(clear.Camera.enabled, "no other camera draws — the fallback clears again");
        }

        [Test]
        public void DataLine_AddsTheFileCountOnceTheManifestIsKnown()
        {
            Assert.AreEqual("Loading game data…", UiLoading.DataLine("Loading game data…", 0, 0));
            Assert.AreEqual("Loading game data…  23/56", UiLoading.DataLine("Loading game data…", 23, 56));
            Assert.AreEqual("Loading game data…  56/56", UiLoading.DataLine("Loading game data…", 60, 56));
        }
    }
}
