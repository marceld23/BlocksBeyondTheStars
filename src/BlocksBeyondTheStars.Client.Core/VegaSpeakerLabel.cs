// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The speaker VEGA's panel names over a line (#3394): VEGA herself (her name key, her cyan), or another voice —
    /// the player's robot companion by its own name in its light blue.</summary>
    public readonly struct VegaSpeaker
    {
        public VegaSpeaker(bool isVega, string name, string nameKey, int rgb)
        {
            IsVega = isVega;
            Name = name ?? string.Empty;
            NameKey = nameKey ?? string.Empty;
            Rgb = rgb;
        }

        /// <summary>VEGA speaks: the panel shows her own label and colour.</summary>
        public bool IsVega { get; }

        /// <summary>The name to show (already safe for rich text); empty = localize <see cref="NameKey"/>.</summary>
        public string Name { get; }

        /// <summary>The locale key of the name when <see cref="Name"/> is empty.</summary>
        public string NameKey { get; }

        /// <summary>The name's colour (0xRRGGBB); unused for VEGA (the panel keeps her cyan).</summary>
        public int Rgb { get; }
    }

    /// <summary>
    /// The rule behind the speaker label of VEGA's panel (#3394, Justus: "the robot's name shows in the window when it answers
    /// VEGA"). A line with an empty <see cref="ShipAiLine.Speaker"/> and no <see cref="ShipAiLine.SpeakerName"/> is VEGA's —
    /// so is a story pack's narrator id the client has no own look for (#2466). The robot's line shows its name (the default
    /// name of the species when the server sent none) in its light blue; any other named voice in a neutral pale tone.
    /// Unity-free so the headless suite covers it.
    /// </summary>
    public static class VegaSpeakerLabel
    {
        /// <summary>VEGA's own name label.</summary>
        public const string VegaNameKey = "ui.vega.name";

        /// <summary>The robot's species name — what its label reads when the server sent no name.</summary>
        public const string RobotNameKey = "creature.robot.name";

        /// <summary>A named voice that is not the robot.</summary>
        public const int GuestRgb = 0xE8E2C8;

        /// <summary>The longest name shown (the server keeps companion names to 24 characters as well).</summary>
        public const int MaxNameLength = 24;

        /// <summary>VEGA as the speaker.</summary>
        public static readonly VegaSpeaker Vega = new VegaSpeaker(true, string.Empty, VegaNameKey, 0);

        /// <summary>Who speaks a line with these <see cref="ShipAiLine.Speaker"/> and <see cref="ShipAiLine.SpeakerName"/>.</summary>
        public static VegaSpeaker For(string? speaker, string? speakerName)
        {
            string name = Clean(speakerName);
            bool robot = string.Equals(speaker, RobotChatter.SpeakerId, System.StringComparison.Ordinal);
            if (name.Length == 0 && !robot)
            {
                return Vega;
            }

            return new VegaSpeaker(false, name, name.Length == 0 ? RobotNameKey : string.Empty, robot ? RobotChatter.SpeakerRgb : GuestRgb);
        }

        /// <summary>Whether a line is the robot's chatter (its own line, or VEGA's question to it) — small talk that stays out of
        /// the Journal's news badge, VEGA's tips log and the story flashback.</summary>
        public static bool IsCompanionChatter(ShipAiLine? line)
            => line != null && (RobotChatter.IsChatterKey(line.LineKey) || !string.IsNullOrEmpty(line.SpeakerName));

        private static string Clean(string? name)
        {
            string s = (name ?? string.Empty).Trim();
            if (s.Length > MaxNameLength)
            {
                s = s.Substring(0, MaxNameLength);
            }

            return ChatMarkup.RichSafe(s);
        }
    }
}
