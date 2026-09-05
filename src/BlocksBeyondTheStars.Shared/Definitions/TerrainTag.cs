// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Terrain feature tags a planet type opts into (#1644). The world generator gates its landform families on
/// these instead of on planet-type KEY strings, so a new data-only type can carry lava rivers, buttes or salt
/// polygons without a code change. Parsed once at content load from <see cref="PlanetType.TerrainTags"/>.
/// </summary>
[System.Flags]
public enum TerrainTag
{
    None = 0,

    /// <summary>Molten geology: lava rivers into a lava sea, hex basalt column fields, steam/lava vents, basalt
    /// continents in a lava ocean (the classic <c>lava</c> / <c>ashen</c> behaviour).</summary>
    Volcanic = 1 << 0,

    /// <summary>Salt-pan surface: the Voronoi salt-polygon ridge network (the classic <c>salt_flats</c>).</summary>
    Salt = 1 << 1,

    /// <summary>Dry rocky-reading country: table mountains / buttes and natural arches (the classic dunes / mesa /
    /// canyons / tablelands / badlands styles plus savanna and the varied start world).</summary>
    Buttes = 1 << 2,

    /// <summary>Hoodoo / fairy-chimney fields (the classic badlands / tablelands / mesa / canyons styles).</summary>
    Hoodoos = 1 << 3,

    /// <summary>Crystal-bearing surface: crystal shard outcrops in the set dressing (the classic <c>crystal*</c> keys).</summary>
    Crystal = 1 << 4,

    /// <summary>Wind-carved country (yardang fields, star dunes) — terrain generation 1.</summary>
    Wind = 1 << 5,

    /// <summary>Wetland: marsh sheets and reed beds on flats — terrain generation 1.</summary>
    Wetland = 1 << 6,

    /// <summary>Glacial landforms: troughs, drumlins, glacier tongues, tarns — terrain generation 1.</summary>
    Glacial = 1 << 7,

    /// <summary>Lone granite domes rising from flat ground — terrain generation 1.</summary>
    Inselbergs = 1 << 8,
}

/// <summary>Parses the data-side tag names of <see cref="PlanetType.TerrainTags"/>.</summary>
public static class TerrainTags
{
    /// <summary>Parses tag names (case-insensitive) into the flags enum. Unknown names are reported through
    /// <paramref name="unknown"/> (null when every name resolved) so content validation can fail loudly.</summary>
    public static TerrainTag Parse(System.Collections.Generic.IEnumerable<string>? names, out string? unknown)
    {
        unknown = null;
        var tags = TerrainTag.None;
        if (names is null)
        {
            return tags;
        }

        foreach (var name in names)
        {
            if (System.Enum.TryParse<TerrainTag>(name, ignoreCase: true, out var tag) && tag != TerrainTag.None)
            {
                tags |= tag;
            }
            else
            {
                unknown ??= name;
            }
        }

        return tags;
    }
}
