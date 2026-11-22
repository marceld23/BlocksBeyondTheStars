// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    public sealed partial class BlockTextureAtlas
    {
        // ---------------------------------------------------------------- flora looks (#2832)
        //
        // A flora archetype can have extra tiles of the same plant in other shapes (BlockDefinition.Looks); every world
        // wears one of them (FloraLooks). The look is painted into the block's OWN slot, so the mesh, the variants and
        // every UV stay as they are: a world change repaints a handful of slots and nothing re-meshes.

        private readonly System.Collections.Generic.Dictionary<ushort, string> _looks = new();

        /// <summary>#2832: the looks the current world wears (block id → texture key; a block missing from the map wears
        /// its own tile). Repaints only the slots whose look changed, then publishes the batch like a texture change.</summary>
        public void SetLooks(System.Collections.Generic.IReadOnlyDictionary<ushort, string> looks)
        {
            if (Texture == null || _content == null)
            {
                return;
            }

            var changed = new System.Collections.Generic.List<ushort>();
            foreach (var kv in looks)
            {
                if (!_looks.TryGetValue(kv.Key, out string old) || old != kv.Value)
                {
                    changed.Add(kv.Key);
                }
            }

            foreach (var kv in _looks)
            {
                if (!looks.ContainsKey(kv.Key))
                {
                    changed.Add(kv.Key);
                }
            }

            if (changed.Count == 0)
            {
                return;
            }

            _looks.Clear();
            foreach (var kv in looks)
            {
                _looks[kv.Key] = kv.Value;
            }

            bool any = false;
            foreach (ushort id in changed)
            {
                var def = _content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(id));
                if (def != null)
                {
                    any |= RepaintBlock(def);
                }
            }

            if (any)
            {
                FinishRepaint();
            }
        }

        /// <summary>Repaints the blocks whose current look is the texture <paramref name="key"/> (a pack or the world
        /// layer changed it).</summary>
        private bool RepaintLookWearers(string key)
        {
            bool any = false;
            foreach (var kv in _looks)
            {
                if (kv.Value == key && _content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(kv.Key)) is { } def)
                {
                    any |= RepaintBlock(def);
                }
            }

            return any;
        }

        /// <summary>Paints the look the current world gives this block, if it has one and the block's own tile is not
        /// replaced by a texture pack or the world layer (a deliberate repaint of the block wins over a look).</summary>
        private bool TryPaintLook(int id, string key, int ox, int oy)
        {
            if (_officialOnly || !_looks.TryGetValue((ushort)id, out string look) || look == key)
            {
                return false;
            }

            var layer = GameTextures.LayerOf(key);
            return (layer == null || layer == TextureLayer.Official) && TryPaintFromAsset(look, ox, oy);
        }
    }
}
