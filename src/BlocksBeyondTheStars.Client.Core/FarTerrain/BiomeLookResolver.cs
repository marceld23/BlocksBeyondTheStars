// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>
    /// #3289: which biome palette (<see cref="BiomePalettes"/>) a column of the current world wears — the colours a rune
    /// stone or an ancient brick standing there takes. Asks a private, server-identical generator (the far terrain's setup,
    /// <see cref="FarWorldGenerator"/>) for the column's biome surface, exactly as the server decides it
    /// (<see cref="WorldGenerator.BiomeSurfaceKeyAt"/>), so no protocol, save or world-generation change is needed and a
    /// rune a player carries to another biome shows that biome's colours.
    /// <para>Thread-safe: the chunk mesher asks from its worker threads, the generator is not thread-safe, so every query
    /// takes one lock and the answers are kept per column (bounded). A world with a single biome answers without either.</para>
    /// </summary>
    public sealed class BiomeLookResolver
    {
        /// <summary>The most columns the cache holds before it starts over.</summary>
        public const int MaxCachedColumns = 1 << 14;

        private const int Mixed = int.MinValue;

        private readonly object _lock = new object();
        private readonly WorldGenerator _generator;
        private readonly GameContent _content;
        private readonly PlanetType _planet;
        private readonly Dictionary<long, sbyte> _columns = new Dictionary<long, sbyte>();
        private readonly int _single; // the palette of a single-biome world, or Mixed

        private BiomeLookResolver(GameContent content, WorldGenerator generator, PlanetType planet, long worldSeed, int worldId)
        {
            _content = content;
            _generator = generator;
            _planet = planet;
            WorldSeed = worldSeed;
            WorldId = worldId;
            _single = generator.BiomeCount(planet) <= 1
                ? BiomePalettes.OfSurface(content, generator.BiomeSurfaceKeyAt(planet, 0, 0))
                : Mixed;
            PaletteFn = PaletteAt;
        }

        /// <summary>The world (<see cref="FarTerrainWorldInfo.WorldId"/>) this resolver answers for.</summary>
        public int WorldId { get; }

        /// <summary>The world seed it was built with.</summary>
        public long WorldSeed { get; }

        /// <summary><see cref="PaletteAt"/> as a delegate, bound once — what the chunk mesher takes.</summary>
        public Func<int, int, int> PaletteFn { get; }

        /// <summary>The resolver for a world, or null for a void world (station, ship interior) or an unknown planet type:
        /// there is no biome, and a look block keeps its own tile.</summary>
        public static BiomeLookResolver? Create(GameContent content, long worldSeed, FarTerrainWorldInfo info)
        {
            var generator = FarWorldGenerator.Create(content, worldSeed, info, out var planet);
            return generator == null ? null : new BiomeLookResolver(content, generator, planet, worldSeed, info.WorldId);
        }

        /// <summary>The palette index of a column (<see cref="BiomePalettes.Names"/>), or -1 when its biome surface names
        /// none.</summary>
        public int PaletteAt(int worldX, int worldZ)
        {
            if (_single != Mixed)
            {
                return _single;
            }

            long key = ((long)worldX << 32) ^ (uint)worldZ;
            lock (_lock)
            {
                if (_columns.TryGetValue(key, out sbyte cached))
                {
                    return cached;
                }

                if (_columns.Count >= MaxCachedColumns)
                {
                    _columns.Clear();
                }

                int palette = BiomePalettes.OfSurface(_content, _generator.BiomeSurfaceKeyAt(_planet, worldX, worldZ));
                _columns[key] = (sbyte)palette;
                return palette;
            }
        }
    }
}
