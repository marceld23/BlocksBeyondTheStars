// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>
    /// A private <see cref="WorldGenerator"/> configured exactly like the server's for one world (#1820): seed, body
    /// identity, size, cratering, landing pads, continents, volcanoes and terrain generation, all from the
    /// <see cref="FarTerrainWorldInfo"/> the server sends after a join or a world change. The far terrain
    /// (<see cref="FarTerrainSource"/>) and the biome looks (<see cref="BiomeLookResolver"/>, #3289) each build their own —
    /// a generator is not thread-safe, so every user owns one.
    /// </summary>
    internal static class FarWorldGenerator
    {
        /// <summary>The generator and the world's planet type, or null for a void world (station, ship interior) or a planet
        /// type this content does not know.</summary>
        public static WorldGenerator? Create(GameContent content, long worldSeed, FarTerrainWorldInfo info, out PlanetType planet)
        {
            planet = null!;
            if (info.Void || content.GetPlanet(info.PlanetType) is not { } p || p.Void)
            {
                return null;
            }

            var generator = new WorldGenerator(worldSeed, content);
            generator.SetContinentsEnabled(info.ContinentsEnabled);
            generator.SetLavaCoreVolcanoes(info.LavaCoreVolcanoes);
            generator.SetTerrainGeneration(info.TerrainGeneration);
            generator.SetWorldMode(info.Circumference > 0 ? info.Circumference : WorldConstants.Circumference,
                info.Cratered, FarTerrainSource.UnpackPads(info.Pads), string.IsNullOrEmpty(info.LocationId) ? null : info.LocationId);
            planet = p;
            return generator;
        }
    }
}
