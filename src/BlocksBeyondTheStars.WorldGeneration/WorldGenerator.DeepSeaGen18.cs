// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The deep sea (#2111, generation 18): where the leviathan lives. Nothing here changes a block — these are queries over
/// the water the world was born with (<see cref="TryGetWaterSurface"/>), the sea giant's counterpart of
/// <see cref="IsSandSeaAt"/>: a column is deep sea when liquid water stands at least <see cref="GiantRules.LeviathanMinDepth"/>
/// blocks over its bed, and a world hosts the giant when enough of its surface is.
/// </summary>
public sealed partial class WorldGenerator
{
    private readonly Dictionary<string, bool> _deepSeaWorlds = new();

    /// <summary>Whether (x, z) lies under the deep sea of this world — the leviathan's habitat, where a vibration carries
    /// through the water and where the giant may rise. False on a dry column, a puddle, a river and a shallow shelf.</summary>
    public bool IsDeepSeaAt(PlanetType planet, int worldX, int worldZ)
        => TryGetWaterSurface(planet, worldX, worldZ, out int top, out int bed) && top - bed >= GiantRules.LeviathanMinDepth;

    /// <summary>The liquid water column at (x, z) if it is deep sea: the topmost water cell and the seabed.</summary>
    public bool TryGetDeepSea(PlanetType planet, int worldX, int worldZ, out int waterTopY, out int seabedY)
        => TryGetWaterSurface(planet, worldX, worldZ, out waterTopY, out seabedY) && waterTopY - seabedY >= GiantRules.LeviathanMinDepth;

    /// <summary>Whether this world has a deep sea worth a leviathan: at least <see cref="GiantRules.LeviathanMinDeepShare"/>
    /// of a coarse grid of its columns is deep sea. Measured once per type (the answer is a property of the world's own
    /// calibration, not of a spot) — a scan of a few thousand columns through the surface cache.</summary>
    public bool HostsDeepSea(PlanetType planet)
    {
        if (planet is null || planet.Void || SeaLevel(planet) == int.MinValue)
        {
            return false;
        }

        lock (_deepSeaWorlds)
        {
            if (_deepSeaWorlds.TryGetValue(planet.Key, out bool known))
            {
                return known;
            }
        }

        bool hosts = DeepSeaShare(planet) >= GiantRules.LeviathanMinDeepShare;
        lock (_deepSeaWorlds)
        {
            _deepSeaWorlds[planet.Key] = hosts;
        }

        return hosts;
    }

    /// <summary>The share of this world's surface that is deep sea, sampled on a coarse grid (the hosting rule, tests).</summary>
    internal double DeepSeaShare(PlanetType planet, int step = 48)
    {
        int n = 0, deep = 0;
        int period = LatPeriod;
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < _circumference; x += step)
            {
                n++;
                if (IsDeepSeaAt(planet, x, z))
                {
                    deep++;
                }
            }

        return n == 0 ? 0.0 : deep / (double)n;
    }
}
