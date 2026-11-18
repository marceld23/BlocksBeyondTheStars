// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The figure's voice (#3767): the two sliders of the character editor's Figure tab — the pitch from low to high and
    /// the age from young to old, 0…100 each, the middle (50) the sound the game always had. They retune the figure's own
    /// mouth and breath sounds (<see cref="PlayerVoice"/>) and travel with the colours, so other players hear a hit and a
    /// fall in this voice. Serialized with the rest of the class (JsonUtility); an older settings file leaves the middle.
    /// </summary>
    public sealed partial class ClientSettings
    {
        /// <summary>The voice's pitch, 0 (low) … 100 (high).</summary>
        public int VoicePitch = PlayerVoice.Middle;

        /// <summary>The voice's age, 0 (young) … 100 (old).</summary>
        public int VoiceAge = PlayerVoice.Middle;

        /// <summary>Both sliders packed for the wire and the audio layer (<see cref="PlayerVoice.Pack"/>).</summary>
        public int VoicePacked => PlayerVoice.Pack(VoicePitch, VoiceAge);
    }
}
