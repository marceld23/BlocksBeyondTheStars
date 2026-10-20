// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GPU → CPU pixel reads that work on every graphics API the game ships on (#2390). WebGPU forbids the
    /// synchronous reads (<see cref="Texture2D.ReadPixels(Rect, int, int)"/>,
    /// <see cref="ScreenCapture.CaptureScreenshotAsTexture()"/>) the photo item, the chat and F1/F2 screenshots
    /// and the texture editor used; WebGL 2 has no <see cref="AsyncGPUReadback"/> at all. So every read goes
    /// through here: a synchronous <c>ReadPixels</c> wherever the API allows it (the result is available at once,
    /// which the texture editor relies on), an asynchronous readback request on WebGPU, with the callback invoked
    /// a frame or two later. Whole-frame captures use <see cref="ScreenCapture.CaptureScreenshotIntoRenderTexture"/>
    /// (a GPU-side copy that keeps the overlay HUD in the image) and may downscale on the GPU before reading, so a
    /// report thumbnail costs one small readback instead of a full-frame one plus a CPU resize. The asynchronous path
    /// always reads RGBA32 and converts on the CPU (<see cref="ReadbackPixels"/>: WebGPU ignored an RGB24 request), and
    /// a whole-frame capture is cut down to the part the frame actually covered.
    /// </summary>
    public static class GpuReadback
    {
        /// <summary>Dev switch (the clip recorder's <c>-clipReadbackCheck</c>): take the asynchronous path even where
        /// synchronous reads are allowed, so the WebGPU code path can be exercised on a desktop build.</summary>
        public static bool ForceAsync;

        /// <summary>True where <c>ReadPixels</c> / <c>CaptureScreenshotAsTexture</c> are allowed (everything but
        /// WebGPU). WebGL 2 is in this set: it has no async readback, so the synchronous path is the only one.</summary>
        public static bool SyncReadsAllowed
            => !ForceAsync && SystemInfo.graphicsDeviceType != GraphicsDeviceType.WebGPU;

        /// <summary>Reads <paramref name="rt"/> into a new <see cref="Texture2D"/> of <paramref name="format"/>
        /// (RGB24 or RGBA32). <paramref name="done"/> gets the texture (caller destroys it) or null when the read
        /// failed; it runs inline on a synchronous-read API, otherwise once the async request has landed.</summary>
        public static void Read(RenderTexture rt, TextureFormat format, Action<Texture2D> done)
        {
            if (rt == null || done == null)
            {
                done?.Invoke(null);
                return;
            }

            if (SyncReadsAllowed)
            {
                var prev = RenderTexture.active;
                Texture2D tex = null;
                try
                {
                    RenderTexture.active = rt;
                    tex = new Texture2D(rt.width, rt.height, format, mipChain: false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpuReadback] ReadPixels failed: {e.Message}");
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
                finally
                {
                    RenderTexture.active = prev;
                }

                done(tex);
                return;
            }

            int w = rt.width, h = rt.height;
            // #2845: always RGBA32 from the GPU — WebGPU answered an RGB24 request with four bytes per pixel anyway, and laying
            // those out as three made every browser screenshot three squeezed copies of the frame. ReadbackPixels takes
            // the row length from the data and converts to what the caller asked for.
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    Debug.LogWarning("[GpuReadback] async readback failed.");
                    done(null);
                    return;
                }

                Texture2D tex = null;
                try
                {
                    // The raw rows come top-down on a top-left-origin API; Texture2D expects bottom-up.
                    bool rgb = format == TextureFormat.RGB24;
                    byte[] rows = ReadbackPixels.FromRgba(request.GetData<byte>().ToArray(), w, h, rgb ? 3 : 4,
                        flipRows: SystemInfo.graphicsUVStartsAtTop);
                    if (rows == null)
                    {
                        Debug.LogWarning($"[GpuReadback] async readback returned too little data for {w}x{h}.");
                        done(null);
                        return;
                    }

                    tex = new Texture2D(w, h, rgb ? TextureFormat.RGB24 : TextureFormat.RGBA32, mipChain: false);
                    tex.LoadRawTextureData(rows);
                    tex.Apply(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpuReadback] async readback failed: {e.Message}");
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }

                done(tex);
            });
        }

        /// <summary>Coroutine form of <see cref="Read"/>: yields until the texture is on the CPU.</summary>
        public static IEnumerator ReadInto(RenderTexture rt, TextureFormat format, Action<Texture2D> done)
        {
            bool finished = false;
            Texture2D result = null;
            Read(rt, format, t =>
            {
                result = t;
                finished = true;
            });
            while (!finished)
            {
                yield return null;
            }

            done?.Invoke(result);
        }

        /// <summary>Captures the full composited frame — overlay HUD included — downscaled on the GPU so its longest
        /// side is at most <paramref name="maxDim"/> (0 = full size), then read into a <see cref="Texture2D"/>. Call
        /// from a coroutine right after <c>WaitForEndOfFrame</c>, so the frame is complete when the copy is made.
        /// <paramref name="captured"/> (optional) runs the moment the frame is copied on the GPU — before the readback,
        /// which takes a frame or two on WebGPU — so a caller can change what the screen shows from then on (#2622: the
        /// F1 dialog takes the menu over only once its screenshot holds the HUD).</summary>
        public static IEnumerator CaptureScreen(int maxDim, TextureFormat format, Action<Texture2D> done, Action captured = null)
        {
            int w = Mathf.Max(2, Screen.width), h = Mathf.Max(2, Screen.height);
            var full = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture small = null;
            Texture2D rgba = null;
            try
            {
                // #2845: cleared to the sentinel first — in the browser the copied back buffer can be smaller than Screen says,
                // and the frame then covered only part of this texture — the rest is cut away below instead of
                // reaching the report as uninitialised memory.
                using (var clear = new CommandBuffer { name = "GpuReadback sentinel clear" })
                {
                    clear.SetRenderTarget(full);
                    clear.ClearRenderTarget(false, true, new Color32(ReadbackPixels.SentinelR, ReadbackPixels.SentinelG,
                        ReadbackPixels.SentinelB, ReadbackPixels.SentinelA));
                    Graphics.ExecuteCommandBuffer(clear);
                }

                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                captured?.Invoke();
                var source = full;
                float scale = maxDim > 0 ? Mathf.Min(1f, (float)maxDim / Mathf.Max(w, h)) : 1f;
                // #2430: on a top-left-origin API (Direct3D, Vulkan, Metal) the captured frame lies in the texture
                // top-down, and the synchronous ReadPixels copies it as it lies — every F1 screenshot of v2026.10.10
                // reached the inbox upside down. The async path flips its raw rows itself (above), so only the
                // synchronous read gets the flip, done on the GPU as a mirrored blit; OpenGL / WebGL 2 need neither.
                bool flip = SyncReadsAllowed && SystemInfo.graphicsUVStartsAtTop;
                if (scale < 1f || flip)
                {
                    int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), th = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                    small = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                    if (flip)
                    {
                        Graphics.Blit(full, small, new Vector2(1f, -1f), new Vector2(0f, 1f));
                    }
                    else
                    {
                        Graphics.Blit(full, small);
                    }

                    source = small;
                }

                yield return ReadInto(source, TextureFormat.RGBA32, t => rgba = t);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(full);
                if (small != null)
                {
                    RenderTexture.ReleaseTemporary(small);
                }
            }

            done?.Invoke(CropToFrame(rgba, format, w, h));
        }

        /// <summary>The part of the last screen capture the frame covered (<c>W×H of W×H</c>; empty when it covered all
        /// of it) — for the report's graphics node.</summary>
        public static string LastCaptureCover { get; private set; } = string.Empty;

        /// <summary>Cuts a capture down to the part the frame covered (<see cref="ReadbackPixels.TryFindCoveredRect"/>) and
        /// converts it to <paramref name="format"/> (RGB24 or RGBA32). Consumes <paramref name="rgba"/>.</summary>
        private static Texture2D CropToFrame(Texture2D rgba, TextureFormat format, int screenW, int screenH)
        {
            if (rgba == null)
            {
                return null;
            }

            int tw = rgba.width, th = rgba.height;
            byte[] px = rgba.GetRawTextureData<byte>().ToArray();
            UnityEngine.Object.Destroy(rgba);
            if (ReadbackPixels.TryFindCoveredRect(px, tw, th, out int x0, out int y0, out int cw, out int ch))
            {
                string cover = $"{cw}x{ch} of {tw}x{th}";
                if (cover != LastCaptureCover)
                {
                    Debug.LogWarning($"[GpuReadback] the captured frame covered {cover} (Screen {screenW}x{screenH}) — cut to it.");
                }

                LastCaptureCover = cover;
                px = ReadbackPixels.Crop(px, tw, 4, x0, y0, cw, ch);
                tw = cw;
                th = ch;
            }
            else
            {
                LastCaptureCover = string.Empty;
            }

            bool rgb = format == TextureFormat.RGB24;
            if (rgb)
            {
                px = ReadbackPixels.DropAlpha(px, tw * th);
            }

            var tex = new Texture2D(tw, th, rgb ? TextureFormat.RGB24 : TextureFormat.RGBA32, mipChain: false);
            tex.LoadRawTextureData(px);
            tex.Apply(false);
            return tex;
        }
    }
}
