// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>Vertex + index arrays of one far-terrain patch, positions relative to the patch origin (y absolute).</summary>
    public sealed class FarPatchGeometry
    {
        public int VertexCount;
        public float[] Positions = Array.Empty<float>();   // xyz per vertex
        public float[] Normals = Array.Empty<float>();     // xyz per vertex
        public uint[] Colors = Array.Empty<uint>();        // 0xAABBGGRR per vertex
        public int[] Indices = Array.Empty<int>();

        /// <summary>The level's discard radius (blocks from the player), carried per vertex for the shader.</summary>
        public float InnerRadius;
        public float MinY;
        public float MaxY;
    }

    /// <summary>
    /// Samples one far-terrain patch a row at a time (#1820) — the browser build runs it on the render thread, so a
    /// patch spreads over as many frames as the time budget needs — and turns the samples into a grid mesh with skirts.
    /// Builds (the persisted structures from <see cref="FarTerrainOverlay"/>) raise a vertex where they stand taller
    /// than the terrain.
    /// </summary>
    public sealed class FarPatchBuilder
    {
        private readonly FarTerrainSource _source;
        private readonly FarTerrainOverlay _overlay;
        private readonly int _cell;
        private readonly int _n;           // cells per side; vertices per side = _n + 1
        private readonly int _side;        // sampled points per side including a one-point border for the normals
        private readonly int[] _top;
        private readonly FarSample[] _samples;
        private readonly bool[] _edited;
        private readonly FarEditTop[] _edits;
        private int _row;

        public FarPatchKey Key { get; }
        public int Range { get; }
        public bool Sampled => _row >= _side;

        public FarPatchBuilder(FarPatchKey key, int range, FarTerrainSource source, FarTerrainOverlay overlay)
        {
            Key = key;
            Range = range;
            _source = source;
            _overlay = overlay;
            _cell = FarTerrainLayout.CellSize(key.Level);
            _n = FarTerrainLayout.CellsPerPatch(key.Level);
            _side = _n + 3;
            _top = new int[_side * _side];
            _samples = new FarSample[_side * _side];
            _edited = new bool[_side * _side];
            _edits = new FarEditTop[_side * _side];
        }

        /// <summary>Samples one row of points. Call until <see cref="Sampled"/>.</summary>
        public void SampleRow()
        {
            if (Sampled)
            {
                return;
            }

            bool detail = Key.Level == 0;
            int j = _row;
            int z = Key.OriginZ + (j - 1) * _cell;
            for (int i = 0; i < _side; i++)
            {
                int x = Key.OriginX + (i - 1) * _cell;
                var s = _source.Sample(x, z, detail);
                int idx = j * _side + i;
                _samples[idx] = s;
                _top[idx] = s.Top;
                if (_overlay.TryGetTopInArea(x - _cell / 2, z - _cell / 2, _cell, out var build) && build.Top > s.Top)
                {
                    _top[idx] = build.Top;
                    _edited[idx] = true;
                    _edits[idx] = build;
                }
            }

            _row++;
        }

        /// <summary>Builds the mesh arrays. <paramref name="colorOf"/> maps a column (and its build, if one stands
        /// there) to a vertex colour.</summary>
        public FarPatchGeometry BuildGeometry(Func<FarSample, bool, FarEditTop, uint> colorOf)
        {
            while (!Sampled)
            {
                SampleRow();
            }

            int vs = _n + 1;
            int skirt = 4 * vs;
            int count = vs * vs + skirt;
            var geo = new FarPatchGeometry
            {
                VertexCount = count,
                Positions = new float[count * 3],
                Normals = new float[count * 3],
                Colors = new uint[count],
                Indices = new int[_n * _n * 6 + 4 * _n * 6],
                InnerRadius = FarTerrainLayout.InnerRadius(Key.Level, Range),
                MinY = float.MaxValue,
                MaxY = float.MinValue,
            };

            float offset = FarTerrainLayout.HeightOffset(Key.Level);
            float skirtDepth = _cell * 1.5f + 8f;
            for (int j = 0; j < vs; j++)
                for (int i = 0; i < vs; i++)
                {
                    int s = (j + 1) * _side + (i + 1);
                    int v = j * vs + i;
                    float y = _top[s] + offset;
                    geo.Positions[v * 3] = i * _cell;
                    geo.Positions[v * 3 + 1] = y;
                    geo.Positions[v * 3 + 2] = j * _cell;

                    float nx = (_top[s - 1] - _top[s + 1]) / (2f * _cell);
                    float nz = (_top[s - _side] - _top[s + _side]) / (2f * _cell);
                    float inv = 1f / (float)Math.Sqrt(nx * nx + 1f + nz * nz);
                    geo.Normals[v * 3] = nx * inv;
                    geo.Normals[v * 3 + 1] = inv;
                    geo.Normals[v * 3 + 2] = nz * inv;
                    geo.Colors[v] = colorOf(_samples[s], _edited[s], _edits[s]);
                    geo.MinY = Math.Min(geo.MinY, y);
                    geo.MaxY = Math.Max(geo.MaxY, y);
                }

            int t = 0;
            for (int j = 0; j < _n; j++)
                for (int i = 0; i < _n; i++)
                {
                    int a = j * vs + i, b = a + 1, c = a + vs, d = c + 1;
                    geo.Indices[t++] = a; geo.Indices[t++] = c; geo.Indices[t++] = b;
                    geo.Indices[t++] = b; geo.Indices[t++] = c; geo.Indices[t++] = d;
                }

            // Skirts: each edge's vertices repeated lower down, so the seam to a neighbour patch or the other level
            // never shows sky through a crack.
            int next = vs * vs;
            for (int edge = 0; edge < 4; edge++)
            {
                int first = next;
                for (int k = 0; k < vs; k++)
                {
                    int src = edge switch
                    {
                        0 => k,                       // z = 0
                        1 => (vs - 1) * vs + k,       // z = max
                        2 => k * vs,                  // x = 0
                        _ => k * vs + (vs - 1),       // x = max
                    };
                    geo.Positions[next * 3] = geo.Positions[src * 3];
                    geo.Positions[next * 3 + 1] = geo.Positions[src * 3 + 1] - skirtDepth;
                    geo.Positions[next * 3 + 2] = geo.Positions[src * 3 + 2];
                    geo.Normals[next * 3] = geo.Normals[src * 3];
                    geo.Normals[next * 3 + 1] = geo.Normals[src * 3 + 1];
                    geo.Normals[next * 3 + 2] = geo.Normals[src * 3 + 2];
                    geo.Colors[next] = geo.Colors[src];
                    geo.MinY = Math.Min(geo.MinY, geo.Positions[next * 3 + 1]);
                    next++;
                }

                for (int k = 0; k < _n; k++)
                {
                    int top0 = edge switch { 0 => k, 1 => (vs - 1) * vs + k, 2 => k * vs, _ => k * vs + (vs - 1) };
                    int top1 = edge switch { 0 => k + 1, 1 => (vs - 1) * vs + k + 1, 2 => (k + 1) * vs, _ => (k + 1) * vs + (vs - 1) };
                    int bot0 = first + k, bot1 = first + k + 1;
                    // The far-terrain shader draws both faces (Cull Off): a skirt is seen from whichever side the gap is on.
                    geo.Indices[t++] = top0; geo.Indices[t++] = bot0; geo.Indices[t++] = top1;
                    geo.Indices[t++] = top1; geo.Indices[t++] = bot0; geo.Indices[t++] = bot1;
                }
            }

            if (t < geo.Indices.Length)
            {
                Array.Resize(ref geo.Indices, t);
            }

            return geo;
        }
    }
}
