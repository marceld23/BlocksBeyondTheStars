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

        private GUIStyle _titleStyle, _headStyle;

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = UiKit.Cyan } };
            _headStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = UiKit.Cyan } };
        }

        private static void Frame(Rect r)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.12f, 0.24f, 0.86f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = UiKit.Cyan;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        public void Draw()
        {
            _shell.DrawBackground();
            EnsureStyles();
            var s = _shell.Settings;
            // Centre the settings column horizontally (was pinned top-left).
            float x = Mathf.Max(20f, Screen.width / 2f - 150f);
            float y = Mathf.Max(20f, Screen.height / 2f - 230f);

            Frame(new Rect(x - 28, y - 22, 356, 690));

            GUI.Label(new Rect(x, y, 400, 30), _shell.L("ui.settings.title"), _titleStyle);
            y += 38;

            // --- Graphics ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.graphics"), _headStyle);
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
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.audio"), _headStyle);
            y += 26;
            s.MasterVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.master_volume"), s.MasterVolume);
            s.MusicVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.music_volume"), s.MusicVolume);
            s.SfxVolume = VolumeSlider(x, ref y, _shell.L("ui.settings.sfx_volume"), s.SfxVolume);

            // --- Controls ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.controls"), _headStyle);
            y += 26;
            GUI.Label(new Rect(x, y, 300, 20), $"{_shell.L("ui.settings.mouse_sensitivity")}: {s.MouseSensitivity:0.0}");
            y += 22;
            s.MouseSensitivity = GUI.HorizontalSlider(new Rect(x, y, 300, 18), s.MouseSensitivity, 0.5f, 6f);
            y += 28;
            s.InvertY = GUI.Toggle(new Rect(x, y, 300, 22), s.InvertY, _shell.L("ui.settings.invert_y"));
            y += 34;

            // --- Character appearance (M23b) ---
            GUI.Label(new Rect(x, y, 400, 20), _shell.L("ui.settings.character"), _headStyle);
            y += 26;
            s.SkinColor = ColorRow(x, ref y, _shell.L("ui.settings.skin"), s.SkinColor);
            s.TorsoColor = ColorRow(x, ref y, _shell.L("ui.settings.torso"), s.TorsoColor);
            s.ArmColor = ColorRow(x, ref y, _shell.L("ui.settings.arms"), s.ArmColor);
            s.LegColor = ColorRow(x, ref y, _shell.L("ui.settings.legs"), s.LegColor);
            y += 6;

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

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f),
        };

        /// <summary>A label + "next colour" button + a swatch; clicking cycles through the palette.</summary>
        private Color ColorRow(float x, ref float y, string label, Color current)
        {
            GUI.Label(new Rect(x, y, 120, 22), label);
            if (GUI.Button(new Rect(x + 120, y, 120, 22), _shell.L("ui.settings.next_color")))
            {
                current = NextColor(current);
            }

            var prev = GUI.color;
            GUI.color = current;
            GUI.DrawTexture(new Rect(x + 250, y, 40, 22), Texture2D.whiteTexture);
            GUI.color = prev;

            y += 28;
            return current;
        }

        private static Color NextColor(Color current)
        {
            int idx = -1;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Mathf.Approximately(Palette[i].r, current.r) &&
                    Mathf.Approximately(Palette[i].g, current.g) &&
                    Mathf.Approximately(Palette[i].b, current.b))
                {
                    idx = i;
                    break;
                }
            }

            return Palette[(idx + 1) % Palette.Length];
        }
    }
}
