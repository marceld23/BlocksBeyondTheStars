// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Measures the browser build's real frame times during the fixed shell scenes (splash → intro →
    /// main menu) and steps the quality preset one notch up or down per session (#1423). Device class
    /// alone cannot answer "is this device fast enough" — a budget tablet reports desktop-like specs
    /// and a weak office laptop is a "computer" — so the measured frame time is the authority; the
    /// <see cref="BrowserDevice"/> start guess only picks where the ladder begins. One step per
    /// session converges over a few launches and avoids visible mid-session flapping. Runs only while
    /// <see cref="ClientSettings.PresetAuto"/> is set — a preset the player chose by hand is never
    /// touched — and only samples shell phases: in-game frame times measure the world, not the device.
    /// WebGL frames are vsynced to the display, so a loaded device shows up as DROPPED frames rather
    /// than a raised median — the step-up test therefore requires a clean p90, not a low median.
    /// </summary>
    public sealed class AutoQualityCalibrator : MonoBehaviour
    {
        /// <summary>The owning shell; supplies the settings and the current phase.</summary>
        public AppShell Shell;

        private const float WarmupSeconds = 6f;    // skip startup spikes (shader warmup, content HTTP burst)
        private const float SampleSeconds = 15f;   // enough frames for stable percentiles, done before play starts
        private const float StepDownMedianMs = 40f; // median worse than 25 fps → the device is struggling
        private const float StepUpP90Ms = 18f;      // ≥90 % of frames hit the 60 Hz budget → headroom for more

        private readonly List<float> _samplesMs = new List<float>(1200);
        private float _elapsed;
        private bool _done;

        private void Update()
        {
            if (_done || Shell == null || Shell.Settings == null)
            {
                return;
            }

            var settings = Shell.Settings;
            if (!settings.PresetAuto)
            {
                _done = true; // the player took over in the settings menu — never fight a manual choice
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
            if (_elapsed < WarmupSeconds)
            {
                return;
            }

            _samplesMs.Add(Time.unscaledDeltaTime * 1000f);
            if (_elapsed < WarmupSeconds + SampleSeconds)
            {
                return;
            }

            _done = true;
            _samplesMs.Sort();
            float median = _samplesMs[_samplesMs.Count / 2];
            float p90 = _samplesMs[Mathf.Min(_samplesMs.Count - 1, Mathf.FloorToInt(_samplesMs.Count * 0.9f))];

            var preset = settings.Preset;
            if (median > StepDownMedianMs && preset > QualityPreset.Potato)
            {
                preset--;
            }
            else if (p90 < StepUpP90Ms && preset < QualityPreset.High)
            {
                preset++;
            }

            if (preset == settings.Preset)
            {
                Debug.Log($"[AutoQuality] Preset {settings.Preset} confirmed (median {median:0.0} ms, p90 {p90:0.0} ms).");
                return;
            }

            Debug.Log($"[AutoQuality] Preset {settings.Preset} → {preset} (median {median:0.0} ms, p90 {p90:0.0} ms).");
            settings.Preset = preset;
            settings.Apply();
            settings.Save(); // the next launch starts on the calibrated preset right away
        }
    }
}
