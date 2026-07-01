// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One minigame's local personal best, stored as a flat pair so Unity's JsonUtility can persist
    /// it (it can't serialize a Dictionary). Highscores are LOCAL and per-player — there is no leaderboard.</summary>
    [Serializable]
    public sealed class MinigameScore
    {
        public string Key = "";
        public int Best;
    }

    /// <summary>One remapped control, stored as a flat (action-name, KeyCode-name) pair so Unity's JsonUtility
    /// can persist the bindings as a list (it can't serialize a Dictionary) — mirrors <see cref="MinigameScore"/>.</summary>
    [Serializable]
    public sealed class KeyBinding
    {
        public string Action = "";
        public string Key = ""; // a UnityEngine.KeyCode name, e.g. "E"
    }

    /// <summary>Graphics quality presets, including a Potato profile for weak / low-power machines.</summary>
    public enum QualityPreset { Potato, Low, Medium, High }

    /// <summary>
    /// Which background-music source the player prefers. <see cref="Synth"/> = the original code-synth
    /// ambient pads (the short bundled <c>music_*</c> loops with synthesized fallbacks); <see cref="Tracks"/>
    /// = the granular AI-composed track library under <c>Resources/music</c>. SFX/ambience are unaffected.
    /// </summary>
    public enum MusicMode { Synth, Tracks }

    /// <summary>
    /// How the standalone window is presented. <see cref="Windowed"/> = a normal resizable, draggable
    /// window (can be moved to another monitor and maximized via the OS title bar); <see cref="Borderless"/>
    /// = borderless fullscreen filling the monitor the window currently sits on; <see cref="Exclusive"/> =
    /// true exclusive fullscreen at the display's native resolution. Maps to <see cref="FullScreenMode"/>.
    /// </summary>
    public enum WindowMode { Windowed, Borderless, Exclusive }

    /// <summary>
    /// Local, client-only settings (display, audio, input, comfort). These never affect the
    /// authoritative server rules (PvP, aliens, weapons stay server-decided). Persisted as JSON
    /// in <c>Application.persistentDataPath/client_settings.json</c>. See
    /// <c>docs/developer/CLIENT_SHELL_AND_ASSETS.md</c>.
    /// </summary>
    [Serializable]
    public sealed class ClientSettings
    {
        // Graphics
        public QualityPreset Preset = QualityPreset.Medium;

        /// <summary>How the window is presented (windowed / borderless / exclusive). Default borderless so the
        /// game opens fullscreen on whichever monitor it launches on; switch to Windowed (in the settings menu)
        /// for a draggable, maximizable window, or to Exclusive for true exclusive fullscreen.</summary>
        public WindowMode Window = WindowMode.Borderless;

        /// <summary>Windowed-mode size, persisted so toggling back from fullscreen restores it. Default fits a
        /// 1080p display with room for the title bar; clamped to the display in <see cref="Apply"/>.</summary>
        public int WindowedWidth = 1600;
        public int WindowedHeight = 900;

        // Default render distance in 16-block chunks (slider range 1–8 in the settings menu). Raised from the old
        // default of 2 (≈32 m — a near, foggy horizon) to 4 (≈64 m) so the world reads farther out of the box; the
        // per-planet/weather haze still scales off this (Sky.ApplyFog), so denser-atmosphere worlds stay hazier.
        // Singleplayer forwards this to the bundled server as the streaming radius (AppShell → --view-distance),
        // and the server now reclaims out-of-range chunks (far-chunk sweep), so the larger radius stays bounded.
        public int ViewDistanceChunks = 4;
        public float UiScale = 1f;

        // Frame pacing. Exposed as its own switch instead of being baked into the quality preset: with VSync
        // on, the frame rate syncs to the display and tearing is gone, but on some setups — notably the
        // Windows client run through Proton/Wine on Linux — a GPU that just misses the refresh gets locked
        // to a hard 30 fps and the game feels sluggish. Turning VSync off lets those players run uncapped (or
        // at a chosen cap) for smoother frame times. Applied in Apply() AFTER SetQualityLevel, which would
        // otherwise stamp the preset's own vSyncCount.
        /// <summary>Sync frames to the display refresh (no tearing). Off lets the frame rate run free, capped
        /// only by <see cref="FrameRateCap"/> — the recommended setting for the Linux/Proton client.</summary>
        public bool VSync = true;

        /// <summary>Frame-rate cap in fps applied when <see cref="VSync"/> is off; 0 = unlimited. One of the
        /// values in <see cref="UiSettings"/>'s cap cycle (30/60/72/90/120/144/240).</summary>
        public int FrameRateCap = 0;

        // Look effects (the "professional / sci-fi look" layer). Each is also preset-gated at runtime — these
        // toggles only matter from Medium upward; Potato/Low force the expensive ones off regardless.
        /// <summary>Subpixel-morphological anti-aliasing (SMAA) on top of MSAA — smooths the shader/specular
        /// edges MSAA can't (voxel highlights, normal-map relief). A post-pass, so it needs camera post-processing
        /// on; gated to Medium+ (Potato/Low skip it for the frame-time budget). Applied in <see cref="ApplyCameraLook"/>.</summary>
        public bool Smaa = true;
        /// <summary>Global scene brightness, applied as a post-exposure lift on the colour grade so it affects every
        /// world uniformly (and is tunable per display). 1.0 = neutral; the default sits a touch above neutral so the
        /// ACES-tonemapped scene isn't too dark. Driven into <see cref="UrpScenePost"/>.</summary>
        public float Brightness = 1.15f;
        /// <summary>Screen-space lens flare on the sun + bright emitters (cheap, very sci-fi).</summary>
        public bool LensFlare = true;
        /// <summary>Subtle camera motion blur while flying the ship / driving the speeder (High+ only).</summary>
        public bool MotionBlur = true;
        /// <summary>Volumetric fog + god-rays (light shafts). Needs the depth texture (Medium+).</summary>
        public bool VolumetricFog = true;
        /// <summary>Screen-space reflections on water / glossy hull / metal. Needs depth + opaque (High+).</summary>
        public bool Reflections = true;

        // Audio (0..1)
        public float MasterVolume = 0.8f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.8f;
        public bool MenuAudio = true;

        /// <summary>Background-music source: the AI-composed track library (default) or the original
        /// code-synth ambient pads. SFX/ambience volumes are independent (<see cref="SfxVolume"/>).</summary>
        public MusicMode MusicMode = MusicMode.Tracks;

        // Controls
        public float MouseSensitivity = 2f;
        public bool InvertY = false;

        // Voice chat. Shipped on by default; this master switch turns the whole feature off (no capture, no
        // playback). The server must also have voice enabled, and you still need a radio. Push-to-talk by
        // default — hold the key to transmit. VoiceInputEnabled keeps playback while never transmitting.
        public bool VoiceEnabled = true;
        public float VoiceVolume = 1f;
        public bool VoiceInputEnabled = true;
        /// <summary>Push-to-talk key, stored as a <see cref="UnityEngine.KeyCode"/> name (default "V").</summary>
        public string PushToTalkKey = "V";

        /// <summary>Remapped controls (Stream C): per-<see cref="InputAction"/> key overrides resolved by
        /// <see cref="InputMap"/>. An action absent here uses its built-in default, so an empty list = stock
        /// controls. Stored as a flat list so JsonUtility can persist it.</summary>
        public List<KeyBinding> KeyBindings = new List<KeyBinding>();

        /// <summary>Remapped GAMEPAD buttons: per-<see cref="InputAction"/> pad-button overrides (stored as
        /// <see cref="UnityEngine.KeyCode"/> names like "JoystickButton2"), resolved by
        /// <see cref="GamepadInputSource"/>. Mirrors <see cref="KeyBindings"/>: absent = the built-in pad
        /// default, empty list = stock pad controls.</summary>
        public List<KeyBinding> PadBindings = new List<KeyBinding>();
        /// <summary>Optional named microphone device ("" = the system default).</summary>
        public string MicrophoneDevice = "";
        /// <summary>Player names the local player has muted (voice playback suppressed). Runtime-toggleable.</summary>
        public System.Collections.Generic.List<string> MutedVoicePlayers = new System.Collections.Generic.List<string>();

        /// <summary>Language code that drives the localizer: "en" or "de".</summary>
        public string Language = "en";

        /// <summary>Last singleplayer world the player launched (pre-selected in the world picker).</summary>
        public string LastWorld = "singleplayer";

        /// <summary>Optional Velopack auto-update feed URL — the self-hosting server's update endpoint
        /// (e.g. <c>http://192.168.1.50:31416/updates</c>, shown on that server's <c>/portal</c> page).
        /// Empty = no in-app updates. Only effective in an installed build (see <see cref="ClientUpdater"/>).</summary>
        public string UpdateFeedUrl = "";

        /// <summary>The player's name — shown to other players and keying the server-side player state.</summary>
        public string PlayerName = "Pilot";

        /// <summary>Per-install secret backing name verification: sent with every join; the first join
        /// under a name claims it, later joins must match. Generated once on load, never shown in UI.</summary>
        public string PlayerToken = "";

        // Accessibility (flags wired now; visual effects applied when the render layer lands)
        public bool ReducedEffects = false;
        public bool LargeUi = false;

        /// <summary>Camera motion comfort toggle: head bob, the moving FOV kick and impact camera
        /// shake. Off = a steady camera for motion-sensitive players; sounds are unaffected.</summary>
        public bool CameraMotion = true;

        /// <summary>Holographic visor HUD styling (curvature + chromatic fringe + scanlines + glow). On = the
        /// stylised visor look; off = a clean, flat HUD overlay (better readability). Default on but subtle.</summary>
        public bool VisorEffects = true;

        // Avatar appearance (M23b). Per-part colours; later armor overrides the matching part.
        public Color SkinColor = new Color(0.85f, 0.68f, 0.55f);
        public Color TorsoColor = new Color(0.20f, 0.45f, 0.80f);
        public Color ArmColor = new Color(0.20f, 0.45f, 0.80f);
        public Color LegColor = new Color(0.25f, 0.25f, 0.32f);

        /// <summary>Ship hull colour (item 32) — tints the player's ship. Default = the steel tint the hull
        /// used before hull colours existed, so an unchanged ship looks the same.</summary>
        public Color HullColor = new Color(0.82f, 0.84f, 0.88f);

        /// <summary>The player's custom pixel face drawn in the in-game face editor, as a 16×16 palette-index
        /// string (see <see cref="FacePalette"/>); empty = the default procedural face. Shown on this player's
        /// avatar and sent to the server so other players see it. The server also persists it (the face follows
        /// the player), but this local copy is the source re-sent on each join/edit.</summary>
        public string FacePixels = "";

        /// <summary>Start in third-person (showing your own figure) instead of first-person.</summary>
        public bool ThirdPerson = false;

        /// <summary>Show the ship AI's (VEGA) advisor hints and story lines. The onboarding objective chip
        /// always shows until the tutorial is finished or skipped; this mutes the optional coaching.</summary>
        public bool VegaHints = true;

        // Comfort / wellbeing (playtime). Purely client-side: the session timer counts real wall-clock from
        // the moment you enter a world; the reminder is VEGA gently suggesting a break (a real-world nudge, not
        // an in-fiction event). Both default on but unobtrusive.
        /// <summary>Show a small "session / total playtime" readout in the in-game HUD.</summary>
        public bool ShowSessionTime = true;

        /// <summary>Let VEGA remind you to take a break after a long unbroken session, repeating each interval.</summary>
        public bool PlaytimeReminder = true;

        /// <summary>Minutes of continuous session play between break reminders (also the first reminder's delay).</summary>
        public int ReminderMinutes = 60;

        /// <summary>Auto-stow loose materials/components into the ship's cargo hold the moment you board (tools
        /// and weapons stay on you). Off by default so boarding never silently empties your inventory; opt in to
        /// keep your personal pack clear for exploring on foot.</summary>
        public bool AutoStowOnBoard = false;

        /// <summary>Local personal-best scores for the bundled arcade minigames, keyed by game key. Local only —
        /// no server leaderboard. Stored as a flat list so JsonUtility can persist it.</summary>
        public List<MinigameScore> MinigameScores = new List<MinigameScore>();

        /// <summary>The player's best recorded score for a minigame (0 if never played).</summary>
        public int GetMinigameBest(string key)
        {
            if (string.IsNullOrEmpty(key) || MinigameScores == null) return 0;
            for (int i = 0; i < MinigameScores.Count; i++)
            {
                if (MinigameScores[i].Key == key) return MinigameScores[i].Best;
            }

            return 0;
        }

        /// <summary>Records a minigame score, keeping only the personal best. Returns true if it was a new best
        /// (so the caller can save + celebrate).</summary>
        public bool RecordMinigameScore(string key, int score)
        {
            if (string.IsNullOrEmpty(key) || score <= 0) return false;
            MinigameScores ??= new List<MinigameScore>();
            for (int i = 0; i < MinigameScores.Count; i++)
            {
                if (MinigameScores[i].Key == key)
                {
                    if (score <= MinigameScores[i].Best) return false;
                    MinigameScores[i].Best = score;
                    return true;
                }
            }

            MinigameScores.Add(new MinigameScore { Key = key, Best = score });
            return true;
        }

        /// <summary>The bound KeyCode NAME for an input action (empty = the action uses its default key). String-
        /// keyed so this stays decoupled from the <see cref="InputAction"/> enum; <see cref="InputMap"/> parses it.</summary>
        public string BoundKeyName(string action)
        {
            if (string.IsNullOrEmpty(action) || KeyBindings == null) return "";
            for (int i = 0; i < KeyBindings.Count; i++)
            {
                if (KeyBindings[i].Action == action) return KeyBindings[i].Key;
            }

            return "";
        }

        /// <summary>Sets (or, when <paramref name="keyName"/> is empty, clears) the binding for an action. Returns
        /// true if anything changed (so the caller can Save).</summary>
        public bool SetBoundKey(string action, string keyName)
        {
            if (string.IsNullOrEmpty(action)) return false;
            KeyBindings ??= new List<KeyBinding>();
            for (int i = 0; i < KeyBindings.Count; i++)
            {
                if (KeyBindings[i].Action == action)
                {
                    if (KeyBindings[i].Key == keyName) return false;
                    if (string.IsNullOrEmpty(keyName)) KeyBindings.RemoveAt(i);
                    else KeyBindings[i].Key = keyName;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(keyName)) return false;
            KeyBindings.Add(new KeyBinding { Action = action, Key = keyName });
            return true;
        }

        /// <summary>The bound gamepad-button NAME for an input action (empty = the pad default). Mirrors
        /// <see cref="BoundKeyName"/> for the pad binding list.</summary>
        public string BoundPadName(string action)
        {
            if (string.IsNullOrEmpty(action) || PadBindings == null) return "";
            for (int i = 0; i < PadBindings.Count; i++)
            {
                if (PadBindings[i].Action == action) return PadBindings[i].Key;
            }

            return "";
        }

        /// <summary>Sets (or, when <paramref name="keyName"/> is empty, clears) the gamepad binding for an
        /// action. Returns true if anything changed. Mirrors <see cref="SetBoundKey"/>.</summary>
        public bool SetBoundPad(string action, string keyName)
        {
            if (string.IsNullOrEmpty(action)) return false;
            PadBindings ??= new List<KeyBinding>();
            for (int i = 0; i < PadBindings.Count; i++)
            {
                if (PadBindings[i].Action == action)
                {
                    if (PadBindings[i].Key == keyName) return false;
                    if (string.IsNullOrEmpty(keyName)) PadBindings.RemoveAt(i);
                    else PadBindings[i].Key = keyName;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(keyName)) return false;
            PadBindings.Add(new KeyBinding { Action = action, Key = keyName });
            return true;
        }

        private static string FilePath => Path.Combine(Application.persistentDataPath, "client_settings.json");

        public static ClientSettings Load()
        {
            // Capture before touching the file: a genuine first run is the only time we auto-pick the
            // language from the OS. Returning players keep whatever they chose (even an explicit "en").
            bool freshInstall = !File.Exists(FilePath);

            ClientSettings settings = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    settings = JsonUtility.FromJson<ClientSettings>(File.ReadAllText(FilePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read client settings, using defaults: {e.Message}");
            }

            settings ??= new ClientSettings();
            if (freshInstall)
            {
                // German Windows starts in German; everything else falls back to English. The chosen value
                // is persisted by the Save below, so the pre-engine launcher splash picks it up next launch.
                settings.Language = Application.systemLanguage == SystemLanguage.German ? "de" : "en";

                // Tablets and the browser build are GPU-weak next to a desktop, and the scene is heavy (custom
                // URP, SSAO, SMAA, PBR). Start those on a Lite preset so the first run is playable; the player
                // can still raise it in Settings. Only on a genuine first run — a returning player keeps theirs.
                if (Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    settings.Preset = QualityPreset.Low;
                }
            }

            if (string.IsNullOrEmpty(settings.PlayerToken))
            {
                settings.PlayerToken = Guid.NewGuid().ToString("N");
                settings.Save(); // persist the claim secret right away so it survives a crash before the next save
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save client settings: {e.Message}");
            }
        }

        /// <summary>Applies engine-owned settings. View distance feeds <see cref="GameBootstrap"/>; audio buses land later.</summary>
        public void Apply()
        {
            ApplyWindowMode();
            AudioListener.volume = Mathf.Clamp01(MasterVolume); // master bus (M26)
            UiKit.ReducedMotion = ReducedEffects; // UI transitions snap instantly for reduced-effects users

            int levels = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (levels > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp((int)Preset, 0, levels - 1), applyExpensiveChanges: true);
            }

            // Frame pacing — the player's own switch, applied AFTER SetQualityLevel (which stamps the preset's
            // baked vSyncCount). VSync on = sync to the display; off = uncapped unless FrameRateCap limits it.
            // Application.targetFrameRate only takes effect when vSyncCount == 0, so clear it (−1) under VSync.
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = (!VSync && FrameRateCap > 0) ? FrameRateCap : -1;

            // URP: one pipeline asset serves every quality level, so scale the expensive part — shadow reach —
            // by preset here (Potato: shadows off entirely; High: the full tuned distance).
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = Preset switch
                {
                    QualityPreset.Potato => 0f,
                    QualityPreset.Low => 40f,
                    QualityPreset.Medium => 70f,
                    _ => 90f,
                };

                // Depth + opaque copies feed the screen-space effects (volumetric fog, SSR, water refraction).
                // They cost a prepass + a colour copy, so the two weakest presets turn them off entirely —
                // which also disables every dependent effect for free (the features early-out without the textures).
                bool wantsScreenSpace = Preset >= QualityPreset.Medium;
                urp.supportsCameraDepthTexture = wantsScreenSpace;
                urp.supportsCameraOpaqueTexture = wantsScreenSpace;

                // Tell the shaders whether the depth/opaque textures exist this preset. The water shader uses it to
                // fall back to the simple alpha look on Potato/Low (otherwise its depth colour / refraction / SSR
                // would sample unbound textures and render wrong/black).
                UnityEngine.Shader.SetGlobalFloat("_Sc_ScreenFx", wantsScreenSpace ? 1f : 0f);
            }

            ApplyCameraLook();
        }

        /// <summary>The active gameplay camera's URP data, set by <see cref="WorldRig"/> so graphics changes made in
        /// the pause menu push live (SMAA + the SSAO-renderer choice). Null in the main menu (no world camera yet);
        /// the camera reads the settings on creation, so menu changes still apply on entry. Static (not serialized).</summary>
        public static UniversalAdditionalCameraData ActiveCameraData;

        /// <summary>Pushes the per-camera look settings to the gameplay camera: post-processing on (the global
        /// Volume — bloom/tonemap/grade — and SMAA both need it), SMAA from <see cref="Smaa"/> (Medium+), and the
        /// renderer choice — index 0 carries SSAO, index 1 (Potato/Low) drops it for the frame-time budget.</summary>
        public void ApplyCameraLook()
        {
            var cd = ActiveCameraData;
            if (cd == null)
            {
                return;
            }

            cd.renderPostProcessing = true;
            cd.SetRenderer(Preset >= QualityPreset.Medium ? 0 : 1);

            bool smaa = Smaa && Preset >= QualityPreset.Medium;
            cd.antialiasing = smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            cd.antialiasingQuality = AntialiasingQuality.High;
        }

        /// <summary>Applies the chosen <see cref="Window"/> mode. Windowed uses the persisted
        /// <see cref="WindowedWidth"/>/<see cref="WindowedHeight"/> (clamped to the display) so the window has a
        /// title bar and can be dragged to another monitor and maximized; Borderless/Exclusive fill the current
        /// display at its native resolution. The standalone window must be resizable (Player Settings) for the
        /// OS maximize/resize affordances to appear in Windowed mode.</summary>
        private void ApplyWindowMode()
        {
            var native = Screen.currentResolution;
            switch (Window)
            {
                case WindowMode.Borderless:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
                    break;
                case WindowMode.Exclusive:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.ExclusiveFullScreen);
                    break;
                default: // Windowed
                    int w = Mathf.Clamp(WindowedWidth, 640, Mathf.Max(640, native.width));
                    int h = Mathf.Clamp(WindowedHeight, 480, Mathf.Max(480, native.height));
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    break;
            }
        }
    }
}
