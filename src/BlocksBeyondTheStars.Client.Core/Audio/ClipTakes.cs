// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Several recordings ("takes") of one sound, so the most repeated events never play the same clip twice in a row.
    /// A take is a file next to the first one: <c>mine_stone</c>, <c>mine_stone_2</c>, <c>mine_stone_3</c> … — a new
    /// take needs no code, only the file. Which sounds rotate is opt-in (<see cref="Families"/>): many other clips
    /// carry a number that MEANS something (a creature's call per species, a surface's footstep take picked by
    /// <see cref="FootstepRules"/>), and those callers keep choosing for themselves.
    /// </summary>
    public static class ClipTakes
    {
        /// <summary>The most takes a family is searched for.</summary>
        public const int MaxTakes = 6;

        /// <summary>The sounds whose takes rotate whenever they are played by their plain name.</summary>
        public static readonly IReadOnlyList<string> Families = new[]
        {
            "mine_stone", "mine_metal", "mine_crystal", "mine_dirt",
            "place_stone", "place_metal", "place_crystal", "place_dirt", "place_wood", "place_glass",
            "melee_swing", "melee_hit",
            "weapon_laser", "weapon_plasma", "weapon_gauss", "weapon_scrap",
            "land", "jump",
            "emote_cheer", "emote_bow",
            "hurt_player", "breath_effort", // #3767: the figure's voice — three pain grunts, two breaths of effort
        };

        /// <summary>The clip name of take <paramref name="take"/> (1-based; take 1 is the plain name).</summary>
        public static string Name(string family, int take) => take <= 1 ? family : family + "_" + take;

        /// <summary>The takes of <paramref name="family"/> that exist, in order — it stops at the first gap, so
        /// <c>x</c>, <c>x_2</c>, <c>x_4</c> counts two. Empty when even the plain name is missing.</summary>
        public static string[] Find(string family, Func<string, bool> has)
        {
            if (string.IsNullOrEmpty(family) || has == null || !has(family))
            {
                return Array.Empty<string>();
            }

            var takes = new List<string> { family };
            for (int take = 2; take <= MaxTakes; take++)
            {
                string name = Name(family, take);
                if (!has(name))
                {
                    break;
                }

                takes.Add(name);
            }

            return takes.ToArray();
        }

        /// <summary>The index of the next take among <paramref name="count"/>: a random one (<paramref name="roll"/> in
        /// 0..1) that is never <paramref name="previous"/> (−1 = none played yet) when there is a choice.</summary>
        public static int Next(int count, int previous, double roll)
        {
            if (count <= 1)
            {
                return count - 1;
            }

            double r = roll < 0d ? 0d : roll >= 1d ? 0.999999d : roll;
            bool avoid = previous >= 0 && previous < count;
            int choices = avoid ? count - 1 : count;
            int pick = (int)(r * choices);
            return avoid && pick >= previous ? pick + 1 : pick;
        }
    }
}
