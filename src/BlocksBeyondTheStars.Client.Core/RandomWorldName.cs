// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The world the save picker's "Random world" button creates: its save name and the seed it is
    /// generated from (#2522).</summary>
    public readonly struct RandomWorldChoice
    {
        public RandomWorldChoice(string name, long seed, bool usesTypedName)
        {
            Name = name ?? string.Empty;
            Seed = seed;
            UsesTypedName = usesTypedName;
        }

        /// <summary>The save name the world is created under.</summary>
        public string Name { get; }

        /// <summary>The generation seed handed to the server as <c>--seed</c>. 0 means "derive it from the name"
        /// (the server's default) — used for the <c>world_&lt;n&gt;</c> fallback, whose name is random already.</summary>
        public long Seed { get; }

        /// <summary>True when the player's typed name was used (with a random seed of its own).</summary>
        public bool UsesTypedName { get; }
    }

    /// <summary>
    /// Decides what "Random world" creates (#2522). The button used to ignore the name box and always create
    /// <c>world_&lt;n&gt;</c>, while the typed name stayed in the box as if it had been used — a tester typed
    /// "Lyxette_2", pressed it, and found a <c>world_&lt;number&gt;</c> in her list.
    /// <para>
    /// Now a name the player actually typed wins: when the box holds an edited name — not empty, not the
    /// untouched default, not an existing save — the world is created under that name with a random seed (so it
    /// differs from what "Create" would make of the same name, which derives the seed from the name). Otherwise
    /// the old <c>world_&lt;n&gt;</c> behaviour stays, now rerolled past numbers that already name a save, so the
    /// button can never quietly resume an old world.
    /// </para>
    /// </summary>
    public static class RandomWorldName
    {
        /// <summary>The name the picker's name box starts with.</summary>
        public const string DefaultName = "new_world";

        /// <summary>Prefix of the fallback name.</summary>
        public const string FallbackPrefix = "world_";

        /// <summary>Range of the fallback number (lower inclusive, upper exclusive — the old
        /// <c>Random.Range(1000, 999999)</c>).</summary>
        public const int FallbackMin = 1000, FallbackMax = 999999;

        /// <summary>How often the fallback rerolls a number that names an existing save before it gives up.</summary>
        public const int MaxRerolls = 32;

        /// <summary>
        /// The world "Random world" creates. <paramref name="typedName"/> is the name box's content as the save
        /// would be called (already sanitized and trimmed by the caller); <paramref name="existingNames"/> are the
        /// saves on disk, compared case-insensitively like the filesystem does.
        /// </summary>
        public static RandomWorldChoice Resolve(string? typedName, string defaultName, IEnumerable<string>? existingNames, Random rng)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingNames != null)
            {
                foreach (string existingName in existingNames)
                {
                    if (!string.IsNullOrEmpty(existingName))
                    {
                        existing.Add(existingName);
                    }
                }
            }

            string typed = (typedName ?? string.Empty).Trim();
            bool edited = typed.Length > 0 && !string.Equals(typed, defaultName, StringComparison.Ordinal);
            if (edited && !existing.Contains(typed))
            {
                return new RandomWorldChoice(typed, RandomSeed(rng), usesTypedName: true);
            }

            string name = FallbackPrefix + rng.Next(FallbackMin, FallbackMax);
            for (int i = 0; i < MaxRerolls && existing.Contains(name); i++)
            {
                name = FallbackPrefix + rng.Next(FallbackMin, FallbackMax);
            }

            return new RandomWorldChoice(name, 0L, usesTypedName: false);
        }

        /// <summary>A random seed that is always positive: never 0 (which tells the server to derive the seed
        /// from the name, the very thing "Random" must not do) and never negative (the seed travels as a
        /// command-line number, and a culture-specific minus sign is not worth the risk).</summary>
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
    }
}
