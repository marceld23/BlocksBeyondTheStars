// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "last played" picture of a local world, shown by "My worlds" and the main menu's Continue card
    /// (<see cref="WorldPreviewFrame"/>). While the player is in a world the bundled server runs — singleplayer, or a save
    /// opened to friends — this takes a small picture of the game view now and then and writes it as
    /// <see cref="WorldPreviewFiles.FileName"/> into the world's save folder (atomically), so deleting the world deletes it.
    /// <para>
    /// The picture is the world without HUD, menus, chat or overlays: like the camera item (<see cref="CameraTool"/>) a
    /// throwaway clone of the view camera renders into a 640 × 360 render texture — never the screen — and
    /// <see cref="GpuReadback"/> brings it to the CPU; the JPG is encoded in the frame (tiny) and written off the main thread.
    /// When (<see cref="WorldPreviewSchedule"/>): after half a minute of clear view, then every three minutes, right BEFORE the
    /// pause menu opens (<see cref="PauseMenuOpening"/>, so neither the menu nor the blur it brings is in it — the picture of
    /// leaving through the pause menu) and when the application quits. Never in a bad moment (<see cref="WorldPreviewRules"/>:
    /// a cutscene, an open panel or the chat, the death prompt, the loading veil, a landing or a jump, a view still settling).
    /// </para>
    /// Multiplayer and official worlds get none (they are not in My worlds), nor does the browser's in-tab world: its save
    /// is one IndexedDB blob without a folder, and the browser menu has neither My worlds nor Continue.
    /// </summary>
    public sealed class WorldPreviewCapture : MonoBehaviour
    {
        private static WorldPreviewCapture s_current;

        private readonly WorldPreviewSchedule _schedule = new WorldPreviewSchedule();
        private GameBootstrap _game;
        private Camera _source;
        private WorldLoadingOverlay _loading;
        private string _path;
        private float _jumpedAt = float.NegativeInfinity;
        private bool _busy;

        // The capture in flight: the clone camera and its texture (released when the picture is read, or with the world).
        private Camera _clone;
        private RenderTexture _rt;

        /// <summary>Hangs the capture on a freshly built world rig — only for a world the bundled server runs (the shell's
        /// <see cref="AppShell.LocalServer"/>; its save is <see cref="ClientSettings.LastWorld"/>). Null when there is nothing
        /// to capture for: a remote world, the browser, a name that is no save folder.</summary>
        public static WorldPreviewCapture Attach(GameObject root, GameBootstrap game, Camera source, AppShell shell)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null; // the in-tab world is one IndexedDB blob: no save folder, and no My worlds to show it
#else
            // A performance probe run (-perfProbe) measures frame times: an extra render must not land in its numbers.
            if (root == null || game == null || source == null || shell == null || shell.LocalServer == null
                || FindAnyObjectByType<PerfProbe>() != null)
            {
                return null;
            }

            string path = WorldPreviewFiles.PathFor(LocalServerLauncher.SavesRoot, shell.Settings?.LastWorld);
            if (path == null)
            {
                return null;
            }

            var capture = root.AddComponent<WorldPreviewCapture>();
            capture._game = game;
            capture._source = source;
            capture._path = path;
            capture._loading = root.GetComponent<WorldLoadingOverlay>();
            game.HyperjumpStarted += capture.OnJump;
            s_current = capture;
            return capture;
#endif
        }

        /// <summary>
        /// The pause menu is about to open (<see cref="AppShell.OpenPauseMenu"/>): the picture of leaving the world, taken now —
        /// before the menu registers, so the post stack's menu blur has not begun — unless the view is not settled or a picture
        /// was taken a moment ago. A no-op outside a local world.
        /// </summary>
        public static void PauseMenuOpening()
        {
            var capture = s_current;
            if (capture != null && capture._schedule.WantsLeavePicture())
            {
                capture.CaptureNow(background: true);
            }
        }

        private void OnJump() => _jumpedAt = Time.unscaledTime;

        private void Update()
        {
            if (_game == null)
            {
                return;
            }

            var blocker = WorldPreviewRules.Blocker(Moment());
            if (_schedule.Tick(Time.unscaledDeltaTime, blocker) && !_busy)
            {
                CaptureNow(background: true);
            }
        }

        // Quitting from inside the world (closing the window): one last picture, synchronously — the process is going.
        private void OnApplicationQuit()
        {
            if (!_busy && GpuReadback.SyncReadsAllowed && _schedule.WantsLeavePicture())
            {
                CaptureNow(background: false);
            }
        }

        private void OnDestroy()
        {
            if (_game != null)
            {
                _game.HyperjumpStarted -= OnJump;
            }

            if (s_current == this)
            {
                s_current = null;
            }

            Close();
        }

        /// <summary>What the world shows right now, as far as the picture cares.</summary>
        private WorldPreviewMoment Moment()
        {
            var game = _game;
            var transitions = game.Views.Transitions;
            var resolution = game.Views.Resolution;
            return new WorldPreviewMoment
            {
                HasCamera = _source != null && _source.isActiveAndEnabled && Application.isFocused,
                Loading = (_loading != null && _loading.VeilActive) || !game.WorldReady || game.PlayerSettling
                          || (transitions != null && transitions.Busy),
                SecondsSinceJump = float.IsNegativeInfinity(_jumpedAt) ? (double?)null : Time.unscaledTime - _jumpedAt,
                Cutscene = GameBootstrap.CutsceneActive || GameBootstrap.HudHidden || game.CinematicCameraActive
                           || game.VegaPrologueActive || (resolution != null && resolution.Playing),
                Dead = game.AwaitingRespawnConfirm,
                MenuOpen = game.MenuOpen || game.ConsoleOpen || game.StarChartOpen,
                ChatOpen = game.ChatTyping,
            };
        }

        /// <summary>Renders the clone now and reads it back — inline where the graphics API allows a synchronous read (every
        /// desktop API), a frame or two later otherwise. <paramref name="background"/> writes the file off the main thread.</summary>
        private void CaptureNow(bool background)
        {
            if (_busy || _source == null)
            {
                return;
            }

            _busy = true;
            try
            {
                Open();
                _clone.Render();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorldPreview] capture failed: {e.Message}");
                Finish(null, background);
                return;
            }

            GpuReadback.Read(_rt, TextureFormat.RGB24, tex => Finish(tex, background));
        }

        /// <summary>The clone of the view camera (FOV, clip planes, the HUD-free culling mask, the URP post stack) aimed like
        /// it, drawing into a 640 × 360 texture. Disabled: it renders only when told to.</summary>
        private void Open()
        {
            _rt = new RenderTexture(WorldPreviewFiles.Width, WorldPreviewFiles.Height, 24, RenderTextureFormat.ARGB32) { name = "WorldPreviewRT" };
            _rt.Create();
            var go = new GameObject("WorldPreviewCamera");
            go.transform.SetParent(transform, false); // leaves with the world, should it close mid-capture
            _clone = go.AddComponent<Camera>();
            _clone.CopyFrom(_source);
            UrpCameraClone.Sync(_source, _clone, _game.Settings != null ? _game.Settings.RendererIndex : -1);
            _clone.transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
            _clone.targetTexture = _rt;
            _clone.aspect = (float)WorldPreviewFiles.Width / WorldPreviewFiles.Height;
            _clone.enabled = false;
            Sky.WorldCameras.Add(_clone); // the picture shows the world, haze included (#2393)
        }

        /// <summary>The read-back picture (or null): encoded, the clone released, the file written; the schedule learns
        /// whether it worked.</summary>
        private void Finish(Texture2D tex, bool background)
        {
            byte[] jpg = null;
            try
            {
                jpg = tex != null ? tex.EncodeToJPG(WorldPreviewFiles.JpegQuality) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorldPreview] encode failed: {e.Message}");
            }
            finally
            {
                BlockCubeFaces.DestroyAsset(tex);
                Close();
                _busy = false;
            }

            if (jpg == null)
            {
                _schedule.MarkFailed();
                return;
            }

            _schedule.MarkTaken();
            string path = _path;
            if (background)
            {
                _ = Task.Run(() => Write(path, jpg)); // a few dozen KB — but never a disk wait in the frame
            }
            else
            {
                Write(path, jpg);
            }
        }

        private static void Write(string path, byte[] jpg)
        {
            if (!WorldPreviewFiles.Write(path, jpg))
            {
                Debug.LogWarning("[WorldPreview] the picture could not be written (the world folder is gone or not writable).");
            }
        }

        private void Close()
        {
            if (!ReferenceEquals(_clone, null))
            {
                Sky.WorldCameras.Remove(_clone); // also when the world took the clone with it meanwhile
                if (_clone != null)
                {
                    _clone.targetTexture = null;
                    Destroy(_clone.gameObject);
                }
            }

            _clone = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }

            _rt = null;
        }
    }
}
