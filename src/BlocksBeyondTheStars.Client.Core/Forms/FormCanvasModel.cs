// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Why a form on the canvas cannot be saved as it is.</summary>
    public enum FormProblem
    {
        None = 0,

        /// <summary>Nothing is drawn.</summary>
        Empty,

        /// <summary>Everything is filled — that is a cube (or several), which needs no form.</summary>
        SolidCubes,

        /// <summary>A block of the footprint holds nothing: it would claim its place in the world and show nothing.</summary>
        EmptyCell,

        /// <summary>A block needs more boxes than the render/collider budget allows.</summary>
        TooDetailed,
    }

    /// <summary>
    /// The model behind the main-menu form editor (#1960): a player form as ONE voxel volume, however many blocks
    /// it spans (#1961). The editor paints horizontal layers of that volume; this class owns everything that is
    /// not pixels on a screen — the footprint, the helpers (mirror, copy the layer below, shift), undo/redo, and
    /// the translation to and from the wire formats of <see cref="CustomShape"/>. Unity-free, so the rules are
    /// unit-tested: what the editor lets through is exactly what the server will register.
    ///
    /// A one-block form may use the coarse 4³ grid or the fine 8³ one; a form over several blocks is always 8³
    /// per block (the multi-cell payload has no coarse variant).
    /// </summary>
    public sealed class FormCanvasModel
    {
        /// <summary>Undo steps kept. A step is one stroke or one helper action.</summary>
        public const int MaxUndo = 48;

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private byte[] _v = Array.Empty<byte>();
        private Snapshot? _strokeStart;

        public FormCanvasModel()
        {
            Reset(1, 1, 1, CustomShape.GridLarge);
        }

        /// <summary>Footprint in blocks: X.</summary>
        public int Width { get; private set; }

        /// <summary>Footprint in blocks: Y (up).</summary>
        public int Height { get; private set; }

        /// <summary>Footprint in blocks: Z.</summary>
        public int Length { get; private set; }

        /// <summary>Micro cells per block side (4 or 8).</summary>
        public int Grid { get; private set; }

        public int SizeX => Width * Grid;

        public int SizeY => Height * Grid;

        public int SizeZ => Length * Grid;

        public int Cells => Width * Height * Length;

        /// <summary>The layer (Y slice of the whole form) the canvas shows.</summary>
        public int Layer { get; private set; }

        /// <summary>True when the form changed since it was loaded or marked saved.</summary>
        public bool Dirty { get; private set; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public void MarkSaved() => Dirty = false;

        // ---------------------------------------------------------------- reading

        public bool Get(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ && _v[Index(x, y, z)] != 0;

        private int Index(int x, int y, int z) => (((y * SizeZ) + z) * SizeX) + x;

        /// <summary>Boxes the given block needs after merging — what the budget line shows.</summary>
        public int BoxesOfCell(int cell) => CustomShape.Merge(CellBitmap(cell)).Count;

        /// <summary>The most boxes any block of the form needs.</summary>
        public int MaxBoxesUsed()
        {
            int max = 0;
            for (int cell = 0; cell < Cells; cell++)
            {
                max = Math.Max(max, BoxesOfCell(cell));
            }

            return max;
        }

        /// <summary>What keeps the form from being saved; <paramref name="cell"/> names the block for the two
        /// per-block problems.</summary>
        public FormProblem Problem(out int cell)
        {
            cell = -1;
            bool anyFilled = false, anyEmpty = false;
            foreach (byte b in _v)
            {
                if (b != 0)
                {
                    anyFilled = true;
                }
                else
                {
                    anyEmpty = true;
                }
            }

            if (!anyFilled)
            {
                return FormProblem.Empty;
            }

            if (!anyEmpty)
            {
                return FormProblem.SolidCubes;
            }

            for (int c = 0; c < Cells; c++)
            {
                string bitmap = CellBitmap(c);
                if (bitmap.IndexOf('1') < 0)
                {
                    cell = c;
                    return FormProblem.EmptyCell;
                }

                if (CustomShape.Merge(bitmap).Count > CustomShape.MaxBoxes)
                {
                    cell = c;
                    return FormProblem.TooDetailed;
                }
            }

            return FormProblem.None;
        }

        /// <summary>The form in the format the game stores and sends — empty while <see cref="Problem"/> names one.</summary>
        public string Encode()
        {
            if (Problem(out _) != FormProblem.None)
            {
                return string.Empty;
            }

            if (Cells == 1)
            {
                return CellBitmap(0);
            }

            var cells = new List<string>(Cells);
            for (int c = 0; c < Cells; c++)
            {
                cells.Add(CellBitmap(c));
            }

            return CustomShape.ComposeMulti(Width, Height, Length, cells);
        }

        /// <summary>The form for the PREVIEW: like <see cref="Encode"/>, but also while the form is not valid yet
        /// (a half-drawn form must still show). Empty only when nothing is drawn.</summary>
        public IReadOnlyList<(int X, int Y, int Z, string Bitmap)> CellBitmaps()
        {
            var result = new List<(int, int, int, string)>(Cells);
            for (int c = 0; c < Cells; c++)
            {
                var (cx, cy, cz) = CustomShape.CellPosition(c, Width, Length);
                result.Add((cx, cy, cz, CellBitmap(c)));
            }

            return result;
        }

        private string CellBitmap(int cell)
        {
            var (cx, cy, cz) = CustomShape.CellPosition(cell, Width, Length);
            var chars = new char[Grid * Grid * Grid];
            for (int y = 0; y < Grid; y++)
            {
                for (int z = 0; z < Grid; z++)
                {
                    for (int x = 0; x < Grid; x++)
                    {
                        chars[CustomShape.IndexOf(x, y, z, Grid)] =
                            _v[Index((cx * Grid) + x, (cy * Grid) + y, (cz * Grid) + z)] != 0 ? '1' : '0';
                    }
                }
            }

            return new string(chars);
        }

        // ---------------------------------------------------------------- loading

        /// <summary>Loads a stored form (either legacy length or the multi-cell payload). False — and nothing
        /// changes — for anything else. Clears the undo history: it belongs to the form that was open.</summary>
        public bool Load(string? voxels)
        {
            if (!CustomShape.IsValidVoxels(voxels) || !CustomShape.TryFootprint(voxels, out int w, out int h, out int l))
            {
                return false;
            }

            int grid = CustomShape.IsMulti(voxels) ? CustomShape.GridLarge : CustomShape.GridOf(voxels);
            Reset(w, h, l, grid);
            for (int cell = 0; cell < Cells; cell++)
            {
                var (cx, cy, cz) = CustomShape.CellPosition(cell, w, l);
                string bitmap = CustomShape.CellVoxels(voxels!, cell);
                for (int y = 0; y < grid; y++)
                {
                    for (int z = 0; z < grid; z++)
                    {
                        for (int x = 0; x < grid; x++)
                        {
                            if (bitmap[CustomShape.IndexOf(x, y, z, grid)] != '0')
                            {
                                _v[Index((cx * grid) + x, (cy * grid) + y, (cz * grid) + z)] = 1;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>An empty one-block canvas.</summary>
        public void New() => Reset(1, 1, 1, CustomShape.GridLarge);

        private void Reset(int w, int h, int l, int grid)
        {
            Width = w;
            Height = h;
            Length = l;
            Grid = grid;
            _v = new byte[SizeX * SizeY * SizeZ];
            Layer = 0;
            _undo.Clear();
            _redo.Clear();
            _strokeStart = null;
            Dirty = false;
        }

        // ---------------------------------------------------------------- editing

        public void SetLayer(int layer) => Layer = Math.Max(0, Math.Min(SizeY - 1, layer));

        /// <summary>Starts a stroke: everything painted until <see cref="EndStroke"/> is ONE undo step.</summary>
        public void BeginStroke() => _strokeStart ??= Take();

        public void EndStroke()
        {
            if (_strokeStart is { } start)
            {
                _strokeStart = null;
                if (!Same(start.Voxels, _v))
                {
                    Push(start);
                }
            }
        }

        /// <summary>Sets one micro cell of the current layer. Inside a stroke it joins that stroke's undo step;
        /// outside it is a step of its own. True when something changed.</summary>
        public bool Paint(int x, int z, bool filled)
        {
            if (x < 0 || z < 0 || x >= SizeX || z >= SizeZ)
            {
                return false;
            }

            int i = Index(x, Layer, z);
            byte value = filled ? (byte)1 : (byte)0;
            if (_v[i] == value)
            {
                return false;
            }

            if (_strokeStart == null)
            {
                Push(Take());
            }

            _v[i] = value;
            Dirty = true;
            return true;
        }

        public void CopyLayerBelow()
        {
            if (Layer == 0)
            {
                return;
            }

            Edit(() =>
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    for (int x = 0; x < SizeX; x++)
                    {
                        _v[Index(x, Layer, z)] = _v[Index(x, Layer - 1, z)];
                    }
                }
            });
        }

        public void ClearLayer() => Edit(() =>
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    _v[Index(x, Layer, z)] = 0;
                }
            }
        });

        public void FillLayer() => Edit(() =>
        {
            for (int z = 0; z < SizeZ; z++)
            {
                for (int x = 0; x < SizeX; x++)
                {
                    _v[Index(x, Layer, z)] = 1;
                }
            }
        });

        public void ClearAll() => Edit(() => Array.Clear(_v, 0, _v.Length));

        /// <summary>Mirrors the WHOLE form onto its other half (what is drawn stays, its mirror image is added) —
        /// symmetry is what most hand-built forms want.</summary>
        public void Mirror(bool alongX) => Edit(() =>
        {
            var source = (byte[])_v.Clone();
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    for (int x = 0; x < SizeX; x++)
                    {
                        int sx = alongX ? SizeX - 1 - x : x;
                        int sz = alongX ? z : SizeZ - 1 - z;
                        if (source[Index(sx, y, sz)] != 0)
                        {
                            _v[Index(x, y, z)] = 1;
                        }
                    }
                }
            }
        });

        /// <summary>Moves the whole form by micro cells; what leaves the volume is cut off.</summary>
        public void Shift(int dx, int dy, int dz) => Edit(() =>
        {
            var source = (byte[])_v.Clone();
            Array.Clear(_v, 0, _v.Length);
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    for (int x = 0; x < SizeX; x++)
                    {
                        int tx = x + dx, ty = y + dy, tz = z + dz;
                        if (source[Index(x, y, z)] != 0 && tx >= 0 && ty >= 0 && tz >= 0 && tx < SizeX && ty < SizeY && tz < SizeZ)
                        {
                            _v[Index(tx, ty, tz)] = 1;
                        }
                    }
                }
            }
        });

        /// <summary>
        /// Changes the footprint, keeping what is drawn where the old and the new volume overlap (the form stays
        /// anchored at its first block). False when the footprint is not allowed: a side over
        /// <see cref="CustomShape.MaxFootprint"/> or more than <see cref="CustomShape.MaxCells"/> blocks. A form
        /// that grows beyond one block switches to the fine grid — the multi-cell payload has no coarse one.
        /// </summary>
        public bool SetFootprint(int width, int height, int length)
        {
            if (width < 1 || height < 1 || length < 1
                || width > CustomShape.MaxFootprint || height > CustomShape.MaxFootprint || length > CustomShape.MaxFootprint
                || width * height * length > CustomShape.MaxCells)
            {
                return false;
            }

            if (width == Width && height == Height && length == Length)
            {
                return true;
            }

            int grid = width * height * length > 1 ? CustomShape.GridLarge : Grid;
            Resample(width, height, length, grid);
            return true;
        }

        /// <summary>Switches a ONE-block form between the coarse and the fine grid, re-sampling what is drawn.</summary>
        public bool SetGrid(int grid)
        {
            if (Cells != 1 || (grid != CustomShape.GridSmall && grid != CustomShape.GridLarge))
            {
                return false;
            }

            if (grid != Grid)
            {
                Resample(1, 1, 1, grid);
            }

            return true;
        }

        private void Resample(int width, int height, int length, int grid)
        {
            var before = Take();
            int oldGrid = Grid;
            Width = width;
            Height = height;
            Length = length;
            Grid = grid;
            _v = new byte[SizeX * SizeY * SizeZ];
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    for (int x = 0; x < SizeX; x++)
                    {
                        // The same block-relative position in the old volume (nearest sample when the grid changed).
                        int sx = x * oldGrid / grid, sy = y * oldGrid / grid, sz = z * oldGrid / grid;
                        if (before.Get(sx, sy, sz))
                        {
                            _v[Index(x, y, z)] = 1;
                        }
                    }
                }
            }

            Layer = Math.Min(Layer, SizeY - 1);
            Push(before);
            Dirty = true;
        }

        // ---------------------------------------------------------------- undo / redo

        public bool Undo() => Step(_undo, _redo);

        public bool Redo() => Step(_redo, _undo);

        private bool Step(List<Snapshot> from, List<Snapshot> to)
        {
            EndStroke();
            if (from.Count == 0)
            {
                return false;
            }

            to.Add(Take());
            var snapshot = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Width = snapshot.Width;
            Height = snapshot.Height;
            Length = snapshot.Length;
            Grid = snapshot.Grid;
            _v = (byte[])snapshot.Voxels.Clone();
            Layer = Math.Min(Layer, SizeY - 1);
            Dirty = true;
            return true;
        }

        private void Edit(Action change)
        {
            EndStroke();
            var before = Take();
            change();
            if (!Same(before.Voxels, _v))
            {
                Push(before);
                Dirty = true;
            }
        }

        private void Push(Snapshot snapshot)
        {
            _undo.Add(snapshot);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
        }

        private Snapshot Take() => new Snapshot(Width, Height, Length, Grid, (byte[])_v.Clone());

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class Snapshot
        {
            public Snapshot(int width, int height, int length, int grid, byte[] voxels)
            {
                Width = width;
                Height = height;
                Length = length;
                Grid = grid;
                Voxels = voxels;
            }

            public int Width { get; }

            public int Height { get; }

            public int Length { get; }

            public int Grid { get; }

            public byte[] Voxels { get; }

            public bool Get(int x, int y, int z)
            {
                int sx = Width * Grid, sy = Height * Grid, sz = Length * Grid;
                return x >= 0 && y >= 0 && z >= 0 && x < sx && y < sy && z < sz && Voxels[(((y * sz) + z) * sx) + x] != 0;
            }
        }
    }
}
