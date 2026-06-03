using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// The settings screen in the new uGUI design (replacing the legacy IMGUI settings screen):
    /// graphics / audio / controls / appearance / language, bound to the local <see cref="ClientSettings"/>.
    /// Built in code on a DPI-independent canvas (UiKit) and rebuilt on each change. Numeric options use
    /// −/+ steppers with a fill bar (robust, no Slider component); booleans toggle; preset/language cycle.
    /// </summary>
    public sealed class UiSettings : MonoBehaviour
    {
        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f),
        };

        private AppShell _shell;
        private Transform _root;
        private float _px, _pw, _x, _rowW;

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SettingsUI");
            var ui = canvas.gameObject.AddComponent<UiSettings>();
            ui._shell = shell;
            ui._root = canvas.transform;
            ui.Rebuild();
            return canvas.gameObject;
        }

        private ClientSettings S => _shell.Settings;
        private string L(string k) => _shell.L(k);

        private void Rebuild()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }

            UiKit.AddImage(_root, 0, 0, 1920, 1080, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.55f));
            _px = 660f; _pw = 600f;
            UiKit.AddPanel(_root, _px, 80, _pw, 920, UiKit.Panel);
            _x = _px + 30f; _rowW = _pw - 60f;

            float y = 104f;
            UiKit.AddLogo(_root, _x, y, 420, 40, L("ui.settings.title"), 26);
            y += 64f;

            Head(ref y, L("ui.settings.graphics"));
            Cycle(ref y, L("ui.settings.preset"), S.Preset.ToString(), () => { S.Preset = (QualityPreset)(((int)S.Preset + 1) % 4); Rebuild(); });
            Toggle(ref y, L("ui.settings.fullscreen"), S.Fullscreen, () => { S.Fullscreen = !S.Fullscreen; Rebuild(); });
            Stepper(ref y, L("ui.settings.view_distance"), (S.ViewDistanceChunks - 1) / 7f, 1, 8,
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks - 1, 1, 8); Rebuild(); },
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks + 1, 1, 8); Rebuild(); },
                S.ViewDistanceChunks.ToString());

            Head(ref y, L("ui.settings.audio"));
            VolRow(ref y, L("ui.settings.master_volume"), () => S.MasterVolume, v => S.MasterVolume = v);
            VolRow(ref y, L("ui.settings.music_volume"), () => S.MusicVolume, v => S.MusicVolume = v);
            VolRow(ref y, L("ui.settings.sfx_volume"), () => S.SfxVolume, v => S.SfxVolume = v);

            Head(ref y, L("ui.settings.controls"));
            Stepper(ref y, L("ui.settings.mouse_sensitivity"), (S.MouseSensitivity - 0.5f) / 5.5f, 0, 1,
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity - 0.5f, 0.5f, 6f); Rebuild(); },
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity + 0.5f, 0.5f, 6f); Rebuild(); },
                S.MouseSensitivity.ToString("0.0"));
            Toggle(ref y, L("ui.settings.invert_y"), S.InvertY, () => { S.InvertY = !S.InvertY; Rebuild(); });

            Head(ref y, L("ui.settings.character"));
            ColorRow(ref y, L("ui.settings.skin"), S.SkinColor, () => { S.SkinColor = Next(S.SkinColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.torso"), S.TorsoColor, () => { S.TorsoColor = Next(S.TorsoColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.arms"), S.ArmColor, () => { S.ArmColor = Next(S.ArmColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.legs"), S.LegColor, () => { S.LegColor = Next(S.LegColor); Rebuild(); });

            y += 14f;
            UiKit.AddButton(_root, _x, y, 250, 50, $"{L("ui.settings.language")}: {(S.Language == "de" ? "DE" : "EN")}",
                () => { S.Language = S.Language == "de" ? "en" : "de"; _shell.LoadLocalizer(); Rebuild(); });
            UiKit.AddButton(_root, _x + 270, y, 250, 50, L("ui.menu.back"), () => _shell.CloseSettings());
        }

        private void Head(ref float y, string text)
        {
            UiKit.AddText(_root, _x, y, _rowW, 24, text, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
        }

        private void Cycle(ref float y, string label, string value, System.Action onClick)
        {
            UiKit.AddText(_root, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_root, _x + 250, y, _rowW - 250, 44, value, onClick);
            y += 52f;
        }

        private void Toggle(ref float y, string label, bool on, System.Action onClick)
        {
            UiKit.AddText(_root, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            var b = UiKit.AddButton(_root, _x + 250, y, _rowW - 250, 44, on ? L("ui.toggle.on") : L("ui.toggle.off"), onClick);
            if (on)
            {
                b.GetComponent<Image>().color = UiKit.Cyan;
            }

            y += 52f;
        }

        private void Stepper(ref float y, string label, float frac01, float min, float max, System.Action minus, System.Action plus, string value)
        {
            UiKit.AddText(_root, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_root, _x + 250, y, 50, 44, "−", minus);
            UiKit.AddText(_root, _x + 304, y, _rowW - 250 - 50 - 50 - 8, 44, value, 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(_root, _x + _rowW - 50, y, 50, 44, "+", plus);
            // fill bar
            float fw = _rowW - 250f;
            UiKit.AddImage(_root, _x + 250, y + 40, fw, 4, UiKit.SolidSprite, new Color(0.1f, 0.18f, 0.3f));
            UiKit.AddImage(_root, _x + 250, y + 40, fw * Mathf.Clamp01(frac01), 4, UiKit.SolidSprite, UiKit.Cyan);
            y += 56f;
        }

        private void VolRow(ref float y, string label, System.Func<float> get, System.Action<float> set)
        {
            float v = get();
            Stepper(ref y, label, v, 0, 1,
                () => { set(Mathf.Clamp01(get() - 0.1f)); Rebuild(); },
                () => { set(Mathf.Clamp01(get() + 0.1f)); Rebuild(); },
                Mathf.RoundToInt(v * 100f) + "%");
        }

        private void ColorRow(ref float y, string label, Color color, System.Action onNext)
        {
            UiKit.AddText(_root, _x, y, 200, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_root, _x + 210, y, _rowW - 210 - 70, 44, L("ui.settings.next_color"), onNext);
            UiKit.AddImage(_root, _x + _rowW - 56, y + 6, 56, 32, UiKit.SolidSprite, color);
            y += 52f;
        }

        private static Color Next(Color current)
        {
            int idx = -1;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Mathf.Approximately(Palette[i].r, current.r) && Mathf.Approximately(Palette[i].g, current.g) && Mathf.Approximately(Palette[i].b, current.b))
                {
                    idx = i;
                    break;
                }
            }

            return Palette[(idx + 1) % Palette.Length];
        }
    }
}
