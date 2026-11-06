// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The frame-time statistics of one sample window: the wall-clock median and p90 (what the player feels — CPU,
    /// GPU and VSync together) and, where the platform reports it, the GPU p90 (#3280).</summary>
    public readonly struct FrameSampleStats
    {
        public FrameSampleStats(int frames, float medianMs, float p90Ms, float gpuP90Ms)
        {
            Frames = frames;
            MedianMs = medianMs;
            P90Ms = p90Ms;
            GpuP90Ms = gpuP90Ms;
        }

        /// <summary>Frames in the window.</summary>
        public int Frames { get; }

        /// <summary>Median wall-clock frame time in ms.</summary>
        public float MedianMs { get; }

        /// <summary>90th-percentile wall-clock frame time in ms.</summary>
        public float P90Ms { get; }

        /// <summary>90th-percentile GPU frame time in ms; 0 when the platform reports no GPU time (the browser, an API
        /// without timer queries).</summary>
        public float GpuP90Ms { get; }

        /// <summary>Whether <see cref="GpuP90Ms"/> is a real measurement.</summary>
        public bool HasGpuTime => GpuP90Ms > 0f;

        /// <summary>The statistics of a window. <paramref name="wallMs"/> holds every frame; <paramref name="gpuMs"/> only
        /// the frames that reported a GPU time — fewer than half of the frames counts as "no GPU time" (a driver that
        /// answers now and then is not a measurement). Sorts both lists in place.</summary>
        public static FrameSampleStats From(List<float> wallMs, List<float>? gpuMs)
        {
            if (wallMs == null || wallMs.Count == 0)
            {
                return default;
            }

            wallMs.Sort();
            float gpu = 0f;
            if (gpuMs != null && gpuMs.Count * 2 >= wallMs.Count)
            {
                gpuMs.Sort();
                gpu = Percentile(gpuMs, 0.9f);
            }

            return new FrameSampleStats(wallMs.Count, Percentile(wallMs, 0.5f), Percentile(wallMs, 0.9f), gpu);
        }

        /// <summary>The value at fraction <paramref name="p"/> (0..1) of an ascending list; 0 for an empty one.</summary>
        public static float Percentile(List<float> sorted, float p)
        {
            if (sorted == null || sorted.Count == 0)
            {
                return 0f;
            }

            int i = (int)Math.Floor(p * sorted.Count);
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, i))];
        }

        public override string ToString()
            => HasGpuTime
                ? $"median {MedianMs:0.0} ms, p90 {P90Ms:0.0} ms, GPU p90 {GpuP90Ms:0.0} ms, {Frames} frames"
                : $"median {MedianMs:0.0} ms, p90 {P90Ms:0.0} ms, no GPU time, {Frames} frames";
    }

    /// <summary>
    /// The rules of the automatic graphics-preset calibration (#1423, #3279–#3283). The preset is an index on the ladder
    /// Potato 0 · Low 1 · Medium 2 · High 3 (the client's <c>QualityPreset</c>). Three moments decide:
    /// <list type="bullet">
    ///   <item><b>Session start</b> (<see cref="StartStep"/>): Medium → High, and only on the strength of a measurement
    ///   taken IN THE WORLD at Medium on this GPU — the menu backdrop is a space scene without terrain, SSAO, shadow
    ///   cascades or grass and says nothing about what High costs (#3281). The step lands before the first frame of the
    ///   session, never during play.</item>
    ///   <item><b>Shell sample</b> (<see cref="ShellStep"/>): splash, intro and main menu step one notch down when the device
    ///   struggles even there, and up — at most to Medium — when there is headroom.</item>
    ///   <item><b>World</b>: the measurement is only recorded for the next start; a slow world never lowers a preset
    ///   by itself, it only earns the player a hint (<see cref="SlowWorldWatch"/>, #3282).</item>
    /// </list>
    /// Step-ups read the GPU time where the platform reports it (#3280); wall-clock frames are VSync-capped and often
    /// CPU-bound, so they only say "hits the display rate". Step-downs read the wall-clock median: what the player feels.
    /// </summary>
    public static class QualityCalibration
    {
        public const int Potato = 0;
        public const int Low = 1;
        public const int Medium = 2;
        public const int High = 3;

        /// <summary>Median worse than 25 fps: the device is struggling → one notch down.</summary>
        public const float StepDownMedianMs = 40f;

        /// <summary>Without GPU time: ≥ 90 % of the (VSync-capped) frames hit the 60 Hz budget → headroom for more.</summary>
        public const float StepUpP90Ms = 18f;

        /// <summary>The frame budget the calibration aims for: 60 fps.</summary>
        public const float FrameBudgetMs = 1000f / 60f;

        /// <summary>Share of <see cref="FrameBudgetMs"/> the GPU may fill after a step up (a margin for heavier places
        /// than the measured one: a jungle, a settlement at night, rain).</summary>
        public const float GpuBudgetShare = 0.8f;

        /// <summary>With GPU time, a step up also needs the wall-clock median at or under this — a CPU that already misses
        /// 50 fps gains nothing from a heavier preset (High adds CPU work too: grass shells, decals, the room probe).</summary>
        public const float CpuMedianLimitMs = 20f;

        /// <summary>GPU time of High relative to Medium in the same world spot (#3283). Measured with PerfProbe — see
        /// docs/developer/ADVANCED_GRAPHICS.md § "Automatic preset calibration" for the run, the machine and the numbers.</summary>
        public const float HighGpuCostFactor = 2.0f;

        /// <summary>True for a software rasterizer: such a device never steps up.</summary>
        public static bool IsSoftwareRenderer(string? deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            return Contains(deviceName, "Microsoft Basic Render") || Contains(deviceName, "llvmpipe")
                || Contains(deviceName, "softpipe") || Contains(deviceName, "SwiftShader");
        }

        /// <summary>The name under which a GPU is remembered. A platform that hides the adapter name (a privacy-masked
        /// browser) gets a fixed stand-in, so its measurement still matches the next session.</summary>
        public static string DeviceKey(string? deviceName)
            => string.IsNullOrWhiteSpace(deviceName) ? "(unnamed GPU)" : deviceName.Trim();

        /// <summary>Whether a step up is allowed at all this session: not on a software renderer, not on battery.</summary>
        public static bool MayStepUp(string? deviceName, bool onBattery) => !onBattery && !IsSoftwareRenderer(deviceName);

        /// <summary>The shell sample's verdict: one notch down when even the menu struggles, one notch up — never past
        /// Medium — when there is headroom and stepping up is allowed; otherwise the preset stays.</summary>
        public static int ShellStep(int preset, FrameSampleStats shell, bool mayStepUp)
        {
            if (shell.Frames == 0)
            {
                return preset;
            }

            if (shell.MedianMs > StepDownMedianMs && preset > Potato)
            {
                return preset - 1;
            }

            if (mayStepUp && preset < Medium && HasHeadroom(shell, 1f))
            {
                return preset + 1;
            }

            return preset;
        }

        /// <summary>Whether a window leaves room for a preset that costs <paramref name="gpuCostFactor"/> times the GPU time
        /// of the measured one. With GPU time: that GPU time fits the budget share and the CPU is not already struggling.
        /// Without: the VSync-capped p90 hits the 60 Hz budget (the factor cannot be applied to a capped number).</summary>
        public static bool HasHeadroom(FrameSampleStats s, float gpuCostFactor)
        {
            if (s.Frames == 0)
            {
                return false;
            }

            if (s.HasGpuTime)
            {
                return s.GpuP90Ms * gpuCostFactor <= FrameBudgetMs * GpuBudgetShare && s.MedianMs <= CpuMedianLimitMs;
            }

            return s.P90Ms < StepUpP90Ms;
        }

        /// <summary>Whether a world measurement taken at Medium shows room for High.</summary>
        public static bool WorldAllowsHigh(FrameSampleStats world) => HasHeadroom(world, HighGpuCostFactor);

        /// <summary>The session-start step (#3281): Medium → High when the last world measurement was taken at Medium on
        /// THIS GPU, shows room for High, and High was not already rejected on this GPU. Anything else keeps the preset.</summary>
        public static int StartStep(int preset, bool mayStepUp, WorldSampleRecord world, string? deviceName, string? highRejectedOn)
        {
            if (!mayStepUp || preset != Medium || world.Preset != Medium)
            {
                return preset;
            }

            if (!SameDevice(world.DeviceName, deviceName) || SameDevice(highRejectedOn, deviceName))
            {
                return preset;
            }

            return WorldAllowsHigh(world.Stats) ? High : preset;
        }

        /// <summary>True when a shell step left High: the GPU is remembered so High is not tried on it again.</summary>
        public static bool RejectsHigh(int before, int after) => before == High && after < High;

        /// <summary>Whether a "the game runs slowly" hint can suggest anything: a lower preset, or a shorter view distance
        /// than two chunks.</summary>
        public static bool HintUseful(int preset, int viewDistanceChunks) => preset > Potato || viewDistanceChunks > 2;

        private static bool SameDevice(string? a, string? b)
            => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.Ordinal);

        private static bool Contains(string haystack, string needle)
            => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>A world measurement kept for the next session start (#3281): the preset it was taken on, its statistics,
    /// and the GPU that rendered it.</summary>
    public readonly struct WorldSampleRecord
    {
        public WorldSampleRecord(int preset, FrameSampleStats stats, string? deviceName)
        {
            Preset = preset;
            Stats = stats;
            DeviceName = deviceName ?? string.Empty;
        }

        /// <summary>The preset it was measured on; -1 = no measurement.</summary>
        public int Preset { get; }

        public FrameSampleStats Stats { get; }

        public string DeviceName { get; }

        /// <summary>No measurement yet.</summary>
        public static WorldSampleRecord None => new WorldSampleRecord(-1, default, string.Empty);
    }

    /// <summary>
    /// The slow-world watchdog (#3282): collects wall-clock frame times in tumbling windows of
    /// <see cref="WindowSeconds"/> and fires once when a whole window ran at a median slower than
    /// <see cref="SlowMedianMs"/> (under 25 fps). A short hitch moves no median; <see cref="Reset"/> drops a half-filled
    /// window when the world stops counting (loading, a cutscene, the space view). It only ever triggers a hint — it never
    /// changes a setting.
    /// </summary>
    public sealed class SlowWorldWatch
    {
        public const float WindowSeconds = 30f;
        public const float SlowMedianMs = 40f;

        private readonly List<float> _frames = new List<float>(2048);
        private float _seconds;

        /// <summary>True once the hint fired; it stays fired for the rest of the session.</summary>
        public bool Fired { get; private set; }

        /// <summary>Drops the window in progress.</summary>
        public void Reset()
        {
            _frames.Clear();
            _seconds = 0f;
        }

        /// <summary>Adds one frame; true exactly once, on the frame that closes the first slow window.</summary>
        public bool Add(float frameMs)
        {
            if (Fired || frameMs <= 0f)
            {
                return false;
            }

            _frames.Add(frameMs);
            _seconds += frameMs / 1000f;
            if (_seconds < WindowSeconds)
            {
                return false;
            }

            _frames.Sort();
            bool slow = FrameSampleStats.Percentile(_frames, 0.5f) > SlowMedianMs;
            Reset();
            if (slow)
            {
                Fired = true;
            }

            return slow;
        }
    }
}
