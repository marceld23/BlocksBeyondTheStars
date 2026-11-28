// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Globalization;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The optional seed field of the new-world form (World options, #4078): what the player typed → the seed the new
    /// world is generated from. A world's name is only its label; the seed decides the galaxy.
    /// <list type="bullet">
    /// <item>empty → a random seed (<see cref="RandomSeed"/>) — the default, so the field is never required;</item>
    /// <item>a whole number (<c>4711</c>, <c>-4711</c>) → exactly that number;</item>
    /// <item>any other text → <see cref="WorldGenerator.StableHash"/> of it — the hash the server used to take of the
    /// world's NAME, so typing an old world's name here recreates exactly that old world.</item>
    /// </list>
    /// The result is never 0: the server reads <c>--seed 0</c> (and no <c>--seed</c>) as "derive the seed from the world
    /// name", the fallback dedicated servers and the tool runs keep. A typed <c>0</c>, or a number too long for a
    /// <see cref="long"/>, is therefore hashed as text.
    /// </summary>
    public static class WorldSeedInput
    {
        /// <summary>The most characters the field takes — a <see cref="long"/> needs 20 with its sign; a word seed rarely more.</summary>
        public const int MaxLength = 32;

        /// <summary>The seed for <paramref name="text"/> (see the class summary); <paramref name="rng"/> only rolls the
        /// random seed of an empty field.</summary>
        public static long Resolve(string? text, Random rng)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            string typed = (text ?? string.Empty).Trim();
            if (typed.Length == 0)
            {
                return RandomSeed(rng);
            }

            if (long.TryParse(typed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) && number != 0)
            {
                return number;
            }

            long hashed = WorldGenerator.StableHash(typed);
            return hashed != 0 ? hashed : 1L;
        }

        /// <summary>A random seed that is always positive: never 0 (which tells the server to derive the seed from the
        /// name — the very thing a random world must not do) and never negative (the same range the dice button rolled,
        /// #2522).</summary>
        public static long RandomSeed(Random rng)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var bytes = new byte[8];
            long seed = 0L;
            for (int i = 0; i < 16 && seed == 0L; i++)
            {
                rng.NextBytes(bytes);
                seed = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
            }

            return seed != 0L ? seed : 1L;
        }

        /// <summary>How the picker writes a seed: digits with an ASCII minus in every language, so a copied seed pastes
        /// back into the field unchanged.</summary>
        public static string Format(long seed) => seed.ToString(CultureInfo.InvariantCulture);
    }
}
