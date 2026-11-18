// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions
{
    /// <summary>
    /// The body's own healing and its pause after a hit (#3794). Wherever a player can breathe — a breathable world, the
    /// ship, a station, a base with life support — health comes back by itself. It used to come back in the middle of a
    /// fight too, which made every attacker that deals no more than the healing rate harmless: the health bar did not
    /// move. So a <b>hit</b> — damage armour works against: a bite, a machine's or a robber's attack, a giant, another
    /// player's blow, a fall, a crash, lava and fire — makes the healing wait <see cref="PauseAfterHitSeconds"/>; every
    /// further hit starts the wait again. Damage of the surroundings that armour does not stop (thin air, cold and heat,
    /// toxic air and water, hunger, weather) is balanced against the healing and does not pause it, and nothing but the
    /// body's own healing waits: the heal tank, a bed, food, a medkit and lab preparations heal as before.
    /// </summary>
    public static class NaturalHealing
    {
        /// <summary>Seconds the body's own healing waits after a hit.</summary>
        public const double PauseAfterHitSeconds = 5.0;

        /// <summary>True while the healing waits: <paramref name="now"/> has not yet reached the moment the last hit
        /// set (<paramref name="pausedUntil"/>; 0 = never hit).</summary>
        public static bool Paused(double now, double pausedUntil) => now < pausedUntil;

        /// <summary>The moment the healing may go on after a hit at <paramref name="now"/> — never earlier than a wait
        /// that is already running.</summary>
        public static double PauseUntil(double now, double pausedUntil)
        {
            double until = now + PauseAfterHitSeconds;
            return until > pausedUntil ? until : pausedUntil;
        }
    }
}
