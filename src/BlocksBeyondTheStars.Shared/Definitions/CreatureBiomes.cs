// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Which biomes a fixed species calls its own (#1763, #3973): those whose SURFACE block it names
/// (<see cref="CreatureSpecies.BiomeSurfaces"/> — Leni's snow and ice) and those whose CLIMATE ZONE it names
/// (<see cref="CreatureSpecies.FloraThemes"/> — the flora theme of the biome: a cow's temperate meadows and savanna). One
/// rule for the roster's biome affinity and for the spawner's exclusive ground.
/// </summary>
public static class CreatureBiomes
{
    /// <summary>The climate zone of a biome of <paramref name="planet"/>: its own flora theme, else the type's (temperate when
    /// neither names one), by its canonical name.</summary>
    public static string ThemeOf(Biome? biome, PlanetType planet)
        => FloraThemes.Resolve(biome is { FloraTheme.Length: > 0 } ? biome.FloraTheme : planet.FloraTheme).Name;

    /// <summary>The climate zone of the type's pool biome <paramref name="poolIndex"/> (−1 or out of range = the type's own).</summary>
    public static string ThemeAt(PlanetType planet, int poolIndex)
        => ThemeOf(poolIndex >= 0 && poolIndex < planet.Biomes.Count ? planet.Biomes[poolIndex] : null, planet);

    /// <summary>Whether <paramref name="themes"/> names the climate zone <paramref name="theme"/> (names compare as the
    /// themes resolve, so "Savanna" and "savanna" are one zone).</summary>
    public static bool NamesTheme(IReadOnlyList<string>? themes, string theme)
    {
        if (themes is null)
        {
            return false;
        }

        foreach (var t in themes)
        {
            if (FloraThemes.IsKnown(t) && string.Equals(FloraThemes.Resolve(t).Name, theme, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a species with these surfaces and zones names <paramref name="biome"/> of <paramref name="planet"/>.</summary>
    public static bool NamesBiome(IReadOnlyList<string> surfaces, IReadOnlyList<string> themes, Biome biome, PlanetType planet)
    {
        foreach (var s in surfaces)
        {
            if (s == biome.SurfaceBlock)
            {
                return true;
            }
        }

        return themes.Count > 0 && NamesTheme(themes, ThemeOf(biome, planet));
    }

    /// <summary>The roster's biome affinity of a fixed species: the first of the type's pool biomes it names, or −1 (at home
    /// anywhere). The spawner's native pass prefers it there; the hard rule is <see cref="OnOwnGround"/>.</summary>
    public static int AffinityIn(IReadOnlyList<string> surfaces, IReadOnlyList<string> themes, PlanetType planet)
    {
        for (int i = 0; i < planet.Biomes.Count; i++)
        {
            if (NamesBiome(surfaces, themes, planet.Biomes[i], planet))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The exclusive rule (<see cref="CreatureSpecies.BiomeExclusive"/>): whether ground of block
    /// <paramref name="groundKey"/> in a biome of zone <paramref name="theme"/> is the species' own — its surface, or its zone.
    /// A species that names neither never rejects.</summary>
    public static bool OnOwnGround(CreatureSpecies sp, string? groundKey, string theme)
    {
        var surfaces = sp.BiomeSurfaces ?? Array.Empty<string>();
        var themes = sp.FloraThemes ?? Array.Empty<string>();
        if (surfaces.Length == 0 && themes.Length == 0)
        {
            return true;
        }

        return (groundKey is not null && Array.IndexOf(surfaces, groundKey) >= 0) || NamesTheme(themes, theme);
    }
}
