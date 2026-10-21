// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #2856: the gentle colour drift between groves. A world gives each crown block ONE colour and every trunk ONE bark
/// colour (<see cref="FloraTints"/>), so a wood read as copies of one tree. The client mesher shifts that colour by a slow
/// field over the ground — a few degrees of hue and about ±10 % of brightness, changing over ~<see cref="CellBlocks"/>
/// blocks — so neighbouring groves differ while one crown (a few blocks across, its trunk in the same columns) stays
/// practically uniform. Only the tinted crowns and the trunks take it; small plants keep their per-column wobble (#675).
/// <para>
/// A pure function of the column (x, z) and the world's size, so every client agrees without any traffic and nothing is
/// saved. Integer-hash value noise with smoothstep-weighted bilinear interpolation (no trig, no exp), periodic over both
/// seams of a round world — X wraps at the circumference, Z at its latitude period — so the drift never jumps where the
/// world wraps, and any coordinate (negative, or a lap away) reads its canonical cell.
/// </para>
/// </summary>
public static class TreeTintJitter
{
    /// <summary>The wavelength the field aims at, in blocks. The real lattice step is rounded so a whole number of steps
    /// fits around each seam of the world.</summary>
    public const int CellBlocks = 26;

    /// <summary>The largest hue shift, as a fraction of the colour circle (6°).</summary>
    public const float MaxHueShift = 6f / 360f;

    /// <summary>The largest brightness change (±10 %), as it reads on screen.</summary>
    public const float MaxValueShift = 0.10f;

    // Own salts, so the hue and the brightness drift independently (a darker grove is not always a bluer one).
    private const uint HueSalt = 0x2856_0001u;
    private const uint ValueSalt = 0x2856_0002u;

    /// <summary>The drift at a column: the hue shift (a fraction of the colour circle, within ±<see cref="MaxHueShift"/>)
    /// and the brightness factor (within 1 ± <see cref="MaxValueShift"/>). <paramref name="circumference"/> ≤ 0 means the
    /// classic world size.</summary>
    public static (float HueShift, float Value) At(int x, int z, int circumference)
    {
        int c = circumference > 0 ? circumference : WorldConstants.Circumference;
        int pz = WorldConstants.LatitudePeriodFor(c);
        float hue = Field(x, z, c, pz, HueSalt) * MaxHueShift;
        float value = 1f + (Field(x, z, c, pz, ValueSalt) * MaxValueShift);
        return (hue, value);
    }

    /// <summary>A tint colour (RGB 0..1) with the drift of its column applied: the hue turned, the brightness scaled,
    /// clamped to 0..1. <paramref name="linear"/>: the colour is in linear space (what the block shader receives in a
    /// Linear project), so the brightness factor is taken to the display gamma first — ±10 % reads as ±10 % either way.</summary>
    public static (float R, float G, float B) Apply((float R, float G, float B) rgb, int x, int z, int circumference, bool linear)
    {
        var (hueShift, value) = At(x, z, circumference);
        return Apply(rgb, hueShift, value, linear);
    }

    /// <summary>A tint colour with a drift already read by <see cref="At"/> applied — for a caller that colours several
    /// blocks of one column (the mesher reads the drift once per column and applies it to the crown and the trunk).</summary>
    public static (float R, float G, float B) Apply((float R, float G, float B) rgb, float hueShift, float value, bool linear)
    {
        float gain = linear ? LinearGain(value) : value;
        RgbToHsv(rgb.R, rgb.G, rgb.B, out float h, out float s, out float v);
        h += hueShift;
        if (h < 0f)
        {
            h += 1f;
        }
        else if (h >= 1f)
        {
            h -= 1f;
        }

        return HsvToRgb(h, s, Clamp01(v * gain));
    }

    /// <summary>A perceived (sRGB) brightness factor near 1 as the factor on linear values: f^2.2, by its second-order
    /// expansion around 1 (exact to ~0.1 % over the ±10 % the drift uses, and free of transcendental functions).</summary>
    internal static float LinearGain(float perceived)
    {
        float d = perceived - 1f;
        return 1f + (2.2f * d) + (1.32f * d * d);
    }

    /// <summary>Smooth value noise in -1..1, periodic in X over <paramref name="periodX"/> blocks and in Z over
    /// <paramref name="periodZ"/> blocks.</summary>
    internal static float Field(int x, int z, int periodX, int periodZ, uint salt)
    {
        Lattice(x, periodX, out int x0, out int x1, out float fx);
        Lattice(z, periodZ, out int z0, out int z1, out float fz);
        float sx = fx * fx * (3f - (2f * fx)); // smoothstep weights: no crease at the lattice lines
        float sz = fz * fz * (3f - (2f * fz));
        float south = Lerp(Corner(x0, z0, salt), Corner(x1, z0, salt), sx);
        float north = Lerp(Corner(x0, z1, salt), Corner(x1, z1, salt), sx);
        return Lerp(south, north, sz);
    }

    /// <summary>The lattice cell a coordinate falls in on an axis that wraps every <paramref name="period"/> blocks: the
    /// two lattice indices around it (the last cell wraps to index 0) and the fraction across the cell.</summary>
    private static void Lattice(int coord, int period, out int i0, out int i1, out float frac)
    {
        if (period <= 0)
        {
            period = WorldConstants.Circumference;
        }

        int cells = System.Math.Max(1, (period + (CellBlocks / 2)) / CellBlocks);
        int p = ((coord % period) + period) % period; // the canonical cell, safe for negative coordinates
        long scaled = (long)p * cells;               // the position in lattice units, times the period
        i0 = (int)(scaled / period);
        frac = (float)(scaled - ((long)i0 * period)) / period;
        i1 = i0 + 1 >= cells ? 0 : i0 + 1;
    }

    /// <summary>The noise value (-1..1) at a lattice point: an integer hash, the same on every runtime.</summary>
    private static float Corner(int i, int j, uint salt)
    {
        unchecked
        {
            uint h = ((uint)i * 0x9E3779B1u) ^ ((uint)j * 0x85EBCA77u) ^ salt;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return ((h & 0xFFFFu) / 32767.5f) - 1f;
        }
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Clamp01(float f) => f < 0f ? 0f : f > 1f ? 1f : f;

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = System.Math.Max(r, System.Math.Max(g, b));
        float min = System.Math.Min(r, System.Math.Min(g, b));
        float d = max - min;
        v = max;
        s = max > 1e-6f ? d / max : 0f;
        if (d <= 1e-6f)
        {
            h = 0f; // grey: no hue to turn
            return;
        }

        if (r >= g && r >= b)
        {
            h = (g - b) / d / 6f;
        }
        else if (g >= b)
        {
            h = (((b - r) / d) + 2f) / 6f;
        }
        else
        {
            h = (((r - g) / d) + 4f) / 6f;
        }

        if (h < 0f)
        {
            h += 1f;
        }
    }

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h * 6f;
        float x = c * (1f - System.Math.Abs((hp % 2f) - 1f));
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
}
