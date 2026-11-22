// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- calm tiles
    //
    // The bundled tiles are soft forms that need no calming (CalmTileRule; a still bundled tile that wins for its key,
    // decided in TryPaintFromAsset). Two things follow:
    //   - the block shader's texture calm (the fade toward a tile's mean colour) must leave them alone. The atlas notes
    //     per slot whether it holds such a tile and publishes that as a Cols x Rows mask the shader reads at the tile's
    //     cell: 0 = keep the pattern, 1 = calm. Tiles painted in code and the classic data tiles stay calmed;
    //   - a ground block's two variant tiles must not get the classic cracks and speckles — single dark and light texels
    //     on a soft tile are exactly the grit the tile was drawn without. A calm tile's variants are the same form
    //     mirrored, a small step darker and lighter (CalmVariantTone): on a calm tile the classic 4 % and 6 % read as a
    //     chequerboard of blocks.
    // A tile a PLAYER painted (the local pack or the world layer, GameTextures.ShowsPaintedTile) is exempt the same way,
    // its ground and plant variants are only the tone step and never mirrored, and the same holds for the strip slots of
    // a painted animation — the rule follows the origin of the pixels each slot actually shows.
    public sealed partial class BlockTextureAtlas
    {
        /// <summary>The shader's name of the calm mask (read by <c>BlockAtlas.shader</c>).</summary>
        private static readonly int TileMaskId = Shader.PropertyToID("_Sc_LookTileMask");

        /// <summary>The brightness step between a calm ground tile and its two variants (2 % darker and 2 % lighter; the
        /// classic tiles' cracked and speckled variants use 4 % and 6 %).</summary>
        private const float CalmVariantTone = 0.02f;

        /// <summary>[slot] → the slot shows a calm tile, a tile a player painted, or a variant derived from one. Such a
        /// slot is exempt from the texture calm (mask texel 0).</summary>
        private readonly bool[] _calmSlot = new bool[Cols * Rows];

        /// <summary>[slot] → of the exempt slots, the ones whose pixels a player painted (the local pack or the world layer,
        /// <see cref="GameTextures.ShowsPaintedTile"/>): their variants keep the painting's orientation.</summary>
        private readonly bool[] _playerSlot = new bool[Cols * Rows];

        private Texture2D _calmMask;

        /// <summary>Set by <see cref="TryPaintFromAsset"/>: the tile it just blitted is exempt from the texture calm (a
        /// calm tile, or a painted one).</summary>
        private bool _paintedCalm;

        /// <summary>Set by <see cref="TryPaintFromAsset"/> together with <see cref="_paintedCalm"/>: the tile it just
        /// blitted was painted by a player.</summary>
        private bool _paintedPlayer;

        private bool IsCalmSlot(int slot) => (uint)slot < (uint)_calmSlot.Length && _calmSlot[slot];

        private bool IsPlayerSlot(int slot) => (uint)slot < (uint)_playerSlot.Length && _playerSlot[slot];

        /// <summary>Notes whether a slot holds a calm tile; an exempt tile is a player's when the asset blit that set
        /// <see cref="_paintedCalm"/> said so (both flags come from the same <see cref="TryPaintFromAsset"/>).</summary>
        private void MarkCalmSlot(int slot, bool calm) => MarkSlot(slot, calm, calm && _paintedPlayer);

        /// <summary>Notes a slot's two flags. Slot 0 (air) is the scratch slot of the "official tile" read-backs and never
        /// rendered, so it is never marked.</summary>
        private void MarkSlot(int slot, bool calm, bool player)
        {
            if (slot > 0 && slot < _calmSlot.Length)
            {
                _calmSlot[slot] = calm;
                _playerSlot[slot] = calm && player;
            }
        }

        /// <summary>A frame of an animated tile in its strip (#1957): the strip slot is what the shader samples while the
        /// tile animates, so it carries the flags of the tile's origin. An animated tile is never a calm tile
        /// (<see cref="TryPaintFromAsset"/>), so only a painted one is exempt.</summary>
        private void MarkStripSlot(int slot, string key)
        {
            bool player = GameTextures.ShowsPaintedTile(key);
            MarkSlot(slot, player, player);
        }

        /// <summary>The variant of an exempt tile. A calm tile's: the base tile mirrored (variant 0 left-right, variant 1
        /// top-bottom — a tile that wraps still wraps mirrored) and a small step darker or lighter. A painted tile's: the
        /// same small tone step only, never mirrored — a painted arrow or letter must stay readable. Used for ground and
        /// (painted only) plant variants; the alpha is kept, so a plant's silhouette stays as painted.</summary>
        private void PaintCalmVariant(ushort baseId, ushort slot, int variant)
        {
            int sx = (baseId % Cols) * Tile, sy = (baseId / Cols) * Tile;
            int dx = (slot % Cols) * Tile, dy = (slot / Cols) * Tile;
            var px = Texture.GetPixels(sx, sy, Tile, Tile);
            var turned = new Color[px.Length];
            bool painted = IsPlayerSlot(baseId);
            float k = variant == 0 ? 1f - CalmVariantTone : 1f + CalmVariantTone;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    int from = painted ? (y * Tile) + x
                        : variant == 0 ? (y * Tile) + (Tile - 1 - x) : ((Tile - 1 - y) * Tile) + x;
                    Color c = px[from];
                    turned[(y * Tile) + x] = new Color(
                        Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
                }
            }

            Texture.SetPixels(dx, dy, Tile, Tile, turned);
            MarkSlot(slot, true, painted);
        }

        /// <summary>A plant variant of a painted plant tile is the painted tile with a tone step (no eroded edge, no warm
        /// lean) and exempt from the calm; any other plant variant is marked as calmed. True when the variant was painted
        /// here.</summary>
        private bool TryPaintPaintedFloraVariant(ushort baseId, ushort slot, int variant)
        {
            if (IsPlayerSlot(baseId))
            {
                PaintCalmVariant(baseId, slot, variant);
                return true;
            }

            MarkSlot(slot, false, false);
            return false;
        }

        /// <summary>Publishes the mask of the calm slots. Called after the build and after every repaint batch.</summary>
        private void PublishCalmMask()
        {
            if (_calmMask == null)
            {
                _calmMask = new Texture2D(Cols, Rows, TextureFormat.R8, mipChain: false, linear: true)
                {
                    name = "CalmTileMask",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            var raw = new byte[Cols * Rows];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = _calmSlot[i] ? (byte)0 : (byte)255;
            }

            _calmMask.SetPixelData(raw, 0);
            _calmMask.Apply(updateMipmaps: false);
            Shader.SetGlobalTexture(TileMaskId, _calmMask);
        }

        private void DestroyCalmMask()
        {
            if (_calmMask != null)
            {
                UnityEngine.Object.Destroy(_calmMask);
                _calmMask = null;
            }
        }
    }
}
