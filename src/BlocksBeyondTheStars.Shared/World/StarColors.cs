// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The colour of a star system's star (#3123/#3124) — one key for the star map, the planet sky and the ship interior.
/// <para>
/// The classes are DEALT per galaxy, not rolled per system: every block of four procedural systems (<c>sys0</c>–<c>sys3</c>,
/// <c>sys4</c>–<c>sys7</c>, …) holds one cool star (blue / blue-white), one warm one (orange / red-orange / red dwarf) and
/// two calm ones (white / sun-like / yellow), in a per-world shuffled order. Rolled one by one from the name hash, about
/// one twelve-system galaxy in 400 had no blue or red star at all — a playtest explored all twelve and met only white
/// and yellow suns. A star depends on nothing but the world seed and its system index, so a galaxy that grows at the
/// frontier keeps its colours.
/// </para>
/// Pure functions, netstandard2.1 — cosmetic only, nothing is saved: a change recolours every world on its next start.
/// </summary>
public static class StarColors
{
    /// <summary>The sun-like (G) anchor. The client measures its light lift against it (<c>Sky.SunLikeLuma</c>, #1610).</summary>
    public const int SunLikeRgb = 0xFFF1CE;

    /// <summary>Procedural systems per dealt block: one cool, one warm, two calm.</summary>
    public const int BlockSize = 4;

    // Stellar anchors from hot to cool (#3124: stronger extremes and a red dwarf; the calm middle is unchanged). Each
    // class is a contiguous run, and a star only blends toward a neighbour inside its own run, so a blue star stays blue.
    private static readonly int[] Ramp =
    {
        0x9BB2FF,   // blue (hot O/B)
        0xC3D2FF,   // blue-white (A)
        0xFFF6F0,   // white (F)
        SunLikeRgb, // yellow-white, sun-like (G)
        0xFFE2A0,   // yellow (early K)
        0xFFBC6E,   // orange (late K)
        0xFF9852,   // red-orange (M)
        0xFF7444,   // red dwarf (late M)
    };

    private enum StarClass
    {
        Cool,
        Warm,
        Calm,
    }

    // The anchors a card of each class picks from, repeats as weights: sun-like stays the most common single class.
    private static readonly int[] CoolAnchors = { 0, 0, 1 };
    private static readonly int[] WarmAnchors = { 5, 6, 7 };
    private static readonly int[] CalmAnchors = { 2, 3, 3, 4 };

    private static readonly StarClass[] Deck = { StarClass.Cool, StarClass.Warm, StarClass.Calm, StarClass.Calm };

    /// <summary>How far a star may blend toward its neighbouring anchor.</summary>
    private const double MaxBlend = 0.35;

    private const ulong BlendSalt = 0x3123_0000_B1E0_0001UL;

    /// <summary>The packed RGB of the star of the system <paramref name="systemId"/>. A procedural <c>sys{i}</c> system
    /// takes its dealt class; any other system (the Guardian finale) and a location outside the galaxy (null) take a
    /// hash pick of <paramref name="systemName"/> on the same ramp with the same class shares.</summary>
    public static int ForSystem(long worldSeed, string? systemId, string? systemName)
    {
        int index = ProceduralIndex(systemId);
        ulong star;
        StarClass cls;
        if (index >= 0)
        {
            star = StableHashing.Mixed("star:" + index.ToString(CultureInfo.InvariantCulture), worldSeed);
            cls = DealtClass(worldSeed, index);
        }
        else
        {
            star = StableHashing.Mixed("star-name:" + (systemName ?? string.Empty), worldSeed);
            cls = Deck[StableHashing.Index(StableHashing.Mix64(star ^ 0x5EEDUL), Deck.Length)];
        }

        return Shade(cls, star);
    }

    /// <summary>The index <c>i</c> of a procedural system id <c>sys{i}</c>, or -1.</summary>
    private static int ProceduralIndex(string? systemId)
    {
        if (systemId is null || systemId.Length <= 3 || !systemId.StartsWith("sys", System.StringComparison.Ordinal))
        {
            return -1;
        }

        return int.TryParse(systemId.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out int i) ? i : -1;
    }

    /// <summary>The class dealt to system <paramref name="index"/>: its block's four cards, shuffled per world.</summary>
    private static StarClass DealtClass(long worldSeed, int index)
    {
        int block = index / BlockSize;
        ulong h = StableHashing.Mixed("star-block:" + block.ToString(CultureInfo.InvariantCulture), worldSeed);
        var cards = (StarClass[])Deck.Clone();
        for (int i = cards.Length - 1; i > 0; i--)
        {
            int j = StableHashing.Index(StableHashing.Mix64(h ^ (ulong)i), i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return cards[index % BlockSize];
    }

    /// <summary>A star of class <paramref name="cls"/>: a weighted anchor of the class, blended up to
    /// <see cref="MaxBlend"/> toward a neighbouring anchor of the same class.</summary>
    private static int Shade(StarClass cls, ulong star)
    {
        int[] anchors = cls switch
        {
            StarClass.Cool => CoolAnchors,
            StarClass.Warm => WarmAnchors,
            _ => CalmAnchors,
        };

        int lo = int.MaxValue, hi = int.MinValue;
        foreach (int a in anchors)
        {
            lo = System.Math.Min(lo, a);
            hi = System.Math.Max(hi, a);
        }

        int anchor = anchors[StableHashing.Index(star, anchors.Length)];
        ulong blend = StableHashing.Mix64(star ^ BlendSalt);
        int neighbour = (blend & 1UL) == 0 ? anchor - 1 : anchor + 1;
        if (neighbour < lo || neighbour > hi)
        {
            neighbour = anchor == lo ? anchor + 1 : anchor - 1;
        }

        return LerpRgb(Ramp[anchor], Ramp[neighbour], StableHashing.Unit(blend) * MaxBlend);
    }

    private static int LerpRgb(int a, int b, double t)
    {
        int Channel(int shift)
        {
            int ca = (a >> shift) & 0xFF, cb = (b >> shift) & 0xFF;
            return (int)(ca + ((cb - ca) * t) + 0.5);
        }

        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
