// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The geyser vent wears the ground around it: it takes the texture of its neighbouring block and the geyser hole
    /// is laid on top.
    /// <para>
    /// A vent is a block of its own whose tile brings dark rock, so it stood as a black square in a meadow, on a beach or
    /// in the snow. The mesher draws the vent's cell as the ground block beside it
    /// (<see cref="VentHost"/>: its tile on every face, its world colour, its material) and lays the vent's hole — the
    /// sinter ring and the opening, cut out of the vent's tile (<c>textures/geyser_hole</c>) — on the top face as one
    /// more quad (<see cref="AddVentHole"/>). Display only: the cell stays a vent in the world, for the server, the steam
    /// and the collider. A vent with no ground beside or below it keeps its own tile.
    /// </para>
    /// </summary>
    public static partial class ChunkMesher
    {
        /// <summary>How far the hole floats above the ground's top face (blocks) — enough to win the depth test, too
        /// little to see from the side.</summary>
        private const float VentHoleLift = 0.004f;

        /// <summary>How far the hole's quad stays inside the cell's edge (blocks). The hole's picture keeps 14 of 64
        /// pixels free on every side; a quad that ends before the tile's border never samples the neighbouring atlas
        /// tile at a distance, and stays clear of the ground block's rounded edge.</summary>
        private const float VentHoleInset = 0.17f;

        [System.ThreadStatic] private static Vector3[] _ventQuadScratch;

        private sealed partial class BlockTraits
        {
            private bool[] _ventHost;
            private int _vent = -1;

            /// <summary>The geyser vent's block id, 0 when the content has none. Looked up once.</summary>
            public ushort VentOf()
            {
                if (_vent < 0)
                {
                    var def = Content.GetBlock("geyser_vent");
                    _vent = def != null ? def.NumericId.Value : 0;
                }

                return (ushort)_vent;
            }

            /// <summary>Whether a vent may wear this block: natural ground that fills its cell — an opaque terrain block
            /// that is no liquid, no picture block (another vent, a broken machine) and no plant. Built lazily (a benign
            /// race between worker threads builds the same array twice).</summary>
            public bool HostsVent(BlockId id)
            {
                var host = _ventHost ?? BuildVentHosts();
                return id.Value < host.Length && host[id.Value];
            }

            private bool[] BuildVentHosts()
            {
                var host = new bool[_flags.Length];
                for (int i = 1; i < host.Length; i++)
                {
                    var id = new BlockId((ushort)i);
                    var def = Content.BlockById(id);
                    host[i] = def != null && def.Category == "terrain" && !def.Liquid && string.IsNullOrEmpty(def.TileKind)
                        && !ExposesOpaqueFace(id)
                        && (FlagsOf(id) & (TraitTransparent | TraitFlora | TraitFoliage | TraitSlimProp | TraitFluid | TraitWood)) == 0
                        && (FlagsOf(id) & TraitCollidable) != 0;
                }

                _ventHost = host;
                return host;
            }
        }

        /// <summary>The ground a vent at (wx, wy, wz) wears: the most common ground block among its four neighbours on
        /// its own level, else among the four one level lower (a vent on a ledge), else the block right below it. Air
        /// when there is none — the vent then keeps its own tile.</summary>
        private static BlockId VentHost(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, int wx, int wy, int wz)
        {
            for (int drop = 0; drop <= 1; drop++)
            {
                ushort best = 0;
                int bestCount = 0;
                ushort a = HostAt(traits, worldBlock, wx + 1, wy - drop, wz), b = HostAt(traits, worldBlock, wx - 1, wy - drop, wz);
                ushort c = HostAt(traits, worldBlock, wx, wy - drop, wz + 1), d = HostAt(traits, worldBlock, wx, wy - drop, wz - 1);
                Tally(a, a, b, c, d, ref best, ref bestCount);
                Tally(b, a, b, c, d, ref best, ref bestCount);
                Tally(c, a, b, c, d, ref best, ref bestCount);
                Tally(d, a, b, c, d, ref best, ref bestCount);
                if (best != 0)
                {
                    return new BlockId(best);
                }
            }

            return new BlockId(HostAt(traits, worldBlock, wx, wy - 1, wz));
        }

        private static ushort HostAt(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, int wx, int wy, int wz)
        {
            var id = worldBlock(wx, wy, wz);
            return !id.IsAir && traits.HostsVent(id) ? id.Value : (ushort)0;
        }

        /// <summary>Counts how often <paramref name="candidate"/> occurs among the four and keeps the most common one;
        /// on a tie the first in the fixed order east, west, north, south wins, so every client picks the same.</summary>
        private static void Tally(ushort candidate, ushort a, ushort b, ushort c, ushort d, ref ushort best, ref int bestCount)
        {
            if (candidate == 0)
            {
                return;
            }

            int count = (a == candidate ? 1 : 0) + (b == candidate ? 1 : 0) + (c == candidate ? 1 : 0) + (d == candidate ? 1 : 0);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        /// <summary>Lays the vent's hole on the top face of its cell: one quad a hair above the ground, wearing the
        /// cut-out hole tile (<paramref name="uv"/>) in its own colours (tint mode 0) as a rigid cutout — the shader
        /// clips the transparent rest, so the ground's tile shows right up to the sinter ring.</summary>
        private static void AddVentHole(List<Vector3> verts, List<int> tris, List<Color> colors, List<Vector2> uvs,
            List<Vector4> tangents, List<Vector2> skyUv, List<Vector4> leafUv, List<Vector3> blockLight, List<Vector3> blockLightDir,
            Vector3 cell, Rect uv, float shade, float sky, Vector3 bl, Vector3 blDir)
        {
            const int Top = 0;
            var q = FaceQuad(cell, Top, ref _ventQuadScratch);
            var lift = new Vector3(0f, VentHoleLift, 0f);
            int first = verts.Count;
            var col = new Color(0f, 0f, shade, 0f); // no gloss, no metal, the top face's shade, no glow
            var leaf = new Vector4(LeafCodeRigid, 0f, 0f, 0f);
            Vector3 tan3 = (q[3] - q[0]).normalized;
            float hand = Vector3.Dot(Vector3.Cross(Vector3.up, tan3), q[1] - q[0]) < 0f ? -1f : 1f;
            var tan = new Vector4(tan3.x, tan3.y, tan3.z, hand);
            Vector3 centre = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vector3.Lerp(q[i], centre, VentHoleInset * 2f);
                verts.Add(p + lift);
                Vector3 loc = p - cell;
                uvs.Add(new Vector2(uv.xMin + (Mathf.Clamp01(loc.x) * uv.width), uv.yMin + (Mathf.Clamp01(loc.z) * uv.height)));
                colors.Add(col);
                tangents.Add(tan);
                skyUv.Add(new Vector2(sky, 0f));
                leafUv.Add(leaf);
                blockLight.Add(bl);
                blockLightDir.Add(blDir);
            }

            tris.Add(first); tris.Add(first + 1); tris.Add(first + 2);
            tris.Add(first); tris.Add(first + 2); tris.Add(first + 3);
        }
    }
}
