// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The GPU time of a frame (#3280), from Unity's <see cref="FrameTimingManager"/> — which needs "Frame Timing Stats"
    /// on in the Player Settings. Wall-clock frame times are VSync-capped and often CPU-bound, so they cannot say how much
    /// room the GPU has; the GPU time can. The browser and APIs without timer queries report no GPU time: <see cref="Sample"/>
    /// then returns 0 and callers fall back to the wall-clock rules. The value trails the current frame by a few frames —
    /// fine for statistics over seconds.
    /// </summary>
    public static class GpuFrameTime
    {
        private static readonly FrameTiming[] Latest = new FrameTiming[1];
        private static int _frame = -1;
        private static FrameTiming _timing;
        private static bool _has;

        /// <summary>The GPU time in ms of the latest frame the driver has timed; 0 when there is none. Captures at most once
        /// per frame, so several readers in one frame share one capture.</summary>
        public static float Sample()
        {
            Capture();
            return _has ? Ms(_timing.gpuFrameTime) : 0f;
        }

        /// <summary>The whole timing of the latest timed frame (PerfProbe's breakdown); false when there is none.</summary>
        public static bool TryLatest(out FrameTiming timing)
        {
            Capture();
            timing = _timing;
            return _has;
        }

        private static void Capture()
        {
            if (Time.frameCount == _frame)
            {
                return;
            }

            _frame = Time.frameCount;
            _has = false;
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                return;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, Latest) > 0)
            {
                _timing = Latest[0];
                _has = true;
            }
        }

        private static float Ms(double ms) => ms > 0d && ms < 10000d ? (float)ms : 0f;

        /// <summary>
        /// PerfProbe's per-phase breakdown (#3283): GPU time, the main thread's wait for Present (the VSync slack), and the
        /// main/render thread CPU times — each as p50/p90 over the frames that reported it.
        /// </summary>
        public sealed class Recorder
        {
            private readonly List<float> _gpu = new List<float>(4096);
            private readonly List<float> _wait = new List<float>(4096);
            private readonly List<float> _main = new List<float>(4096);
            private readonly List<float> _render = new List<float>(4096);

            /// <summary>Adds this frame's timing, if the platform reported one.</summary>
            public void Add()
            {
                if (!TryLatest(out var t))
                {
                    return;
                }

                AddIf(_gpu, t.gpuFrameTime);
                _wait.Add(Ms(t.cpuMainThreadPresentWaitTime)); // 0 is a real value here: no slack
                AddIf(_main, t.cpuMainThreadFrameTime);
                AddIf(_render, t.cpuRenderThreadFrameTime);
            }

            /// <summary>p50/p90 of the GPU time, the Present wait, the main thread and the render thread, in ms.</summary>
            public float[] Percentiles()
                => new[]
                {
                    P(_gpu, 0.5f), P(_gpu, 0.9f), P(_wait, 0.5f), P(_wait, 0.9f),
                    P(_main, 0.5f), P(_main, 0.9f), P(_render, 0.5f), P(_render, 0.9f),
                };

            private static void AddIf(List<float> list, double ms)
            {
                float v = Ms(ms);
                if (v > 0f)
                {
                    list.Add(v);
                }
            }

            private static float P(List<float> list, float p)
            {
                list.Sort();
                return FrameSampleStats.Percentile(list, p);
            }
        }
    }
}
