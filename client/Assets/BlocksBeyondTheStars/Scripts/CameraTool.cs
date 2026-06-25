// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-world camera item (item: <c>camera</c>). Right-clicking the held camera photographs the
    /// player's current view — WITHOUT the HUD — and saves it as a JPG via <see cref="PhotoStore"/> (per
    /// world). Capture is purely client-side; there is no server round-trip.
    ///
    /// HUD-free shot: the diegetic HUD renders on its own layer that the main camera already culls out
    /// (see <see cref="VisorHud"/>), and the only thing that overlays it back onto the frame is the visor
    /// composite pass that runs on the main camera. So we render a throwaway clone of the view camera —
    /// which has neither the composite nor the screen-space HUD canvas — into a render texture, and read
    /// that back. Pipeline-agnostic (works under Built-in and URP) and never touches the live frame.
    /// </summary>
    public sealed class CameraTool : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Source;      // the player's view camera (set by the rig)

        private const float Cooldown = 0.6f; // anti-spam; the camera costs no energy
        private float _readyAt;
        private PhotoStore _store;
        private long _storeSeed = long.MinValue;

        /// <summary>Right-click handler from <see cref="PlayerController"/>. Returns true if a capture started.</summary>
        public bool TryCapture()
        {
            if (Source == null || Game == null || Time.unscaledTime < _readyAt)
            {
                return false;
            }

            _readyAt = Time.unscaledTime + Cooldown;
            StartCoroutine(CaptureRoutine());
            return true;
        }

        private IEnumerator CaptureRoutine()
        {
            DateTime taken = DateTime.UtcNow;
            int w = Mathf.Max(2, Screen.width);
            int h = Mathf.Max(2, Screen.height);

            // A throwaway off-screen camera cloned from the view camera. Rendering into a target texture means
            // it never draws to the screen, and it carries neither the visor composite (a component on the main
            // camera) nor the screen-space HUD canvas — so the shot is the clean, HUD-free world. Letting the
            // active render pipeline draw it (enabled) keeps this correct under both Built-in and URP.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "PhotoRT" };
            rt.Create();

            var camGo = new GameObject("PhotoCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(Source); // FOV, clip planes and the HUD-excluded culling mask
            cam.transform.SetPositionAndRotation(Source.transform.position, Source.transform.rotation);
            cam.targetTexture = rt;
            cam.enabled = true;

            // Wait until the end of this frame, by which point the pipeline has rendered our camera into the RT.
            yield return new WaitForEndOfFrame();

            byte[] jpg = null;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                jpg = tex.EncodeToJPG(92);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CameraTool] capture failed: {ex.Message}");
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                if (tex != null) { Destroy(tex); }
                Destroy(camGo);
                rt.Release();
                Destroy(rt);
            }

            if (jpg == null)
            {
                yield break;
            }

            var store = StoreForCurrentWorld();
            var entry = store?.Add(jpg, taken);
            if (entry != null)
            {
                Game.ShowMessage(Game.Localizer?.Get("ui.photos.saved") ?? "Photo saved");
                ClientAudio.Instance?.Cue("camera_shutter");
                StartCoroutine(FlashRoutine());
            }
        }

        /// <summary>The photo store for the world the player is currently in (keyed by world seed). Reused
        /// across shots; rebuilt if the world changes (e.g. after a hyperjump to a different seed).</summary>
        public PhotoStore StoreForCurrentWorld()
        {
            long seed = Game != null ? Game.WorldSeed : 0L;
            if (_store == null || _storeSeed != seed)
            {
                _store?.UnloadTextures();
                _store = PhotoStore.Open(seed);
                _storeSeed = seed;
            }

            return _store;
        }

        // A brief white flash overlay so a capture is felt, drawn on its own top-most canvas (so it never
        // ends up in the photo, which was already read back before this runs).
        private IEnumerator FlashRoutine()
        {
            var go = new GameObject("PhotoFlash");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // above the HUD
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.55f);
            img.raycastTarget = false;

            float t = 0f;
            const float dur = 0.22f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                img.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0f, t / dur));
                yield return null;
            }

            Destroy(go);
        }

        private void OnDestroy() => _store?.UnloadTextures();
    }
}
