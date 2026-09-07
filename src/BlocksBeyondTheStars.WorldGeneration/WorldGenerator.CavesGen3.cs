// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 3 — the caves: dripstone (stalactites hanging from a carve span's roof,
/// stalagmites rising from its floor, both meeting in a column where they are long enough) in every tunnel and
/// cavern of a wet karst or wetland world, and the taller karst caverns (the cathedral halls) that
/// <c>TryGetCavernSpan</c> rolls from generation 3. Dripstone is a per-column length pair the column phase
/// resolves once (<see cref="DripstoneAt"/>); the y-loop reads it at the span ends. Nothing here runs on a
/// generation 0–2 world.</summary>
public sealed partial class WorldGenerator
{
    private const long DripRoofSalt = 0x4A2572;
    private const long DripFloorSalt = 0x4A2573;

    /// <summary>The dripstone gate: a cave-bearing, air-bearing world with limestone country (Karst) or standing
    /// water everywhere (Wetland) and enough of it to drip — jungle, karst, fungal, boreal, swamp, ocean,
    /// archipelago. Deliberately NOT every wet world with caves (most planet types inherit the default water
    /// abundance), so the highland control world stays a world without any generation-3 family.</summary>
    private bool HasDripstone(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && planet.CaveThreshold > 0.0
           && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4
           && (planet.HasTag(TerrainTag.Karst) || planet.HasTag(TerrainTag.Wetland));

    /// <summary>Dripstone lengths for this column: how many cells hang from a carve span's roof (1–4) and how many
    /// rise from its floor (1–3). 0/0 on most columns — the roof mask spikes on ~8 % of columns, the floor mask on
    /// ~6 % — so the formations stand alone, a hanging spike here, a rising one there, and now and then both in
    /// one column. Deterministic per column and seam-safe (wrapped X / canonical Z like every column hash).</summary>
    private (int Down, int Up) DripstoneAt(WonderProfile w, int worldX, int worldZ)
    {
        int wx = WorldConstants.WrapX(worldX, _circumference), wz = Wz(worldZ);
        double r = Noise.Value01(w.Seed + DripRoofSalt, wx, 0, wz);
        double f = Noise.Value01(w.Seed + DripFloorSalt, wx, 0, wz);
        int down = r > 0.92 ? 1 + (int)((r - 0.92) / 0.08 * 3.0) : 0; // 1..4
        int up = f > 0.94 ? 1 + (int)((f - 0.94) / 0.06 * 2.0) : 0;   // 1..3
        return (down, up);
    }

    /// <summary>The dripstone lengths the column phase would resolve at this column, or 0/0 when the world has
    /// no dripstone (tests).</summary>
    internal (int Down, int Up) DripstoneForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.Dripstone ? DripstoneAt(w, worldX, worldZ) : (0, 0);
    }

    /// <summary>Whether a carve span [lo, hi] takes dripstone at all: room for both formations AND a cell of air
    /// between them, and rock above the roof (a span open to the sky is a cave mouth, and a spike hanging from
    /// nothing is a floating block). <paramref name="groundY"/> is the column's ground top (the seabed on a
    /// water column).</summary>
    private static bool DripstoneFits(int lo, int hi, int down, int up, int groundY)
        => hi - lo >= down + up + 2 && hi < groundY - 1;
}
