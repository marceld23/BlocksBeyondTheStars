// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3405 (Justus: "you can communicate with the robot from the endgame"): the dialogue panel also holds the conversation
    /// with the own robot. It arrives with the reserved <see cref="NpcDialogState.RobotNpcId"/>; the panel then names the
    /// robot in its light blue (the colour VEGA's panel uses for it, <see cref="RobotChatter.SpeakerRgb"/>) and shows no
    /// speech bubble over a person. Unity-free so the headless suite covers it.
    /// </summary>
    public static class RobotTalkPanel
    {
        /// <summary>Whether a dialogue step with this id is the robot's.</summary>
        public static bool IsRobot(int npcId) => npcId == NpcDialogState.RobotNpcId;

        /// <summary>The title's colour (0xRRGGBB) for this id — the robot's light blue, or null for a person (the panel's cyan).</summary>
        public static int? TitleRgb(int npcId) => IsRobot(npcId) ? RobotChatter.SpeakerRgb : (int?)null;
    }
}
