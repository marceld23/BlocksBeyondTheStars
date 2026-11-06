// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Watches the frame times while you play — the place that shows what a preset really costs (terrain, SSAO, shadow
    /// cascades, grass, volumetric light), unlike the menu's space backdrop. Two jobs, both only once the spawn area has
    /// settled and never during loading, a cutscene or the space view (a window in the background keeps rendering —
    /// Run In Background is on — and a hidden browser tab gets no frames at all, so neither needs a rule):
    /// <list type="bullet">
    ///   <item><b>World measurement</b> (#3281, only while <see cref="ClientSettings.PresetAuto"/> is set): one sample of
    ///   <see cref="SampleSeconds"/> per session — wall-clock median/p90 and the GPU p90 (<see cref="GpuFrameTime"/>) —
    ///   kept in the settings. The NEXT session start reads it to decide Medium → High
    ///   (<see cref="AutoQualityCalibrator.SessionStart"/>); nothing changes while you play.</item>
    ///   <item><b>Slow-world hint</b> (#3282, every preset, hand-picked or auto): when a whole window of
    ///   <see cref="SlowWorldWatch.WindowSeconds"/> runs under 25 fps, one toast per session says that a lower preset or
    ///   a shorter view distance helps. It never changes a setting.</item>
    /// </list>
    /// </summary>
    public sealed class WorldPerfWatch : MonoBehaviour
    {
        /// <summary>The owning shell; supplies the settings, the phase, the running world and the localizer.</summary>
        public AppShell Shell;

        private const float SettleSeconds = 12f;  // like PerfProbe: the spawn area has finished meshing
        private const float SampleSeconds = 20f;

        private readonly List<float> _wallMs = new List<float>(2400);
        private readonly List<float> _gpuMs = new List<float>(2400);
        private readonly SlowWorldWatch _slow = new SlowWorldWatch();
        private float _settled;
        private float _sampled;
        private bool _sampleDone;
        private string _paused = string.Empty; // why the watch last stopped counting, logged once per change

        private void Update()
        {
            var settings = Shell != null ? Shell.Settings : null;
            if (!AutoQualityCalibrator.Enabled)
            {
                return; // an unattended capture or probe run
            }

            string paused = settings == null ? "no settings" : PauseReason(Shell.CurrentBoot);
            if (paused != _paused)
            {
                if (Shell != null && Shell.Phase == ShellPhase.InGame)
                {
                    Debug.Log(paused.Length == 0 ? "[AutoQuality] World watch counting." : $"[AutoQuality] World watch paused: {paused}.");
                }

                _paused = paused;
            }

            if (paused.Length > 0)
            {
                // Half-filled windows are dropped: they would mix loading frames, another world or another preset (the
                // menu calibration may step between two visits) into the next one.
                _settled = 0f;
                _slow.Reset();
                ResetSample();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float gpu = GpuFrameTime.Sample();
            _settled += dt;
            if (_settled < SettleSeconds)
            {
                return;
            }

            float ms = dt * 1000f;
            Sample(settings, ms, gpu);
            if (QualityCalibration.HintUseful((int)settings.Preset, settings.ViewDistanceChunks) && _slow.Add(ms))
            {
                Debug.Log($"[AutoQuality] The world runs slowly on {settings.Preset} (median under 25 fps for {SlowWorldWatch.WindowSeconds:0} s) — hint shown.");
                Shell.CurrentBoot.ShowMessage(Shell.Localizer != null ? Shell.Localizer.Get("ui.perf.slow_hint") : "The game is running slowly here.");
            }
        }

        /// <summary>Why this frame is not a play frame — empty when it is: in the world, loaded, no cutscene, not the space
        /// view.</summary>
        private string PauseReason(GameBootstrap boot)
        {
            if (Shell.Phase != ShellPhase.InGame || boot == null)
            {
                return "not in a world";
            }

            if (!boot.WorldReady || boot.PlayerSettling)
            {
                return "loading"; // WorldReady starts true before the join; PlayerSettling covers that gap
            }

            if (boot.SpaceViewActive)
            {
                return "space view";
            }

            return GameBootstrap.CutsceneActive ? "cutscene" : string.Empty;
        }

        private void Sample(ClientSettings settings, float ms, float gpu)
        {
            if (_sampleDone || !settings.PresetAuto)
            {
                return;
            }

            _wallMs.Add(ms);
            if (gpu > 0f)
            {
                _gpuMs.Add(gpu);
            }

            _sampled += ms / 1000f;
            if (_sampled < SampleSeconds)
            {
                return;
            }

            _sampleDone = true;
            var stats = FrameSampleStats.From(_wallMs, _gpuMs);
            settings.RecordWorldSample(settings.Preset, stats, AutoQualityCalibrator.DeviceKey);
            settings.Save();
            bool high = settings.Preset == QualityPreset.Medium && QualityCalibration.WorldAllowsHigh(stats);
            Debug.Log($"[AutoQuality] World sample on {settings.Preset}: {stats}"
                + (settings.Preset == QualityPreset.Medium ? (high ? " — room for High from the next start." : " — no room for High.") : "."));
            ResetSample();
        }

        private void ResetSample()
        {
            _wallMs.Clear();
            _gpuMs.Clear();
            _sampled = 0f;
        }
    }
}
