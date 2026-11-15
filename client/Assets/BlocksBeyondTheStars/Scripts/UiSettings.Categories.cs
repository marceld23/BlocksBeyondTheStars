// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The categories of the settings screen (#3666) and the rows of each — every setting the old single list
    /// had, distributed: the graphics rows live in <c>UiSettings.Graphics.cs</c>, the rest here.</summary>
    public sealed partial class UiSettings
    {
        /// <summary>The categories, in the order of the left-hand column.</summary>
        private enum Category
        {
            Graphics,
            Display,
            Audio,
            Controls,
            Controller,
            Voice,
            Accessibility,
            Game,
            Language,
        }

        private const int CategoryCount = 9;

        /// <summary>The icon of each category, in <see cref="Category"/> order.</summary>
        private static readonly string[] CategoryIcons =
        {
            "set_graphics", "set_display", "set_audio", "set_controls", "set_pad", "set_voice", "set_access", "info_mode", "set_language",
        };

        /// <summary>The category shown last — the screen reopens there (from the main menu or the pause menu) for the rest
        /// of the session: a player tuning the volume mid-game finds it again at once.</summary>
        private static Category s_category = Category.Graphics;

        /// <summary>A category's name (the description card's heading).</summary>
        private string CategoryTitle(Category category) => category switch
        {
            Category.Graphics => L("ui.settings.graphics"),
            Category.Display => L("ui.settings.cat.display"),
            Category.Audio => L("ui.settings.audio"),
            Category.Controls => L("ui.settings.controls"),
            Category.Controller => L("ui.settings.controller"),
            Category.Voice => L("ui.settings.voice"),
            Category.Accessibility => L("ui.settings.cat.accessibility"),
            Category.Game => L("ui.settings.cat.game"),
            _ => L("ui.settings.language"),
        };

        /// <summary>A category's button caption — the language one also names the language in use.</summary>
        private string CategoryLabel(Category category) => category == Category.Language
            ? $"{L("ui.settings.language")}: {GameLocaleExtensions.Parse(S.Language).NativeName()}"
            : CategoryTitle(category);

        private void BuildCategoryRows(ref float y)
        {
            switch (s_category)
            {
                case Category.Graphics: GraphicsRows(ref y); break;
                case Category.Display: DisplayRows(ref y); break;
                case Category.Audio: AudioRows(ref y); break;
                case Category.Controls: ControlsRows(ref y); break;
                case Category.Controller: ControllerRows(ref y); break;
                case Category.Voice: VoiceRows(ref y); break;
                case Category.Accessibility: AccessibilityRows(ref y); break;
                case Category.Game: GameRows(ref y); break;
                default: LanguageRows(ref y); break;
            }
        }

        /// <summary>Display &amp; HUD: the window, the frame pacing and what the in-game display shows.</summary>
        private void DisplayRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.screen"));
#if !UNITY_WEBGL || UNITY_EDITOR
            // Window mode cycles Windowed → Borderless → Exclusive and applies immediately so the player sees
            // the window change without leaving the menu (resolution/mode changes are pushed via Apply()). Not in the
            // browser: the page owns the canvas there and ClientSettings.ApplyWindowMode does nothing (#1420).
            Cycle(ref y, L("ui.settings.window_mode"), L(WindowModeKey(S.Window)),
                dir => { S.Window = (WindowMode)Wrap((int)S.Window + dir, 3); S.Apply(); Rebuild(); });
#endif
            // Frame pacing. VSync off (+ optional fps cap) is the recommended fix when the game runs sluggish
            // on the Linux/Proton client, where VSync can lock to a hard 30 fps.
            Toggle(ref y, L("ui.settings.vsync"), S.VSync, () => { S.VSync = !S.VSync; S.Apply(); Rebuild(); }, L("ui.settings.vsync_info"));
            if (!S.VSync)
            {
                Cycle(ref y, L("ui.settings.fps_cap"), FpsCapLabel(S.FrameRateCap),
                    dir => { S.FrameRateCap = StepFpsCap(S.FrameRateCap, dir); S.Apply(); Rebuild(); });
            }

            Kicker(ref y, L("ui.settings.group.hud"));
            // UI scale (#483): drives UiKit.UserScale, which divides the HUD canvases' reference resolution. Applies live
            // via S.Apply() so the player can size the HUD while looking at it from the pause menu. Menus are deliberately
            // unaffected (absolute 1920 layout).
            Stepper(ref y, L("ui.settings.ui_scale"), (S.UiScale - UiKit.UserScaleMin) / (UiKit.UserScaleMax - UiKit.UserScaleMin),
                () => { S.UiScale = Mathf.Clamp(S.UiScale - 0.1f, UiKit.UserScaleMin, UiKit.UserScaleMax); S.Apply(); Rebuild(); },
                () => { S.UiScale = Mathf.Clamp(S.UiScale + 0.1f, UiKit.UserScaleMin, UiKit.UserScaleMax); S.Apply(); Rebuild(); },
                Mathf.RoundToInt(S.UiScale * 100f) + "%", L("ui.settings.ui_scale_info"));
            // #3668: the controls line at the bottom right — the short set (at most four keys), the full line, or none.
            Cycle(ref y, L("ui.settings.controls_line"), L(ControlsLineLabel(S.ControlsHintMode)),
                dir => { S.ControlsHint = Wrap(S.ControlsHintMode + dir, ClientSettings.ControlsHintOff + 1); Rebuild(); },
                L("ui.settings.controls_line_info"));
            Toggle(ref y, L("ui.settings.show_coordinates"), S.ShowCoordinates, () => { S.ShowCoordinates = !S.ShowCoordinates; Rebuild(); }); // #2993
            Toggle(ref y, L("ui.settings.show_enemy_health"), S.ShowEnemyHealthBars, () => { S.ShowEnemyHealthBars = !S.ShowEnemyHealthBars; Rebuild(); });
            // #2471: up to three objectives under VEGA's chip (one per story), hidden while the chat is open.
            Toggle(ref y, L("ui.settings.show_all_objectives"), S.ShowAllObjectives, () => { S.ShowAllObjectives = !S.ShowAllObjectives; Rebuild(); },
                L("ui.settings.show_all_objectives_hint"));
            // Chat overlay: fade out on its own (default), stay up, or never show unprompted (#636). The in-game toggle key
            // does the same thing for one session without opening this menu.
            Cycle(ref y, L("ui.settings.chat_visibility"), L(ChatVisibilityLabel(S.ChatVisibility)),
                dir => { S.ChatVisibility = (ChatVisibility)Wrap((int)S.ChatVisibility + dir, 3); Rebuild(); }, L("ui.settings.chat_visibility_info"));
        }

        private void AudioRows(ref float y)
        {
            VolRow(ref y, L("ui.settings.master_volume"), () => S.MasterVolume, v => S.MasterVolume = v);
            VolRow(ref y, L("ui.settings.music_volume"), () => S.MusicVolume, v => S.MusicVolume = v);
            VolRow(ref y, L("ui.settings.sfx_volume"), () => S.SfxVolume, v => S.SfxVolume = v);
            Cycle(ref y, L("ui.settings.music_style"),
                L(S.MusicMode == MusicMode.Tracks ? "ui.settings.music_style.tracks" : "ui.settings.music_style.synth"),
                _ => { S.MusicMode = S.MusicMode == MusicMode.Tracks ? MusicMode.Synth : MusicMode.Tracks; Rebuild(); });
            Toggle(ref y, L("ui.settings.mute_alarms"), S.MuteAlarms, () => { S.MuteAlarms = !S.MuteAlarms; ClientAudio.Instance?.ApplyAlarmMute(); Rebuild(); }, // #2052
                L("ui.settings.mute_alarms_info"));
        }

        /// <summary>Voice chat (on by default; only carries on a server that also enabled voice) and the muted players. A
        /// build without voice (the browser: <c>BBS_VOICE</c> is a standalone define, VoiceChat stays inert) shows the muted
        /// players alone — they mute text chat too.</summary>
        private void VoiceRows(ref float y)
        {
#if BBS_VOICE
            // The master toggle turns the whole feature off; the rows below tune it.
            Toggle(ref y, L("ui.settings.voice"), S.VoiceEnabled, () => { S.VoiceEnabled = !S.VoiceEnabled; Rebuild(); }, L("ui.settings.voice_info"));
            if (S.VoiceEnabled)
            {
                VolRow(ref y, L("ui.settings.voice_volume"), () => S.VoiceVolume, v => S.VoiceVolume = v);
                Toggle(ref y, L("ui.settings.voice_input"), S.VoiceInputEnabled, () => { S.VoiceInputEnabled = !S.VoiceInputEnabled; Rebuild(); },
                    L("ui.settings.voice_input_info"));
                Cycle(ref y, L("ui.settings.push_to_talk"), PttLabel(S.PushToTalkKey), dir =>
                {
                    S.PushToTalkKey = dir > 0 ? NextPttKey(S.PushToTalkKey) : Previous(S.PushToTalkKey, NextPttKey);
                    Rebuild();
                }, L("ui.settings.push_to_talk_info"));
            }
#endif

            // Muted players (#1209) — one row each with an Unmute button, so the list is never a dead end a player can only
            // leave by editing a file. It hides text chat too, but this is where players look for it.
            Kicker(ref y, L("ui.mute.muted_players"));
            if (S.MutedPlayers.Count == 0)
            {
                Note(ref y, L("ui.mute.none"));
                return;
            }

            foreach (var who in S.MutedPlayers.ToArray()) // copy: the button mutates the list
            {
                var row = Row(ref y, who, null);
                UiKit.AddButton(row, CtrlX, 0f, CtrlW, RowH, L("ui.mute.unmute"), () => { S.Unmute(who); S.Save(); Rebuild(); });
            }
        }

        /// <summary>Controls: the mouse, then one rebind row per remappable action (click to capture a new key).</summary>
        private void ControlsRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.mouse"));
            Stepper(ref y, L("ui.settings.mouse_sensitivity"), (S.MouseSensitivity - 0.5f) / 5.5f,
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity - 0.5f, 0.5f, 6f); ApplyLiveWorld(); Rebuild(); },
                () => { S.MouseSensitivity = Mathf.Clamp(S.MouseSensitivity + 0.5f, 0.5f, 6f); ApplyLiveWorld(); Rebuild(); },
                S.MouseSensitivity.ToString("0.0"));
            Toggle(ref y, L("ui.settings.invert_y"), S.InvertY, () => { S.InvertY = !S.InvertY; ApplyLiveWorld(); Rebuild(); });

            KeyGroup(ref y, L("ui.settings.controls_keys"), InputMap.Remappable);
            KeyGroup(ref y, L("ui.settings.controls_flight"), InputMap.FlightRemappable);
            KeyGroup(ref y, L("ui.settings.controls_vehicle"), InputMap.VehicleRemappable);
            KeyGroup(ref y, L("ui.settings.controls_menu"), InputMap.MenuRemappable);
        }

        private void KeyGroup(ref float y, string title, IEnumerable<InputAction> actions)
        {
            Kicker(ref y, title);
            foreach (var action in actions)
            {
                KeyRow(ref y, action);
            }
        }

        /// <summary>Controller (#1219). Everything here is pad-only, so it says plainly when there is no pad to tune rather
        /// than letting a player wonder why the sliders do nothing.</summary>
        private void ControllerRows(ref float y)
        {
            if (!InputMap.GamepadConnected)
            {
                Note(ref y, L("ui.settings.controller_none"));
            }

            Stepper(ref y, L("ui.settings.pad_deadzone"),
                (S.PadDeadzone - ClientSettings.PadDeadzoneMin) / (ClientSettings.PadDeadzoneMax - ClientSettings.PadDeadzoneMin),
                () => { S.PadDeadzone = Mathf.Clamp(S.PadDeadzone - 0.05f, ClientSettings.PadDeadzoneMin, ClientSettings.PadDeadzoneMax); Rebuild(); },
                () => { S.PadDeadzone = Mathf.Clamp(S.PadDeadzone + 0.05f, ClientSettings.PadDeadzoneMin, ClientSettings.PadDeadzoneMax); Rebuild(); },
                Mathf.RoundToInt(S.PadDeadzone * 100f) + "%");
            PadLookRow(ref y, L("ui.settings.pad_look_x"), () => S.PadLookX, v => S.PadLookX = v);
            PadLookRow(ref y, L("ui.settings.pad_look_y"), () => S.PadLookY, v => S.PadLookY = v);
            Toggle(ref y, L("ui.settings.pad_invert_y"), S.PadInvertY, () => { S.PadInvertY = !S.PadInvertY; Rebuild(); });
            Toggle(ref y, L("ui.settings.pad_triggers"), S.PadTriggersMinePlace,
                () => { S.PadTriggersMinePlace = !S.PadTriggersMinePlace; Rebuild(); }, L("ui.settings.pad_triggers_hint"));
            Cycle(ref y, L("ui.settings.pad_glyphs"), L("ui.settings.pad_glyphs." + S.PadGlyphs.ToString().ToLowerInvariant()),
                dir => { S.PadGlyphs = (PadGlyphSet)Wrap((int)S.PadGlyphs + dir, 3); Rebuild(); });
            // #2746: the explanation says what rumbles — or, where no backend exists (WebGL, the native Linux build), that
            // vibration is not available here (the choice is still remembered).
            Toggle(ref y, L("ui.settings.pad_vibration"), S.PadVibration, () => { S.PadVibration = !S.PadVibration; Rebuild(); },
                L(PadRumble.Supported ? "ui.settings.pad_vibration_hint" : "ui.settings.pad_vibration_unsupported"));
        }

        /// <summary>A pad look-speed row: a multiplier in 0.25 steps.</summary>
        private void PadLookRow(ref float y, string label, System.Func<float> get, System.Action<float> set)
        {
            float v = get();
            Stepper(ref y, label, (v - ClientSettings.PadLookMin) / (ClientSettings.PadLookMax - ClientSettings.PadLookMin),
                () => { set(Mathf.Clamp(get() - 0.25f, ClientSettings.PadLookMin, ClientSettings.PadLookMax)); Rebuild(); },
                () => { set(Mathf.Clamp(get() + 0.25f, ClientSettings.PadLookMin, ClientSettings.PadLookMax)); Rebuild(); },
                v.ToString("0.00") + "×");
        }

        /// <summary>Accessibility: the view's motion, the flashes and effects, and the reading pace of the story texts.</summary>
        private void AccessibilityRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.motion"));
            // #2518: head bob, the moving FOV kick and impact shake — the comfort switch players look for first.
            Toggle(ref y, L("ui.settings.camera_motion"), S.CameraMotion, () => { S.CameraMotion = !S.CameraMotion; ApplyLiveWorld(); Rebuild(); },
                L("ui.settings.camera_motion_info"));
            // #2152: how hard hits, weapon kicks and explosions rattle the view (0–100 %, 10 % steps).
            Stepper(ref y, L("ui.settings.screen_shake"), Mathf.Clamp01(S.ScreenShake),
                () => { S.ScreenShake = Mathf.Clamp01(Mathf.Round((S.ScreenShake - 0.1f) * 10f) / 10f); ApplyLiveWorld(); Rebuild(); },
                () => { S.ScreenShake = Mathf.Clamp01(Mathf.Round((S.ScreenShake + 0.1f) * 10f) / 10f); ApplyLiveWorld(); Rebuild(); },
                Mathf.RoundToInt(Mathf.Clamp01(S.ScreenShake) * 100f) + "%", L("ui.settings.screen_shake_info"));
            // Field of view (#1590): 50–100° in 5° steps, live from the pause menu (ApplyLiveWorld → SetBaseFov).
            Stepper(ref y, L("ui.settings.fov"),
                (S.ClampedFieldOfView - ClientSettings.FieldOfViewMin) / (ClientSettings.FieldOfViewMax - ClientSettings.FieldOfViewMin),
                () => SetFieldOfView(S.ClampedFieldOfView - ClientSettings.FieldOfViewStep),
                () => SetFieldOfView(S.ClampedFieldOfView + ClientSettings.FieldOfViewStep),
                Mathf.RoundToInt(S.ClampedFieldOfView) + "°", L("ui.settings.fov_hint"));

            Kicker(ref y, L("ui.settings.group.flashes"));
            // #2152: photosensitivity — big full-screen flashes become a soft wash.
            Toggle(ref y, L("ui.settings.reduce_flashes"), S.ReduceFlashes, () => { S.ReduceFlashes = !S.ReduceFlashes; ApplyLiveWorld(); Rebuild(); },
                L("ui.settings.reduce_flashes_hint"));
            // Fewer particles and calmer screen effects (the ReducedEffects flag).
            Toggle(ref y, L("ui.settings.reduced_effects"), S.ReducedEffects, () => { S.ReducedEffects = !S.ReducedEffects; S.Apply(); ApplyLiveWorld(); Rebuild(); },
                L("ui.settings.reduced_effects_hint"));

            Kicker(ref y, L("ui.settings.group.reading"));
            // #2468: story-pack cutscene captions wait for the reader (default) or turn the page by themselves.
            Toggle(ref y, L("ui.settings.story_text_wait"), S.StoryTextWaitForMe, () => { S.StoryTextWaitForMe = !S.StoryTextWaitForMe; Rebuild(); },
                L("ui.settings.story_text_wait_hint"));
        }

        private void SetFieldOfView(float degrees)
        {
            S.FieldOfView = Mathf.Clamp(degrees, ClientSettings.FieldOfViewMin, ClientSettings.FieldOfViewMax);
            ApplyLiveWorld();
            Rebuild();
        }

        /// <summary>Language: every selectable language as a button, the one in use marked. The selectable ones are
        /// <see cref="BlocksBeyondTheStars.Shared.Content.GameContent.SelectableLocales"/> (EN/DE always, community
        /// languages once their coverage clears the bar); a hand-set language below the bar still shows, first.</summary>
        private void LanguageRows(ref float y)
        {
            var active = GameLocaleExtensions.Parse(S.Language);
            var languages = SelectableLanguages(active);
            const float gap = 16f;
            float colW = (_rowW - gap) / 2f;
            for (int i = 0; i < languages.Count; i++)
            {
                var language = languages[i];
                bool inUse = language == active;
                var button = UiKit.AddButton(_content, _x + (i % 2) * (colW + gap), y + (i / 2) * RowStep, colW, RowH,
                    language.NativeName(), () => PickLanguage(language));
                UiKit.StyleTier(button, inUse ? ButtonTier.Secondary : ButtonTier.Quiet);
                if (inUse)
                {
                    button.GetComponent<UnityEngine.UI.Image>().color = UiKit.Cyan;
                }
            }

            y += (languages.Count + 1) / 2 * RowStep;
        }

        private List<GameLocale> SelectableLanguages(GameLocale active)
        {
            var languages = new List<GameLocale>();
            var selectable = _shell.Content?.SelectableLocales();
            if (selectable == null || selectable.Count == 0)
            {
                languages.Add(GameLocale.English);
                languages.Add(GameLocale.German);
            }
            else
            {
                languages.AddRange(selectable);
            }

            if (!languages.Contains(active))
            {
                languages.Insert(0, active);
            }

            return languages;
        }

        private void PickLanguage(GameLocale language)
        {
            S.Language = language.Code();
            _shell.LoadLocalizer();
            _infoTitle = null; // the card's line in the new language
            Rebuild();
        }
    }
}
