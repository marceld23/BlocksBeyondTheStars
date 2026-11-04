// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3178: the colours of the avatar faces the body-paint editor cannot reach. The editor has no top or bottom faces
    /// (dropped by design, #874), and those faces used to show the part's plain base tint — blue on a default suit — however
    /// completely the part was painted: the tops of the shoulders, a strip under the chest, a ring at the wrist, a wedge at
    /// a bent elbow, the first-person cuff. Now a top or bottom face continues the edge row of the side faces next to it,
    /// and what still has no neighbour (the first-person cuff, the solid pad of the atlas) takes the part's most common
    /// colour. Engine-free, so the headless suite pins the rules; the meshes live in <c>PlayerAvatar.PaintMesh</c>.
    /// </summary>
    public static class BodyPaintEdges
    {
        /// <summary>Slack for float noise at a texel boundary (a box edge that lies exactly on one).</summary>
        private const float Slack = 1e-4f;

        /// <summary>
        /// The most common palette index of a decoded canvas: what the part mostly shows. A transparent pixel (0) shows
        /// the base tint, so it counts as the tint — a part painted only in one spot keeps its tint here, a part painted
        /// all over takes its paint. A tie goes to the paint (the lower index among paints), so a half-painted part shows
        /// its painting. Null or empty → 0.
        /// </summary>
        public static int MostCommon(int[]? grid, int paletteSize = 32)
        {
            if (grid == null || grid.Length == 0 || paletteSize < 1)
            {
                return 0;
            }

            var counts = new int[paletteSize];
            foreach (int v in grid)
            {
                if (v >= 0 && v < paletteSize)
                {
                    counts[v]++;
                }
            }

            int best = 0, bestCount = 0;
            for (int i = 1; i < paletteSize; i++)
            {
                if (counts[i] > bestCount)
                {
                    best = i;
                    bestCount = counts[i];
                }
            }

            return counts[0] > bestCount ? 0 : best;
        }

        /// <summary>
        /// The texel row (0 = the chunk's bottom row, <paramref name="texels"/> − 1 its top row) a side face shows right
        /// at its edge with a top or bottom face, from that edge's height <paramref name="v"/> within the chunk (0..1, as
        /// the side face's planar UVs give it). A TOP edge shows the row just below <paramref name="v"/>, a BOTTOM edge the
        /// row just above — the row a cap must continue so the colour runs on round the corner.
        /// </summary>
        public static int EdgeTexel(float v, bool topEdge, int texels)
        {
            if (texels <= 1 || float.IsNaN(v))
            {
                return 0;
            }

            float t = Math.Max(0f, Math.Min(1f, v)) * texels;
            int row = topEdge ? (int)Math.Ceiling(t - Slack) - 1 : (int)Math.Floor(t + Slack);
            return Math.Max(0, Math.Min(texels - 1, row));
        }

        /// <summary>The centre of texel row <paramref name="row"/> as a 0..1 height within the chunk (point sampling then
        /// reads exactly that row).</summary>
        public static float TexelCentre(int row, int texels) => texels <= 0 ? 0.5f : (row + 0.5f) / texels;
    }
}
