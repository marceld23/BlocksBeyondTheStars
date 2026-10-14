// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Dry natural ground for a test that lets the spawner place land animals (#2553). The spawner puts them on the
/// generator's ground, and a land animal never appears with water on its feet any more — tests that stood at a fixed
/// spot were, on their seed, standing over a sea, and their herds had been placed on the seabed. Found from the
/// generator alone (no chunk is loaded), so it follows any change to terrain generation instead of pinning a spot.
/// </summary>
internal static class DryGround
{
    /// <summary>The centre of a square of half size <paramref name="radius"/> with no generated water in it, searched
    /// on a coarse grid in rings around the origin.</summary>
    public static (int X, int Z) Find(SvGameServer server, int radius)
    {
        const int Step = 48;
        for (int ring = 0; ring <= 40; ring++)
        {
            for (int i = -ring; i <= ring; i++)
            {
                foreach (var (gx, gz) in new[] { (i, -ring), (i, ring), (-ring, i), (ring, i) })
                {
                    int x = gx * Step, z = gz * Step;
                    if (IsDry(server, x, z, radius))
                    {
                        return (x, z);
                    }
                }
            }
        }

        throw new InvalidOperationException($"no dry ground of radius {radius} found on this world");
    }

    private static bool IsDry(SvGameServer server, int x, int z, int radius)
    {
        for (int dx = -radius; dx <= radius; dx += 2)
        {
            for (int dz = -radius; dz <= radius; dz += 2)
            {
                if (server.WaterSurfaceForTest(x + dx, z + dz) is not null)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
