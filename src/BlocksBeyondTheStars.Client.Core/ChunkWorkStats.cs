// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a <see cref="WorkSampler"/> saw since its last reset: how many pieces of work, their summed time,
    /// and the median, the 90th percentile and the longest of the most recent ones.</summary>
    public readonly struct WorkSample
    {
        public WorkSample(long count, double totalMs, float p50Ms, float p90Ms, float maxMs)
        {
            Count = count;
            TotalMs = totalMs;
            P50Ms = p50Ms;
            P90Ms = p90Ms;
            MaxMs = maxMs;
        }

        public long Count { get; }

        public double TotalMs { get; }

        public float P50Ms { get; }

        public float P90Ms { get; }

        public float MaxMs { get; }
    }

    /// <summary>
    /// Timing samples of one kind of work (#4044), safe to feed from any thread: a count and a sum over everything since
    /// the last reset, and percentiles over the most recent <see cref="Capacity"/> samples. One lock per sample — the
    /// callers add a few hundred a second at most.
    /// </summary>
    public sealed class WorkSampler
    {
        /// <summary>How many of the most recent samples the percentiles are taken over.</summary>
        public const int Capacity = 4096;

        private readonly object _gate = new object();
        private readonly float[] _ring = new float[Capacity];
        private long _count;
        private double _totalMs;
        private float _maxMs;

        /// <summary>Adds one piece of work that took <paramref name="ms"/> milliseconds.</summary>
        public void Add(double ms)
        {
            float value = ms > 0d ? (float)ms : 0f;
            lock (_gate)
            {
                _ring[(int)(_count % Capacity)] = value;
                _count++;
                _totalMs += value;
                if (value > _maxMs)
                {
                    _maxMs = value;
                }
            }
        }

        /// <summary>Adds the time since <paramref name="startTimestamp"/> (a <see cref="Stopwatch.GetTimestamp"/> value).</summary>
        public void AddSince(long startTimestamp)
            => Add((Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency);

        /// <summary>What was added since the last reset; <paramref name="reset"/> starts a new window.</summary>
        public WorkSample Snapshot(bool reset)
        {
            float[] recent;
            long count;
            double total;
            float max;
            lock (_gate)
            {
                count = _count;
                total = _totalMs;
                max = _maxMs;
                recent = new float[(int)Math.Min(count, Capacity)];
                Array.Copy(_ring, recent, recent.Length);
                if (reset)
                {
                    _count = 0;
                    _totalMs = 0d;
                    _maxMs = 0f;
                }
            }

            Array.Sort(recent);
            return new WorkSample(count, total, Percentile(recent, 0.5f), Percentile(recent, 0.9f), max);
        }

        private static float Percentile(float[] sorted, float p)
        {
            if (sorted.Length == 0)
            {
                return 0f;
            }

            int i = (int)Math.Round(p * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, i))];
        }
    }

    /// <summary>
    /// Where the client's chunk work goes (#4044), for PerfProbe: the mesher's build on its thread, the main thread's part
    /// of a dispatch and of an upload, the whole chunk pass of a frame, and the receive path (a payload's decode and its
    /// handler). Diagnostics only — nothing reads these to decide anything.
    /// </summary>
    public static class ChunkWorkStats
    {
        /// <summary>One chunk's geometry build, on the thread that ran it.</summary>
        public static readonly WorkSampler Build = new WorkSampler();

        /// <summary>The main thread's part of starting a build: the copy of the chunk and its neighbourhood.</summary>
        public static readonly WorkSampler Dispatch = new WorkSampler();

        /// <summary>The main thread's part of finishing a build: the mesh upload and the chunk object.</summary>
        public static readonly WorkSampler Upload = new WorkSampler();

        /// <summary>The whole chunk pass of one frame on the main thread (drain, dispatch, reposition, visibility).</summary>
        public static readonly WorkSampler FramePass = new WorkSampler();

        /// <summary>One chunk payload on the receive path: its decode and its handler together.</summary>
        public static readonly WorkSampler Receive = new WorkSampler();

        /// <summary>The handler's share of <see cref="Receive"/>: block decode, store and marking the neighbours.</summary>
        public static readonly WorkSampler Store = new WorkSampler();

        /// <summary>#4046: the detail pass of one frame on the main thread — tufts, meadow, clumps, plant bodies and crown
        /// clusters of the chunks in reach.</summary>
        public static readonly WorkSampler DetailPass = new WorkSampler();

        /// <summary>From a chunk's arrival to its first mesh on screen: what the player sees as pop-in.</summary>
        public static readonly WorkSampler MeshLatency = new WorkSampler();

        /// <summary>Starts a new window on every sampler.</summary>
        public static void Reset()
        {
            MeshLatency.Snapshot(reset: true);
            DetailPass.Snapshot(reset: true);
            Build.Snapshot(reset: true);
            Dispatch.Snapshot(reset: true);
            Upload.Snapshot(reset: true);
            FramePass.Snapshot(reset: true);
            Receive.Snapshot(reset: true);
            Store.Snapshot(reset: true);
        }
    }
}
