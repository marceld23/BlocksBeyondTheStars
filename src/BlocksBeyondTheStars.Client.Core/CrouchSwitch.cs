// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The keyboard's crouch key as a switch (Settings → Accessibility → "Crouch: press once instead of holding"),
    /// Unity-free so the suite can hold it. The key means two things: <b>sneak</b> where the body stands on the ground,
    /// and <b>down</b> everywhere else — sinking in creative flight and with the jetpack, diving, climbing down. "Down"
    /// reads the key as held (<c>InputMap.DescendHeld</c>); the switch is for sneaking alone. So a press only flips
    /// the switch where sneaking can start (#3793): a press in the air used to flip it too, and the figure landed
    /// crouched — or stood up — without having been asked.
    /// </summary>
    public static class CrouchSwitch
    {
        /// <summary>The switch after this frame. <paramref name="pressed"/>: the crouch key went down this frame;
        /// <paramref name="jumped"/>: so did a jump (it always stands the body up); <paramref name="canSneak"/>: the body
        /// is on foot on the ground, not in water and not on a ladder or a wall — a press there is a sneak, anywhere
        /// else it is "down" and leaves the switch as it is.</summary>
        public static bool Next(bool latched, bool pressed, bool jumped, bool canSneak)
        {
            if (jumped)
            {
                return false;
            }

            return pressed && canSneak ? !latched : latched;
        }
    }
}
