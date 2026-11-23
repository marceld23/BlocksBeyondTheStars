// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A city is a place, not a planet flag (#3980): where one composed city (<see cref="CityGenerator"/>) stands and what it
/// is composed from. The server stamps every city through one path from a list of these; the G.D.S. world's list is one
/// place centred on landing pad 0 (its record is settlement 0, its theme the planet type's <c>cityWorld</c>). A world may
/// carry several — each its own settlement index, record and seed.
/// </summary>
public sealed class CityPlace
{
    /// <summary>The theme of the G.D.S. city — the composer's rng lane and the record's <c>city:&lt;theme&gt;</c> template.</summary>
    public const string DefaultTheme = "gds";

    public CityPlace(int settlementIndex, GivenSeat centre, string theme, long seed, int grid = CityGenerator.Modules)
    {
        SettlementIndex = settlementIndex;
        Centre = centre;
        Theme = string.IsNullOrEmpty(theme) ? DefaultTheme : theme;
        Seed = seed;
        Grid = grid;
    }

    /// <summary>The settlement record's index (kind <c>settlement</c>) the city is pinned under.</summary>
    public int SettlementIndex { get; }

    /// <summary>The city's centre column and — when fixed — its ground row (the G.D.S. city: pad 0 and its level).</summary>
    public GivenSeat Centre { get; }

    /// <summary>The theme key, as the record names it (<c>city:&lt;theme&gt;</c>); the composer reads it lower-case.</summary>
    public string Theme { get; }

    /// <summary>The city's seed: its composition, kit draw and name come from it (the G.D.S. city: the settlement seed).</summary>
    public long Seed { get; }

    /// <summary>Districts per side of a city composed without a kit (default 7: the G.D.S. city's 7×7).</summary>
    public int Grid { get; }

    /// <summary>A city kit to compose from (its key); empty = drawn from the planet type's city kits as the G.D.S. city is.</summary>
    public string Kit { get; set; } = string.Empty;

    /// <summary>The city's name; empty = the G.D.S. name roll.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Later (coastlines): whether a world column is land the city may build on. Null = everywhere. Not read yet.</summary>
    public System.Func<int, int, bool>? LandMask { get; set; }

    /// <summary>Later (landmarks in cities): the district (grid cell) kept free for a landmark. Not read yet.</summary>
    public (int Gx, int Gz)? LandmarkSquare { get; set; }

    /// <summary>World rectangles (inclusive) the composer keeps as open ground — a landing pad, the wreck's crash site.</summary>
    public List<(int MinX, int MinZ, int MaxX, int MaxZ)> OpenZones { get; } = new();

    /// <summary>The composer's theme key (lower-case, so the rng lane never depends on how the data spells it).</summary>
    public string ThemeKey => Theme.ToLowerInvariant();

    /// <summary>The layout of a city composed without a kit: the G.D.S. layout with <see cref="Grid"/> districts per side
    /// (odd, 3..9 — a centre must exist).</summary>
    public CityLayoutSpec Layout => Grid == CityGenerator.Modules
        ? CityLayoutSpec.Default
        : new CityLayoutSpec(System.Math.Clamp(Grid | 1, 3, 9), CityGenerator.ModuleSize, CityGenerator.Street, CityGenerator.Height, null);

    /// <summary>The open zones in structure-local coordinates of a city whose origin corner is (<paramref name="originX"/>,
    /// <paramref name="originZ"/>).</summary>
    public List<CityGenerator.OpenZone> LocalOpenZones(int originX, int originZ)
    {
        var zones = new List<CityGenerator.OpenZone>(OpenZones.Count);
        foreach (var (minX, minZ, maxX, maxZ) in OpenZones)
        {
            zones.Add(new CityGenerator.OpenZone(minX - originX, minZ - originZ, maxX - originX, maxZ - originZ));
        }

        return zones;
    }
}
