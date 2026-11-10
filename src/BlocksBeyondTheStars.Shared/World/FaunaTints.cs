// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #3448: the colours of a planet type's ROLLED fauna when the type names a <see cref="PlanetType.FaunaPalette"/> (Soaniri's
/// light blues, aquas and mints — the mirror of the flora palette, <see cref="FloraTints"/>). The roster's post-pass
/// (<see cref="Dress"/>, from <c>CreatureGenerator.GenerateRoster</c>) gives every rolled species a body of one palette entry,
/// its saturation and brightness jittered a little with the hue kept, and a belly of another, lighter entry — or of the body
/// lightened when no other entry is lighter. The client keeps the coat patterns (<c>CreatureCoat.PatternRgb</c>) and the
/// micro-fauna inside the same colours.
/// <para>Pure functions of the species' own seed (its voice seed, itself folded from the species' sub-seed): no RNG of the roster
/// is drawn, so every type without a palette rolls bit-identically to before, and the authored records — appended after the
/// pass — keep their designed colours (the Soundi stays the Soundi). Nothing is saved: a roster is rebuilt on every load.</para>
/// </summary>
public static class FaunaTints
{
    /// <summary>How much lighter (Rec. 601 luma, 0..1) another palette entry must be to serve as the belly.</summary>
    public const float MinBellyLift = 0.04f;

    /// <summary>How far toward white a body is lightened for the belly when no other entry is lighter.</summary>
    public const float BellyLighten = 0.45f;

    /// <summary>The salt of the palette pick, so it reads other bits than the seed's other uses (the coat, the favourite food).</summary>
    private const ulong PaletteSalt = 0x3448_FA17_0000_0001UL;

    /// <summary><paramref name="planet"/>'s <see cref="PlanetType.FaunaPalette"/> as 0xRRGGBB (an entry that is no
    /// <c>"#rrggbb"</c> is skipped — the flora palette's rule); empty for a type without one, or none.</summary>
    public static IReadOnlyList<int> PaletteOf(PlanetType? planet)
    {
        var colours = new List<int>();
        if (planet?.FaunaPalette is not { Count: > 0 } specs)
        {
            return colours;
        }

        foreach (string spec in specs)
        {
            if (FloraTints.TryParseRgb(spec, out int rgb))
            {
                colours.Add(rgb & 0xFFFFFF);
            }
        }

        return colours;
    }

    /// <summary>The body and belly colour (0xRRGGBB) of a rolled species with <paramref name="speciesSeed"/> on a world whose
    /// type has <paramref name="palette"/>: the body one entry, jittered (saturation ×0.88..1.12, brightness ×0.92..1.08, the
    /// hue kept); the belly another entry when that is at least <see cref="MinBellyLift"/> lighter in luma, else the body
    /// lightened by <see cref="BellyLighten"/> toward white. An empty palette leaves the colours to the caller (0, 0).</summary>
    public static (int Body, int Belly) CoatFor(int speciesSeed, IReadOnlyList<int> palette)
    {
        if (palette is null || palette.Count == 0)
        {
            return (0, 0);
        }

        ulong h = StableHashing.Mix64(unchecked((ulong)(uint)speciesSeed ^ PaletteSalt));
        int bodyIndex = (int)(h % (ulong)palette.Count);
        int body = FloraTints.ToRgb24(FloraTints.Jittered(Channels(palette[bodyIndex]), h), 1f);
        int belly = Lighten(body, BellyLighten);
        if (palette.Count > 1)
        {
            int other = (bodyIndex + 1 + (int)((h >> 40) % (ulong)(palette.Count - 1))) % palette.Count;
            if (Luma(palette[other]) >= Luma(body) + MinBellyLift)
            {
                belly = palette[other];
            }
        }

        return (body, belly);
    }

    /// <summary>The roster's post-pass: every species in <paramref name="rolled"/> wears <paramref name="planet"/>'s fauna
    /// palette (<see cref="CoatFor"/> by its <see cref="CreatureSpecies.VoiceSeed"/>). A type without a palette leaves the list
    /// untouched. The caller passes only rolled species — the authored ones join after it.</summary>
    public static void Dress(IList<CreatureSpecies> rolled, PlanetType planet)
    {
        var palette = PaletteOf(planet);
        if (palette.Count == 0)
        {
            return;
        }

        foreach (var species in rolled)
        {
            (species.ColorRgb, species.BellyRgb) = CoatFor(species.VoiceSeed, palette);
        }
    }

    /// <summary>Rec. 601 luma of a 0xRRGGBB colour (0..1).</summary>
    public static float Luma(int rgb)
    {
        var (r, g, b) = Channels(rgb);
        return (0.299f * r) + (0.587f * g) + (0.114f * b);
    }

    /// <summary><paramref name="rgb"/> moved <paramref name="amount"/> (0..1) of the way toward white — the hue kept.</summary>
    public static int Lighten(int rgb, float amount)
    {
        var (r, g, b) = Channels(rgb);
        return FloraTints.ToRgb24((r + ((1f - r) * amount), g + ((1f - g) * amount), b + ((1f - b) * amount)), 1f);
    }

    private static (float R, float G, float B) Channels(int rgb)
        => (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
}
