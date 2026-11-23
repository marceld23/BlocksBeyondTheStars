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
    public sealed class FormCanvasModel : ILayerCanvas
    {
        /// <summary>Undo steps kept. A step is one stroke or one helper action.</summary>
        public const int MaxUndo = UndoHistory<byte[]>.DefaultCapacity;

        /// <summary>The editors' shared snapshot history (#3695): a stroke is one step, a change that changed nothing is
        /// no step, and undoing back to the saved form makes it unsaved no more.</summary>
        private readonly UndoHistory<Snapshot> _history = new UndoHistory<Snapshot>(MaxUndo, SnapshotComparer.Instance);
        private byte[] _v = Array.Empty<byte>();

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

        /// <summary>True when the form differs from the one loaded or last marked saved — undoing back to that state
        /// makes it false again.</summary>
        public bool Dirty => _history.IsDirty;

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        public void MarkSaved() => _history.MarkSaved();

        /// <summary>The cell value a filled stroke writes — 1 (main) by default, 2..5 the colour slots of
        /// <see cref="ShapeColourSlots"/>. The model keeps every value 1..15 it loads, so a form round-trips unchanged.</summary>
        public int PaintValue
        {
            get => _paintValue;
            set => _paintValue = Math.Max(1, Math.Min(15, value));
        }

        private int _paintValue = 1;

        /// <summary>When true the box count (<see cref="BoxesOfCell"/>, and with it the
        /// <see cref="FormProblem.TooDetailed"/> check) also counts the per-slot boxes the client will draw.</summary>
        public bool CountSlotBoxes { get; set; }

        // ---------------------------------------------------------------- reading

        public bool Get(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ && _v[Index(x, y, z)] != 0;

        /// <summary>The stored value of a micro cell: 0 empty, 1 main, 2..15 the colour slot values; 0 outside.</summary>
        public int ValueAt(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ ? _v[Index(x, y, z)] : 0;

        private int Index(int x, int y, int z) => (((y * SizeZ) + z) * SizeX) + x;

        /// <summary>Boxes the given block needs after merging — what the budget line shows. With
        /// <see cref="CountSlotBoxes"/> the larger of the value-blind count (the server's budget) and the per-slot count (what
        /// is drawn).</summary>
        public int BoxesOfCell(int cell)
        {
            string bitmap = CellBitmap(cell);
            int plain = CustomShape.Merge(bitmap).Count;
            return CountSlotBoxes && ShapeColourSlots.HasSlots(bitmap) ? Math.Max(plain, ShapeColourSlots.Merge(bitmap).Count) : plain;
        }

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
                if (bitmap.Replace("0", string.Empty).Length == 0)
                {
                    cell = c;
                    return FormProblem.EmptyCell;
                }

                if (BoxesOfCell(c) > CustomShape.MaxBoxes)
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
                            ShapeColourSlots.CharOf(_v[Index((cx * Grid) + x, (cy * Grid) + y, (cz * Grid) + z)]);
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
                            // The value is kept (an older form holds only 0 and 1)
                            int value = ShapeColourSlots.ValueOf(bitmap[CustomShape.IndexOf(x, y, z, grid)]);
                            if (value != 0)
                            {
                                _v[Index((cx * grid) + x, (cy * grid) + y, (cz * grid) + z)] = (byte)value;
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
            _history.Reset();
        }

        // ---------------------------------------------------------------- editing

        public void SetLayer(int layer) => Layer = Math.Max(0, Math.Min(SizeY - 1, layer));

        /// <summary>Starts a stroke: everything painted until <see cref="EndStroke"/> is ONE undo step.</summary>
        public void BeginStroke()
        {
            if (!_history.InGesture)
            {
                _history.Begin(Take());
            }
        }

        public void EndStroke()
        {
            if (_history.InGesture)
            {
                _history.Commit(Take());
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
            byte value = filled ? (byte)PaintValue : (byte)0;
            if (_v[i] == value)
            {
                return false;
            }

            var before = _history.InGesture ? null : Take(); // inside a stroke the stroke's start is the step
            _v[i] = value;
            if (before != null)
            {
                _history.Record(before);
            }

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
                    _v[Index(x, Layer, z)] = (byte)PaintValue;
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
                            _v[Index(x, y, z)] = source[Index(sx, y, sz)];
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
                            _v[Index(tx, ty, tz)] = source[Index(x, y, z)];
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
            EndStroke(); // a change recorded during an open stroke would belong to the stroke
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
                            _v[Index(x, y, z)] = before.Value(sx, sy, sz);
                        }
                    }
                }
            }

            Layer = Math.Min(Layer, SizeY - 1);
            _history.Record(before);
        }

        // ---------------------------------------------------------------- undo / redo

        public bool Undo()
        {
            EndStroke();
            if (!_history.TryUndo(Take(), out var snapshot))
            {
                return false;
            }

            Restore(snapshot);
            return true;
        }

        public bool Redo()
        {
            EndStroke();
            if (!_history.TryRedo(Take(), out var snapshot))
            {
                return false;
            }

            Restore(snapshot);
            return true;
        }

        private void Restore(Snapshot snapshot)
        {
            Width = snapshot.Width;
            Height = snapshot.Height;
            Length = snapshot.Length;
            Grid = snapshot.Grid;
            _v = (byte[])snapshot.Voxels.Clone();
            Layer = Math.Min(Layer, SizeY - 1);
        }

        private void Edit(Action change)
        {
            EndStroke();
            var before = Take();
            change();
            _history.Record(before, Take());
        }

        private Snapshot Take() => new Snapshot(Width, Height, Length, Grid, (byte[])_v.Clone());

        // ---------------------------------------------------------------- drafts and pasting (#3689)

        /// <summary>
        /// The canvas as it stands, valid or not — <c>"w,h,l,grid:"</c> plus one <c>0</c>/<c>1</c> per micro cell (x
        /// fastest, then z, then y). Unlike <see cref="Encode"/> it keeps a half-drawn form, which is what a draft must
        /// bring back. <see cref="ReplaceRaw"/> reads it.
        /// </summary>
        public string Raw()
        {
            var chars = new char[_v.Length];
            for (int i = 0; i < _v.Length; i++)
            {
                chars[i] = ShapeColourSlots.CharOf(_v[i]); // a draft keeps the colour slots
            }

            return string.Join(",", Width, Height, Length, Grid) + ":" + new string(chars);
        }

        /// <summary>Puts a <see cref="Raw"/> canvas on the canvas as ONE undoable step (a restored draft is unsaved
        /// work). False — and nothing changes — when the text is not a canvas this editor could have drawn.</summary>
        public bool ReplaceRaw(string? raw)
        {
            if (!TryParseRaw(raw, out int w, out int h, out int l, out int grid, out byte[] voxels))
            {
                return false;
            }

            EndStroke();
            var before = Take();
            Width = w;
            Height = h;
            Length = l;
            Grid = grid;
            _v = voxels;
            Layer = Math.Min(Layer, SizeY - 1);
            _history.Record(before, Take());
            return true;
        }

        /// <summary>Puts a stored form (a pasted share code) on the canvas as ONE undoable step instead of opening it
        /// with a fresh history. False — and nothing changes — for anything <see cref="Load"/> refuses.</summary>
        public bool Replace(string? voxels)
        {
            var other = new FormCanvasModel();
            return other.Load(voxels) && ReplaceRaw(other.Raw());
        }

        private static bool TryParseRaw(string? raw, out int w, out int h, out int l, out int grid, out byte[] voxels)
        {
            w = h = l = grid = 0;
            voxels = Array.Empty<byte>();
            int colon = raw == null ? -1 : raw.IndexOf(':');
            string[] dims = colon > 0 ? raw!.Substring(0, colon).Split(',') : Array.Empty<string>();
            if (dims.Length != 4 || !Digits(dims[0], out w) || !Digits(dims[1], out h) || !Digits(dims[2], out l)
                || !Digits(dims[3], out grid) || w < 1 || h < 1 || l < 1 || w > CustomShape.MaxFootprint
                || h > CustomShape.MaxFootprint || l > CustomShape.MaxFootprint || w * h * l > CustomShape.MaxCells
                || (grid != CustomShape.GridLarge && (grid != CustomShape.GridSmall || w * h * l != 1)))
            {
                return false;
            }

            string bits = raw!.Substring(colon + 1);
            if (bits.Length != w * h * l * grid * grid * grid)
            {
                return false;
            }

            voxels = new byte[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                int value = ShapeColourSlots.ValueOf(bits[i]);
                if (value == 0 && bits[i] != '0')
                {
                    return false;
                }

                voxels[i] = (byte)value;
            }

            return true;
        }

        /// <summary>A small non-negative decimal (no sign, no culture).</summary>
        private static bool Digits(string text, out int value)
            => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);

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

            public bool Get(int x, int y, int z) => Value(x, y, z) != 0;

            public byte Value(int x, int y, int z)
            {
                int sx = Width * Grid, sy = Height * Grid, sz = Length * Grid;
                return x >= 0 && y >= 0 && z >= 0 && x < sx && y < sy && z < sz ? Voxels[(((y * sz) + z) * sx) + x] : (byte)0;
            }
        }

        /// <summary>Two snapshots are the same state when footprint, grid and every micro cell agree.</summary>
        private sealed class SnapshotComparer : IEqualityComparer<Snapshot>
        {
            public static readonly SnapshotComparer Instance = new SnapshotComparer();

            public bool Equals(Snapshot? a, Snapshot? b)
                => ReferenceEquals(a, b) || (a != null && b != null && a.Width == b.Width && a.Height == b.Height
                    && a.Length == b.Length && a.Grid == b.Grid && UndoSnapshots.Bytes.Equals(a.Voxels, b.Voxels));

            public int GetHashCode(Snapshot s) => UndoSnapshots.Bytes.GetHashCode(s.Voxels) ^ (s.Width << 24) ^ (s.Grid << 16);
        }
    }
}
