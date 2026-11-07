// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The Sounders' music on the wire (#3345/#3346): the use key on a usable block, melodies the server plays at a
    /// place, and the melody bar's tones and state.</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>A melody to play at a place — a rune's word, a puzzle prompt, a Song Disc, a tone someone struck.</summary>
        public event Action<NoteSequence>? NoteSequenceReceived;

        /// <summary>The melody bar's state: a puzzle opened it, a note was judged, a prompt plays, the puzzle ended.</summary>
        public event Action<MelodyPuzzleState>? MelodyPuzzleStateReceived;

        /// <summary>The use key on the usable block at the cell (a rune stone, a Song Spindle, a melody-site pillar).</summary>
        public void SendUseBlock(int x, int y, int z) => Send(new UseBlockIntent { X = x, Y = y, Z = z });

        /// <summary>A tone (0–7) of the melody bar for the running puzzle <paramref name="puzzleId"/> (empty: free play,
        /// sounded with <paramref name="instrument"/> for the players nearby); -1 leaves the puzzle.</summary>
        public void SendMelodyNote(int tone, string puzzleId, int instrument = 0)
            => Send(new MelodyNoteIntent { Tone = tone, PuzzleId = puzzleId ?? string.Empty, Instrument = instrument });
    }
}
