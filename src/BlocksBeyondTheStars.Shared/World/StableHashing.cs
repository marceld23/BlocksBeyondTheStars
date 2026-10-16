// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Well-mixed, deterministic string hashing for "random but always the same" choices (#2686).
/// <para>
/// <c>WorldGenerator.StableHash</c> is a 64-bit FNV-1a WITHOUT a finaliser: its prime acts like "× 435" on the low 40
/// bits, so keys that differ only in a trailing counter ("…:plot3", "…:plot4", "sys1-p2") land on almost the same low
/// bits — <c>(h &amp; 0x7fffffff) % 3</c> over 600 such keys gave 500/10/90 instead of 200/200/200, and seeding
/// <see cref="System.Random"/> from those low bits couples its first draws. <c>StableHash</c> itself must stay
/// bit-identical (existing worlds are derived from it); new choices go through this class instead, which runs the same
/// FNV-1a through a SplitMix64 finaliser so every input bit reaches every output bit.
/// </para>
/// Pure functions, netstandard2.1, no allocations — server, client and tools agree without any network traffic.
/// </summary>
public static class StableHashing
{
    /// <summary>The raw 64-bit FNV-1a of <paramref name="s"/> — bit-identical to <c>WorldGenerator.StableHash</c>
    /// (including its historical offset basis), as an unsigned value.</summary>
    public static ulong Fnv64(string s)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in s ?? string.Empty)
            {
                h ^= c;
                h *= 1099511628211UL;
            }

            return h;
        }
    }

    /// <summary>The SplitMix64 finaliser: a bijection on 64 bits with full avalanche (one flipped input bit flips each
    /// output bit with probability ~½).</summary>
    public static ulong Mix64(ulong x)
    {
        unchecked
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>A well-mixed 64-bit hash of <paramref name="key"/>.</summary>
    public static ulong Mixed(string key) => Mix64(Fnv64(key));

    /// <summary>A well-mixed 64-bit hash of <paramref name="key"/> salted with a world seed — the usual form for
    /// anything that should differ from one world to the next.</summary>
    public static ulong Mixed(string key, long seed) => Mix64(Fnv64(key) ^ Mix64(unchecked((ulong)seed)));

    /// <summary>An index in <c>[0, n)</c> from a mixed hash — multiply-shift on the high 32 bits (no modulo bias
    /// worth measuring for n far below 2³²). Returns 0 for <paramref name="n"/> ≤ 1.</summary>
    public static int Index(ulong mixed, int n) => n <= 1 ? 0 : (int)(((mixed >> 32) * (ulong)n) >> 32);

    /// <summary>An index in <c>[0, n)</c> for <paramref name="key"/>.</summary>
    public static int Index(string key, int n) => Index(Mixed(key), n);

    /// <summary>An index in <c>[0, n)</c> for <paramref name="key"/> in the world <paramref name="seed"/>.</summary>
    public static int Index(string key, long seed, int n) => Index(Mixed(key, seed), n);

    /// <summary>A uniform double in <c>[0, 1)</c> from a mixed hash (the top 53 bits).</summary>
    public static double Unit(ulong mixed) => (mixed >> 11) * (1.0 / (1UL << 53));

    /// <summary>A uniform double in <c>[0, 1)</c> for <paramref name="key"/>.</summary>
    public static double Unit(string key) => Unit(Mixed(key));

    /// <summary>A uniform double in <c>[0, 1)</c> for <paramref name="key"/> in the world <paramref name="seed"/>.</summary>
    public static double Unit(string key, long seed) => Unit(Mixed(key, seed));

    /// <summary>A 32-bit seed for <see cref="System.Random"/> (or any PRNG) from a mixed hash — both halves folded, so
    /// neighbouring keys give unrelated seeds and therefore unrelated first draws.</summary>
    public static int Seed32(ulong mixed) => unchecked((int)(mixed ^ (mixed >> 32)));

    /// <summary>A 32-bit PRNG seed for <paramref name="key"/>.</summary>
    public static int Seed32(string key) => Seed32(Mixed(key));

    /// <summary>A 32-bit PRNG seed for <paramref name="key"/> in the world <paramref name="seed"/>.</summary>
    public static int Seed32(string key, long seed) => Seed32(Mixed(key, seed));
}
