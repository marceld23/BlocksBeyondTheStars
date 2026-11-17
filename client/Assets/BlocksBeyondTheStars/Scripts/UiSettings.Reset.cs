// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>"Reset this category" (#3666): the footer link puts every setting of the shown category back to what a
    /// first run on this device starts with, applies it like the rows do, and saves — after a confirmation, since one
    /// press in Controls clears every key and pad binding.</summary>
    public sealed partial class UiSettings
    {
        private GameObject _resetConfirm; // the "Reset … ?" question, while it is open

        /// <summary>Closes what is open on top of the settings screen — the reset question, the festival list — and says
        /// whether there was something, so the shell's Esc / pad B closes that first instead of leaving the screen.</summary>
        internal static bool CloseTopLayer()
        {
            var ui = s_open;
            if (ui == null)
            {
                return false;
            }

            if (ui._resetConfirm != null && ui._resetConfirm.activeSelf)
            {
                ui.CloseResetConfirm();
                return true;
            }

            return UiFestivalPicker.CloseOpen();
        }

        /// <summary>The footer link: asks before resetting — the destructive choice on the left, Cancel on the right (as
        /// the world-delete question on "My worlds").</summary>
        private void AskResetCategory()
        {
            if (s_category == Category.Language)
            {
                return;
            }

            CloseResetConfirm();
            var (overlay, panel) = UiKit.AddModalOverlay(_root, 610f, 400f, 700f, 280f);
            _resetConfirm = overlay;
            var question = UiKit.AddText(panel, 30f, 30f, 640f, 110f,
                L("ui.settings.reset_category_confirm").Replace("{category}", CategoryTitle(s_category)), 20, UiKit.TextCol,
                TextAnchor.MiddleCenter);
            question.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddTierButton(panel, 40f, 180f, 290f, 58f, L("ui.settings.reset_category"), () =>
            {
                CloseResetConfirm();
                ResetCategory();
            }, ButtonTier.Danger, "btn_settings");
            UiKit.AddTierButton(panel, 370f, 180f, 290f, 58f, L("ui.action.cancel"), CloseResetConfirm, ButtonTier.Quiet, "btn_back");
            UiKit.OpenModal(overlay);
        }

        private void CloseResetConfirm()
        {
            if (_resetConfirm != null)
            {
                Destroy(_resetConfirm);
            }

            _resetConfirm = null;
        }

        private void ResetCategory()
        {
            var d = DefaultSettings();
            switch (s_category)
            {
                case Category.Graphics: ResetGraphics(d); break;
                case Category.Display: ResetDisplay(d); break;
                case Category.Audio: ResetAudio(d); break;
                case Category.Controls: ResetControls(d); break;
                case Category.Controller: ResetController(d); break;
                case Category.Voice: ResetVoice(d); break;
                case Category.Accessibility: ResetAccessibility(d); break;
                case Category.Game: ResetGame(d); break;
                default: return; // Language: no link — the language is a choice, not a tuning
            }

            S.Apply();
            ApplyLiveWorld();
            S.Save();
            Rebuild();
        }

        /// <summary>
        /// The values a first run on this device starts with: a fresh <see cref="ClientSettings"/> plus the platform choices
        /// <see cref="ClientSettings.Load"/> makes for a genuine first run (a lighter start in the browser and on tablets, a
        /// farther view on a desktop, the far view's platform default). Keep the two in step.
        /// </summary>
        private static ClientSettings DefaultSettings()
        {
            var d = new ClientSettings();
            if (Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                d.Preset = QualityPreset.Low;
                if (BrowserDevice.IsMobileBrowser)
                {
                    d.ViewDistanceChunks = 3;
                    d.MusicMode = MusicMode.Synth;
                }
            }
            else
            {
                d.ViewDistanceChunks = ClientSettings.DesktopDefaultViewDistanceChunks;
            }

            d.FarViewBlocks = BlocksBeyondTheStars.Client.FarTerrain.FarViewRange.Normalize(d.FarViewBlocks, ClientSettings.FarViewLightDevice);
            return d;
        }

        private void ResetGraphics(ClientSettings d)
        {
            S.Preset = d.Preset;
            S.PresetAuto = true; // back to the automatic choice of a first run (#1423, #3279)
            S.Brightness = d.Brightness;
            S.Cosiness = d.Cosiness;
            S.DarkVisibility = d.DarkVisibility;
            S.ViewDistanceChunks = d.ViewDistanceChunks;
            SetFarView(d.FarViewBlocks);
            S.Smaa = d.Smaa;
            S.LensFlare = d.LensFlare;
            S.MotionBlur = d.MotionBlur;
            S.DistanceHaze = d.DistanceHaze;
            S.DistanceSoftness = d.DistanceSoftness;
            S.GroundDetails = d.GroundDetails;
            S.ApplyAtmosphereMode(d.Atmosphere); // the mode and every per-effect switch
            S.HighRelief = d.HighRelief;
            S.HighGrass = d.HighGrass;
            S.HighFlowers = d.HighFlowers;
            S.HighRoomReflections = d.HighRoomReflections;
            S.HighDecals = d.HighDecals;
            S.HighVolumetricLight = d.HighVolumetricLight;
            S.HighLampShadows = d.HighLampShadows;
            UrpScenePost.Instance?.SetBrightness(S.Brightness);
            S.ApplyCameraLook(); // SMAA
        }

        private void ResetDisplay(ClientSettings d)
        {
            S.Window = d.Window;
            S.VSync = d.VSync;
            S.FrameRateCap = d.FrameRateCap;
            S.UiScale = d.UiScale;
            S.ControlsHint = d.ControlsHint;
            S.ShowCoordinates = d.ShowCoordinates;
            S.ShowEnemyHealthBars = d.ShowEnemyHealthBars;
            S.ShowAllObjectives = d.ShowAllObjectives;
            S.ChatVisibility = d.ChatVisibility;
            ResetFeel(d, display: true); // resolution, monitor, focus pause
        }

        private void ResetAudio(ClientSettings d)
        {
            S.MasterVolume = d.MasterVolume;
            S.MusicVolume = d.MusicVolume;
            S.SfxVolume = d.SfxVolume;
            S.AmbienceVolume = d.AmbienceVolume;
            S.UiVolume = d.UiVolume;
            S.MusicMode = d.MusicMode;
            S.MuteAlarms = d.MuteAlarms;
            ClientAudio.Instance?.ApplyAlarmMute();
        }

        /// <summary>The mouse back to its defaults and every key AND pad binding cleared — the old "Reset key bindings".</summary>
        private void ResetControls(ClientSettings d)
        {
            S.MouseSensitivity = d.MouseSensitivity;
            S.InvertY = d.InvertY;
            S.ResetBindings();
            _rebinding = null;
            _rebindingPad = null;
        }

        private void ResetController(ClientSettings d)
        {
            S.PadDeadzone = d.PadDeadzone;
            S.PadLookX = d.PadLookX;
            S.PadLookY = d.PadLookY;
            S.PadInvertY = d.PadInvertY;
            S.PadTriggersMinePlace = d.PadTriggersMinePlace;
            S.PadGlyphs = d.PadGlyphs;
            S.PadGlyphsAuto = d.PadGlyphsAuto;
            S.PadVibration = d.PadVibration;
        }

        /// <summary>Voice back to its defaults. The muted players stay muted — that list is people, not a tuning.</summary>
        private void ResetVoice(ClientSettings d)
        {
            S.VoiceEnabled = d.VoiceEnabled;
            S.VoiceVolume = d.VoiceVolume;
            S.VoiceInputEnabled = d.VoiceInputEnabled;
            S.PushToTalkKey = d.PushToTalkKey;
        }

        private void ResetAccessibility(ClientSettings d)
        {
            S.CameraMotion = d.CameraMotion;
            S.ScreenShake = d.ScreenShake;
            S.FieldOfView = d.FieldOfView;
            S.ReduceFlashes = d.ReduceFlashes;
            S.ReducedEffects = d.ReducedEffects;
            S.StoryTextWaitForMe = d.StoryTextWaitForMe;
            ResetFeel(d, display: false); // key toggles, story text, signal colours
        }

        private void ResetGame(ClientSettings d)
        {
            S.AutoStowOnBoard = d.AutoStowOnBoard;
            S.NpcCalls = d.NpcCalls;
            SendNpcCalls();
            S.FestivalPreset = d.FestivalPreset; // the own list (FestivalCustom) is kept for the picker
            S.FestivalMood = d.FestivalMood;
            FestivalMood.Enabled = S.FestivalMood;
            FestivalMood.SendPrefs(S);
            _shell.RefreshMainMenu();
            S.ShowSessionTime = d.ShowSessionTime;
            S.PlaytimeReminder = d.PlaytimeReminder;
            S.ReminderMinutes = d.ReminderMinutes;
            S.SkinColor = d.SkinColor;
            S.TorsoColor = d.TorsoColor;
            S.ArmColor = d.ArmColor;
            S.LegColor = d.LegColor;
            S.UpdateCheckOnStart = d.UpdateCheckOnStart;
            S.UpdateFeedUrl = d.UpdateFeedUrl;
        }
    }
}
