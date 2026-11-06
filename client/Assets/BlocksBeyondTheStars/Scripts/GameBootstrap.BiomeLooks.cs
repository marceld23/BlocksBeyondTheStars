// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's biome looks (#3289): the rune stone and the ancient brick take the colours of the biome they stand
    /// in. The world's column → palette lookup (<see cref="BiomeLookResolver"/>) is built from the same world info the far
    /// terrain gets (sent after every join and world change, before the world's first chunk) and handed to every planet
    /// chunk build; a chunk that was meshed without it, or with looks a texture repaint has since switched, is meshed again.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The current world's lookup, or null: before its world info arrived, on a void world, on an old server.
        /// Main thread only — a mesh job captures its delegate at dispatch.</summary>
        private BiomeLookResolver _biomeLooks;

        /// <summary>The atlas <c>BiomeLookVersion</c> the loaded chunks were meshed with.</summary>
        private int _biomeLookVersionSeen;

        /// <summary>Called once from <see cref="WireChunkStreaming"/>.</summary>
        private void WireBiomeLooks()
        {
            Network.JoinAccepted += _ => _biomeLooks = null; // another world: its own info follows
            Network.WorldResetReceived += _ => _biomeLooks = null;
            Network.FarTerrainWorldInfoReceived += OnBiomeLookWorldInfo;
        }

        private void OnBiomeLookWorldInfo(FarTerrainWorldInfo info)
        {
            if (Content == null)
            {
                return;
            }

            _biomeLooks = BiomeLookResolver.Create(Content, _worldSeed, info);
            if (_biomeLooks != null)
            {
                RemeshBiomeLookChunks(); // meshed before the info arrived: they show the blocks' own tiles
                _biomeLookVersionSeen = Atlas != null ? Atlas.BiomeLookVersion : _biomeLookVersionSeen; // those re-meshes know it
            }
        }

        /// <summary>From <see cref="OnAtlasRepainted"/>: a texture pack or the world layer repainted a look block or one of
        /// its looks, which switches its looks on or off — the chunks holding one are meshed again.</summary>
        private void RemeshOnBiomeLookChange()
        {
            if (Atlas == null || Atlas.BiomeLookVersion == _biomeLookVersionSeen)
            {
                return;
            }

            _biomeLookVersionSeen = Atlas.BiomeLookVersion;
            RemeshBiomeLookChunks();
        }

        /// <summary>Marks every loaded chunk that holds a look block for re-meshing (the others look the same either way).</summary>
        private void RemeshBiomeLookChunks()
        {
            if (Atlas == null || Content == null || _chunkObjects.Count == 0)
            {
                return;
            }

            var lookIds = new bool[ushort.MaxValue + 1];
            bool any = false;
            foreach (var def in Content.Blocks.Values)
            {
                if (def.BiomeLooks && Atlas.WearsBiomeLooks(def.NumericId.Value))
                {
                    lookIds[def.NumericId.Value] = true;
                    any = true;
                }
            }

            if (!any)
            {
                return;
            }

            foreach (var coord in _chunkObjects.Keys)
            {
                if (_chunkArrays.TryGetValue(WorldConstants.CanonicalChunk(coord, Circumference), out var blocks)
                    && HoldsLookBlock(blocks, lookIds))
                {
                    _dirty.Add(coord);
                }
            }
        }

        private static bool HoldsLookBlock(ushort[] blocks, bool[] lookIds)
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                if (lookIds[blocks[i]])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
