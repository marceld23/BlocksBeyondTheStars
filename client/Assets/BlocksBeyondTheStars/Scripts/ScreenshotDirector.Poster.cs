// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client.Showcase;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The poster (#3706): launched with <c>-capturePoster</c> (plus <c>-shotOut &lt;dir&gt;</c>), the director waits for the
    /// main menu, switches the backdrop to its poster framing (the thunderbolt before the "varied" world, centred, full detail on
    /// the High preset — never saved), hides every canvas, photographs the frame and stamps the official logo on it. No AI, no
    /// paint: the poster is the game's own picture of itself. It writes, into <c>&lt;out&gt;/</c>:
    /// <list type="bullet">
    /// <item><c>poster.jpg</c> (1920×1080, with the logo) — key art for the README, stores, devblog; <c>poster.png</c> and
    /// <c>poster-nologo.png</c> are its lossless masters (not for the repository);</item>
    /// <item><c>launcher-splash.jpg</c> (1120×640, with the logo) — the Windows and Linux launcher splash and the installer
    /// image (both launchers embed it at build time);</item>
    /// <item><c>bbs-poster.jpg</c> (1600×900, no logo) and <c>bbs-logo.png</c> (the logo alone) — the browser loading page.</item>
    /// </list>
    /// Where each file goes is in <c>docs/developer/CLIENT_SHELL_AND_ASSETS.md</c> ("Menu backdrop and first impressions"), and
    /// <c>scripts/capture-poster.ps1 -Place</c> copies them there. Desktop only (the browser has no command line).
    /// </summary>
    public sealed partial class ScreenshotDirector
    {
        private const int PosterWidth = 1920;
        private const int PosterHeight = 1080;
        private const int PosterLogoTop = 56;           // px from the top edge to the logo image's top
        private const int PosterLogoWidth = 1200;       // the official logo's width on the 1920 px poster
        private const int LauncherWidth = 1120;         // the launcher's 560×320 window at 200 %
        private const int LauncherHeight = 640;
        private const int WebWidth = 1600;              // the browser page background (a JPEG behind the loading bar)
        private const int WebHeight = 900;
        private const int WebJpegQuality = 80;
        private const int PosterJpegQuality = 88;
        private const float PosterReadyTimeout = 90f;   // the map bake and the ship, from the menu on
        private const float PosterSettle = 3f;          // plumes up, eye adaptation and bloom settled

        private bool _poster;

        /// <summary>The <c>-capturePoster</c> switch; never on in the browser build.</summary>
        private static bool PosterCaptureRequested()
        {
#if UNITY_WEBGL
            return false;
#else
            return Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-capturePoster", StringComparison.OrdinalIgnoreCase));
#endif
        }

        /// <summary>The whole poster run. Quits.</summary>
        private IEnumerator CapturePoster(AppShell shell)
        {
            yield return WaitForPhase(shell, ShellPhase.MainMenu, 60f);
            if (shell.Phase != ShellPhase.MainMenu || shell.Settings == null)
            {
                Debug.LogError($"[Capture] The main menu did not come up (phase {shell.Phase}).");
                Quit(1);
                yield break;
            }

            // The High look for the picture, without touching the player's saved settings (never Save()).
            var settings = shell.Settings;
            var presetBefore = settings.Preset;
            bool autoBefore = settings.PresetAuto;
            settings.Preset = QualityPreset.High;
            settings.PresetAuto = false;
            settings.Apply();
            MenuBackground.PosterFraming = true;
            shell.RebuildMenuBackground();

            yield return WaitUntil(() => MenuBackground.Active != null && MenuBackground.Active.Ready, PosterReadyTimeout);
            if (MenuBackground.Active == null || !MenuBackground.Active.Ready)
            {
                Debug.LogWarning("[Capture] The backdrop is not complete (map or ship missing) — capturing anyway.");
            }

            var hidden = HideCanvases();
            yield return new WaitForSecondsRealtime(PosterSettle);
            yield return new WaitForEndOfFrame();
            RgbaImage frame = GrabFrame();
            foreach (var canvas in hidden)
            {
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }

            settings.Preset = presetBefore;
            settings.PresetAuto = autoBefore;
            settings.Apply();
            if (frame != null)
            {
                WritePosterSet(frame, Path.Combine(ResolveOutDir(), "poster"));
            }

            Debug.Log("[Capture] Done (poster).");
            Quit(frame != null ? 0 : 1);
        }

        /// <summary>Disables every enabled canvas (menu, overlays, toasts) and returns them for the way back.</summary>
        private static List<Canvas> HideCanvases()
        {
            var hidden = new List<Canvas>();
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                if (canvas.enabled && canvas.isRootCanvas)
                {
                    canvas.enabled = false;
                    hidden.Add(canvas);
                }
            }

            return hidden;
        }

        /// <summary>The composited frame as an image (rows bottom-up), at the poster size; null when the read-back failed.
        /// Renders the backdrop camera itself at twice the poster size and box-filters it down — sharp whatever the window
        /// is; the screen grab is only the fallback (its super-size factor merely upscales under URP — the first posters
        /// came out soft for that reason).</summary>
        private static RgbaImage GrabFrame()
        {
            var cam = MenuBackground.Active != null ? MenuBackground.Active.Camera : null;
            var rendered = cam != null ? RenderCamera(cam, PosterWidth * 2, PosterHeight * 2) : null;
            if (rendered != null)
            {
                return rendered.Resize(PosterWidth, PosterHeight);
            }

            Texture2D tex = null;
            try
            {
                // A windowed player rarely gets the full 1920 × 1080 (title bar, taskbar): render the frame supersampled
                // to at least the poster size instead of scaling a smaller frame up — the first poster came out soft.
                int super = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(PosterWidth / (float)Mathf.Max(1, Screen.width),
                    PosterHeight / (float)Mathf.Max(1, Screen.height))), 1, 4);
                tex = ScreenCapture.CaptureScreenshotAsTexture(super);
                var px = tex.GetPixels32();
                var rgba = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    rgba[i * 4] = px[i].r;
                    rgba[i * 4 + 1] = px[i].g;
                    rgba[i * 4 + 2] = px[i].b;
                    rgba[i * 4 + 3] = 255;
                }

                var frame = new RgbaImage(tex.width, tex.height, rgba);
                return frame.Width == PosterWidth && frame.Height == PosterHeight
                    ? frame
                    : frame.CropToAspect(PosterWidth / (double)PosterHeight).Resize(PosterWidth, PosterHeight);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Capture] Reading the poster frame failed: {e.Message}");
                return null;
            }
            finally
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }

        /// <summary>One frame of <paramref name="cam"/> (with its post-processing) rendered into a <paramref name="width"/> ×
        /// <paramref name="height"/> texture and read back (rows bottom-up); null when that failed.</summary>
        private static RgbaImage RenderCamera(Camera cam, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var targetBefore = cam.targetTexture;
            var activeBefore = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false);
                var px = tex.GetPixels32();
                var rgba = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    rgba[i * 4] = px[i].r;
                    rgba[i * 4 + 1] = px[i].g;
                    rgba[i * 4 + 2] = px[i].b;
                    rgba[i * 4 + 3] = 255;
                }

                return new RgbaImage(width, height, rgba);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Capture] Rendering the poster camera failed ({e.Message}) — grabbing the screen instead.");
                return null;
            }
            finally
            {
                cam.targetTexture = targetBefore;
                RenderTexture.active = activeBefore;
                rt.Release();
                Destroy(rt);
                Destroy(tex);
            }
        }

        /// <summary>Stamps the logo and writes every picture of the set into <paramref name="dir"/>.</summary>
        private static void WritePosterSet(RgbaImage frame, string dir)
        {
            Directory.CreateDirectory(dir);
            var logo = GameLogo.Raster(PosterLogoWidth); // the official logo, rows bottom-up like the frame
            var poster = frame.Crop(0, 0, frame.Width, frame.Height);
            if (logo != null)
            {
                poster.DrawOver(logo, (poster.Width - logo.Width) / 2, poster.Height - PosterLogoTop - logo.Height);
            }

            WriteImage(Path.Combine(dir, "poster.png"), poster, 0);          // lossless masters (archive, not for the repo)
            WriteImage(Path.Combine(dir, "poster-nologo.png"), frame, 0);
            WriteImage(Path.Combine(dir, "poster.jpg"), poster, PosterJpegQuality); // key art: README, stores, devblog
            WriteImage(Path.Combine(dir, "launcher-splash.jpg"),
                poster.CropToAspect(LauncherWidth / (double)LauncherHeight).Resize(LauncherWidth, LauncherHeight), PosterJpegQuality);
            WriteImage(Path.Combine(dir, "bbs-poster.jpg"), frame.Resize(WebWidth, WebHeight), WebJpegQuality);
            if (logo != null)
            {
                WriteImage(Path.Combine(dir, "bbs-logo.png"), logo, 0);
            }
        }

        /// <summary>Encodes one picture: a PNG when <paramref name="jpegQuality"/> is 0 (the logo needs its alpha), else a JPEG
        /// (the posters are opaque — a fraction of the PNG's size for the repo, the launchers and the browser).</summary>
        private static void WriteImage(string path, RgbaImage image, int jpegQuality)
        {
            var tex = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixelData(image.Rgba, 0);
                tex.Apply(false);
                File.WriteAllBytes(path, jpegQuality <= 0 ? tex.EncodeToPNG() : tex.EncodeToJPG(jpegQuality));
                Debug.Log($"[Capture] wrote {path} ({image.Width}×{image.Height})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Capture] failed {path}: {e.Message}");
            }
            finally
            {
                Destroy(tex);
            }
        }
    }
}
