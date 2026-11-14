// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The G.D.S. city that grew (#3599), the stamper's half: its suburbs are pinned in the city's record like its districts
/// (<see cref="StructurePlacementRecord.Plan"/>, one plan per gate joined by <see cref="SuburbSeparator"/>), and every other
/// landing pad inside its larger footprint stays open ground.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Between the suburb plans of a city record (a plan's text never holds it).</summary>
    private const char SuburbSeparator = '~';

    /// <summary>The suburb plan list a grown city composes with: the pinned plans (a replay), an empty list (a fresh city
    /// records them), or null for a classic city. Also keeps every landing pad inside a grown city's footprint open.</summary>
    private List<string>? CitySuburbPins(StructurePlacementRecord? rec, CityLayoutSpec spec, Vector3i origin, int size,
        List<CityGenerator.OpenZone> zones)
    {
        if (!spec.Grown)
        {
            return null;
        }

        int half = LandingPadRadius + 3;
        foreach (var pad in _landingPads)
        {
            int lx = pad.CenterX - origin.X, lz = pad.CenterZ - origin.Z;
            if (lx >= -half && lz >= -half && lx < size + half && lz < size + half)
            {
                zones.Add(new CityGenerator.OpenZone(lx - half, lz - half, lx + half, lz + half));
            }
        }

        return rec?.Plan is { Length: > 0 } pinned ? new List<string>(pinned.Split(SuburbSeparator)) : new List<string>();
    }

    /// <summary>Writes a fresh grown city's suburb plans into its record (once).</summary>
    private void PinCitySuburbs(StructurePlacementRecord? loaded, List<string>? suburbs)
    {
        if (suburbs is not { Count: > 0 } || loaded?.Plan is { Length: > 0 } || FindPlacementRecord("settlement", 0) is not { } rec)
        {
            return;
        }

        rec.Plan = string.Join(SuburbSeparator.ToString(), suburbs);
        _placementRecordsDirty = true;
    }
}
