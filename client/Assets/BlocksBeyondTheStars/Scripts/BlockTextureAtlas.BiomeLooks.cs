// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- biome looks (#3289)
    //
    // A block with BlockDefinition.BiomeLooks (the rune stone, the ancient brick) has one more tile per biome palette —
    // texture-only keys (BlockFaceTextures.TextureOnlyKeys), dealt into the extras band and painted there like the face
    // tiles. Unlike a flora look the tile is NOT painted into the block's own slot: one world shows several biomes, so the
    // chunk mesher maps each cell onto the tile of the palette its column wears (ChunkMesher.BiomeLooks.cs). This table
    // tells it which slot that is. The block's own slot keeps its own tile — ships, stations, the held block and the
    // icons show that one.
    public sealed partial class BlockTextureAtlas
    {
        /// <summary>[block id] → the atlas slot of each palette's look (0 = the block's own tile), null for a block without
        /// looks. Immutable once published: the mesher reads it from worker threads, a refresh replaces it whole.</summary>
        private volatile ushort[][] _biomeLookSlots = System.Array.Empty<ushort[]>();

        /// <summary>Bumps whenever a block's look slots changed (a texture pack or the world layer repainted a look block or
        /// one of its looks) — the chunks holding look blocks must re-mesh then.</summary>
        public int BiomeLookVersion { get; private set; }

        /// <summary>Whether the block wears biome looks at all (<see cref="BlockDefinition.BiomeLooks"/>) — whatever tiles
        /// it shows right now.</summary>
        public bool WearsBiomeLooks(ushort id)
        {
            var table = _biomeLookSlots;
            return id < table.Length && table[id] != null;
        }

        /// <summary>The atlas slot of a block's look in a palette, or 0 when the cell shows the block's own tile (no looks,
        /// no such palette, or a look that a repaint of the block overrules).</summary>
        public ushort BiomeLookSlot(ushort id, int palette)
        {
            var table = _biomeLookSlots;
            var slots = id < table.Length ? table[id] : null;
            return slots != null && (uint)palette < (uint)slots.Length ? slots[palette] : (ushort)0;
        }

        /// <summary>Rebuilds the look table from the face-tile slots and the texture layers (main thread: at the build and
        /// after every repaint batch).</summary>
        private void RefreshBiomeLooks()
        {
            if (_content == null)
            {
                return;
            }

            var table = BuildBiomeLookTable();
            if (SameLookTable(_biomeLookSlots, table))
            {
                return;
            }

            _biomeLookSlots = table;
            BiomeLookVersion++;
        }

        private ushort[][] BuildBiomeLookTable()
        {
            int size = 0;
            foreach (var b in _content.Blocks.Values)
            {
                if (b.BiomeLooks && b.NumericId.Value + 1 > size)
                {
                    size = b.NumericId.Value + 1;
                }
            }

            var table = new ushort[size][];
            foreach (var b in _content.Blocks.Values)
            {
                if (!b.BiomeLooks || b.NumericId.Value == 0)
                {
                    continue;
                }

                // Like a flora look (TryPaintLook): a deliberate repaint of the block — a texture pack, the world layer —
                // wins over a look that was not repainted as well, and a look the build does not ship keeps the own tile.
                var own = GameTextures.LayerOf(b.Key) ?? TextureLayer.Official;
                var slots = new ushort[BiomePalettes.Count];
                for (int p = 0; p < slots.Length; p++)
                {
                    string key = BiomePalettes.LookKey(b.Key, p);
                    if (_faceTiles.TryGetValue(key, out int slot) && GameTextures.LayerOf(key) is { } layer && layer >= own)
                    {
                        slots[p] = (ushort)slot;
                    }
                }

                table[b.NumericId.Value] = slots;
            }

            return table;
        }

        private static bool SameLookTable(ushort[][] a, ushort[][] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || b[i] == null)
                {
                    if (a[i] != b[i])
                    {
                        return false;
                    }

                    continue;
                }

                if (!System.Linq.Enumerable.SequenceEqual(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
