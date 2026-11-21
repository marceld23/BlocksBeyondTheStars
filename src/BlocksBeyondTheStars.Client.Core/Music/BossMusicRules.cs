// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>
    /// When the boss-fight track plays (#3676, #3901). One track (<see cref="Track"/>, the finale's gauntlet piece) for
    /// both bosses: a guardian giant fighting near the player on foot, and a space boss — the escort cruiser — in the
    /// player's flight. The music director asks this after its own set-pieces: a cutscene's track and the finale's
    /// staged music come first, so a boss summoned into the Guardian's finale system never takes the finale's music
    /// away.
    /// </summary>
    public static class BossMusicRules
    {
        /// <summary>The boss fights' track (a file in <c>client/Music</c> without extension).</summary>
        public const string Track = "music_boss_gauntlet";

        /// <summary>Whether the boss track plays.</summary>
        /// <param name="finaleStaged">The finale's own music context is on — it wins.</param>
        /// <param name="inSpace">The player is in a flight (at the helm or on a spacewalk).</param>
        /// <param name="aboard">The player walks inside their ship.</param>
        /// <param name="giantFighting">A guardian giant fights near the player (its fight is not decided yet).</param>
        /// <param name="spaceBossFighting">A space boss fights in the player's flight.</param>
        public static bool Plays(bool finaleStaged, bool inSpace, bool aboard, bool giantFighting, bool spaceBossFighting)
        {
            if (finaleStaged)
            {
                return false;
            }

            return inSpace ? spaceBossFighting : !aboard && giantFighting;
        }
    }
}
