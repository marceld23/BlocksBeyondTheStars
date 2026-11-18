// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions
{
    /// <summary>
    /// The death rule of a Hard Survival world (#3765, <c>DeathPenalty.Revive</c> — a young player's wish: "a death means
    /// you can look at the world but nothing else; in multiplayer the others can bring you back"). A player who dies on
    /// foot on a world does not wake up at the ship: they have <b>fallen</b> and watch — flying freely, unseen, touching
    /// nothing — until another player uses a medkit at the place they fell. Nobody else left standing changes that:
    /// alone, the fallen player may start again at the ship after <see cref="AloneSeconds"/>; when every player has
    /// fallen, all of them may at once. One place for the numbers the server rules by and the client counts down with.
    /// </summary>
    public static class FallenRules
    {
        /// <summary>Seconds a fallen player watches before they may start again at the ship, when nobody is left who could
        /// bring them back (singleplayer, or the others have gone).</summary>
        public const double AloneSeconds = 60.0;

        /// <summary>Seconds between two pulses of the light that marks the place somebody fell.</summary>
        public const double BeaconSeconds = 2.0;

        /// <summary>Hunger a revived player has at least — they must not starve in the next breath.</summary>
        public const float RevivedHunger = 30f;

        /// <summary>The <c>RestartInSeconds</c> a fallen player is told while somebody can still bring them back.</summary>
        public const float WaitForTeam = -1f;

        /// <summary>Seconds until a fallen player may start again at the ship: 0 when everybody has fallen, the rest of
        /// <see cref="AloneSeconds"/> when nobody else is there, <see cref="WaitForTeam"/> while a team-mate still stands.</summary>
        public static float RestartIn(bool someoneStands, bool everybodyFell, double fallenFor)
        {
            if (everybodyFell)
            {
                return 0f;
            }

            if (someoneStands)
            {
                return WaitForTeam;
            }

            double left = AloneSeconds - fallenFor;
            return left > 0.0 ? (float)left : 0f;
        }
    }
}
