// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3394 — the speaker slot of VEGA's panel. VEGA's own lines keep her name and cyan; a line spoken by the player's robot
    /// companion shows the robot's name in its light blue (Justus: "the robot's name shows in the window when it answers
    /// VEGA"). The rule (who, which name, which colour) is <see cref="VegaSpeakerLabel"/> in Client.Core; this part only draws
    /// it, once per line as it comes up on screen.
    /// </summary>
    public sealed partial class VegaPanel
    {
        private TMPro.TMP_Text _speakerName; // the name over the speech text ("VEGA", or the robot's name)
        private Image _speakerAvatar;        // VEGA's icon beside it (tinted in the speaker's colour); null without the icon
        private string _speakerShown;        // the label last drawn (skip equal lines)

        /// <summary>Names the speaker of the line coming up on screen.</summary>
        private void ShowSpeaker(VegaSpeaker who)
        {
            if (_speakerName == null)
            {
                return;
            }

            string name = who.IsVega || who.Name.Length == 0 ? L(who.IsVega ? VegaSpeakerLabel.VegaNameKey : who.NameKey) : who.Name;
            Color color = who.IsVega ? UiKit.Cyan : SpeakerColor(who.Rgb);
            string signature = name + "|" + ColorUtility.ToHtmlStringRGB(color);
            if (signature == _speakerShown)
            {
                return;
            }

            _speakerShown = signature;
            _speakerName.text = name;
            _speakerName.color = color;
            if (_speakerAvatar != null)
            {
                _speakerAvatar.color = color;
            }
        }

        private static Color SpeakerColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
