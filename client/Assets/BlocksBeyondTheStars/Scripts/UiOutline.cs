// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A text outline that rebuilds without garbage (#1552). uGUI's <see cref="Outline"/> expands the glyph quads
    /// into a triangle stream, appends four shifted copies and hands the stream back through
    /// <c>AddUIVertexTriangleStream</c>, which splits it into nine attribute lists — ~550 KB of allocations per
    /// rebuild of a 60-character HUD line, and the VEGA objective chip rebuilt three times a second while its
    /// counter ticked. This effect reads the glyph quads straight out of the <see cref="VertexHelper"/> into one
    /// retained scratch list and writes the four offset copies plus the original back as quads, so a rebuild
    /// touches no heap at all once the lists have grown. Same look as <see cref="Outline"/> (four diagonal
    /// copies, alpha multiplied by the glyph alpha), same field names, so <see cref="UiKit.AddOutline"/> is the
    /// only caller that changed.
    /// </summary>
    public sealed class UiOutline : BaseMeshEffect
    {
        public Color effectColor = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 effectDistance = new Vector2(1f, -1f);

        private static readonly List<UIVertex> _scratch = new List<UIVertex>(1024);
        private static readonly List<UIVertex> _stream = new List<UIVertex>(1024);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
            {
                return;
            }

            int n = vh.currentVertCount;
            if (n == 0)
            {
                return;
            }

            // Text (and every other quad-based Graphic) fills the helper with 4 vertices + 6 indices per quad in
            // AddUIVertexQuad order; anything else goes through the general (allocating) stream path.
            bool quads = n % 4 == 0 && vh.currentIndexCount == n / 4 * 6;
            if (!quads)
            {
                ModifyStream(vh);
                return;
            }

            _scratch.Clear();
            var v = default(UIVertex);
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                _scratch.Add(v);
            }

            vh.Clear();
            float dx = effectDistance.x, dy = effectDistance.y;
            AddQuads(vh, dx, dy, true);
            AddQuads(vh, dx, -dy, true);
            AddQuads(vh, -dx, dy, true);
            AddQuads(vh, -dx, -dy, true);
            AddQuads(vh, 0f, 0f, false); // the glyphs themselves, on top
        }

        private void AddQuads(VertexHelper vh, float dx, float dy, bool tint)
        {
            int start = vh.currentVertCount;
            int n = _scratch.Count;
            for (int i = 0; i < n; i++)
            {
                var v = _scratch[i];
                if (tint)
                {
                    var p = v.position;
                    p.x += dx;
                    p.y += dy;
                    v.position = p;
                    Color32 c = effectColor;
                    c.a = (byte)(c.a * v.color.a / 255);
                    v.color = c;
                }

                vh.AddVert(v);
            }

            for (int q = 0; q < n; q += 4)
            {
                int b = start + q;
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b + 2, b + 3, b);
            }
        }

        /// <summary>The general path for non-quad geometry — what <see cref="Outline"/> does, kept for completeness.</summary>
        private void ModifyStream(VertexHelper vh)
        {
            _stream.Clear();
            vh.GetUIVertexStream(_stream);
            int n = _stream.Count;
            _scratch.Clear();
            _scratch.AddRange(_stream);
            _stream.Clear();
            float dx = effectDistance.x, dy = effectDistance.y;
            AppendShifted(dx, dy, n);
            AppendShifted(dx, -dy, n);
            AppendShifted(-dx, dy, n);
            AppendShifted(-dx, -dy, n);
            _stream.AddRange(_scratch);
            vh.Clear();
            vh.AddUIVertexTriangleStream(_stream);
        }

        private void AppendShifted(float dx, float dy, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var v = _scratch[i];
                var p = v.position;
                p.x += dx;
                p.y += dy;
                v.position = p;
                Color32 c = effectColor;
                c.a = (byte)(c.a * v.color.a / 255);
                v.color = c;
                _stream.Add(v);
            }
        }
    }
}
