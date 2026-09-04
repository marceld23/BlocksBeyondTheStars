// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one place that reads the machine's OS / CPU / RAM / GPU / driver facts for a report (#1564). Both the
    /// F1 feedback payload (<see cref="FeedbackUi"/>) and the automatic crash report (<see cref="CrashReporter"/>)
    /// carry the same six keys — <c>os</c>, <c>cpu</c>, <c>ramMb</c>, <c>gpu</c>, <c>gpuDriver</c>, <c>vramMb</c> —
    /// so the inbox can tell a driver TDR / blue-screen class of report ("what GPU, which driver?") apart from a
    /// game bug without asking the player. <see cref="SystemInfo"/> is main-thread-only, so the values are read
    /// once on the first main-thread call and cached; the crash reporter's threaded log callback only ever sees
    /// the cached copy. Never throws — a report must not fail because a driver string could not be read.
    /// </summary>
    public static class DeviceInfo
    {
        /// <summary>The cached field values. Plain immutable strings/ints: safe to read from any thread once built.</summary>
        public sealed class Snapshot
        {
            public string Os = string.Empty;
            public string Cpu = string.Empty;
            public int RamMb;
            public string Gpu = string.Empty;
            public string GpuDriver = string.Empty;
            public int VramMb;

            /// <summary>Adds the six device keys to a report's <c>ReportJson</c> dictionary.</summary>
            public void WriteTo(Dictionary<string, object> json)
            {
                if (json == null)
                {
                    return;
                }

                json["os"] = Os;
                json["cpu"] = Cpu;
                json["ramMb"] = RamMb;
                json["gpu"] = Gpu;
                json["gpuDriver"] = GpuDriver;
                json["vramMb"] = VramMb;
            }
        }

        private static Snapshot _cached;

        /// <summary>Returns the device snapshot, reading <see cref="SystemInfo"/> on the first call. MAIN THREAD
        /// for that first call (<see cref="CrashReporter"/> primes it in Awake); later calls are cache hits from
        /// any thread.</summary>
        public static Snapshot Get()
        {
            var cached = _cached;
            if (cached != null)
            {
                return cached;
            }

            var snapshot = new Snapshot();
            try
            {
                snapshot.Os = SystemInfo.operatingSystem ?? string.Empty;
                snapshot.Cpu = SystemInfo.processorType ?? string.Empty;
                snapshot.RamMb = SystemInfo.systemMemorySize;
                snapshot.Gpu = SystemInfo.graphicsDeviceName ?? string.Empty;
                snapshot.GpuDriver = SystemInfo.graphicsDeviceVersion ?? string.Empty;
                snapshot.VramMb = SystemInfo.graphicsMemorySize;
            }
            catch (Exception e)
            {
                // Off-thread or an exotic platform: keep whatever was read, report the rest empty.
                Debug.LogWarning($"[DeviceInfo] Could not read the device info: {e.Message}");
            }

            _cached = snapshot;
            return snapshot;
        }
    }
}
