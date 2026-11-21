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
    /// The other side on the HUD (#3906, #3908) — the HUD keeps its colours while the world is grey:
    /// <list type="bullet">
    /// <item><b>The mark</b>: a calm chip under the top toast lane, "On the other side", while the player is over there —
    /// with the sound-block meter (how many may still be shaped) while the Resonance Blaster is held.</item>
    /// <item><b>The way back</b>: the entry gate as a white blip on the compass and, while the shared caption row is free,
    /// "Your rune gate 34 m" under it. (In the world the entry gate carries the brightest light — <see cref="OtherSideFx"/>.)</item>
    /// <item><b>The palette</b>: over there the melody bar (it shows while the blaster is held) draws its eight tones in the
    /// brightness of the sound blocks they shape (<c>OtherSideRules.ToneGlow</c>), rings the tone played last — the next
    /// block's tone — shows the meter and says which key shapes and which dissolves.</item>
    /// </list>
    /// The need warnings (hunger, air, cold) stay quiet over there: <c>SetVital</c> and <c>UpdateOxygenAlarm</c> ask
    /// <see cref="OtherSideView.ShowsNeedWarnings"/>.
    /// </summary>
    public sealed partial class HudUi
    {
        private const float OtherSideChipW = 440f, OtherSideChipH = 30f;

        /// <summary>The chip's top edge: under the boss bar's lane (which is under the research toast).</summary>
        private const float OtherSideChipY = BossBarY + BossBarH + 6f;

        private static readonly Color OtherSideInk = new Color(0.94f, 0.96f, 1f);
        private static readonly Color PaletteMark = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color PaletteDarkInk = new Color(0.08f, 0.09f, 0.12f);

        private GameObject _otherSideChip;
        private TMP_Text _otherSideChipText;
        private string _otherSideChipShown, _otherSideChipCount;
        private object _otherSideChipLoc;
        private RectTransform _compassGate;
        private TMP_Text _compassGateDist;
        private int _lastCompassGateDist = int.MinValue;
        private string _otherSideCount;
        private int _otherSideCountVersion = -1;
        private object _otherSideCountLoc;
        private string _otherSideHint;
        private object _otherSideHintLoc;
        private InputDeviceKind _otherSideHintDevice = (InputDeviceKind)(-1);
        private Text[] _melodySymbols;
        private bool _paletteInk;

        private void BuildOtherSideChip(Transform root)
        {
            _otherSideChip = Panel(root, (W - OtherSideChipW) / 2f, OtherSideChipY, OtherSideChipW, OtherSideChipH).gameObject;
            _otherSideChip.name = "OtherSideChip";
            _otherSideChipText = UiText.Add(_otherSideChip.transform, 8, 3, OtherSideChipW - 16f, OtherSideChipH - 6f, string.Empty, 15,
                OtherSideInk, TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Outline);
            _otherSideChip.SetActive(false);
        }

        /// <summary>Per frame: the chip shows while the player is over there (and the HUD is shown); its text changes only
        /// when the meter or the held item does.</summary>
        private void UpdateOtherSideChip(bool show)
        {
            if (_otherSideChip == null || Game == null)
            {
                return;
            }

            var view = Game.OtherSide;
            bool on = show && view.Over && !Game.SpaceViewActive;
            if (_otherSideChip.activeSelf != on)
            {
                _otherSideChip.SetActive(on);
            }

            var loc = Game.Localizer;
            if (!on || loc == null)
            {
                return;
            }

            string count = HoldsSoundBlaster() ? OtherSideCountText(loc) : null;
            if (_otherSideChipShown == null || !ReferenceEquals(count, _otherSideChipCount) || !ReferenceEquals(loc, _otherSideChipLoc))
            {
                _otherSideChipCount = count;
                _otherSideChipLoc = loc;
                _otherSideChipShown = count != null ? loc.Get("ui.otherside.chip") + " · " + count : loc.Get("ui.otherside.chip");
                _otherSideChipText.text = _otherSideChipShown;
            }
        }

        /// <summary>Whether the selected hotbar item is the Resonance Blaster.</summary>
        private bool HoldsSoundBlaster()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && BlocksBeyondTheStars.Shared.State.ItemKey.Base(held) == BlocksBeyondTheStars.Shared.Sounders.SoundersIds.ResonanceBlaster;
        }

        /// <summary>The sound-block meter — how many the player may still shape ({0} left, {1} the cap; rebuilt only when
        /// the server sent a new state) — or null while the server names no cap.</summary>
        private string OtherSideCountText(Localizer loc)
        {
            var view = Game.OtherSide;
            if (view.SoundBlockCap <= 0)
            {
                return null;
            }

            if (view.Version != _otherSideCountVersion || !ReferenceEquals(loc, _otherSideCountLoc))
            {
                _otherSideCountVersion = view.Version;
                _otherSideCountLoc = loc;
                _otherSideCount = string.Format(CultureInfo.InvariantCulture, loc.Get("ui.otherside.count"), view.SoundBlocksLeft, view.SoundBlockCap);
            }

            return _otherSideCount;
        }

        // ---------------- The way back on the compass ----------------

        /// <summary>The entry gate on the compass (from <see cref="RefreshCompass"/>): a white blip, and its distance on the
        /// shared caption row while the core, the story marker and the fragment signal leave it free.</summary>
        private void RefreshCompassGate(float radius)
        {
            var view = Game.OtherSide;
            bool have = view.HasGate;
            if (!have && _compassGate == null)
            {
                return; // never been over there: nothing was built
            }

            if (_compassGate == null)
            {
                _compassGate = Blip(_compassParent, Color.white, 10f);
                _compassGateDist = UiText.Add(_compassParent, -10, HudRightColumn.CaptionY(2), 140, HudRightColumn.CaptionRowH, string.Empty, 12,
                    OtherSideInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            PlaceBlip(_compassGate, have, new Vector3(view.GateX, view.GateY, view.GateZ), radius, out float dist);
            bool rowFree = _lastCompassCoreDist < 0 && _lastCompassStoryDist < 0 && _lastCompassFragDist < 0;
            int distNow = have && rowFree ? Mathf.RoundToInt(dist) : -1;
            if (distNow != _lastCompassGateDist)
            {
                _lastCompassGateDist = distNow;
                _compassGateDist.text = distNow >= 0 ? $"{CompassLabel("ui.otherside.gate_marker", "Your rune gate")} {distNow} m" : string.Empty;
            }
        }

        // ---------------- The melody bar as the palette ----------------

        /// <summary>The tone the bar rings as "the next block's tone" — the tone played last (<see cref="SoundPalette"/>) —
        /// while the bar is the palette: over there, in free play (the Resonance Blaster in hand). −1 otherwise.</summary>
        private int SoundPaletteTone(MelodyBarModel bar)
            => Game.OtherSide.Over && bar.FreePlay ? Game.SoundPalette.Tone : -1;

        /// <summary>A tone's key in the palette: the brightness of the sound block it shapes, a little lifted while it sounds.</summary>
        private static Color PaletteToneColor(int tone, bool sounding)
        {
            var (r, g, b) = SoundPalette.ColorOf(tone);
            float k = sounding ? 1.12f : 1f;
            return new Color(Mathf.Min(1f, r * k), Mathf.Min(1f, g * k), Mathf.Min(1f, b * k), 1f);
        }

        /// <summary>The symbols and captions on the keys are white; on the palette's light keys they turn dark so they
        /// stay readable. Runs only when the bar becomes the palette or stops being it.</summary>
        private void ApplyPaletteInk(bool palette)
        {
            if (palette == _paletteInk || _melodyTones == null)
            {
                return;
            }

            _paletteInk = palette;
            if (_melodySymbols == null)
            {
                _melodySymbols = new Text[_melodyTones.Length];
                for (int i = 0; i < _melodyTones.Length; i++)
                {
                    _melodySymbols[i] = FindMelodySymbol(i);
                }
            }

            for (int i = 0; i < _melodyTones.Length; i++)
            {
                var (r, g, b) = SoundPalette.ColorOf(i);
                bool light = palette && (0.299f * r) + (0.587f * g) + (0.114f * b) > 0.6f;
                var ink = light ? PaletteDarkInk : Color.white;
                if (_melodySymbols[i] != null)
                {
                    _melodySymbols[i].color = ink;
                }

                if (_melodyCaptions != null && _melodyCaptions[i] != null)
                {
                    _melodyCaptions[i].color = ink;
                }
            }
        }

        /// <summary>The symbol label on a tone's key (the text that shows the tone's symbol), or null.</summary>
        private Text FindMelodySymbol(int tone)
        {
            if (_melodyTones[tone] == null)
            {
                return null;
            }

            foreach (var label in _melodyTones[tone].GetComponentsInChildren<Text>(true))
            {
                if (label.text == MelodyBarModel.Symbols[tone])
                {
                    return label;
                }
            }

            return null;
        }

        /// <summary>The palette's hint row: which key shapes, which dissolves, and that a tone picks the next block's tone
        /// (the key tokens expanded for the device in hand; rebuilt only when the language or the device changes).</summary>
        private string OtherSideBarHint(Localizer loc, InputDeviceKind device)
        {
            if (_otherSideHint == null || device != _otherSideHintDevice || !ReferenceEquals(loc, _otherSideHintLoc))
            {
                _otherSideHintDevice = device;
                _otherSideHintLoc = loc;
                _otherSideHint = ExpandKeyTokens(loc, loc.Get("ui.otherside.bar_hint"));
            }

            return _otherSideHint;
        }
    }
}
