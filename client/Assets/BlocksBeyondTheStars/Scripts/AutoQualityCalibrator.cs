// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The automatic graphics preset (#1423; every platform since #3279). Device class alone cannot answer "is this device
    /// fast enough" — a budget tablet reports desktop-like specs and a weak office laptop is a "computer" — so measured
    /// frame times are the authority. Runs only while <see cref="ClientSettings.PresetAuto"/> is set: a preset the player
    /// chose by hand is never touched. The rules live in <see cref="QualityCalibration"/>; this class feeds them:
    /// <list type="bullet">
    ///   <item><see cref="SessionStart"/> (AppShell, before the first Apply) takes Medium → High when the last world
    ///   measurement (<see cref="WorldPerfWatch"/>) showed room for it on this GPU (#3281).</item>
    ///   <item>The shell sample — studio logo, splash, intro, main menu, the fixed scenes comparable between devices —
    ///   steps one notch per session: down when even the menu struggles, up at most to Medium. The menu backdrop is a
    ///   space scene without terrain, so it cannot vouch for High.</item>
    /// </list>
    /// Step-ups read the GPU time where the platform reports one (<see cref="GpuFrameTime"/>, #3280); in the browser the
    /// frames are VSynced to the display, so a loaded device shows up as dropped frames — the VSync rule there needs a
    /// clean p90, not a low median. No step-up on a software renderer or on battery.
    /// </summary>
    public sealed class AutoQualityCalibrator : MonoBehaviour
    {
        /// <summary>The owning shell; supplies the settings and the current phase.</summary>
        public AppShell Shell;

        private const float WarmupSeconds = 6f;    // skip startup spikes (shader warmup, content HTTP burst)
        private const float SampleSeconds = 15f;   // enough frames for stable percentiles, done before play starts

        private readonly List<float> _samplesMs = new List<float>(1200);
        private readonly List<float> _gpuMs = new List<float>(1200);
        private float _elapsed;
        private bool _done;

        private static bool? _enabled;

        /// <summary>False on an unattended run — PerfProbe, screenshot and clip captures — where the calibration would move
        /// a measured preset between runs and a hint toast would land in the frames. <c>-autoQuality</c> turns it back on
        /// to test the calibration itself (e.g. with <c>-perfProbe</c>, which then also records a world sample).</summary>
        public static bool Enabled => _enabled ??= ReadEnabled();

        /// <summary>Whether this session may step the preset up: not on a software renderer, not on battery.</summary>
        public static bool MayStepUp()
            => QualityCalibration.MayStepUp(SystemInfo.graphicsDeviceName, OnBattery);

        private static bool OnBattery => SystemInfo.batteryStatus == BatteryStatus.Discharging;

        /// <summary>The GPU as the calibration remembers it (a fixed stand-in when the platform hides the name).</summary>
        public static string DeviceKey => QualityCalibration.DeviceKey(SystemInfo.graphicsDeviceName);

        /// <summary>The session-start step (#3281): Medium → High on last session's world measurement. Call before the
        /// first <see cref="ClientSettings.Apply"/> so the very first frame already renders the new preset.</summary>
        public static void SessionStart(ClientSettings settings)
        {
            if (settings == null || !settings.PresetAuto || !Enabled)
            {
                return;
            }

            string gpu = DeviceKey;
            int next = QualityCalibration.StartStep((int)settings.Preset, MayStepUp(), settings.AutoWorldSample, gpu, settings.AutoHighRejectedGpu);
            if (next == (int)settings.Preset)
            {
                return;
            }

            Debug.Log($"[AutoQuality] Session start: {settings.Preset} → {(QualityPreset)next} (world sample at "
                + $"{(QualityPreset)settings.AutoWorldPreset}: {settings.AutoWorldSample.Stats}).");
            settings.Preset = (QualityPreset)next;
            settings.Save();
        }

        private void Update()
        {
            if (_done || Shell == null || Shell.Settings == null)
            {
                return;
            }

            var settings = Shell.Settings;
            if (!settings.PresetAuto || !Enabled)
            {
                _done = true; // the player took over in the settings menu — never fight a manual choice
                return;
            }

            // A desktop window in the background keeps rendering (Run In Background) while the player is elsewhere and
            // the machine may be busy — those frames say nothing about the game. Pause (don't reset). The browser keeps
            // its old behaviour: a hidden tab gets no frames anyway, and canvas focus is not window focus.
            if (!Application.isFocused && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                return;
            }

            // Only the fixed shell scenes are comparable between devices; pause (don't reset) elsewhere.
            switch (Shell.Phase)
            {
                case ShellPhase.Studio:
                case ShellPhase.Splash:
                case ShellPhase.Intro:
                case ShellPhase.MainMenu:
                    break;
                default:
                    return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float gpu = GpuFrameTime.Sample(); // every frame, so the capture keeps up with the driver
            if (_elapsed < WarmupSeconds)
            {
                return;
            }

            _samplesMs.Add(Time.unscaledDeltaTime * 1000f);
            if (gpu > 0f)
            {
                _gpuMs.Add(gpu);
            }

            if (_elapsed < WarmupSeconds + SampleSeconds)
            {
                return;
            }

            _done = true;
            Decide(settings, FrameSampleStats.From(_samplesMs, _gpuMs));
        }

        private static void Decide(ClientSettings settings, FrameSampleStats shell)
        {
            var before = settings.Preset;
            var after = (QualityPreset)QualityCalibration.ShellStep((int)before, shell, MayStepUp());
            if (after == before)
            {
                Debug.Log($"[AutoQuality] Preset {before} confirmed ({shell}).");
                return;
            }

            Debug.Log($"[AutoQuality] Preset {before} → {after} ({shell}).");
            if (QualityCalibration.RejectsHigh((int)before, (int)after) && !OnBattery)
            {
                settings.AutoHighRejectedGpu = DeviceKey; // on battery the slowness may pass — don't ban High for good
            }

            settings.Preset = after;
            settings.Apply();
            settings.Save(); // the next launch starts on the calibrated preset right away
        }

        private static bool ReadEnabled()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            bool Has(string flag) => System.Array.Exists(args, a => string.Equals(a, flag, System.StringComparison.OrdinalIgnoreCase));
            return Has("-autoQuality") || !(Has("-perfProbe") || Has("-captureClip") || Has("-captureShots"));
        }
    }
}
