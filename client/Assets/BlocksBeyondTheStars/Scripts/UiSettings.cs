// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The settings screen, bound to the local <see cref="ClientSettings"/>. A full menu screen on the template of
    /// <see cref="UiKit.AddScreenHeader"/> (#3666): the categories on the left (the chosen one drawn as the screen's
    /// primary button), the chosen category's rows in the middle, and on the right a description card that explains
    /// the row under the pointer or the pad's focus. Every change applies at once and the screen is rebuilt — the
    /// category and the scroll position stay. Numbers use −/+ steppers with a fill bar, yes/no settings are switches,
    /// choices are <c>‹ value ›</c> cycles; the footer resets the shown category to its defaults.
    /// <para>
    /// Until #3666 the screen was one long scrolling list in a 900-px dialog: only Graphics showed at first, and Audio,
    /// the key bindings, the controller, voice and the comfort switches could only be reached by scrolling.
    /// </para>
    /// The parts: the categories (<c>UiSettings.Categories.cs</c>), the graphics rows (<c>.Graphics.cs</c>,
    /// <c>.HighDetails.cs</c>), the row controls (<c>.Rows.cs</c>), the description card (<c>.Info.cs</c>) and the
    /// category reset (<c>.Reset.cs</c>).
    /// </summary>
    public sealed partial class UiSettings : MonoBehaviour
    {
        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f),
        };

        // Geometry in the menus' 1920×1080 reference space: the category column, the rows' card and the description card
        // share the content band of the menu template (MenuContentTop..MenuContentBottom).
        private const float NavX = UiKit.MenuLeft, NavW = 300f, NavH = 62f, NavGap = 10f;
        private const float CardX = 420f, CardW = 940f;
        private const float CardY = UiKit.MenuContentTop, CardH = UiKit.MenuContentBottom - UiKit.MenuContentTop;
        private const float ViewInset = 14f;
        private const float InfoX = CardX + CardW + 30f, InfoW = UiKit.MenuRight - InfoX;

        private AppShell _shell;
        private Transform _root;            // the canvas
        private RectTransform _content;     // the scroll content the rows of the shown category live in
        private Category? _contentCategory; // the category _content was built for (keeps the scroll position on a rebuild)
        private float _x, _rowW;            // the rows' left edge and width inside _content
        private InputAction? _rebinding;    // the control row currently capturing a new KEYBOARD key (else null)
        private InputAction? _rebindingPad; // the control row currently capturing a new GAMEPAD button (else null)

        /// <summary>True while a control row waits for "press a key" / "press a pad button" (#2523): the shell's F1
        /// feedback form must not open on the very key the player is binding, and the shell's Esc / pad B must not close
        /// the screen — B is a button a pad player may bind, and Esc cancels the capture. Sampled at the top of
        /// <see cref="Update"/>, so it still reads true for the rest of the frame the capture consumes the key in.</summary>
        public static bool CapturingKey { get; private set; }

        /// <summary>The settings screen on show, or null.</summary>
        private static UiSettings s_open;

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SettingsUI");
            UiNav.Enable(canvas.gameObject); // gamepad can drive the settings (inert on KB/mouse)
            var ui = canvas.gameObject.AddComponent<UiSettings>();
            ui._shell = shell;
            ui._root = canvas.transform;
            s_open = ui;
            ui.Rebuild();
            return canvas.gameObject;
        }

        private ClientSettings S => _shell.Settings;
        private string L(string k) => _shell.L(k);

        /// <summary>Builds the whole screen anew — after every change, so each label shows the current value.</summary>
        private void Rebuild()
        {
            // A switch near the bottom of a long category must not throw the list back to its top; a new category starts
            // at the top. Read before the old content is destroyed (Destroy only takes effect at the end of the frame).
            float keepScroll = _content != null && _contentCategory == s_category ? _content.anchoredPosition.y : 0f;
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }

            _resetConfirm = null; // destroyed with the rest
            UiKit.AddModalDim(_root); // the menu backdrop or the paused world stays behind the screen (#588)
            // The categories first: the pad's first focus lands on them — on the one the screen reopened on — and a rebuild
            // restores the focus by position.
            UiNav.Prefer(_root.gameObject, BuildNav());
            UiKit.AddScreenHeader(_root, L("ui.settings.title"), L("ui.settings.subtitle"), () => _shell.CloseSettings(), L("ui.menu.back"));
            BuildRows(keepScroll);
            BuildInfoCard();
            if (s_category != Category.Language)
            {
                UiKit.AddLinkButton(_root, CardX + 4f, UiKit.MenuFooterY, 560f, UiKit.MenuFooterH, L("ui.settings.reset_category"), AskResetCategory);
            }
        }

        /// <summary>The category column: one button per category, the shown one drawn as the screen's primary button.
        /// Returns the shown one's button.</summary>
        private UnityEngine.UI.Button BuildNav()
        {
            UnityEngine.UI.Button shown = null;
            for (int i = 0; i < CategoryCount; i++)
            {
                var category = (Category)i;
                var button = UiKit.AddTierButton(_root, NavX, CardY + i * (NavH + NavGap), NavW, NavH, CategoryLabel(category),
                    () => SelectCategory(category), category == s_category ? ButtonTier.Primary : ButtonTier.Quiet, CategoryIcons[i]);
                if (category == s_category)
                {
                    shown = button;
                }
            }

            return shown;
        }

        private void SelectCategory(Category category)
        {
            if (category == s_category)
            {
                return;
            }

            s_category = category;
            _rebinding = null; // a capture armed in another category would wait for a key the player cannot see
            _rebindingPad = null;
            _infoTitle = null; // the description card starts on the new category's own line
            Rebuild();
        }

        /// <summary>The rows' card: an opaque card with a clipped scroll viewport over it. The shown category's rows go
        /// onto the viewport's content; a scrollbar only appears when they are taller than the card.</summary>
        private void BuildRows(float keepScroll)
        {
            UiKit.AddCard(_root, CardX, CardY, CardW, CardH);
            float viewX = CardX + ViewInset, viewY = CardY + ViewInset, viewW = CardW - 2f * ViewInset, viewH = CardH - 2f * ViewInset;
            _content = UiKit.AddScrollViewport(_root, "SettingsScroll", viewX, viewY, viewW, viewH, out var scroll);
            _contentCategory = s_category;
            _x = 14f;
            _rowW = viewW - _x - 30f; // room on the right for the scrollbar

            float y = 10f;
            BuildCategoryRows(ref y);
            float height = y + 12f;
            _content.sizeDelta = new Vector2(0f, height);
            if (height > viewH)
            {
                UiKit.AddVerticalScrollbar(_root, scroll, viewX + viewW - 16f, viewY + 4f, 12f, viewH - 8f);
                _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(keepScroll, 0f, height - viewH));
            }
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
            string detail = ClientUpdater.Detail + ClientUpdater.ProgressSuffix; // "2026.8.24 · 42 %" while downloading
            return string.IsNullOrEmpty(detail) ? s : $"{s} ({detail})";
        }

        /// <summary>Locale key for an NPC-radio-calls mode's caption.</summary>
        private static string NpcCallsLabel(int mode) => mode switch
        {
            1 => "ui.settings.npc_calls.missions",
            2 => "ui.settings.npc_calls.off",
            _ => "ui.settings.npc_calls.all",
        };

        /// <summary>Locale key for a chat-visibility mode's caption.</summary>
        private static string ChatVisibilityLabel(ChatVisibility mode) => mode switch
        {
            ChatVisibility.Always => "ui.settings.chat_visibility.always",
            ChatVisibility.Off => "ui.settings.chat_visibility.off",
            _ => "ui.settings.chat_visibility.auto",
        };

        /// <summary>Locale key for a controls-line mode's caption (#3668).</summary>
        private static string ControlsLineLabel(int mode) => mode switch
        {
            ClientSettings.ControlsHintFull => "ui.settings.controls_line.full",
            ClientSettings.ControlsHintOff => "ui.toggle.off",
            _ => "ui.settings.controls_line.short",
        };

        // The action → locale-key table lives in InputMap.LabelKey (shared with the context-actions list, #1042).
        private static string KeyLabel(InputAction action) => InputMap.LabelKey(action);

        /// <summary>The screen is torn down with every phase change — a capture left open must not keep blocking F1.</summary>
        private void OnDestroy()
        {
            CapturingKey = false;
            if (s_open == this)
            {
                s_open = null;
            }
        }

        /// <summary>Follows the pad's focus with the description card, and while a control row is capturing, binds the
        /// next key / pad button pressed (Escape cancels). Costs one selection lookup per frame in normal use.</summary>
        private void Update()
        {
            CapturingKey = _rebinding != null || _rebindingPad != null;
            TrackFocus();

            // Gamepad capture: only joystick buttons count, so stray keyboard presses can't land here.
            if (_rebindingPad is { } padAction)
            {
                CapturePadButton(padAction);
            }
            else if (_rebinding is { } action)
            {
                CaptureKey(action);
            }
        }

        private void CapturePadButton(InputAction padAction)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rebindingPad = null;
                Rebuild();
                return;
            }

            for (var kc = KeyCode.JoystickButton0; kc <= KeyCode.JoystickButton19; kc++)
            {
                if (Input.GetKeyDown(kc))
                {
                    // Store the override — or clear it when the chosen button is already this action's default.
                    S.SetBoundPad(padAction.ToString(),
                        kc == GamepadInputSource.DefaultButtonFor(padAction) ? string.Empty : kc.ToString());
                    S.Save();
                    _rebindingPad = null;
                    Rebuild();
                    return;
                }
            }
        }

        private void CaptureKey(InputAction action)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rebinding = null;
                Rebuild();
                return;
            }

            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape || (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6)
                    || (kc >= KeyCode.JoystickButton0 && kc <= KeyCode.Joystick8Button19))
                {
                    continue; // skip "no key", the cancel key, mouse buttons and pad buttons (own column)
                }

                if (Input.GetKeyDown(kc))
                {
                    // Store the override — or clear it when the chosen key is already this action's default.
                    S.SetBoundKey(action.ToString(), kc == InputMap.DefaultKey(action) ? string.Empty : kc.ToString());
                    S.Save();
                    _rebinding = null;
                    Rebuild();
                    return;
                }
            }
        }

        /// <summary>Pushes the settings that <see cref="WorldRig"/> snapshots at world build to the LIVE world
        /// objects, so changes made from the in-game pause menu take effect immediately instead of only on the
        /// next world entry (Severin playtest follow-up). Safe no-op in the main menu (nothing is spawned);
        /// the find-by-type lookups only run on a settings click, never per frame.</summary>
        private void ApplyLiveWorld()
        {
            var post = UrpScenePost.Instance;
            if (post != null)
            {
                post.Preset = S.Preset; // gates lens flare (Medium+) / motion blur (High+)
                post.LensFlareEnabled = S.LensFlare;
                post.MotionBlurEnabled = S.MotionBlur;
                post.ReducedEffects = S.ReducedEffects; // #2811: the pause menu's switch reaches the alarm/damage pulses at once
                post.SetCosiness(S.Cosiness);
            }

            var sky = FindAnyObjectByType<Sky>();
            if (sky != null)
            {
                sky.FogEnabled = S.DistanceHaze;
                sky.HeightFogEnabled = S.AtmosphereEffect(S.HeightFog, cheap: true);
                sky.FogWispsEnabled = S.AtmosphereEffect(S.FogWisps, cheap: true); // #3271
                sky.CloudFogEnabled = S.AtmosphereEffect(S.CloudFog, cheap: true); // #3272
                sky.CloudShadowsEnabled = S.AtmosphereEffect(S.CloudShadows, cheap: false);
                sky.FlickerScale = !S.ReduceFlashes && S.AtmosphereEffect(S.TorchFlicker, cheap: true) ? 1f : 0f;
                sky.GlowPulseEnabled = !S.ReduceFlashes; // #2822
                sky.WindSwayEnabled = S.AtmosphereEffect(S.WindSway, cheap: true);
                sky.WetSurfacesEnabled = S.AtmosphereEffect(S.WetSurfaces, cheap: true);
                sky.UnderwaterEnabled = S.AtmosphereEffect(S.Underwater, cheap: true);
                sky.DarkVisibility = S.DarkVisibility; // #3265
                sky.DistanceSoftEnabled = DistanceSoftView.EnabledFor(S); // #3462
                sky.TextureSoftEnabled = S.DistanceSoftness; // #3480: the same switch governs the texture softening
                sky.ReducedEffects = S.ReducedEffects;
            }

            var probe = FindAnyObjectByType<AtmosphereProbe>();
            if (probe != null)
            {
                probe.AdaptationEnabled = S.AtmosphereEffect(S.EyeAdaptation, cheap: true);
                probe.ReducedEffects = S.ReducedEffects;
                probe.DarkVisibility = S.DarkVisibility; // #3265
                probe.BubblesEnabled = S.AtmosphereEffect(S.Underwater, cheap: true);
            }

            var shafts = FindAnyObjectByType<LightShafts>();
            if (shafts != null)
            {
                shafts.MaxCards = WorldRig.ShaftCardsFor(S);
            }

            var mistCards = FindAnyObjectByType<MistCards>(); // #3273
            if (mistCards != null)
            {
                mistCards.MaxCards = MistCards.BudgetFor(S);
                mistCards.CoverageCap = MistCards.CoverageCapFor(S);
            }

            var cloudSea = FindAnyObjectByType<CloudSea>(); // #3272
            if (cloudSea != null)
            {
                cloudSea.Enabled = CloudSea.EnabledFor(S);
            }

            var stars = FindAnyObjectByType<ShootingStars>();
            if (stars != null)
            {
                stars.Enabled = S.AtmosphereEffect(S.ShootingStars, cheap: true);
            }

            var weather3d = FindAnyObjectByType<WeatherFx3D>();
            if (weather3d != null)
            {
                weather3d.Particles = S.AtmosphereEffect(S.WeatherParticles, cheap: true);
            }

            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                pc.MouseSensitivity = S.MouseSensitivity;
                pc.InvertY = S.InvertY;
                pc.CameraMotion = S.CameraMotion;
                pc.ThirdPersonView = ThirdPersonRig.Normalize((int)S.ThirdPersonView); // #4027
                pc.SetBaseFov(S.ClampedFieldOfView);
            }

            FindAnyObjectByType<VisorHud>()?.ApplyPreset(S.Preset, S.ReducedEffects);
            FxKit.Configure(S.Preset, S.ReducedEffects, S.ScreenShake, S.ReduceFlashes, S.CameraMotion); // #2152, live
        }

        /// <summary>Localizer key for a window-presentation mode (label shown on the cycle).</summary>
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

        /// <summary>#1820: "Off" or the far-view range in blocks.</summary>
        private string FarViewLabel(int blocks) => blocks <= 0
            ? L("ui.settings.far_view.off")
            : L("ui.settings.far_view.blocks").Replace("{n}", blocks.ToString());

        /// <summary>The frame-rate cap one step forward (<paramref name="dir"/> +1) or back (-1); a value outside the list
        /// steps onto its first (forward) or last (back) entry.</summary>
        private static int StepFpsCap(int current, int dir)
        {
            int idx = System.Array.IndexOf(FpsCaps, current);
            if (idx < 0)
            {
                return dir > 0 ? FpsCaps[0] : FpsCaps[FpsCaps.Length - 1];
            }

            return FpsCaps[Wrap(idx + dir, FpsCaps.Length)];
        }

        /// <summary>#2568: the next push-to-talk key — from a curated list (<see cref="PushToTalkKeys.Candidates"/>), minus
        /// every key an input action is bound to right now (the player's rebinds included), so talking never also fires
        /// a game control. The old list cycled V (camera view), B, T, F, Ctrl and the middle mouse button.</summary>
        private static string NextPttKey(string current) => PushToTalkKeys.Next(current, BoundKeyNames());

        /// <summary>The live keyboard key of every input action (rebinds included), as KeyCode names.</summary>
        private static System.Collections.Generic.IEnumerable<string> BoundKeyNames()
        {
            foreach (InputAction action in System.Enum.GetValues(typeof(InputAction)))
            {
                var key = InputMap.Key(action);
                if (key != KeyCode.None)
                {
                    yield return key.ToString();
                }
            }
        }

        /// <summary>The push-to-talk key's on-screen name ("Caps Lock" / "Feststelltaste" instead of "CapsLock").</summary>
        private string PttLabel(string keyName) => PushToTalkKeys.LabelKey(keyName) is { } labelKey ? L(labelKey) : keyName;

        /// <summary>The palette colour one step forward (<paramref name="dir"/> +1) or back (-1) from
        /// <paramref name="current"/>; a colour outside the palette steps onto its first (forward) or last (back) entry.</summary>
        private static Color StepColor(Color current, int dir)
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

            if (idx < 0)
            {
                return dir > 0 ? Palette[0] : Palette[Palette.Length - 1];
            }

            return Palette[Wrap(idx + dir, Palette.Length)];
        }

        /// <summary><paramref name="value"/> wrapped into 0..<paramref name="count"/>-1 — a cycle's step past either end.</summary>
        private static int Wrap(int value, int count) => ((value % count) + count) % count;

        /// <summary>The value BEFORE <paramref name="current"/> in a cycle that only knows its next step: walks the cycle
        /// until it comes back round. A value outside the cycle (it never comes back) steps forward instead.</summary>
        private static T Previous<T>(T current, System.Func<T, T> next)
        {
            var same = System.Collections.Generic.EqualityComparer<T>.Default;
            T at = current;
            for (int i = 0; i < 32; i++)
            {
                T after = next(at);
                if (same.Equals(after, current))
                {
                    return at;
                }

                at = after;
            }

            return next(current);
        }
    }
}
