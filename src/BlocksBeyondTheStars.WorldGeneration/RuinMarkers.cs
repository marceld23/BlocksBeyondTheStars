// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The markers of a ruin (#3487; shared since #3515 so every generator that runs the shared ruin pass treats them alike):
/// which marker types a ruin keeps, and how a surviving marker comes to rest on what the pass left.
/// </summary>
public static class RuinMarkerRules
{
    /// <summary>The marker types a RUIN keeps. Everything alive or working — residents, vendors, boards, doors, rooms,
    /// pre-built circuits — is gone from a ruin; what is left is what a visitor finds.</summary>
    public static readonly string[] Kept =
    {
        LandmarkRules.ChestMarker, "loot", "data_terminal", "relic_cache", LandmarkRules.SoundMarker, LandmarkRules.DenMarker,
    };

    /// <summary>Whether a ruin keeps a marker of this type (<see cref="Kept"/>).</summary>
    public static bool IsKept(string type) => Array.IndexOf(Kept, type) >= 0;

    /// <summary>Drops every marker a ruin does not keep, in place: those of <see cref="Kept"/>, or of
    /// <paramref name="kept"/> when a generator keeps others (its own caches).</summary>
    public static void KeepOnly(List<SettlementMarker> markers, IReadOnlyList<string>? kept = null)
    {
        var types = kept ?? Kept;
        markers.RemoveAll(m => !Contains(types, m.Type));
    }

    /// <summary>
    /// After the ruin pass: every surviving marker comes to rest on what is left (<see cref="Seat(VoxelGrid, Vector3i, int)"/>),
    /// in place.
    /// </summary>
    internal static void Seat(VoxelGrid grid, List<SettlementMarker> markers, int groundRow)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            var at = Seat(grid, m.LocalPos, groundRow);
            if (at.Y != m.LocalPos.Y)
            {
                markers[i] = new SettlementMarker(m.Type, at, m.DoorAxis, m.Data);
            }
        }
    }

    /// <summary>
    /// One marker cell after the ruin pass: when its floor fell it drops straight down to the next solid cell (below
    /// <paramref name="groundRow"/> a cell that is neither a block nor a cavity is the planet's rock and carries it too),
    /// and its own cell and the one above are freed — so a chest lies in the rubble of the floor it stood on instead of
    /// hanging in the air. Returns where it rests; a cell outside the grid stays as it is.
    /// </summary>
    internal static Vector3i Seat(VoxelGrid grid, Vector3i at, int groundRow)
    {
        int x = at.X, y = at.Y, z = at.Z;
        if (!grid.In(x, y, z))
        {
            return at;
        }

        bool Carries(int cy) => cy < 0 || grid.Solid(x, cy, z) || (cy < groundRow && !grid.Cavity[grid.Index(x, cy, z)]);

        while (y > 0 && !Carries(y - 1))
        {
            y--;
        }

        for (int dy = 0; dy <= 1; dy++)
        {
            if (grid.Solid(x, y + dy, z))
            {
                grid.Clear(x, y + dy, z);
            }
        }

        return y == at.Y ? at : new Vector3i(x, y, z);
    }

    private static bool Contains(IReadOnlyList<string> types, string type)
    {
        for (int i = 0; i < types.Count; i++)
        {
            if (string.Equals(types[i], type, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
