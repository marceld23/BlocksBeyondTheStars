// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The persisted state of the automatic preset calibration (#3281): the last world measurement, which the next
    /// session start reads to decide Medium → High, and the GPU on which High was already rejected.</summary>
    public sealed partial class ClientSettings
    {
        /// <summary>The preset the last world measurement was taken on; -1 = none yet. Only written while
        /// <see cref="PresetAuto"/> is set.</summary>
        public int AutoWorldPreset = -1;

        /// <summary>Its wall-clock median and p90 and its GPU p90 (0 = the platform reported no GPU time), in ms.</summary>
        public float AutoWorldMedianMs;
        public float AutoWorldP90Ms;
        public float AutoWorldGpuP90Ms;
        public int AutoWorldFrames;

        /// <summary>The GPU (<c>SystemInfo.graphicsDeviceName</c>) that rendered it — another GPU ignores the measurement.</summary>
        public string AutoWorldGpu = "";

        /// <summary>The GPU on which the menu calibration had to step down from High: High is not tried again on it.</summary>
        public string AutoHighRejectedGpu = "";

        /// <summary>The stored world measurement as the calibration rules read it.</summary>
        public WorldSampleRecord AutoWorldSample => AutoWorldPreset < 0
            ? WorldSampleRecord.None
            : new WorldSampleRecord(AutoWorldPreset,
                new FrameSampleStats(AutoWorldFrames, AutoWorldMedianMs, AutoWorldP90Ms, AutoWorldGpuP90Ms), AutoWorldGpu);

        /// <summary>Keeps a world measurement for the next session start.</summary>
        public void RecordWorldSample(QualityPreset preset, FrameSampleStats stats, string gpu)
        {
            AutoWorldPreset = (int)preset;
            AutoWorldMedianMs = stats.MedianMs;
            AutoWorldP90Ms = stats.P90Ms;
            AutoWorldGpuP90Ms = stats.GpuP90Ms;
            AutoWorldFrames = stats.Frames;
            AutoWorldGpu = gpu ?? "";
        }
    }
}
