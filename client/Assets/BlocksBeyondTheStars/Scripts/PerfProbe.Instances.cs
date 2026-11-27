// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// PerfProbe's check of #4046 part b: do the near drawers draw the same picture from buffers on the graphics card
    /// (<see cref="GpuInstances"/>) as from the arrays they handed over each frame before? At the end of the outdoor
    /// phase, where the player stands still among crowns, plants and grass, three frames in a row are compared in
    /// numbers: one drawn from the buffers, one from the arrays, one from the buffers again. The first two differ by the
    /// way of drawing and by one frame of wind and sky; the first and the third differ by two frames of wind and sky
    /// only — the noise the first number has to stay within. A shader that put the instances somewhere else, or a variant
    /// that went missing in a build, shows as a difference far above that noise. Nothing is written to disk and nobody
    /// looks at a picture; the result is two log lines.
    /// <para><c>-perfFeature instances=arrays</c> runs the whole probe the old way (nothing is uploaded), for the
    /// before/after of the main-thread time in one build.</para>
    /// </summary>
    public sealed partial class PerfProbe
    {
        /// <summary>A pixel counts as changed when one of its colour channels moved by more than this (of 255).</summary>
        private const int ChangedBeyond = 16;

        private static IEnumerator CompareInstancePaths()
        {
            if (!GpuInstances.Active)
            {
                Debug.Log($"[PerfProbe] Instance paths: not compared — the drawers hand over arrays (supported {GpuInstances.Supported}, switch {GpuInstances.Enabled}).");
                yield break;
            }

            long uploaded = GpuInstances.UploadedInstances, draws = GpuInstances.BufferDraws;
            Texture2D buffers = null, arrays = null, again = null;
            yield return new WaitForEndOfFrame();
            buffers = ScreenCapture.CaptureScreenshotAsTexture();
            long drawsPerFrame = GpuInstances.BufferDraws;
            GpuInstances.Enabled = false;
            yield return new WaitForEndOfFrame();
            arrays = ScreenCapture.CaptureScreenshotAsTexture();
            GpuInstances.Enabled = true;
            yield return new WaitForEndOfFrame();
            again = ScreenCapture.CaptureScreenshotAsTexture();
            drawsPerFrame = GpuInstances.BufferDraws - drawsPerFrame;

            try
            {
                Difference(buffers, arrays, out float byPath, out float changedByPath);
                Difference(buffers, again, out float byTime, out float changedByTime);
                Debug.Log(FormattableString.Invariant(
                    $"[PerfProbe] Instance paths: buffers vs arrays differ by {byPath:0.000} % of full scale, {changedByPath:0.00} % of the pixels changed; two buffer frames differ by {byTime:0.000} %, {changedByTime:0.00} % changed (the noise of wind and sky)."));
                Debug.Log($"[PerfProbe] Instance buffers: {uploaded} instances uploaded and {draws} buffer draws up to here, {drawsPerFrame} buffer draws in the last frame, {buffers.width}x{buffers.height} px compared.");
            }
            finally
            {
                Destroy(buffers);
                Destroy(arrays);
                Destroy(again);
            }
        }

        /// <summary>The mean difference of two frames' colours as a share of full scale (%), and the share of pixels with a
        /// channel that moved by more than <see cref="ChangedBeyond"/> (%).</summary>
        private static void Difference(Texture2D one, Texture2D two, out float mean, out float changed)
        {
            mean = 100f;
            changed = 100f;
            if (one == null || two == null || one.width != two.width || one.height != two.height)
            {
                return;
            }

            var a = one.GetPixels32();
            var b = two.GetPixels32();
            long sum = 0, over = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int dr = Math.Abs(a[i].r - b[i].r), dg = Math.Abs(a[i].g - b[i].g), db = Math.Abs(a[i].b - b[i].b);
                sum += dr + dg + db;
                if (dr > ChangedBeyond || dg > ChangedBeyond || db > ChangedBeyond)
                {
                    over++;
                }
            }

            mean = a.Length == 0 ? 0f : (float)(sum / (a.Length * 3.0 * 255.0) * 100.0);
            changed = a.Length == 0 ? 0f : (float)(over * 100.0 / a.Length);
        }
    }
}
