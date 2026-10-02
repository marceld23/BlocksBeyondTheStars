// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The per-world sky and cloud colours (#2170) — moved here from the server's weather code so the orbit view can
/// paint a body's atmosphere rim and cloud shell in exactly the colours its surface shows, without being on it.
/// Like <see cref="FloraTints"/> and <see cref="FluidTints"/>: pure functions of (save seed, location id, planet
/// type). The server ships the results of the active world in the environment message; the client calls the same
/// functions for every other body. The bit patterns are the historical server formulas — changing them would
/// recolour every existing world.
/// </summary>
public static class AtmosphereTints
{
    /// <summary>The cloud colour of a type without one (and of an unknown type).</summary>
    public const int DefaultCloudRgb = 0xEDEFF2;

    // Per-world daytime sky/atmosphere base hue: blue-dominant (most worlds read earth-like), with rarer green /
    // yellow / orange / red and an exotic violet. Pastel / mid-bright on purpose — the client feeds this straight
    // into the sky base, distance fog, ambient and reflections, so a too-saturated value would tint the whole world.
    private static readonly (int Rgb, int Weight)[] SkyPalette =
    {
        (0x8CBFF2, 30), // blue (earth-like default)
        (0x9FD0E8, 14), // pale cyan
        (0x8FD0B0, 10), // teal-green
        (0xB8D89A, 8),  // yellow-green
        (0xE8D89A, 10), // amber / sandy
        (0xE8B488, 8),  // orange haze
        (0xE0A0A0, 6),  // rust / red
        (0xC8A8E0, 4),  // violet (exotic)
    };

    /// <summary>The location hash every per-world look is seeded with (h = h·31 + c from 17).</summary>
    public static int StableHash(string? s)
    {
        int h = 17;
        foreach (char c in s ?? string.Empty)
        {
            h = unchecked(h * 31 + c);
        }

        return h;
    }

    /// <summary>The daytime sky hue (0xRRGGBB) of one world: the type's authored sky (#2063) when it names one,
    /// else a seeded pick from the blue-dominant palette. Only worlds with an atmosphere show it.</summary>
    public static int SkyRgb(long worldSeed, string? locationId, PlanetType? planet)
        => planet is { SkyColor: > 0 }
            ? planet.SkyColor
            : SkyHue(unchecked((uint)(StableHash(locationId) ^ (int)worldSeed)));

    /// <summary>The cloud tint (0xRRGGBB) of one world: the type's base cloud colour with a small per-world jitter,
    /// pushed apart in brightness when it drifted too close to <paramref name="skyRgb"/> (that world's
    /// <see cref="SkyRgb"/>), so clouds always read against their sky.</summary>
    public static int CloudRgb(long worldSeed, string? locationId, PlanetType? planet, int skyRgb)
        => CloudTint(
            planet?.CloudColor ?? DefaultCloudRgb,
            skyRgb,
            unchecked((uint)((StableHash(locationId) ^ (int)worldSeed) ^ 0x5F356495)));

    /// <summary>A deterministic sky hue from a hash: a weighted palette pick plus a small per-channel jitter.</summary>
    public static int SkyHue(uint h)
    {
        int total = 0;
        foreach (var (_, w) in SkyPalette)
        {
            total += w;
        }

        int roll = (int)(h % (uint)total);
        int i = 0;
        for (; i < SkyPalette.Length; i++)
        {
            roll -= SkyPalette[i].Weight;
            if (roll < 0)
            {
                break;
            }
        }

        if (i >= SkyPalette.Length)
        {
            i = SkyPalette.Length - 1;
        }

        int anchor = SkyPalette[i].Rgb;
        int r = (anchor >> 16) & 0xFF, g = (anchor >> 8) & 0xFF, b = anchor & 0xFF;
        int Jit(int shift) => (int)((h >> shift) & 0x1F) - 16; // -16..+15
        return (Clamp8b(r + Jit(3)) << 16) | (Clamp8b(g + Jit(8)) << 8) | Clamp8b(b + Jit(13));
    }

    /// <summary>A deterministic cloud tint: the base colour plus a small jitter, then the contrast guarantee — if the
    /// tint ended up within ~64 RGB units of <paramref name="skyRgb"/>, its brightness is pushed apart (lighter over
    /// a dark sky, greyer under a bright one).</summary>
    public static int CloudTint(int baseRgb, int skyRgb, uint h)
    {
        int r = (baseRgb >> 16) & 0xFF, g = (baseRgb >> 8) & 0xFF, b = baseRgb & 0xFF;
        // Modest jitter: keeps the type's character (storm-grey ash, sandy desert) but makes each world unique.
        int Jit(int shift) => (int)((h >> shift) & 0xF) - 8; // -8..+7
        r = Clamp8b(r + Jit(2));
        g = Clamp8b(g + Jit(7));
        b = Clamp8b(b + Jit(12));

        int sr = (skyRgb >> 16) & 0xFF, sg = (skyRgb >> 8) & 0xFF, sb = skyRgb & 0xFF;
        int dr = r - sr, dg = g - sg, db = b - sb;
        if (dr * dr + dg * dg + db * db < 64 * 64)
        {
            int skyLum = (sr * 299 + sg * 587 + sb * 114) / 1000;
            int push = skyLum > 140 ? -60 : 60; // bright sky → darker (grey) cloud, dark sky → brighter cloud
            r = Clamp8b(r + push);
            g = Clamp8b(g + push);
            b = Clamp8b(b + push);
        }

        return (r << 16) | (g << 8) | b;
    }

    private static int Clamp8b(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
}
