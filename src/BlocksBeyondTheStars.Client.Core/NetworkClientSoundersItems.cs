// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The Sounders' story minigames on the wire (#3352): the server opens the note-key minigame for a Song Disc, the
    /// client answers with what the player spelled — or asks to skip after failed tries.</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>The server opened a story minigame (or re-sent it with a new failure count).</summary>
        public event Action<StoryMinigameStart>? StoryMinigameStartReceived;

        /// <summary>Reports a story minigame run: <paramref name="answers"/> are the spelled words, one per round.</summary>
        public void SendStoryMinigameResult(string game, string context, bool completed, IReadOnlyList<string>? answers)
        {
            var words = new string[answers?.Count ?? 0];
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = answers![i] ?? string.Empty;
            }

            Send(new StoryMinigameResultIntent { Game = game ?? string.Empty, Context = context ?? string.Empty, Completed = completed, Answers = words });
        }

        /// <summary>Asks to skip the story minigame (accepted by the server only after failed tries).</summary>
        public void SendStoryMinigameSkip(string game, string context)
            => Send(new StoryMinigameResultIntent { Game = game ?? string.Empty, Context = context ?? string.Empty, Skip = true });
    }
}
