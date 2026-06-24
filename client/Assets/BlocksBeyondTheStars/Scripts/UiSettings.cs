using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
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
        private Transform _root;        // the canvas (holds the dimmer + the fixed panel frame + the scroll area)
        private Transform _content;     // scroll content the rows live in (taller than the panel → it scrolls)
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

            // The settings list is taller than the panel, so host it in a scroll viewport (mouse wheel / drag)
            // clipped to the panel — previously everything was placed straight on the canvas, so the lower rows
            // (updates / language / back) ran off the bottom of the screen with no way to reach them.
            _content = BuildScrollViewport();

            // Rows are now positioned relative to the scroll content (the panel's top-left), not the whole canvas.
            _x = 30f; _rowW = _pw - 60f;

            float y = 24f;
            UiKit.AddLogo(_content, _x, y, 420, 40, L("ui.settings.title"), 26);
            y += 64f;

            Head(ref y, L("ui.settings.graphics"));
            Cycle(ref y, L("ui.settings.preset"), S.Preset.ToString(), () => { S.Preset = (QualityPreset)(((int)S.Preset + 1) % 4); Rebuild(); });
            // Window mode cycles Windowed → Borderless → Exclusive and applies immediately so the player sees
            // the window change without leaving the menu (resolution/mode changes are pushed via Apply()).
            Cycle(ref y, L("ui.settings.window_mode"), L(WindowModeKey(S.Window)),
                () => { S.Window = (WindowMode)(((int)S.Window + 1) % 3); S.Apply(); Rebuild(); });
            Stepper(ref y, L("ui.settings.view_distance"), (S.ViewDistanceChunks - 1) / 7f, 1, 8,
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks - 1, 1, 8); Rebuild(); },
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks + 1, 1, 8); Rebuild(); },
                S.ViewDistanceChunks.ToString());
            Stepper(ref y, L("ui.settings.brightness"), (S.Brightness - 0.7f) / 0.8f, 0.7f, 1.5f,
                () => { S.Brightness = Mathf.Clamp(S.Brightness - 0.05f, 0.7f, 1.5f); UrpScenePost.Instance?.SetBrightness(S.Brightness); Rebuild(); },
                () => { S.Brightness = Mathf.Clamp(S.Brightness + 0.05f, 0.7f, 1.5f); UrpScenePost.Instance?.SetBrightness(S.Brightness); Rebuild(); },
                Mathf.RoundToInt(S.Brightness * 100f) + "%");
            // Frame pacing. VSync off (+ optional fps cap) is the recommended fix when the game runs sluggish
            // on the Linux/Proton client, where VSync can lock to a hard 30 fps.
            Toggle(ref y, L("ui.settings.vsync"), S.VSync, () => { S.VSync = !S.VSync; Rebuild(); });
            if (!S.VSync)
            {
                Cycle(ref y, L("ui.settings.fps_cap"), FpsCapLabel(S.FrameRateCap),
                    () => { S.FrameRateCap = NextFpsCap(S.FrameRateCap); Rebuild(); });
            }

            Toggle(ref y, L("ui.settings.smaa"), S.Smaa, () => { S.Smaa = !S.Smaa; S.ApplyCameraLook(); Rebuild(); });
            Toggle(ref y, L("ui.settings.lens_flare"), S.LensFlare, () => { S.LensFlare = !S.LensFlare; Rebuild(); });
            Toggle(ref y, L("ui.settings.motion_blur"), S.MotionBlur, () => { S.MotionBlur = !S.MotionBlur; Rebuild(); });
            Toggle(ref y, L("ui.settings.volumetric_fog"), S.VolumetricFog, () => { S.VolumetricFog = !S.VolumetricFog; Rebuild(); });
            Toggle(ref y, L("ui.settings.reflections"), S.Reflections, () => { S.Reflections = !S.Reflections; Rebuild(); });

            Head(ref y, L("ui.settings.audio"));
            VolRow(ref y, L("ui.settings.master_volume"), () => S.MasterVolume, v => S.MasterVolume = v);
            VolRow(ref y, L("ui.settings.music_volume"), () => S.MusicVolume, v => S.MusicVolume = v);
            VolRow(ref y, L("ui.settings.sfx_volume"), () => S.SfxVolume, v => S.SfxVolume = v);
            Cycle(ref y, L("ui.settings.music_style"),
                L(S.MusicMode == MusicMode.Tracks ? "ui.settings.music_style.tracks" : "ui.settings.music_style.synth"),
                () => { S.MusicMode = S.MusicMode == MusicMode.Tracks ? MusicMode.Synth : MusicMode.Tracks; Rebuild(); });

            // Voice chat (on by default; only carries on a server that also enabled voice). The master toggle
            // turns the whole feature off; the rows below tune it.
            Toggle(ref y, L("ui.settings.voice"), S.VoiceEnabled, () => { S.VoiceEnabled = !S.VoiceEnabled; Rebuild(); });
            if (S.VoiceEnabled)
            {
                VolRow(ref y, L("ui.settings.voice_volume"), () => S.VoiceVolume, v => S.VoiceVolume = v);
                Toggle(ref y, L("ui.settings.voice_input"), S.VoiceInputEnabled, () => { S.VoiceInputEnabled = !S.VoiceInputEnabled; Rebuild(); });
                Cycle(ref y, L("ui.settings.push_to_talk"), S.PushToTalkKey, () => { S.PushToTalkKey = NextPttKey(S.PushToTalkKey); Rebuild(); });
            }

            Head(ref y, L("ui.settings.controls"));
            Stepper(ref y, L("ui.settings.mouse_sensitivity"), (S.MouseSensitivity - 0.5f) / 5.5f, 0, 1,
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity - 0.5f, 0.5f, 6f); Rebuild(); },
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity + 0.5f, 0.5f, 6f); Rebuild(); },
                S.MouseSensitivity.ToString("0.0"));
            Toggle(ref y, L("ui.settings.invert_y"), S.InvertY, () => { S.InvertY = !S.InvertY; Rebuild(); });
            Toggle(ref y, L("ui.settings.camera_motion"), S.CameraMotion, () => { S.CameraMotion = !S.CameraMotion; Rebuild(); });

            Head(ref y, L("ui.settings.comfort"));
            Toggle(ref y, L("ui.settings.show_session_time"), S.ShowSessionTime, () => { S.ShowSessionTime = !S.ShowSessionTime; Rebuild(); });
            Toggle(ref y, L("ui.settings.playtime_reminder"), S.PlaytimeReminder, () => { S.PlaytimeReminder = !S.PlaytimeReminder; Rebuild(); });
            // Reminder interval: 15-minute steps from 15 min up to 4 hours; greyed in effect when the toggle is off.
            Stepper(ref y, L("ui.settings.reminder_interval"), (S.ReminderMinutes - 15) / 225f, 15, 240,
                () => { S.ReminderMinutes = Mathf.Clamp(S.ReminderMinutes - 15, 15, 240); Rebuild(); },
                () => { S.ReminderMinutes = Mathf.Clamp(S.ReminderMinutes + 15, 15, 240); Rebuild(); },
                S.ReminderMinutes + " min");

            Head(ref y, L("ui.settings.character"));
            ColorRow(ref y, L("ui.settings.skin"), S.SkinColor, () => { S.SkinColor = Next(S.SkinColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.torso"), S.TorsoColor, () => { S.TorsoColor = Next(S.TorsoColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.arms"), S.ArmColor, () => { S.ArmColor = Next(S.ArmColor); Rebuild(); });
            ColorRow(ref y, L("ui.settings.legs"), S.LegColor, () => { S.LegColor = Next(S.LegColor); Rebuild(); });

            Head(ref y, L("ui.settings.updates"));
            UiKit.AddText(_content, _x, y, 240, 38, L("ui.settings.update_url"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(_content, _x + 250, y, _rowW - 250, 38, S.UpdateFeedUrl, v => S.UpdateFeedUrl = (v ?? string.Empty).Trim());
            y += 46f;
            UiKit.AddButton(_content, _x, y, 250, 44,
                ClientUpdater.Busy ? L("ui.settings.update_checking") : L("ui.settings.update_check"),
                () => { if (!ClientUpdater.Busy) ClientUpdater.CheckForUpdates(S.UpdateFeedUrl, () => { if (this != null) Rebuild(); }); });
            UiKit.AddText(_content, _x + 270, y, _rowW - 270, 44, UpdateStatusText(), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 52f;

            y += 14f;
            UiKit.AddButton(_content, _x, y, 250, 50, $"{L("ui.settings.language")}: {(S.Language == "de" ? "DE" : "EN")}",
                () => { S.Language = S.Language == "de" ? "en" : "de"; _shell.LoadLocalizer(); Rebuild(); });
            UiKit.AddButton(_content, _x + 270, y, 250, 50, L("ui.menu.back"), () => _shell.CloseSettings());
            y += 50f;

            // Size the scroll content to the rows so the viewport can scroll to the very bottom.
            ((RectTransform)_content).sizeDelta = new Vector2(0f, y + 24f);
        }

        /// <summary>Creates a vertical ScrollRect clipped to the settings panel and returns its content transform.
        /// Rows are placed absolutely (top-left, like the rest of the screen) onto the returned content, whose
        /// height is set by <see cref="Rebuild"/> once all rows are laid out.</summary>
        private Transform BuildScrollViewport()
        {
            var viewGo = new GameObject("SettingsScroll", typeof(RectTransform));
            viewGo.transform.SetParent(_root, false);
            UiKit.Place(viewGo, _px, 80, _pw, 920);

            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            viewGo.AddComponent<RectMask2D>();

            // A near-transparent graphic so the wheel/drag has something to hit over empty areas (clicks on the
            // buttons still work). The visible frame is the AddPanel drawn behind it.
            var hit = viewGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 920f); // replaced with the real height at the end of Rebuild

            scroll.viewport = viewGo.GetComponent<RectTransform>();
            scroll.content = content;
            return content;
        }

        /// <summary>The Velopack update status as a localized line (+ version/error detail when present).</summary>
        private string UpdateStatusText()
        {
            string s = ClientUpdater.State switch
            {
                UpdateState.Checking => L("ui.settings.update_checking"),
                UpdateState.Downloading => L("ui.settings.update_downloading"),
                UpdateState.Restarting => L("ui.settings.update_restarting"),
                UpdateState.UpToDate => L("ui.settings.update_uptodate"),
                UpdateState.NotInstalled => L("ui.settings.update_notinstalled"),
                UpdateState.NoUrl => L("ui.settings.update_nourl"),
                UpdateState.Failed => L("ui.settings.update_failed"),
                _ => string.Empty,
            };
            return string.IsNullOrEmpty(ClientUpdater.Detail) ? s : $"{s} ({ClientUpdater.Detail})";
        }

        private void Head(ref float y, string text)
        {
            UiKit.AddText(_content, _x, y, _rowW, 24, text, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
        }

        private void Cycle(ref float y, string label, string value, System.Action onClick)
        {
            UiKit.AddText(_content, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_content, _x + 250, y, _rowW - 250, 44, value, onClick);
            y += 52f;
        }

        private void Toggle(ref float y, string label, bool on, System.Action onClick)
        {
            UiKit.AddText(_content, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            var b = UiKit.AddButton(_content, _x + 250, y, _rowW - 250, 44, on ? L("ui.toggle.on") : L("ui.toggle.off"), onClick);
            if (on)
            {
                b.GetComponent<Image>().color = UiKit.Cyan;
            }

            y += 52f;
        }

        private void Stepper(ref float y, string label, float frac01, float min, float max, System.Action minus, System.Action plus, string value)
        {
            UiKit.AddText(_content, _x, y, 240, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_content, _x + 250, y, 50, 44, "−", minus);
            UiKit.AddText(_content, _x + 304, y, _rowW - 250 - 50 - 50 - 8, 44, value, 20, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(_content, _x + _rowW - 50, y, 50, 44, "+", plus);
            // fill bar
            float fw = _rowW - 250f;
            UiKit.AddImage(_content, _x + 250, y + 40, fw, 4, UiKit.SolidSprite, new Color(0.1f, 0.18f, 0.3f));
            UiKit.AddImage(_content, _x + 250, y + 40, fw * Mathf.Clamp01(frac01), 4, UiKit.SolidSprite, UiKit.Cyan);
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
            UiKit.AddText(_content, _x, y, 200, 44, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(_content, _x + 210, y, _rowW - 210 - 70, 44, L("ui.settings.next_color"), onNext);
            UiKit.AddImage(_content, _x + _rowW - 56, y + 6, 56, 32, UiKit.SolidSprite, color);
            y += 52f;
        }

        /// <summary>Localizer key for a window-presentation mode (label shown on the cycle button).</summary>
        private static string WindowModeKey(WindowMode m) => m switch
        {
            WindowMode.Borderless => "ui.settings.window_mode.borderless",
            WindowMode.Exclusive => "ui.settings.window_mode.exclusive",
            _ => "ui.settings.window_mode.windowed",
        };

        // Frame-rate caps the player can cycle through when VSync is off (0 = unlimited). Common refresh
        // rates plus a couple of in-between targets for laptops that want to cap for heat / battery.
        private static readonly int[] FpsCaps = { 0, 30, 60, 72, 90, 120, 144, 240 };

        private string FpsCapLabel(int cap) => cap <= 0 ? L("ui.settings.fps_cap.unlimited") : cap + " fps";

        private static int NextFpsCap(int current)
        {
            int idx = System.Array.IndexOf(FpsCaps, current);
            return FpsCaps[(idx + 1) % FpsCaps.Length];
        }

        // A small curated set of comfortable push-to-talk keys to cycle through.
        private static readonly string[] PttKeys = { "V", "B", "T", "F", "CapsLock", "LeftAlt", "LeftControl", "Mouse2" };

        private static string NextPttKey(string current)
        {
            int idx = System.Array.IndexOf(PttKeys, current);
            return PttKeys[(idx + 1) % PttKeys.Length];
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
