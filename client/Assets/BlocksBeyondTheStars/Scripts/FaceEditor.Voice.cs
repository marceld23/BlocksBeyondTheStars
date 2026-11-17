// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The voice column of the appearance screen's Figure tab (#3767 — Justus: "I want to choose in the skin editor what
    /// the sounds the figure makes with its mouth are like"). Two sliders instead of categories (<see cref="PlayerVoice"/>):
    /// the <b>pitch</b> from low to high and the <b>age</b> from young to old, in steps of ten, with three quick buttons
    /// — Low · Middle · High — that set the pitch. Every change is heard at once (one breath in the new voice) and
    /// applies at once: the voice is the player's, not a part of the painted look, so it is not waiting for Save.
    /// The column stands beside the colour rows (x 490…790), where both hosts — the menu's designer and the in-world
    /// screen — have room.
    /// </summary>
    public sealed partial class FaceEditor
    {
        private const float VoiceX = 490f, VoiceW = 300f, VoiceButtonW = 96f, VoiceButtonGap = 6f;

        private ClientSettings _voiceSettings;
        private Action _voiceChanged;
        private Slider _voicePitch;

        /// <summary>The host hands over where the voice is kept and what to do when it changes (save, or save and tell
        /// the server). Without it the Figure tab has no voice column.</summary>
        public void UseVoiceOf(ClientSettings settings, Action changed)
        {
            _voiceSettings = settings;
            _voiceChanged = changed;
        }

        /// <summary>Builds the column: the kicker, the three quick buttons, the two labelled sliders and "Listen".</summary>
        private void BuildVoiceColumn(Transform card)
        {
            if (_voiceSettings == null)
            {
                return;
            }

            float y = 20f;
            UiKit.AddKicker(card, VoiceX, y, VoiceW, L("ui.avatar.voice"));
            y += 34f;
            string[] keys = { "ui.avatar.voice_low", "ui.avatar.voice_middle", "ui.avatar.voice_high" };
            int[] pitches = { PlayerVoice.Low, PlayerVoice.Middle, PlayerVoice.High };
            for (int i = 0; i < keys.Length; i++)
            {
                int pitch = pitches[i];
                UiKit.AddTierButton(card, VoiceX + (i * (VoiceButtonW + VoiceButtonGap)), y, VoiceButtonW, 44f, L(keys[i]),
                    () => _voicePitch.value = pitch / PlayerVoice.Step, ButtonTier.Quiet); // moving the slider applies it
            }

            y += 56f;
            UiKit.AddText(card, VoiceX, y, VoiceW, 26f, L("ui.avatar.voice_pitch"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _voicePitch = VoiceSlider(card, y + 30f, _voiceSettings.VoicePitch, v => ChangeVoice(v, _voiceSettings.VoiceAge));
            y += 64f;
            UiKit.AddText(card, VoiceX, y, VoiceW, 26f, L("ui.avatar.voice_age"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            VoiceSlider(card, y + 30f, _voiceSettings.VoiceAge, v => ChangeVoice(_voiceSettings.VoicePitch, v));
            y += 64f;
            UiKit.AddTierButton(card, VoiceX, y, VoiceW, 44f, L("ui.avatar.voice_listen"), PlayVoiceSample, ButtonTier.Secondary);
        }

        /// <summary>A slider of eleven whole steps (0, 10 … 100) — the look of the world options' sliders.</summary>
        private static Slider VoiceSlider(Transform card, float y, int value, Action<int> changed)
        {
            var go = new GameObject("VoiceSlider", typeof(RectTransform));
            go.transform.SetParent(card, false);
            UiKit.Place(go, VoiceX, y, VoiceW, 16f);
            UiKit.AddImage(go.transform, 0f, 4f, VoiceW, 8f, UiKit.SolidSprite, new Color(0.10f, 0.16f, 0.24f, 1f));

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.25f);
            fillRt.anchorMax = new Vector2(1f, 0.75f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = UiKit.SolidSprite;
            fill.color = UiKit.Cyan;

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(18f, 10f); // the slider stretches the handle over its 16 px height: 26 px high
            var handle = handleGo.AddComponent<Image>();
            handle.sprite = UiKit.SolidSprite;
            handle.color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.targetGraphic = handle;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.minValue = 0;
            slider.maxValue = PlayerVoice.Max / PlayerVoice.Step;
            slider.wholeNumbers = true;
            slider.value = PlayerVoice.Snap(value) / PlayerVoice.Step;
            slider.onValueChanged.AddListener(v => changed(Mathf.RoundToInt(v) * PlayerVoice.Step));
            return slider;
        }

        /// <summary>A slider moved (or a quick button moved it): keep it, tell the host, and let it be heard.</summary>
        private void ChangeVoice(int pitch, int age)
        {
            _voiceSettings.VoicePitch = PlayerVoice.Clamp(pitch);
            _voiceSettings.VoiceAge = PlayerVoice.Clamp(age);
            _voiceChanged?.Invoke();
            PlayVoiceSample();
        }

        /// <summary>One breath in the voice the sliders show.</summary>
        private void PlayVoiceSample()
        {
            if (ClientAudio.Instance != null)
            {
                ClientAudio.Instance.VoiceSample(_voiceSettings.VoicePacked);
            }
            else
            {
                UiSound.VoiceSample(_voiceSettings.VoicePacked); // the main menu's designer: no world, no ClientAudio
            }
        }
    }
}
