// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.EditorTools
{
    /// <summary>
    /// #4044: where the time of a frame goes, from a Unity Profiler capture (.raw) written by a development player
    /// (PerfProbe <c>-perfProfile &lt;file&gt;</c>) — the time twin of <see cref="ProfilerCaptureReport"/>, which ranks
    /// allocations. For the main thread and the render thread it sums every profiler sample by name, once with its
    /// children (total) and once without (self), and prints the largest as milliseconds per frame.
    /// <para>Run: <c>Unity.exe -batchmode -nographics -projectPath client -executeMethod
    /// BlocksBeyondTheStars.Client.EditorTools.ProfilerTimeReport.Run -quit</c> with env <c>BBS_PROFILE_RAW</c> (the
    /// capture), <c>BBS_PROFILE_OUT</c> (the report path) and optionally <c>BBS_PROFILE_PHASES</c>: the capture's phases
    /// in order with their seconds, e.g. <c>idle:10,gap:2.5,walk:15</c>. The phases are laid out from the capture's END
    /// backwards (the Editor keeps only the last 2000 frames of a capture, so its start is not reliable); a phase named
    /// <c>gap</c> is skipped. Without the variable the whole capture is one phase.</para>
    /// </summary>
    public static class ProfilerTimeReport
    {
        private const int Top = 70;

        private sealed class Sums
        {
            public readonly Dictionary<string, double> Self = new Dictionary<string, double>(StringComparer.Ordinal);
            public readonly Dictionary<string, double> Total = new Dictionary<string, double>(StringComparer.Ordinal);
            public readonly Dictionary<string, long> Calls = new Dictionary<string, long>(StringComparer.Ordinal);
            public int Frames;
            public double FrameMs;
        }

        public static void Run()
        {
            string raw = Environment.GetEnvironmentVariable("BBS_PROFILE_RAW");
            string outPath = Environment.GetEnvironmentVariable("BBS_PROFILE_OUT");
            if (string.IsNullOrEmpty(raw) || !File.Exists(raw))
            {
                Debug.LogError($"ProfilerTimeReport: capture not found: '{raw}'");
                EditorApplication.Exit(2);
                return;
            }

            string report;
            try
            {
                report = Build(raw, Environment.GetEnvironmentVariable("BBS_PROFILE_PHASES"));
            }
            catch (Exception ex)
            {
                Debug.LogError("ProfilerTimeReport failed: " + ex);
                EditorApplication.Exit(3);
                return;
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, report);
            }

            Debug.Log("ProfilerTimeReport:\n" + report);
            EditorApplication.Exit(0);
        }

        public static string Build(string rawPath, string phaseSpec)
        {
            if (!ProfilerDriver.LoadProfile(rawPath, false))
            {
                return "could not load " + rawPath;
            }

            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var starts = new List<double>();
            double end = 0d;
            for (int f = first; f <= last; f++)
            {
                using var view = ProfilerDriver.GetRawFrameDataView(f, 0);
                bool ok = view != null && view.valid;
                starts.Add(ok ? view.frameStartTimeMs : double.NaN);
                if (ok)
                {
                    end = view.frameStartTimeMs + view.frameTimeMs;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Capture {Path.GetFileName(rawPath)}: frames {first}..{last}");
            foreach (var (name, fromMs, toMs) in Phases(phaseSpec, end))
            {
                var frames = new List<int>();
                for (int f = first; f <= last; f++)
                {
                    double start = starts[f - first];
                    if (!double.IsNaN(start) && start >= fromMs && start < toMs)
                    {
                        frames.Add(f);
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"==================== phase '{name}': {frames.Count} frames");
                AppendThread(sb, frames, "Main Thread", 0);
                int render = FindThread(frames.Count > 0 ? frames[0] : first, "Render Thread");
                if (render > 0)
                {
                    AppendThread(sb, frames, "Render Thread", render);
                }
            }

            return sb.ToString();
        }

        /// <summary>The phases as (name, from, to) in capture milliseconds, laid out backwards from the capture's end.</summary>
        private static List<(string Name, double From, double To)> Phases(string spec, double endMs)
        {
            var result = new List<(string, double, double)>();
            if (string.IsNullOrWhiteSpace(spec))
            {
                result.Add(("all", double.NegativeInfinity, double.PositiveInfinity));
                return result;
            }

            double to = endMs;
            foreach (string token in spec.Split(',').Reverse())
            {
                var parts = token.Split(':');
                double seconds = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0d;
                double from = to - (seconds * 1000d);
                if (!string.Equals(parts[0].Trim(), "gap", StringComparison.OrdinalIgnoreCase))
                {
                    result.Insert(0, (parts[0].Trim(), from, to));
                }

                to = from;
            }

            return result;
        }

        private static int FindThread(int frame, string threadName)
        {
            for (int t = 0; t < 256; t++)
            {
                using var view = ProfilerDriver.GetRawFrameDataView(frame, t);
                if (view == null || !view.valid)
                {
                    return -1;
                }

                if (string.Equals(view.threadName, threadName, StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return -1;
        }

        private static void AppendThread(StringBuilder sb, List<int> frames, string label, int thread)
        {
            var sums = new Sums();
            foreach (int f in frames)
            {
                using var view = ProfilerDriver.GetRawFrameDataView(f, thread);
                if (view == null || !view.valid)
                {
                    continue;
                }

                sums.Frames++;
                sums.FrameMs += view.frameTimeMs;
                Accumulate(view, sums);
            }

            double n = Math.Max(1, sums.Frames);
            sb.AppendLine();
            sb.AppendLine($"--- {label}: {sums.Frames} frames, frame time avg {sums.FrameMs / n:0.00} ms");
            sb.AppendLine("Top by SELF time (ms/frame, calls/frame):");
            foreach (var kv in sums.Self.OrderByDescending(k => k.Value).Take(Top))
            {
                sb.AppendLine($"  {kv.Value / n,8:0.000} ms  {sums.Calls[kv.Key] / n,8:0.0}x  {kv.Key}");
            }

            sb.AppendLine("Top by TOTAL time, children included (ms/frame, calls/frame):");
            foreach (var kv in sums.Total.OrderByDescending(k => k.Value).Take(Top))
            {
                sb.AppendLine($"  {kv.Value / n,8:0.000} ms  {sums.Calls[kv.Key] / n,8:0.0}x  {kv.Key}");
            }
        }

        /// <summary>Samples come depth-first with their children counts: a sample's self time is its time less its
        /// direct children's.</summary>
        private static void Accumulate(RawFrameDataView view, Sums sums)
        {
            var names = new List<string>();
            var self = new List<double>();
            var remaining = new List<int>();
            void Pop()
            {
                int top = names.Count - 1;
                Add(sums.Self, names[top], Math.Max(0d, self[top]));
                names.RemoveAt(top);
                self.RemoveAt(top);
                remaining.RemoveAt(top);
            }

            for (int s = 0; s < view.sampleCount; s++)
            {
                while (remaining.Count > 0 && remaining[remaining.Count - 1] == 0)
                {
                    Pop();
                }

                if (remaining.Count > 0)
                {
                    remaining[remaining.Count - 1]--;
                    self[self.Count - 1] -= view.GetSampleTimeMs(s);
                }

                string name = view.GetSampleName(s) ?? "(unnamed)";
                double ms = view.GetSampleTimeMs(s);
                Add(sums.Total, name, ms);
                sums.Calls.TryGetValue(name, out long calls);
                sums.Calls[name] = calls + 1;
                names.Add(name);
                self.Add(ms);
                remaining.Add(view.GetSampleChildrenCount(s));
            }

            while (names.Count > 0)
            {
                Pop();
            }
        }

        private static void Add(Dictionary<string, double> map, string key, double value)
        {
            map.TryGetValue(key, out double current);
            map[key] = current + value;
        }
    }
}
