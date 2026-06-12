using System;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Graphics quality presets, including a Potato/Pi profile for weak machines and Pi-hosted servers.</summary>
    public enum QualityPreset { Potato, Low, Medium, High }

    /// <summary>
    /// Local, client-only settings (display, audio, input, comfort). These never affect the
    /// authoritative server rules (PvP, aliens, weapons stay server-decided). Persisted as JSON
    /// in <c>Application.persistentDataPath/client_settings.json</c>. See
    /// <c>docs/CLIENT_SHELL_AND_ASSETS.md</c>.
    /// </summary>
    [Serializable]
    public sealed class ClientSettings
    {
        // Graphics
        public QualityPreset Preset = QualityPreset.Medium;
        public bool Fullscreen = true;
        public int ViewDistanceChunks = 2;
        public float UiScale = 1f;

        // Audio (0..1)
        public float MasterVolume = 0.8f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.8f;
        public bool MenuAudio = true;

        // Controls
        public float MouseSensitivity = 2f;
        public bool InvertY = false;

        /// <summary>Language code that drives the localizer: "en" or "de".</summary>
        public string Language = "en";

        /// <summary>Last singleplayer world the player launched (pre-selected in the world picker).</summary>
        public string LastWorld = "singleplayer";

        /// <summary>The player's name — shown to other players and keying the server-side player state.</summary>
        public string PlayerName = "Pilot";

        /// <summary>Per-install secret backing name verification: sent with every join; the first join
        /// under a name claims it, later joins must match. Generated once on load, never shown in UI.</summary>
        public string PlayerToken = "";

        // Accessibility (flags wired now; visual effects applied when the render layer lands)
        public bool ReducedEffects = false;
        public bool LargeUi = false;

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

        /// <summary>Start in third-person (showing your own figure) instead of first-person.</summary>
        public bool ThirdPerson = false;

        /// <summary>Show the ship AI's (VEGA) advisor hints and story lines. The onboarding objective chip
        /// always shows until the tutorial is finished or skipped; this mutes the optional coaching.</summary>
        public bool VegaHints = true;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "client_settings.json");

        public static ClientSettings Load()
        {
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
            Screen.fullScreen = Fullscreen;
            AudioListener.volume = Mathf.Clamp01(MasterVolume); // master bus (M26)

            int levels = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (levels > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp((int)Preset, 0, levels - 1), applyExpensiveChanges: true);
            }

            // URP: one pipeline asset serves every quality level, so scale the expensive part — shadow reach —
            // by preset here (Potato/Pi: shadows off entirely; High: the full tuned distance).
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
            }
        }
    }
}
