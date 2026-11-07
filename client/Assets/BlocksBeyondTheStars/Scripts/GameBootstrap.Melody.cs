// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Sounders' music on the client (#3345/#3346): the server's melodies (<c>NoteSequence</c> — rune words, puzzle
    /// prompts, struck pillars, other players' melody-bar tones) go to the audio router's beat, and the melody bar's state
    /// (<c>MelodyPuzzleState</c>) to <see cref="MelodyBar"/>, which the HUD draws (<see cref="HudUi"/>'s melody bar). A
    /// tone the player plays sounds at once here and goes to the server, which judges it.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>What the melody bar shows — open, mode, progress, the prompt's lit tone, flashes, result lines.</summary>
        public MelodyBarModel MelodyBar { get; } = new MelodyBarModel();

        /// <summary>#3345: the key of the usable block under the crosshair this frame (a rune stone, a Song Spindle), else null
        /// — the HUD's prompt names its use (<c>ui.use.&lt;key&gt;</c>).</summary>
        public string AimedUseBlockKey;

        /// <summary>The melody subscriptions; called from <see cref="WireKnowledge"/>.</summary>
        private void WireMelody()
        {
            Network.NoteSequenceReceived += m => ClientAudio.Instance?.PlayNoteSequence(m, ScenePos(m.X, m.Y, m.Z));
            Network.MelodyPuzzleStateReceived += m => MelodyBar.Apply(m, Time.unscaledTimeAsDouble);
            Network.WorldResetReceived += _ => ForgetMelodies();
            Network.JoinAccepted += _ => ForgetMelodies();
        }

        /// <summary>A world change or a fresh join: the bar closes, waiting melodies are dropped (the server ended the puzzle).</summary>
        private void ForgetMelodies()
        {
            MelodyBar.Reset();
            ClientAudio.Instance?.StopNoteSequences();
        }

        /// <summary>The player played <paramref name="tone"/> (0–7) on the melody bar: it sounds and flashes at once, and the
        /// server gets it (a running puzzle judges it; in free play the players nearby hear it).</summary>
        public void PlayMelodyTone(int tone)
        {
            if (tone < 0 || tone >= MelodyBarInput.Tones || !MelodyBar.Open)
            {
                return;
            }

            ClientAudio.Instance?.PlayMelodyTone(MelodyBar.Instrument, tone);
            MelodyBar.Flash(tone, Time.unscaledTimeAsDouble);
            Network?.SendMelodyNote(tone, MelodyBar.PuzzleId, MelodyBar.Instrument);
        }

        /// <summary>The player closes the bar: a running puzzle ends on the server (it answers with the closing state), free
        /// play simply closes.</summary>
        public void LeaveMelodyBar()
        {
            if (MelodyBar.FreePlay)
            {
                MelodyBar.CloseFreePlay();
            }
            else if (MelodyBar.Open)
            {
                Network?.SendMelodyNote(-1, MelodyBar.PuzzleId);
            }
        }
    }
}
