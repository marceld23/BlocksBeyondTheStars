// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Devices drawn as a form inside their cube (<see cref="DeviceForms"/>, ChunkMesher.DeviceForms.cs).
    /// <para>
    /// The forms' parts wear tiles that are no blocks (<c>devform_*</c> from Resources/textures, and face tiles such as
    /// <c>face_forge_side</c>), so those need an atlas slot in the extras band. The band is dealt from the face slots the
    /// content's blocks name (<see cref="FaceTileBand"/>), so — like <see cref="LookSod"/> — each such tile is named on the
    /// device that uses it, in memory only, before the face table is built: as a slot of the PILLOW's bottom, a face no
    /// device ever draws (a pillow exists only in the bed's head form, and its bottom lies inside the mattress). A tile that
    /// does not exist in this build is not named, so the atlas paints no grey stand-in and logs nothing; the mesher then
    /// shows the block's own tile on those faces.
    /// </para>
    /// </summary>
    internal static class LookDeviceForms
    {
        private const string CarrierPart = "pillow";
        private const string CarrierSide = "bottom";

        /// <summary>Names the forms' tiles on their devices — once per content snapshot, called by
        /// <see cref="ShapeFaceTextures"/> right before it builds its table (on the thread that builds the atlas).</summary>
        public static void Apply(GameContent content)
        {
            if (content == null)
            {
                return;
            }

            int named = 0;
            foreach (var form in DeviceForms.Table.Values)
            {
                var def = content.GetBlock(form.Key);
                if (def == null)
                {
                    continue;
                }

                foreach (string key in form.TileKeys())
                {
                    if (content.GetBlock(key) != null || !TileExists(key))
                    {
                        continue; // a block has its own slot; a missing tile falls back to the own tile
                    }

                    def.Faces ??= new List<BlockFaceTexture>();
                    if (!def.Faces.Exists(f => f.Tile == key))
                    {
                        def.Faces.Add(new BlockFaceTexture { Part = CarrierPart, Side = CarrierSide, Tile = key });
                        named++;
                    }
                }
            }

            Debug.Log($"[Look] device forms: {DeviceForms.Table.Count} forms, {named} tile names added to the face band");
        }

        /// <summary>Whether a texture layer has the tile. Resources can only be read on the main thread; anywhere else the
        /// tile counts as missing (the forms then show the own tile there) instead of throwing.</summary>
        private static bool TileExists(string key)
        {
            try
            {
                return GameTextures.TileBytes(key) != null;
            }
            catch (UnityException)
            {
                return false;
            }
        }
    }
}
