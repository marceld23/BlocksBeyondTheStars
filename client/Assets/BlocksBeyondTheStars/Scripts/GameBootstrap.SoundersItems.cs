// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Sounders' items on the client (#3352/#3353): the server opens the note-key minigame for a Song Disc a Song Spindle
    /// sings (<see cref="StoryMinigameUI"/>), and re-sends it with a new failure count after a failed try — the open window
    /// takes the count; a new world closes the window. The Resonance Blaster in hand opens the melody bar for free play in the
    /// Sounder voice; putting it away closes the bar again (only the bar it opened).
    /// </summary>
    public sealed partial class GameBootstrap
    {
        private bool _blasterOpenedMelodyBar;

        /// <summary>The story-minigame subscriptions; called from <see cref="WireKnowledge"/>.</summary>
        private void WireSoundersItems()
        {
            Network.StoryMinigameStartReceived += m => StoryMinigameUI.Show(this, m);
            Network.JoinAccepted += _ => StoryMinigameUI.CloseCurrent();
        }

        /// <summary>#3353: the selected quick-bar item changed (PlayerController.RefreshHeldItem) — the Resonance Blaster opens
        /// the melody bar for free play while it is held.</summary>
        public void OnHeldItemChangedForSounders(string itemKey)
        {
            if (itemKey == SoundersIds.ResonanceBlaster)
            {
                if (!MelodyBar.Open)
                {
                    MelodyBar.OpenFreePlay(SoundersIds.InstrumentVoice);
                    _blasterOpenedMelodyBar = MelodyBar.FreePlay;
                }

                return;
            }

            if (_blasterOpenedMelodyBar)
            {
                _blasterOpenedMelodyBar = false;
                MelodyBar.CloseFreePlay();
            }
        }
    }
}
