// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Crown forms per leaf kind: a conifer should read as a cone narrowing toward the top, a palm by its arching fronds.
    /// <para>
    /// The staged crowns draw every leaf cell as the same cluster of small cubes. That suits a broad-leaved crown; a
    /// conifer became a dark column of cubes and a palm a flat heap. The mesher
    /// tells <see cref="CrownClusters"/> which FORM a needle or a frond cell wears and how it is turned
    /// (<see cref="CrownShapeOf"/>): it finds the tree's axis — the nearest trunk — and
    /// </para>
    /// <list type="bullet">
    /// <item>a needle cell becomes a bough that slopes down and outward, its reach cut to a cone under the tree's tip
    /// (three lengths), so the tiers narrow toward the top;</item>
    /// <item>the bare trunk block a generated conifer ends in gets a spire on top (<see cref="ConiferTopAt"/>) — a display-only
    /// cell, like the crown vines;</item>
    /// <item>a frond cell becomes a long feathered frond that points away from the trunk — level where the frond goes on in
    /// the next cell, drooping at its end — and the palm's heart a rosette of short fronds.</item>
    /// </list>
    /// <para>
    /// Display only, near the camera only (the crowns' range): the world keeps its leaf blocks, and the far picture is the
    /// block shell as before. A needle or frond cell with no trunk within three cells stays a cube cluster.
    /// </para>
    /// </summary>
    public static partial class ChunkMesher
    {
        /// <summary>How far the tree's axis is searched for round a crown cell (cells).</summary>
        private const int AxisReach = 3;

        /// <summary>The cone a conifer's boughs are cut to: its radius right under the tip, and how much it widens per
        /// cell below the tip.</summary>
        private const float ConeTipRadius = 0.35f, ConeSlope = 0.36f;

        private sealed partial class BlockTraits
        {
            private int _needles = -1, _fronds = -1;

            /// <summary>The needle block's id, 0 when the content has none. Looked up once.</summary>
            public ushort NeedlesOf()
            {
                if (_needles < 0)
                {
                    var def = Content.GetBlock(TreeFoliage.NeedlesKey);
                    _needles = def != null ? def.NumericId.Value : 0;
                }

                return (ushort)_needles;
            }

            /// <summary>The palm frond block's id, 0 when the content has none. Looked up once.</summary>
            public ushort FrondsOf()
            {
                if (_fronds < 0)
                {
                    var def = Content.GetBlock(TreeFoliage.FrondKey);
                    _fronds = def != null ? def.NumericId.Value : 0;
                }

                return (ushort)_fronds;
            }
        }

        /// <summary>The form of the crown cell at (wx, wy, wz) holding <paramref name="id"/>: a cube cluster
        /// (<see cref="BlockShapeGeometry.CrownFormLeaf"/>) for every leaf kind but needles and fronds, and for those when
        /// no trunk stands within <see cref="AxisReach"/> cells; else the form, its variant and the turn about Y (degrees)
        /// that points the form's +X away from the tree's axis.</summary>
        private static void CrownShapeOf(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, BlockId id, int wx, int wy, int wz,
            out byte form, out byte variant, out float yaw)
        {
            form = BlockShapeGeometry.CrownFormLeaf;
            variant = 0;
            yaw = 0f;
            bool needles = id.Value != 0 && id.Value == traits.NeedlesOf();
            bool fronds = id.Value != 0 && id.Value == traits.FrondsOf();
            if ((!needles && !fronds) || !TreeAxis(traits, worldBlock, wx, wy, wz, out int ax, out int az))
            {
                return;
            }

            int dx = wx - ax, dz = wz - az;
            if (dx == 0 && dz == 0)
            {
                // On the axis: the cell above the trunk's top.
                form = (byte)(needles ? BlockShapeGeometry.CrownFormNeedleTip : BlockShapeGeometry.CrownFormFrondRosette);
                return;
            }

            // A turn of yaw about Y carries +X to (cos, 0, -sin).
            yaw = Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg;
            if (fronds)
            {
                // A frond runs outward cell by cell and droops at its end: level while the next cell outward (on this
                // level or one lower, where the generated tips hang) is frond too.
                int sx = System.Math.Sign(dx), sz = System.Math.Sign(dz);
                bool goesOn = worldBlock(wx + sx, wy, wz + sz).Value == id.Value || worldBlock(wx + sx, wy - 1, wz + sz).Value == id.Value;
                form = BlockShapeGeometry.CrownFormFrond;
                variant = (byte)(goesOn ? 0 : 1);
                return;
            }

            // A bough: how far it reaches is cut to a cone under the tree's tip — the top of the trunk, or of the
            // needles above it.
            int tipY = wy;
            for (int i = 0; i < 16; i++)
            {
                var up = worldBlock(ax, tipY + 1, az);
                if (up.IsAir || !(traits.Has(up, TraitWood) || up.Value == id.Value))
                {
                    break;
                }

                tipY++;
            }

            float cone = ConeTipRadius + (ConeSlope * (tipY + 1.5f - wy)); // the spire stands in the cell above the tip
            float room = cone - (Mathf.Sqrt((dx * dx) + (dz * dz)) - 0.5f); // from this cell's inner edge to the cone
            form = BlockShapeGeometry.CrownFormNeedleBough;
            variant = (byte)(room >= 0.95f ? 2 : room >= 0.55f ? 1 : 0);
        }

        /// <summary>The tree's axis for a crown cell: the nearest trunk block within <see cref="AxisReach"/> cells on the
        /// cell's own level, else one or two levels lower (the cells above a trunk's top).</summary>
        private static bool TreeAxis(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, int wx, int wy, int wz,
            out int ax, out int az)
        {
            ax = 0;
            az = 0;
            for (int drop = 0; drop <= 2; drop++)
            {
                int best = int.MaxValue;
                for (int dz = -AxisReach; dz <= AxisReach; dz++)
                {
                    for (int dx = -AxisReach; dx <= AxisReach; dx++)
                    {
                        int d2 = (dx * dx) + (dz * dz);
                        if (d2 < best && d2 <= (AxisReach * AxisReach) + 1 && traits.Has(worldBlock(wx + dx, wy - drop, wz + dz), TraitWood))
                        {
                            best = d2;
                            ax = wx + dx;
                            az = wz + dz;
                        }
                    }
                }

                if (best != int.MaxValue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the trunk block at (wx, wy, wz) is the bare top of a conifer: open air above it and needles
        /// on at least two of its four sides, on its own level or the one below.</summary>
        private static bool ConiferTopAt(BlockTraits traits, System.Func<int, int, int, BlockId> worldBlock, int wx, int wy, int wz)
        {
            ushort needles = traits.NeedlesOf();
            if (needles == 0 || !worldBlock(wx, wy + 1, wz).IsAir)
            {
                return false;
            }

            int sides = 0;
            for (int drop = 0; drop <= 1 && sides < 2; drop++)
            {
                sides = (worldBlock(wx + 1, wy - drop, wz).Value == needles ? 1 : 0) + (worldBlock(wx - 1, wy - drop, wz).Value == needles ? 1 : 0)
                    + (worldBlock(wx, wy - drop, wz + 1).Value == needles ? 1 : 0) + (worldBlock(wx, wy - drop, wz - 1).Value == needles ? 1 : 0);
            }

            return sides >= 2;
        }
    }
}
