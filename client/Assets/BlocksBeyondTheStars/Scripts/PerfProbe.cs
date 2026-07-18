// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Automated performance baseline capture (issue #353). When the player is launched with
    /// <c>-perfProbe</c>, this self-installs (same pattern as <see cref="ScreenshotDirector"/>), starts a
    /// fixed-seed singleplayer world and records frame-time / GC statistics over these phases:
    /// <list type="number">
    ///   <item><b>idle</b> — standing still after the spawn area has fully meshed (steady-state cost)</item>
    ///   <item><b>walk</b> — scripted straight-line traversal via <see cref="InputMap.ScriptedMove"/>, so
    ///   chunk streaming + meshing churn continuously (the historical stutter scenario)</item>
    ///   <item><b>dense</b> (only with <c>-perfDense</c>) — Extreme creature abundance at forced visual midnight,
    ///   so the glowing-entity point-light cost (#361) is exercised instead of the sparse baseline walk</item>
    /// </list>
    /// Results go to <c>&lt;out&gt;/perf_baseline_&lt;platform&gt;.json</c> plus a human-readable .txt and the
    /// log; the process exits when done, so a script can run this end-to-end. Flags: <c>-perfProbe</c>,
    /// <c>-perfOut &lt;dir&gt;</c>, <c>-seed &lt;n&gt;</c>, <c>-perfIdle &lt;sec&gt;</c>, <c>-perfWalk &lt;sec&gt;</c>,
    /// <c>-perfPreset &lt;name&gt;</c>, <c>-perfVd &lt;n&gt;</c>, and <c>-perfFeature "ssao=off|half|full,depth=off,
    /// smaa=off,scatter=off,shadowmap=2048,shadowdist=40"</c> (isolate one preset feature's cost — see #374).
    /// The numbers are a coarse CPU/GC baseline (wall-clock frame times), not a GPU profile — for the deep
    /// dive attach the Unity Profiler to a development build. When the preset is GPU-bound (the Medium cliff
    /// in #374), the wall-clock frame time still moves with each feature toggle, so a <c>-perfFeature</c>
    /// sweep at fixed preset/VD gives a usable first-order cost split without a GPU capture.
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        private const string WorldName = "PerfProbe";
        private const long DefaultSeed = 424242L;      // same reproducible world the marketing shots use
        private const float WorldLoadTimeout = 120f;
        private const float ChunkSettle = 12f;         // post-WorldReady settle so 'idle' measures steady state
        private const float HitchMs33 = 1000f / 30f;   // frame longer than a 30 FPS frame
        private const float HitchMs100 = 100f;         // a visible stall

        private long _seed = DefaultSeed;
        private string _outDir;
        private float _idleSeconds = 30f;
        private float _walkSeconds = 60f;
        private string _presetOverride;   // -perfPreset Potato|Low|Medium|High; null = keep the player's settings
        private int _vdOverride = -1;     // -perfVd 1..8; -1 = keep

        // -perfFeature "ssao=off,depth=off,smaa=off,scatter=off,shadowmap=2048,shadowdist=40": after the preset
        // is applied, force individual cost-bearing features off (or to a value) so a run isolates ONE feature's
        // frame-time contribution. This is how the Medium-preset cost split (#374) gets itemized: hold the preset
        // at Medium and toggle one feature per run. Null/empty = no per-feature override.
        private string _featureSpec;
        private string _featureTag;       // sanitized summary of what the override actually changed (for the filename)

        // -perfDense: adds a third "dense" phase (settlement/creature-pack-at-night stand-in for #361). The world
        // is created with Extreme creature abundance on a breathable planet so fauna auto-spawns in the 18–45 block
        // ring around the player; the phase then forces visual midnight (SetCaptureEnvironment) so the glowing
        // entities' point lights dominate a dark frame. Caveat: this pins the CLIENT visual clock only — the server
        // clock stays daytime, so strictly nocturnal glow species may be under-represented; cathemeral/passive
        // species and the raw entity-view density still populate the scene. A first dense probe, refine later.
        private bool _dense;
        private const float DenseSettle = 12f; // extra settle so the creature ring fills toward its cap before sampling
        private const string DensePlanet = "jungle"; // breathable + vegetated ⇒ dense fauna (incl. glowers)

        // The player's real settings file, snapshotted before any override — AppShell paths call
        // Settings.Save(), so an in-memory override could otherwise clobber the user's persisted settings.
        private byte[] _settingsBackup;
        private bool _settingsExisted;
        private bool _didBackup; // restore only when a backup was actually taken (i.e. an override ran)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            var args = Environment.GetCommandLineArgs();
            bool on = false;
            long seed = DefaultSeed;
            string outDir = null;
            float idle = 30f, walk = 60f;
            string preset = null;
            int vd = -1;
            string feature = null;
            bool dense = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-perfProbe", StringComparison.OrdinalIgnoreCase))
                {
                    on = true;
                }
                else if (string.Equals(a, "-perfPreset", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    preset = args[i + 1];
                }
                else if (string.Equals(a, "-perfVd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out var v))
                {
                    vd = Mathf.Clamp(v, 1, 8);
                }
                else if (string.Equals(a, "-perfOut", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outDir = args[i + 1];
                }
                else if (string.Equals(a, "-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && long.TryParse(args[i + 1], out var s))
                {
                    seed = s;
                }
                else if (string.Equals(a, "-perfIdle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && float.TryParse(args[i + 1], out var fi))
                {
                    idle = Mathf.Clamp(fi, 5f, 600f);
                }
                else if (string.Equals(a, "-perfWalk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && float.TryParse(args[i + 1], out var fw))
                {
                    walk = Mathf.Clamp(fw, 5f, 600f);
                }
                else if (string.Equals(a, "-perfFeature", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    feature = args[i + 1];
                }
                else if (string.Equals(a, "-perfDense", StringComparison.OrdinalIgnoreCase))
                {
                    dense = true;
                }
            }

            if (!on)
            {
                return;
            }

            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PerfProbe>();
            p._seed = seed;
            p._outDir = outDir;
            p._idleSeconds = idle;
            p._walkSeconds = walk;
            p._presetOverride = preset;
            p._vdOverride = vd;
            p._featureSpec = feature;
            p._dense = dense;
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            var shell = FindAnyObjectByType<AppShell>();
            if (shell == null)
            {
                Debug.LogError("[PerfProbe] No AppShell in the scene.");
                Quit(1);
                yield break;
            }

            yield return WaitForPhase(shell, ShellPhase.MainMenu, 30f);

            // Optional settings overrides for comparable runs. Snapshot the real settings file first and
            // restore it on exit — AppShell may persist settings mid-run, and the probe must never change
            // what the player actually configured.
            if (!string.IsNullOrEmpty(_presetOverride) || _vdOverride > 0)
            {
                BackupSettingsFile();
                if (!string.IsNullOrEmpty(_presetOverride)
                    && Enum.TryParse<QualityPreset>(_presetOverride, ignoreCase: true, out var qp))
                {
                    shell.Settings.Preset = qp;
                }

                if (_vdOverride > 0)
                {
                    shell.Settings.ViewDistanceChunks = _vdOverride;
                }

                shell.Settings.Apply();
            }

            // Dense scene (#361): Extreme creature abundance on a breathable, vegetated planet so a heavy
            // creature pack (incl. glowers) auto-spawns around the player. Baseline runs pass no options.
            WorldCreationOptions worldOptions = _dense
                ? new WorldCreationOptions { Creatures = 4 /* Extreme */, StartPlanetType = DensePlanet }
                : null;

            Debug.Log($"[PerfProbe] Starting world (seed {_seed}, preset {shell.Settings.Preset}, view distance {shell.Settings.ViewDistanceChunks}{(_dense ? ", dense scene" : "")}).");
            shell.StartSingleplayerWorld(WorldName, _seed, creativeUnlockAll: false, creativeAllShips: false, creativeKit: false, worldOptions);

            yield return WaitForPhase(shell, ShellPhase.InGame, WorldLoadTimeout);
            var boot = shell.CurrentBoot;
            if (boot == null || boot.Network == null)
            {
                Debug.LogError("[PerfProbe] World did not start (bundled server missing?).");
                Quit(1);
                yield break;
            }

            yield return WaitUntil(() => boot.WorldReady, WorldLoadTimeout);
            yield return new WaitForSecondsRealtime(ChunkSettle);

            // Per-feature overrides run AFTER the preset is applied and the gameplay camera exists (so the SSAO
            // renderer / SMAA choice on ActiveCameraData is live), isolating one feature's cost for #374.
            _featureTag = ApplyFeatureOverrides();
            if (_featureTag != null)
            {
                Debug.Log($"[PerfProbe] Feature overrides applied: {_featureTag}");
            }

            var phases = new List<PhaseResult>();

            // Phase 1: idle — steady-state cost with the spawn area fully streamed.
            PhaseResult r = null;
            yield return Sample("idle", _idleSeconds, x => r = x);
            phases.Add(r);

            // Phase 2: walk — scripted forward traversal; fresh chunks stream/mesh the whole time.
            InputMap.ScriptedMove = new Vector2(0f, 1f);
            yield return Sample("walk", _walkSeconds, x => r = x);
            InputMap.ScriptedMove = Vector2.zero;
            phases.Add(r);

            // Phase 3 (optional): dense — force visual midnight and stand among the Extreme creature pack so the
            // glowing entities' point lights dominate the frame (#361). Walking first left a filled ring; a short
            // extra settle lets it top up before sampling.
            if (_dense)
            {
                boot.SetCaptureEnvironment(0f); // midnight — see the DensePlanet/caveat note on the field above
                yield return new WaitForSecondsRealtime(DenseSettle);
                yield return Sample("dense", _idleSeconds, x => r = x);
                phases.Add(r);
            }

            WriteResults(shell, phases);
            RestoreSettingsFile();
            Quit(0);
        }

        private static string SettingsPath => Path.Combine(Application.persistentDataPath, "client_settings.json");

        private void BackupSettingsFile()
        {
            try
            {
                _settingsExisted = File.Exists(SettingsPath);
                _settingsBackup = _settingsExisted ? File.ReadAllBytes(SettingsPath) : null;
                _didBackup = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PerfProbe] Could not snapshot settings: {ex.Message}");
            }
        }

        private void RestoreSettingsFile()
        {
            if (!_didBackup)
            {
                return; // no override ran — never touch the player's settings file
            }

            try
            {
                if (_settingsExisted && _settingsBackup != null)
                {
                    File.WriteAllBytes(SettingsPath, _settingsBackup);
                }
                else if (!_settingsExisted && File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath); // fresh install: leave no trace of the override
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PerfProbe] Could not restore settings: {ex.Message}");
            }
        }

        private void OnApplicationQuit() => RestoreSettingsFile(); // belt & braces if the run is aborted

        /// <summary>Applies the <c>-perfFeature</c> overrides on top of the active preset, mutating the same
        /// runtime knobs <see cref="ClientSettings.Apply"/> owns (URP asset + the gameplay camera's URP data +
        /// the ground-scatter gate). Returns a short sanitized tag of what actually changed (for the output
        /// filename), or null when nothing was overridden. The process exits after the run, so this leaves no
        /// lasting state — no restore needed.</summary>
        private string ApplyFeatureOverrides()
        {
            if (string.IsNullOrEmpty(_featureSpec))
            {
                return null;
            }

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cam = ClientSettings.ActiveCameraData;
            var tags = new List<string>();

            foreach (var raw in _featureSpec.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split('=');
                string key = parts[0].Trim().ToLowerInvariant();
                string val = parts.Length > 1 ? parts[1].Trim() : "off";

                switch (key)
                {
                    case "ssao":
                        // Force a specific SSAO tier by renderer index so full/half/off can be compared in ONE
                        // thermal session (the SSAO cost split for #374): 0 = full-res, 2 = half-res, 1 = off.
                        if (cam != null)
                        {
                            switch (val)
                            {
                                case "full": cam.SetRenderer(0); tags.Add("ssaoFull"); break;
                                case "half": cam.SetRenderer(2); tags.Add("ssaoHalf"); break;
                                default: cam.SetRenderer(1); tags.Add("ssaoOff"); break;
                            }
                        }
                        break;
                    case "depth":
                    case "depthopaque":
                        // Drop the depth prepass + opaque colour copy (and tell the shaders they're gone, so
                        // water/fog fall back cleanly instead of sampling unbound textures).
                        if (urp != null) { urp.supportsCameraDepthTexture = false; urp.supportsCameraOpaqueTexture = false; }
                        Shader.SetGlobalFloat("_Sc_ScreenFx", 0f);
                        tags.Add("depthOff");
                        break;
                    case "smaa":
                        if (cam != null) { cam.antialiasing = AntialiasingMode.None; tags.Add("smaaOff"); }
                        break;
                    case "scatter":
                        GroundScatter.Enabled = false;
                        tags.Add("scatterOff");
                        break;
                    case "shadowmap":
                        if (urp != null && int.TryParse(val, out var sm) && sm > 0) { urp.mainLightShadowmapResolution = sm; tags.Add($"sm{sm}"); }
                        break;
                    case "shadowdist":
                        if (urp != null && float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sd) && sd >= 0f) { urp.shadowDistance = sd; tags.Add($"sd{sd:0}"); }
                        break;
                    default:
                        Debug.LogWarning($"[PerfProbe] Unknown -perfFeature token '{token}' ignored.");
                        break;
                }
            }

            return tags.Count > 0 ? string.Join("-", tags) : null;
        }

        [Serializable]
        private sealed class PhaseResult
        {
            public string name;
            public int frames;
            public float seconds;
            public float avgMs;
            public float p50Ms;
            public float p95Ms;
            public float p99Ms;
            public float maxMs;
            public int framesOver33Ms;
            public int framesOver100Ms;
            public int gcGen0;
            public int gcGen1;
            public int gcGen2;
            public long managedMemDeltaBytes;
        }

        [Serializable]
        private sealed class ProbeResult
        {
            public string capturedUtc;
            public string appVersion;
            public string unity;
            public string platform;
            public string qualityPreset;
            public int viewDistanceChunks;
            public long seed;
            public string device;
            public string featureOverride; // null unless -perfFeature changed something (the itemization run)
            public PhaseResult[] phases;
        }

        private IEnumerator Sample(string name, float seconds, Action<PhaseResult> done)
        {
            Debug.Log($"[PerfProbe] Phase '{name}' — sampling {seconds:0}s...");
            var samples = new List<float>(Mathf.CeilToInt(seconds) * 300); // generous: fits 300 FPS without regrowth
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long mem = GC.GetTotalMemory(false);

            float t = 0f;
            yield return null; // don't count the setup frame
            while (t < seconds)
            {
                float dt = Time.unscaledDeltaTime;
                samples.Add(dt * 1000f);
                t += dt;
                yield return null;
            }

            var r = new PhaseResult
            {
                name = name,
                frames = samples.Count,
                seconds = t,
                gcGen0 = GC.CollectionCount(0) - gc0,
                gcGen1 = GC.CollectionCount(1) - gc1,
                gcGen2 = GC.CollectionCount(2) - gc2,
                managedMemDeltaBytes = GC.GetTotalMemory(false) - mem,
            };

            float sum = 0f, max = 0f;
            foreach (float ms in samples)
            {
                sum += ms;
                if (ms > max) max = ms;
                if (ms > HitchMs33) r.framesOver33Ms++;
                if (ms > HitchMs100) r.framesOver100Ms++;
            }

            samples.Sort();
            r.avgMs = samples.Count > 0 ? sum / samples.Count : 0f;
            r.p50Ms = Percentile(samples, 0.50f);
            r.p95Ms = Percentile(samples, 0.95f);
            r.p99Ms = Percentile(samples, 0.99f);
            r.maxMs = max;
            done(r);
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }

        private void WriteResults(AppShell shell, List<PhaseResult> phases)
        {
            var result = new ProbeResult
            {
                capturedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                appVersion = Application.version,
                unity = Application.unityVersion,
                platform = Application.platform.ToString(),
                qualityPreset = shell.Settings.Preset.ToString(),
                viewDistanceChunks = shell.Settings.ViewDistanceChunks,
                seed = _seed,
                device = $"{SystemInfo.processorType} / {SystemInfo.graphicsDeviceName} / {SystemInfo.systemMemorySize} MB",
                featureOverride = _featureTag,
                phases = phases.ToArray(),
            };

            string dir = !string.IsNullOrEmpty(_outDir) ? _outDir : Path.Combine(Application.persistentDataPath, "perf");
            Directory.CreateDirectory(dir);
            string featureSuffix = string.IsNullOrEmpty(_featureTag) ? "" : $"_{_featureTag}";
            string denseSuffix = _dense ? "_dense" : "";
            string baseName = $"perf_baseline_{Application.platform}_{result.qualityPreset}_vd{result.viewDistanceChunks}{denseSuffix}{featureSuffix}";
            string jsonPath = Path.Combine(dir, baseName + ".json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(result, prettyPrint: true));

            var txt = new StringBuilder();
            txt.AppendLine($"Perf baseline — {result.capturedUtc} UTC — v{result.appVersion} — {result.platform}");
            txt.AppendLine($"Preset {result.qualityPreset}, view distance {result.viewDistanceChunks}, seed {result.seed}");
            if (!string.IsNullOrEmpty(result.featureOverride))
            {
                txt.AppendLine($"Feature override: {result.featureOverride}");
            }
            txt.AppendLine(result.device);
            foreach (var ph in result.phases)
            {
                txt.AppendLine($"[{ph.name}] {ph.frames} frames / {ph.seconds:0.0}s — avg {ph.avgMs:0.00} ms ({1000f / Mathf.Max(0.001f, ph.avgMs):0} FPS), "
                             + $"p50 {ph.p50Ms:0.00}, p95 {ph.p95Ms:0.00}, p99 {ph.p99Ms:0.00}, max {ph.maxMs:0.0} ms; "
                             + $">33ms: {ph.framesOver33Ms}, >100ms: {ph.framesOver100Ms}; "
                             + $"GC {ph.gcGen0}/{ph.gcGen1}/{ph.gcGen2}, managed Δ {ph.managedMemDeltaBytes / (1024f * 1024f):0.0} MB");
            }

            string txtPath = Path.Combine(dir, baseName + ".txt");
            File.WriteAllText(txtPath, txt.ToString());
            Debug.Log($"[PerfProbe] Results written to {jsonPath}\n{txt}");
        }

        private static IEnumerator WaitForPhase(AppShell shell, ShellPhase phase, float timeout)
        {
            float t = 0f;
            while (shell.Phase != phase && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
