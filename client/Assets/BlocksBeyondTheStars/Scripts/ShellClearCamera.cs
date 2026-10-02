// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Paints the screen black while no other camera does (#2185). The launcher scene holds no camera: in the
    /// shell phases the only one is the animated <see cref="MenuBackground"/>, and that one needs the game
    /// content. In the browser the content can take a long while after an update, and until it arrived every
    /// overlay canvas (menu, What's new, loading screen, dialogs) was drawn onto a buffer nobody cleared — a
    /// closed dialog stayed on screen as a ghost image, and the menu's boot fade-in smeared its texts. This
    /// camera renders nothing (culling mask 0) and only clears the frame; it switches itself off as soon as any
    /// other camera draws to the screen, so the backdrop, the intro, the editors and the world keep the screen
    /// to themselves and pay nothing for it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShellClearCamera : MonoBehaviour
    {
        private Camera _cam;

        // Reused by OtherScreenCameraActive — Camera.allCameras allocates a fresh array on every call.
        private static Camera[] _cameras = new Camera[8];

        /// <summary>The clear camera itself (enabled only while it is the screen's sole camera).</summary>
        public Camera Camera => _cam;

        /// <summary>Builds the clear camera on its own object and settles its state for the current frame.</summary>
        public static ShellClearCamera Create()
        {
            var go = new GameObject("ShellClearCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;     // nothing to render — the clear is the whole job
            cam.depth = -100f;       // below every real camera, should two ever overlap for a frame
            cam.useOcclusionCulling = false;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            var clear = go.AddComponent<ShellClearCamera>();
            clear._cam = cam;
            clear.Refresh();
            return clear;
        }

        // LateUpdate: a camera spawned or enabled in any Update this frame (backdrop, world rig, editor) already
        // counts, so the hand-over costs no frame.
        private void LateUpdate() => Refresh();

        /// <summary>Enables the clear camera exactly while no other camera draws to the screen.</summary>
        public void Refresh()
        {
            if (_cam == null)
            {
                return;
            }

            bool clear = !OtherScreenCameraActive(_cam);
            if (_cam.enabled != clear)
            {
                _cam.enabled = clear;
            }
        }

        /// <summary>True when an enabled camera other than <paramref name="self"/> renders to the screen.</summary>
        public static bool OtherScreenCameraActive(Camera self)
        {
            int count = Camera.allCamerasCount;
            if (_cameras.Length < count)
            {
                _cameras = new Camera[count * 2];
            }

            count = Camera.GetAllCameras(_cameras);
            bool found = AnyOtherScreenCamera(self, _cameras, count);
            System.Array.Clear(_cameras, 0, count); // never pin a destroyed camera from a static
            return found;
        }

        /// <summary>The rule behind <see cref="OtherScreenCameraActive"/>, over an explicit camera list: any entry
        /// that is not <paramref name="self"/> and has no target texture. A camera that renders into a texture (the
        /// avatar and ship preview rigs) never clears the screen, so it does not count.</summary>
        public static bool AnyOtherScreenCamera(Camera self, Camera[] cameras, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var cam = cameras[i];
                if (cam != null && cam != self && cam.targetTexture == null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
