using System;
using System.IO;
using UnityEngine;

namespace Spacecraft.Client
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

        // Accessibility (flags wired now; visual effects applied when the render layer lands)
        public bool ReducedEffects = false;
        public bool LargeUi = false;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "client_settings.json");

        public static ClientSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonUtility.FromJson<ClientSettings>(File.ReadAllText(FilePath)) ?? new ClientSettings();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read client settings, using defaults: {e.Message}");
            }

            return new ClientSettings();
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
            AudioListener.volume = Mathf.Clamp01(MasterVolume);

            int levels = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (levels > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp((int)Preset, 0, levels - 1), applyExpensiveChanges: true);
            }
        }
    }
}
