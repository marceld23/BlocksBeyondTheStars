// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
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
    /// #1537: ranks managed allocations in a Unity Profiler capture (.raw) written by a development player
    /// (PerfProbe <c>-perfProfile &lt;file&gt;</c>) — headless, so the attribution runs from a script instead of the
    /// Profiler window. Every <c>GC.Alloc</c> sample carries its size; it is attributed twice: to the profiler
    /// sample path it sits in (the MonoBehaviour callback / instrumented scope — always available) and, when the
    /// player recorded allocation call stacks, to the allocating managed method. Sums are per second of capture.
    /// Run: <c>Unity.exe -batchmode -nographics -projectPath client -executeMethod
    /// BlocksBeyondTheStars.Client.EditorTools.ProfilerCaptureReport.Run -quit</c> with env
    /// <c>BBS_PROFILE_RAW</c> (the capture) and <c>BBS_PROFILE_OUT</c> (the report path).
    /// </summary>
    public static class ProfilerCaptureReport
    {
        public static void Run()
        {
            string raw = Environment.GetEnvironmentVariable("BBS_PROFILE_RAW");
            string outPath = Environment.GetEnvironmentVariable("BBS_PROFILE_OUT");
            if (string.IsNullOrEmpty(raw) || !File.Exists(raw))
            {
                Debug.LogError($"ProfilerCaptureReport: capture not found: '{raw}'");
                EditorApplication.Exit(2);
                return;
            }

            string report;
            try
            {
                report = Build(raw);
            }
            catch (Exception ex)
            {
                Debug.LogError("ProfilerCaptureReport failed: " + ex);
                EditorApplication.Exit(3);
                return;
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, report);
            }

            Debug.Log("ProfilerCaptureReport:\n" + report);
            EditorApplication.Exit(0);
        }

        public static string Build(string rawPath)
        {
            // The Editor keeps only the LAST 2000 frames of a loaded capture (~10 s at 200 fps; the cap lives in the
            // internal ProfilerUserSettings and setting it by reflection did not change what LoadProfile keeps), so
            // a capture of idle + walk reports the walk phase — keep the PerfProbe phases at 10 s or shorter.
            if (!ProfilerDriver.LoadProfile(rawPath, false))
            {
                return "could not load " + rawPath;
            }

            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var byScope = new Dictionary<string, (long Bytes, long Count)>(StringComparer.Ordinal);
            var byMethod = new Dictionary<string, (long Bytes, long Count)>(StringComparer.Ordinal);
            var byStack = new Dictionary<string, (long Bytes, long Count)>(StringComparer.Ordinal);
            var stack = new List<ulong>();
            var path = new List<string>();
            var remaining = new List<int>();
            long totalBytes = 0, totalCount = 0, withStacks = 0;
            int frames = 0, gcAllocMarker = -1;
            double firstTime = double.NaN, lastTime = double.NaN;

            for (int f = first; f <= last; f++)
            {
                using var view = ProfilerDriver.GetRawFrameDataView(f, 0); // main thread
                if (view == null || !view.valid)
                {
                    continue;
                }

                frames++;
                if (double.IsNaN(firstTime))
                {
                    firstTime = view.frameStartTimeMs;
                }

                lastTime = view.frameStartTimeMs + view.frameTimeMs;
                if (gcAllocMarker < 0)
                {
                    gcAllocMarker = view.GetMarkerId("GC.Alloc");
                }

                path.Clear();
                remaining.Clear();
                for (int s = 0; s < view.sampleCount; s++)
                {
                    // Samples come depth-first; rebuild the scope path from the children counts.
                    while (remaining.Count > 0 && remaining[remaining.Count - 1] == 0)
                    {
                        path.RemoveAt(path.Count - 1);
                        remaining.RemoveAt(remaining.Count - 1);
                    }

                    if (remaining.Count > 0)
                    {
                        remaining[remaining.Count - 1]--;
                    }

                    int children = view.GetSampleChildrenCount(s);
                    if (view.GetSampleMarkerId(s) == gcAllocMarker)
                    {
                        long bytes = view.GetSampleMetadataCount(s) > 0 ? view.GetSampleMetadataAsLong(s, 0) : 0;
                        totalBytes += bytes;
                        totalCount++;

                        int from = Math.Max(0, path.Count - 4);
                        string scope = path.Count == 0 ? "(root)" : string.Join(" / ", path.Skip(from).Select(Short));
                        Add(byScope, scope, bytes);

                        stack.Clear();
                        view.GetSampleCallstack(s, stack);
                        string top = null;
                        var chain = new StringBuilder();
                        int shown = 0;
                        foreach (ulong addr in stack)
                        {
                            var info = view.ResolveMethodInfo(addr);
                            if (string.IsNullOrEmpty(info.methodName))
                            {
                                continue;
                            }

                            string name = Short(info.methodName);
                            top ??= name;
                            if (shown < 6)
                            {
                                chain.Append(shown == 0 ? "" : " ← ").Append(name);
                                shown++;
                            }
                        }

                        if (top != null)
                        {
                            withStacks++;
                            Add(byMethod, top, bytes);
                            Add(byStack, chain.ToString(), bytes);
                        }
                    }

                    path.Add(view.GetSampleName(s));
                    remaining.Add(children);
                }
            }

            double seconds = Math.Max(0.001, (lastTime - firstTime) / 1000.0);
            var sb = new StringBuilder();
            sb.AppendLine($"Capture {Path.GetFileName(rawPath)}: {frames} frames, {seconds:0.0} s, {totalCount} GC.Alloc samples ({withStacks} with call stacks), {totalBytes / 1024.0 / seconds:0.0} KB/s, {totalCount / seconds:0.0} allocs/s");
            sb.AppendLine();
            sb.AppendLine("Top allocating scopes (profiler sample path; bytes/s, allocs/s):");
            foreach (var kv in byScope.OrderByDescending(k => k.Value.Bytes).Take(45))
            {
                sb.AppendLine($"  {kv.Value.Bytes / 1024.0 / seconds,9:0.0} KB/s {kv.Value.Count / seconds,8:0.0}/s  {kv.Key}");
            }

            if (byMethod.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Top allocating methods (call stacks; bytes/s, allocs/s):");
                foreach (var kv in byMethod.OrderByDescending(k => k.Value.Bytes).Take(60))
                {
                    sb.AppendLine($"  {kv.Value.Bytes / 1024.0 / seconds,9:0.0} KB/s {kv.Value.Count / seconds,8:0.0}/s  {kv.Key}");
                }

                sb.AppendLine();
                sb.AppendLine("Top call stacks (bytes/s):");
                foreach (var kv in byStack.OrderByDescending(k => k.Value.Bytes).Take(60))
                {
                    sb.AppendLine($"  {kv.Value.Bytes / 1024.0 / seconds,9:0.0} KB/s {kv.Value.Count / seconds,8:0.0}/s  {kv.Key}");
                }
            }

            return sb.ToString();
        }

        /// <summary>Drops the assembly prefix and the project namespace: "X.dll!A.B::C.D()" → "C.D()".</summary>
        private static string Short(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            int bang = name.IndexOf("!", StringComparison.Ordinal);
            if (bang >= 0 && name.IndexOf("::", StringComparison.Ordinal) > bang)
            {
                name = name.Substring(name.IndexOf("::", StringComparison.Ordinal) + 2);
            }

            return name;
        }

        private static void Add(Dictionary<string, (long Bytes, long Count)> map, string key, long bytes)
        {
            map.TryGetValue(key, out var cur);
            map[key] = (cur.Bytes + bytes, cur.Count + 1);
        }
    }
}
