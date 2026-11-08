// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The melody bar of <see cref="HudUi"/> (#3346): eight tones C … C' above the hotbar, each in its own colour AND with
    /// its own symbol (never colour alone, <see cref="MelodyBarModel.Symbols"/>), its key under it. The server opens it
    /// (a melody puzzle — <see cref="GameBootstrap.MelodyBar"/>) and closes it; while it is open the number keys 1–8 play the
    /// tones instead of switching the hotbar, the pad's d-pad plays 1–4 and the same with a shoulder held 5–8
    /// (<see cref="MelodyBarInput"/>), and touch taps the tones. The prompt's tones light up one after the other as the
    /// server sings it; a played tone flashes; a line says "Again!", "Next round!", "Well done!". The player keeps walking
    /// and looking — the bar takes no cursor, so the use key still strikes a shrine's pillars.
    /// </summary>
    public sealed partial class HudUi
    {
        private const float MelodyToneSize = 66f, MelodyTonePitch = 74f, MelodyBarW = 620f, MelodyBarH = 132f;

        /// <summary>#3426: how the spelling line marks the letter the player is at (TMP rich text — the warm chip yellow).</summary>
        private const string MelodySpellMarkOpen = "<color=#ffd27a><b>", MelodySpellMarkClose = "</b></color>";

        private GameObject _melodyPanel;
        private TMP_Text _melodyTitle, _melodyProgress, _melodyLine;
        private Image[] _melodyTones;
        private Text[] _melodyCaptions;
        private Button _melodyStop;
        private readonly MelodyPadReader _melodyPad = new MelodyPadReader();
        private int _melodyShownVersion = -1;
        private string _melodyShownLine;
        private InputDeviceKind _melodyShownDevice = (InputDeviceKind)(-1);

        /// <summary>#3430: whether the melody bar is on screen. Its top row shares the screen band of VEGA's objective chip
        /// and folded tab (<see cref="VegaPanel.ChipY"/>), whose canvas draws above the HUD — <see cref="VegaPanel"/> polls
        /// this and steps that row aside while the bar is up; the slot-action badge and the controls line's hotbar segment
        /// give way too (the number keys play tones then).</summary>
        public bool MelodyBarShown => _melodyPanel != null && _melodyPanel.activeSelf;

        private void BuildMelodyBar(Transform root)
        {
            // Above the hotbar's backplate (its cells start at H − 112), centred like it.
            float y = H - 72f - 40f - 12f - MelodyBarH - 10f;
            _melodyPanel = Panel(root, (W - MelodyBarW) / 2f, y, MelodyBarW, MelodyBarH).gameObject;
            var t = _melodyPanel.transform;
            _melodyTitle = UiText.Add(t, 14, 4, 360, 24, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, UiText.Look.Glow);
            _melodyProgress = UiText.Add(t, 300, 4, 200, 24, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Normal, UiText.Look.Outline);
            _melodyStop = UiKit.AddButton(t, MelodyBarW - 110f, 3f, 96f, 26f, string.Empty, () => Game?.LeaveMelodyBar());

            _melodyTones = new Image[MelodyBarInput.Tones];
            _melodyCaptions = new Text[MelodyBarInput.Tones];
            float x0 = (MelodyBarW - ((MelodyBarInput.Tones - 1) * MelodyTonePitch) - MelodyToneSize) / 2f;
            for (int i = 0; i < MelodyBarInput.Tones; i++)
            {
                int tone = i;
                var button = UiKit.AddButton(t, x0 + (i * MelodyTonePitch), 34f, MelodyToneSize, MelodyToneSize, string.Empty,
                    () => Game?.PlayMelodyTone(tone)); // touch and a free cursor tap the tones
                _melodyTones[i] = button.targetGraphic as Image;
                if (_melodyTones[i] != null)
                {
                    _melodyTones[i].color = ToneColor(i, 0.85f);
                }

                UiKit.AddText(button.transform, 0f, 2f, MelodyToneSize, 42f, MelodyBarModel.Symbols[i], 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                _melodyCaptions[i] = UiKit.AddText(button.transform, 0f, 42f, MelodyToneSize, 22f, string.Empty, 13, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            _melodyLine = UiText.Add(t, 14, 104, MelodyBarW - 28f, 24, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Normal, UiText.Look.Outline);
            _melodyPanel.SetActive(false);
        }

        /// <summary>Per frame (keys must not wait for the throttled refresh): shows or hides the bar, lights the prompt and the
        /// flashes, claims the keys while it is open, and plays the pressed tones.</summary>
        private void UpdateMelodyBar(bool hudShown)
        {
            if (_melodyPanel == null || Game == null)
            {
                return;
            }

            var bar = Game.MelodyBar;
            double now = Time.unscaledTimeAsDouble;
            bool visible = hudShown && bar.Visible(now) && !Game.SpaceViewActive;
            if (_melodyPanel.activeSelf != visible)
            {
                _melodyPanel.SetActive(visible);
                if (visible)
                {
                    UiKit.TransitionIn(_melodyPanel);
                }
            }

            if (!visible)
            {
                _melodyPad.Reset();
                return;
            }

            RefreshMelodyTexts(bar, now);
            AnimateMelodyTones(bar, now);
            bool takesKeys = bar.Open && !Game.MenuOpen && !Game.ChatTyping && !OnScreenKeyboardUi.IsOpen && !InputMap.TextEntryActive;
            if (takesKeys)
            {
                InputMap.MelodyBarFrame = Time.frameCount; // 1–8, the d-pad and the shoulders are the bar's now
                ReadMelodyKeys();
            }
            else
            {
                _melodyPad.Reset();
            }
        }

        /// <summary>The number keys and the pad.</summary>
        private void ReadMelodyKeys()
        {
            for (int i = 0; i < MelodyBarInput.Tones; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Game.PlayMelodyTone(i);
                }
            }

            bool shoulder = InputMap.PadHeld(PadButton.Lb) || InputMap.PadHeld(PadButton.Rb);
            int padTone = _melodyPad.Update(InputMap.PadDpadX(), InputMap.PadDpadY(), shoulder);
            if (padTone >= 0)
            {
                Game.PlayMelodyTone(padTone);
            }
        }

        /// <summary>The prompt's lit tone and a flashing tone grow and glow; the others rest.</summary>
        private void AnimateMelodyTones(MelodyBarModel bar, double now)
        {
            int lit = bar.LitTone(now);
            for (int i = 0; i < _melodyTones.Length; i++)
            {
                var image = _melodyTones[i];
                if (image == null)
                {
                    continue;
                }

                bool on = i == lit || bar.Flashing(i, now);
                image.color = ToneColor(i, on ? 1f : 0.62f);
                float scale = on && !UiKit.ReducedMotion ? 1.14f : 1f;
                image.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>The title, the progress, the captions under the tones (the keys of the device in hand) and the hint or
        /// result line — rebuilt only when something changed.</summary>
        private void RefreshMelodyTexts(MelodyBarModel bar, double now)
        {
            var loc = Game.Localizer;
            if (loc == null)
            {
                return;
            }

            var device = InputMap.ActiveDevice;
            string resultKey = bar.ResultKey(now);
            string result = resultKey != null ? loc.Get(resultKey) : null;

            // #3426: a melody that spells a word (ORNEMIS) shows every letter's two notes in the hint row, the letter the
            // player is at marked; a result line then flashes in the title's place, so the spelling never goes away.
            string spelling = bar.SpellingLine(MelodyNoteNames(loc), MelodySpellMarkOpen, MelodySpellMarkClose);
            bool spells = spelling.Length > 0;
            string title = spells && result != null ? result : loc.Get(bar.TitleKey);
            string line = spells ? spelling : result ?? loc.Get(MelodyHintKey(device));
            string shown = title + "\n" + line;
            if (bar.Version == _melodyShownVersion && device == _melodyShownDevice && shown == _melodyShownLine)
            {
                return;
            }

            _melodyShownVersion = bar.Version;
            _melodyShownDevice = device;
            _melodyShownLine = shown;
            _melodyTitle.text = title;
            _melodyProgress.text = bar.FreePlay || bar.Expected <= 0
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, loc.Get("ui.melody.progress"), bar.Round + 1, bar.RoundCount, bar.Entered, bar.Expected);
            _melodyLine.fontSize = spells ? 13f : 14f; // seven letters with two note names each fit the bar's width
            _melodyLine.text = line;
            SetButtonText(_melodyStop, loc.Get("ui.melody.stop"));
            for (int i = 0; i < _melodyCaptions.Length; i++)
            {
                _melodyCaptions[i].text = MelodyCaption(loc, i, device);
            }
        }

        /// <summary>The eight note names C … C' in the player's language (<c>ui.note.names</c>: German says H where English says
        /// B) — the tones' captions and the spelling line (#3426) read the same names.</summary>
        private static string[] MelodyNoteNames(Localizer loc)
        {
            string[] names = loc.Get("ui.note.names").Split(' ');
            return names.Length == MelodyBarInput.Tones ? names : BlocksBeyondTheStars.Shared.Sounders.SounderAlphabet.ToneNames;
        }

        /// <summary>A tone's caption: its key ("1") or pad chord ("↑", "RB↑") and its note name in the player's language
        /// (<c>ui.note.names</c>: German says H where English says B).</summary>
        private static string MelodyCaption(Localizer loc, int tone, InputDeviceKind device)
        {
            string note = MelodyNoteNames(loc)[tone];
            if (device == InputDeviceKind.Touch)
            {
                return note;
            }

            if (device == InputDeviceKind.Gamepad)
            {
                var (direction, shoulder) = MelodyBarInput.PadForTone(tone);
                string arrow = direction switch
                {
                    PadDirection.Up => "↑",
                    PadDirection.Right => "→",
                    PadDirection.Down => "↓",
                    _ => "←",
                };
                string rb = InputMap.PadGlyph(GamepadInputSource.CodeOf(PadButton.Rb)); // the pad family's own name for it
                return (shoulder ? rb + arrow : arrow) + " " + note;
            }

            return MelodyBarInput.DigitForTone(tone).ToString(CultureInfo.InvariantCulture) + " · " + note;
        }

        /// <summary>#3345: the prompt line's name for the use of a usable block — <c>ui.use.&lt;block key&gt;</c> ("play the
        /// rune") when the locale has one, else the plain "use".</summary>
        private static string UseBlockLabelKey(Localizer loc, string blockKey)
        {
            string key = "ui.use." + blockKey;
            return !string.IsNullOrEmpty(blockKey) && loc.Has(key) ? key : "ui.use.block";
        }

        private static string MelodyHintKey(InputDeviceKind device) => device switch
        {
            InputDeviceKind.Gamepad => "ui.melody.hint.pad",
            InputDeviceKind.Touch => "ui.melody.hint.touch",
            _ => "ui.melody.hint.keys",
        };

        private static Color ToneColor(int tone, float brightness)
        {
            var (r, g, b) = MelodyBarModel.ColorOf(tone);
            return new Color(r * brightness, g * brightness, b * brightness, 1f);
        }
    }
}
