// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// The integer hashing every bio rule draws from. Integer-only on purpose: the server, the Unity client and the
/// tests must agree to the bit on every platform, and a profile must never consume the world generator's random
/// stream (a species seed is folded from values the generator already made, exactly like the creature voice seed).
/// </summary>
public static class BioHash
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>The splitmix64 finaliser.</summary>
    public static ulong Mix(ulong x)
    {
        unchecked
        {
            x += Golden;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>An independent draw number <paramref name="stream"/> from one seed.</summary>
    public static ulong Draw(uint seed, int stream)
        => Mix(unchecked(seed + ((ulong)(uint)stream * Golden)));

    /// <summary>Folds a 64-bit value to the 32 bits a species seed has. Never 0 — 0 means "no seed".</summary>
    public static uint Fold(ulong x)
    {
        ulong m = Mix(x);
        uint folded = unchecked((uint)(m ^ (m >> 32)));
        return folded == 0 ? 1u : folded;
    }

    /// <summary>FNV-1a over a string: stable across runtimes (<c>string.GetHashCode</c> is randomised per process).</summary>
    public static ulong OfString(string? s)
    {
        ulong h = 14695981039346656037UL;
        if (s is null)
        {
            return h;
        }

        foreach (char c in s)
        {
            unchecked
            {
                h = (h ^ c) * 1099511628211UL;
            }
        }

        return h;
    }

    /// <summary>The seed of a creature species: folded from the voice seed the roster already carries.</summary>
    public static uint CreatureSeed(int voiceSeed) => Fold(unchecked((ulong)(uint)voiceSeed ^ 0xB10C_0001UL));

    /// <summary>The seed of a flora or tree species of a body: the roster seed, the planet type and the catalog block.</summary>
    public static uint FloraSeed(long rosterSeed, string planetKey, string blockKey)
        => Fold(unchecked((ulong)rosterSeed ^ OfString(planetKey) ^ (OfString(blockKey) * Golden) ^ 0xB10C_0002UL));

    /// <summary>The seed of an authored species: the same on every world, as the species itself is.</summary>
    public static uint AuthoredSeed(string key) => Fold(OfString("authored:" + key) ^ 0xB10C_0003UL);

    /// <summary>The seed of a deposit: one material on one body.</summary>
    public static uint DepositSeed(long rosterSeed, string itemKey)
        => Fold(unchecked((ulong)rosterSeed ^ (OfString(itemKey) * Golden) ^ 0xB10C_0004UL));

    /// <summary>The seed of a cross: the same for A×B and B×A, and always the same child for the same pair.</summary>
    public static uint CrossSeed(uint a, uint b)
    {
        uint lo = a < b ? a : b;
        uint hi = a < b ? b : a;
        return Fold((((ulong)hi << 32) | lo) ^ 0xB10C_0005UL);
    }

    /// <summary>A weighted pick: the index whose running weight the draw falls into, or -1 for an empty table.</summary>
    public static int Weighted(ulong draw, int[] weights)
    {
        long total = 0;
        foreach (int w in weights)
        {
            total += w > 0 ? w : 0;
        }

        if (total <= 0)
        {
            return -1;
        }

        long roll = (long)(draw % (ulong)total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
            {
                continue;
            }

            roll -= weights[i];
            if (roll < 0)
            {
                return i;
            }
        }

        return -1;
    }
}
