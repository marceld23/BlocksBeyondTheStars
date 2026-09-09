// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Per-(species × planet) flora colours: every flora species rolls ONE deterministic colour per world
/// (uniform within the world, different on the next), applied on top of the desaturated tile by the
/// block shader's tint channel. Pure function of (world seed, location key, block key) so server,
/// client and every player agree without any network traffic.
/// </summary>
public static class FloraTints
{
    /// <summary>The tint colour (RGB 0..1) for a flora block key on a given world. Hue is fully random
    /// per (species, world); saturation/value stay in a friendly band so plants remain readable and the
    /// shader's luminance-times-tint never blows out.</summary>
    public static (float R, float G, float B) For(long worldSeed, string locationKey, string blockKey)
    {
        ulong h = Hash($"{worldSeed}|{locationKey}|flora-tint|{blockKey}");
        float hue = (h % 3600UL) / 3600f;                       // 0..1 — anything goes, alien worlds allowed
        float sat = 0.45f + ((h >> 12) % 1000UL) / 1000f * 0.4f; // 0.45..0.85
        float val = 0.85f + ((h >> 24) % 1000UL) / 1000f * 0.3f; // 0.85..1.15
        return HsvToRgb(hue, sat, val);
    }

    /// <summary>The bark/trunk tint colour (RGB 0..1) for a world — wood gets ONE deterministic colour per
    /// world (uniform across every trunk on the planet). The hue is fully random per world like the leaves,
    /// but value is forced into a DARK band (0.30..0.58) while leaves always sit bright (0.85..1.15), so the
    /// trunk is guaranteed to read clearly darker than ANY leaf species regardless of hue — they can never be
    /// confused. Pure function of (world seed, location key), so server/client/orbit all agree, no traffic.</summary>
    public static (float R, float G, float B) ForWood(long worldSeed, string locationKey)
    {
        ulong h = Hash($"{worldSeed}|{locationKey}|wood-tint");
        float hue = (h % 3600UL) / 3600f;                        // 0..1 — any bark hue, alien woods allowed
        float sat = 0.35f + ((h >> 12) % 1000UL) / 1000f * 0.35f; // 0.35..0.70 (muted, never neon)
        float val = 0.30f + ((h >> 24) % 1000UL) / 1000f * 0.28f; // 0.30..0.58 — always darker than any leaf
        return HsvToRgb(hue, sat, val);
    }

    // The world's base flora hue: green-dominant, with rarer brown / pink / purple / amber exotics.
    private static readonly (int Rgb, int Weight)[] WorldPalette =
    {
        (0x4FA63C, 30), // leaf green
        (0x6FBF4A, 20), // bright green
        (0x3E7D4F, 12), // deep teal-green
        (0x8A7B3A, 12), // olive
        (0x9C6B3A, 10), // brown
        (0xB85C9E, 7),  // pink / magenta (exotic)
        (0x7E4FB0, 6),  // violet / purple (exotic)
        (0xC9A23A, 3),  // amber / yellow (rare)
    };

    /// <summary>The world's ONE base flora hue as 0xRRGGBB — a weighted pick from a green-dominant palette
    /// (with rarer brown / pink / purple / amber exotics) plus a small per-channel jitter, so most worlds are
    /// leafy green but some are strikingly alien. The server ships it in the environment message (the block
    /// shader's fallback for a flora face without a species tint; the micro-fauna borrow it), and it used to
    /// live there alone — #1716 moved the formula next to the per-species colours so the two flora colour
    /// sources are one file with one seed convention. The hash is the server's historical one
    /// (<c>h*31+c</c> over the location id, XOR the low seed word), so every existing world keeps its hue.</summary>
    public static int ForWorld(long worldSeed, string? locationKey)
    {
        int h = 17;
        foreach (char c in locationKey ?? string.Empty)
        {
            h = unchecked(h * 31 + c);
        }

        uint mix = unchecked((uint)(h ^ (int)worldSeed ^ 0x2F0A17));
        int total = 0;
        foreach (var (_, w) in WorldPalette)
        {
            total += w;
        }

        int roll = (int)(mix % (uint)total);
        int i = 0;
        for (; i < WorldPalette.Length; i++)
        {
            roll -= WorldPalette[i].Weight;
            if (roll < 0)
            {
                break;
            }
        }

        if (i >= WorldPalette.Length)
        {
            i = WorldPalette.Length - 1;
        }

        int anchor = WorldPalette[i].Rgb;
        int r = (anchor >> 16) & 0xFF, g = (anchor >> 8) & 0xFF, b = anchor & 0xFF;
        int Jit(int shift) => (int)((mix >> shift) & 0x1F) - 16; // -16..+15
        return (Clamp8b(r + Jit(3)) << 16) | (Clamp8b(g + Jit(8)) << 8) | Clamp8b(b + Jit(13));
    }

    private static int Clamp8b(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

    /// <summary>FNV-1a (stable across platforms/runs — string.GetHashCode is randomized per process).</summary>
    private static ulong Hash(string s)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h = (h ^ c) * 1099511628211UL;
            }

            return h;
        }
    }

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h * 6f;
        float x = c * (1f - System.Math.Abs(hp % 2f - 1f));
        (float r, float g, float b) = ((int)hp % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return (Clamp01(r + m), Clamp01(g + m), Clamp01(b + m));
    }

    private static float Clamp01(float f) => f < 0f ? 0f : f > 1f ? 1f : f;
}
