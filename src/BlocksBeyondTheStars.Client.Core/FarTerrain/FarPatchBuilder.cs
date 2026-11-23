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

        /// <summary>#3958: the vertex count of the height field and its skirts; the floating slabs and waterfalls follow.</summary>
        public int GridVertexCount;

        /// <summary>#3958: the discard radius of the floating slabs and waterfalls (the vertices from
        /// <see cref="GridVertexCount"/> on). On the far level it is the near level's whole reach, not the height field's
        /// overlap: a coarse 32-block slab standing beside the near level's 8-block ones in the overlap ring would show as
        /// a second, bigger island around the true one. The near level always covers its whole reach, so nothing is lost.</summary>
        public float FloatInnerRadius;
    }

    /// <summary>
    /// Samples one far-terrain patch a row at a time (#1820) and turns the samples into a grid mesh with skirts, plus a
    /// block slab for every shape that floats over a column (#3958: sky islands, arch bars, cap overhangs) and a slim
    /// curtain for every waterfall (#3959). The terrain sampling (<see cref="SampleRow"/>) touches only the
    /// <see cref="FarTerrainSource"/>, so the desktop client runs it on a background thread; the browser build runs it on
    /// the render thread, a row at a time within the frame budget. <see cref="BuildGeometry"/> merges the builds from
    /// <see cref="FarTerrainOverlay"/> — persisted structures raise a vertex (or the slab they stand on, #3960) where they
    /// stand taller than the terrain — and must run on the thread that owns the overlay.
    /// </summary>
    public sealed partial class FarPatchBuilder
    {
        private readonly FarTerrainSource _source;
        private readonly int _cell;
        private readonly int _n;           // cells per side; vertices per side = _n + 1
        private readonly int _side;        // sampled points per side including a one-point border for the normals
        private readonly int[] _top;
        private readonly FarSample[] _samples;
        private readonly bool[] _edited;
        private readonly FarEditTop[] _edits;
        private readonly FarFloat[] _floats;     // #3958: FarTerrainSource.MaxFloats per sampled point
        private readonly byte[] _floatCount;
        private readonly sbyte[] _builtFloat;    // #3960: the slab a build stands on (-1 = none), its top in _edits
        private int _row;

        public FarPatchKey Key { get; }
        public int Range { get; }

        /// <summary>The far-terrain epoch this build belongs to (a world change discards older builds).</summary>
        public int Epoch { get; }

        public bool Sampled => _row >= _side;

        public FarPatchBuilder(FarPatchKey key, int range, FarTerrainSource source, int epoch = 0)
        {
            Key = key;
            Range = range;
            Epoch = epoch;
            _source = source;
            _cell = FarTerrainLayout.CellSize(key.Level);
            _n = FarTerrainLayout.CellsPerPatch(key.Level);
            _side = _n + 3;
            _top = new int[_side * _side];
            _samples = new FarSample[_side * _side];
            _edited = new bool[_side * _side];
            _edits = new FarEditTop[_side * _side];
            _floats = new FarFloat[_side * _side * FarTerrainSource.MaxFloats];
            _floatCount = new byte[_side * _side];
            _builtFloat = new sbyte[_side * _side];
        }

        /// <summary>Samples one row of terrain points. Call until <see cref="Sampled"/>.</summary>
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
                int idx = j * _side + i;
                var floats = new Span<FarFloat>(_floats, idx * FarTerrainSource.MaxFloats, FarTerrainSource.MaxFloats);
                _samples[idx] = _source.Sample(x, z, detail, floats, out int count);
                _floatCount[idx] = (byte)count;
            }

            _row++;
        }

        /// <summary>Builds the mesh arrays. <paramref name="colorOf"/> maps a column (and its build, if one stands
        /// there) to a vertex colour.</summary>
        public FarPatchGeometry BuildGeometry(FarTerrainOverlay overlay, Func<FarSample, bool, FarEditTop, uint> colorOf)
        {
            while (!Sampled)
            {
                SampleRow();
            }

            ApplyBuilds(overlay);
            var geo = BuildGrid(colorOf);
            AppendFloats(geo, colorOf);
            return geo;
        }

        /// <summary>Sets every point's height-field top from its sample and the builds on it: a build over a floating slab
        /// raises that slab (#3960), any other build raises the height field where it stands taller.</summary>
        private void ApplyBuilds(FarTerrainOverlay overlay)
        {
            for (int j = 0; j < _side; j++)
                for (int i = 0; i < _side; i++)
                {
                    int idx = j * _side + i;
                    var s = _samples[idx];
                    _top[idx] = s.Top;
                    _edited[idx] = false;
                    _builtFloat[idx] = -1;
                    int x = Key.OriginX + (i - 1) * _cell;
                    int z = Key.OriginZ + (j - 1) * _cell;
                    if (!overlay.TryGetTopInArea(x - _cell / 2, z - _cell / 2, _cell, out var build))
                    {
                        continue;
                    }

                    int slab = SlabUnder(idx, build.Top);
                    if (slab >= 0)
                    {
                        _builtFloat[idx] = (sbyte)slab; // on an island: the island rises, not a pillar from the ground
                        _edits[idx] = build;
                    }
                    else if (build.Top > s.Top)
                    {
                        _top[idx] = build.Top;
                        _edited[idx] = true;
                        _edits[idx] = build;
                    }
                }
        }

        /// <summary>The highest solid slab of a point whose bottom lies under <paramref name="buildTop"/> (the build stands
        /// on it or in it), or -1.</summary>
        private int SlabUnder(int idx, int buildTop)
        {
            int baseIndex = idx * FarTerrainSource.MaxFloats;
            for (int k = _floatCount[idx] - 1; k >= 0; k--)
            {
                var f = _floats[baseIndex + k];
                if (f.Kind == FarFloatKind.Solid && f.Bottom < buildTop)
                {
                    return k;
                }
            }

            return -1;
        }

        /// <summary>The height field: a grid over the patch with a skirt along each edge.</summary>
        private FarPatchGeometry BuildGrid(Func<FarSample, bool, FarEditTop, uint> colorOf)
        {
            int vs = _n + 1;
            int skirt = 4 * vs;
            int count = vs * vs + skirt;
            var geo = new FarPatchGeometry
            {
                VertexCount = count,
                GridVertexCount = count,
                Positions = new float[count * 3],
                Normals = new float[count * 3],
                Colors = new uint[count],
                Indices = new int[_n * _n * 6 + 4 * _n * 6],
                InnerRadius = FarTerrainLayout.InnerRadius(Key.Level, Range),
                FloatInnerRadius = Key.Level == 0 ? 0f : FarTerrainLayout.NearReach(Range),
                MinY = float.MaxValue,
                MaxY = float.MinValue,
            };

            float offset = FarTerrainLayout.HeightOffset(Key.Level);
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

            AddSkirts(geo, vs, t);
            return geo;
        }

        /// <summary>Skirts: each edge's vertices repeated lower down, so the seam to a neighbour patch or the other level
        /// never shows sky through a crack.</summary>
        private void AddSkirts(FarPatchGeometry geo, int vs, int t)
        {
            float skirtDepth = _cell * 1.5f + 8f;
            int next = vs * vs;
            for (int edge = 0; edge < 4; edge++)
            {
                int first = next;
                for (int k = 0; k < vs; k++)
                {
                    int src = EdgeVertex(edge, k, vs);
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
                    int top0 = EdgeVertex(edge, k, vs), top1 = EdgeVertex(edge, k + 1, vs);
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
        }

        /// <summary>The grid vertex at step <paramref name="k"/> along an edge: 0 = z 0, 1 = z max, 2 = x 0, 3 = x max.</summary>
        private static int EdgeVertex(int edge, int k, int vs) => edge switch
        {
            0 => k,
            1 => (vs - 1) * vs + k,
            2 => k * vs,
            _ => k * vs + (vs - 1),
        };
    }
}
