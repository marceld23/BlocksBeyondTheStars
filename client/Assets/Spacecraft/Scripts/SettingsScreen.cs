using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Client settings screen (`anf_textures.md` §6): a small MVP subset of graphics, audio,
    /// controls and language, bound to the local <see cref="ClientSettings"/>. These are local
    /// only and never change the authoritative server rules. Saved on "Back".
    /// </summary>
    public sealed class SettingsScreen
    {
        private readonly AppShell _shell;

        public SettingsScreen(AppShell shell) => _shell = shell;

        public void Draw()
        {
            var s = _shell.Settings;
            float x = 40, y = 40;

            GUI.Label(new Rect(x, y, 400, 30), _shell.L("ui.settings.title"));
            y += 38;

            // --- Graphics ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.graphics"));
            y += 26;
            if (GUI.Button(new Rect(x, y, 300, 28), $"{_shell.L("ui.settings.preset")}: {s.Preset}"))
            {
                s.Preset = (QualityPreset)(((int)s.Preset + 1) % 4);
            }

            y += 34;
            s.Fullscreen = GUI.Toggle(new Rect(x, y, 300, 22), s.Fullscreen, _shell.L("ui.settings.fullscreen"));
            y += 28;
            GUI.Label(new Rect(x, y, 300, 20), $"{_shell.L("ui.settings.view_distance")}: {s.ViewDistanceChunks}");
            y += 22;
            s.ViewDistanceChunks = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, y, 300, 18), s.ViewDistanceChunks, 1, 8));
            y += 32;

            // --- Audio ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.audio"));
            y += 26;
            s.MasterVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.master_volume"), s.MasterVolume);
            s.MusicVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.music_volume"), s.MusicVolume);
            s.SfxVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.sfx_volume"), s.SfxVolume);

            // --- Controls ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.controls"));
            y += 26;
            GUI.Label(new Rect(x, y, 300, 20), $"{_shell.L("ui.settings.mouse_sensitivity")}: {s.MouseSensitivity:0.0}");
            y += 22;
            s.MouseSensitivity = GUI.HorizontalSlider(new Rect(x, y, 300, 18), s.MouseSensitivity, 0.5f, 6f);
            y += 28;
            s.InvertY = GUI.Toggle(new Rect(x, y, 300, 22), s.InvertY, _shell.L("ui.settings.invert_y"));
            y += 34;

            // --- Language + back ---
            if (GUI.Button(new Rect(x, y, 145, 28), $"{_shell.L("ui.settings.language")}: {(s.Language == "de" ? "DE" : "EN")}"))
            {
                s.Language = s.Language == "de" ? "en" : "de";
                _shell.LoadLocalizer(); // live-update the labels
            }

            if (GUI.Button(new Rect(x + 155, y, 145, 28), _shell.L("ui.menu.back")))
            {
                _shell.CloseSettings();
            }
        }

        private float VolumeSlider(float x, ref float y, string label, float value)
        {
            GUI.Label(new Rect(x, y, 300, 20), $"{label}: {Mathf.RoundToInt(value * 100)}%");
            y += 22;
            value = GUI.HorizontalSlider(new Rect(x, y, 300, 18), value, 0f, 1f);
            y += 28;
            return value;
        }
    }
}
