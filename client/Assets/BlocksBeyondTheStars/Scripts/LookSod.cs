// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The grass sod (#3929). A grass block wears its green tile on all six faces, so a grassy slope is a
    /// green wall. With the sod the grass kinds show earth on their sides, with the grass hanging over the upper edge,
    /// and earth below: the face slots every machine uses (<see cref="BlockFaceTexture"/>, #2124) with a side tile
    /// composed from the calm tiles (<c>Resources/textures/face_&lt;key&gt;_side</c>).
    /// <para>
    /// In the game this is three lines of <c>data/blocks.json</c> and three bundled tiles. The client adds the face slots to
    /// its content in memory instead — display only, behind <see cref="GameLook.GrassSod"/> — so the data files,
    /// the server and the content it checks stay untouched.
    /// </para>
    /// </summary>
    internal static class LookSod
    {
        /// <summary>The grass kinds and the earth the world generator lays under each of them.</summary>
        private static readonly (string Block, string Under)[] Kinds =
        {
            ("grass", "dirt"),
            ("alien_grass", "dirt"),
            ("mycelium", "dirt"),
        };

        /// <summary>The key of a grass kind's side tile.</summary>
        public static string SideTile(string block) => "face_" + block + "_side";

        /// <summary>The tile of the mushroom cap's underside.</summary>
        public const string CapGillsTile = "face_mushroom_cap_bottom";

        /// <summary>The tile of the geyser's hole: the vent's sinter ring and opening on a transparent rest.</summary>
        public const string VentHoleTile = "geyser_hole";

        /// <summary>Gives the look's blocks their face slots — once per content snapshot, before the face table is built
        /// from it (<see cref="ShapeFaceTextures"/>): the grass kinds' sod (a kind that already declares faces, or whose
        /// earth the content does not know, is left alone), the mushroom cap's gills underneath, and a slot for the
        /// geyser's hole.</summary>
        public static void Apply(GameContent content)
        {
            if (content == null)
            {
                return;
            }

            if (GameLook.GrassSod)
            {
                foreach (var (block, under) in Kinds)
                {
                    var def = content.GetBlock(block);
                    if (def == null || (def.Faces != null && def.Faces.Count > 0) || content.GetBlock(under) == null)
                    {
                        continue;
                    }

                    def.Faces = new List<BlockFaceTexture>
                    {
                        new BlockFaceTexture { Side = "side", Tile = SideTile(block) },
                        new BlockFaceTexture { Side = "bottom", Tile = under },
                    };
                }
            }

            // The cap of a giant mushroom is built from cap blocks: skin above and at the sides, gills below.
            if (GameLook.CapGills && content.GetBlock("mushroom_cap") is { } cap && (cap.Faces == null || cap.Faces.Count == 0))
            {
                cap.Faces = new List<BlockFaceTexture> { new BlockFaceTexture { Side = "bottom", Tile = CapGillsTile } };
            }

            // The geyser's hole needs an atlas slot. A face of a part a cube never draws (the pot's rim) deals the slot
            // without changing how the vent's own faces look; the mesher lays the hole on the ground itself
            // (ChunkMesher.Vent.cs) and reads the slot from ShapeFaceTextures.TextureSlots.
            if (GameLook.VentGround && content.GetBlock("geyser_vent") is { } vent)
            {
                vent.Faces ??= new List<BlockFaceTexture>();
                if (!vent.Faces.Exists(f => f.Tile == VentHoleTile))
                {
                    vent.Faces.Add(new BlockFaceTexture { Part = "rim", Side = "top", Tile = VentHoleTile });
                }
            }
        }
    }
}
