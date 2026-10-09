// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using Unity.Collections;
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
    /// report thumbnail costs one small readback instead of a full-frame one plus a CPU resize.
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
            AsyncGPUReadback.Request(rt, 0, format, request =>
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
                    var data = request.GetData<byte>();
                    tex = new Texture2D(w, h, format, mipChain: false);
                    if (SystemInfo.graphicsUVStartsAtTop)
                    {
                        // The raw rows come top-down on a top-left-origin API; Texture2D expects bottom-up.
                        int bpp = format == TextureFormat.RGB24 ? 3 : 4;
                        var flipped = new NativeArray<byte>(data.Length, Allocator.Temp);
                        int stride = w * bpp;
                        for (int y = 0; y < h; y++)
                        {
                            NativeArray<byte>.Copy(data, (h - 1 - y) * stride, flipped, y * stride, stride);
                        }

                        tex.LoadRawTextureData(flipped);
                        flipped.Dispose();
                    }
                    else
                    {
                        tex.LoadRawTextureData(data);
                    }

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
        /// from a coroutine right after <c>WaitForEndOfFrame</c>, so the frame is complete when the copy is made.</summary>
        public static IEnumerator CaptureScreen(int maxDim, TextureFormat format, Action<Texture2D> done)
        {
            int w = Mathf.Max(2, Screen.width), h = Mathf.Max(2, Screen.height);
            var full = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture small = null;
            Texture2D result = null;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                var source = full;
                float scale = maxDim > 0 ? Mathf.Min(1f, (float)maxDim / Mathf.Max(w, h)) : 1f;
                if (scale < 1f)
                {
                    int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), th = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                    small = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(full, small);
                    source = small;
                }

                yield return ReadInto(source, format, t => result = t);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(full);
                if (small != null)
                {
                    RenderTexture.ReleaseTemporary(small);
                }
            }

            done?.Invoke(result);
        }
    }
}
