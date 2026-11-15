// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The model behind the tool-look editor (#1963): the coloured voxel grid of a <see cref="ToolLook"/>, painted
    /// one horizontal layer at a time like a form — but with a colour per voxel and a glow switch per colour.
    /// Unity-free and unit-tested: what it encodes is exactly what the server accepts. Undo runs on the editors' shared
    /// <see cref="UndoHistory{T}"/> (#3695).
    /// </summary>
    public sealed class ToolLookCanvasModel : ILayerCanvas
    {
        public const int MaxUndo = UndoHistory<byte[]>.DefaultCapacity;

        private readonly UndoHistory<Snapshot> _history = new UndoHistory<Snapshot>(MaxUndo, SnapshotComparer.Instance);
        private byte[] _v = new byte[ToolLook.VoxelCount];

        /// <summary>The layer (Y slice) the canvas shows: 0 = the bottom of the grip.</summary>
        public int Layer { get; private set; } = 3;

        /// <summary>Bit mask of the palette colours that glow.</summary>
        public int GlowMask { get; private set; }

        /// <summary>True when the look differs from the one loaded or last marked saved — undoing back to that state
        /// makes it false again.</summary>
        public bool Dirty => _history.IsDirty;

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        public void MarkSaved() => _history.MarkSaved();

        /// <summary>Palette index at a voxel (0 = empty); out of range reads as empty.</summary>
        public int Get(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < ToolLook.SizeX && y < ToolLook.SizeY && z < ToolLook.SizeZ
                ? _v[ToolLook.IndexOf(x, y, z)]
                : 0;

        public bool IsEmpty
        {
            get
            {
                foreach (byte b in _v)
                {
                    if (b != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Boxes the look needs after merging per colour — what the budget line shows.</summary>
        public int PartsUsed()
        {
            string model = ComposeUnchecked();
            return model.Length == 0 ? 0 : ToolLook.Merge(model).Count;
        }

        /// <summary>The look in the format the game stores and sends — empty when nothing is drawn or the look
        /// needs more than <see cref="ToolLook.MaxParts"/> boxes.</summary>
        public string Encode() => ToolLook.Compose(_v, GlowMask);

        /// <summary>The parts for the preview — also while the look is over budget (it must still show).</summary>
        public IReadOnlyList<ToolLook.Box> PreviewBoxes()
        {
            string model = ComposeUnchecked();
            return model.Length == 0 ? new List<ToolLook.Box>() : ToolLook.Merge(model);
        }

        private string ComposeUnchecked() => IsEmpty ? string.Empty : Raw();

        /// <summary>
        /// The canvas as it stands, valid or not (also empty or over the part budget): the <see cref="ToolLook"/> payload
        /// layout without its checks — what a draft must bring back (#3689). <see cref="ReplaceRaw"/> reads it.
        /// </summary>
        public string Raw()
        {
            var chars = new char[8 + ToolLook.VoxelCount];
            ("t1:" + (GlowMask & 0xFFFE).ToString("x4", System.Globalization.CultureInfo.InvariantCulture) + ":").CopyTo(0, chars, 0, 8);
            for (int i = 0; i < _v.Length; i++)
            {
                int v = _v[i] & 0xF;
                chars[8 + i] = (char)(v < 10 ? '0' + v : 'a' + (v - 10));
            }

            return new string(chars);
        }

        /// <summary>Loads a stored look; false — and nothing changes — for garbage. Clears the history.</summary>
        public bool Load(string? model)
        {
            if (!ToolLook.IsValid(model) || !ToolLook.TryRead(model, out byte[] voxels, out int glow))
            {
                return false;
            }

            _v = voxels;
            GlowMask = glow;
            _history.Reset();
            return true;
        }

        /// <summary>Puts a <see cref="Raw"/> canvas (a draft) or a pasted look on the canvas as ONE undoable step. False —
        /// and nothing changes — when it is not a look (a colour outside the palette, a broken payload).</summary>
        public bool ReplaceRaw(string? raw)
        {
            if (!ToolLook.TryRead(raw, out byte[] voxels, out int glow))
            {
                return false;
            }

            foreach (byte v in voxels)
            {
                if (v >= ToolLook.Palette.Count)
                {
                    return false;
                }
            }

            EndStroke();
            var before = Take();
            _v = voxels;
            GlowMask = glow & 0xFFFE;
            _history.Record(before, Take());
            return true;
        }

        public void New()
        {
            _v = new byte[ToolLook.VoxelCount];
            GlowMask = 0;
            _history.Reset();
        }

        public void SetLayer(int layer) => Layer = Math.Max(0, Math.Min(ToolLook.SizeY - 1, layer));

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

        /// <summary>Sets one voxel of the current layer to a palette index (0 erases). True when it changed.</summary>
        public bool Paint(int x, int z, int color)
        {
            if (x < 0 || z < 0 || x >= ToolLook.SizeX || z >= ToolLook.SizeZ || color < 0 || color >= ToolLook.Palette.Count)
            {
                return false;
            }

            int i = ToolLook.IndexOf(x, Layer, z);
            if (_v[i] == color)
            {
                return false;
            }

            var before = _history.InGesture ? null : Take(); // inside a stroke the stroke's start is the step
            _v[i] = (byte)color;
            if (before != null)
            {
                _history.Record(before);
            }

            return true;
        }

        /// <summary>Switches the glow of a palette colour.</summary>
        public void ToggleGlow(int color)
        {
            if (color < 1 || color >= ToolLook.Palette.Count)
            {
                return;
            }

            EndStroke();
            var before = Take();
            GlowMask ^= 1 << color;
            _history.Record(before);
        }

        public bool Glows(int color) => (GlowMask & (1 << color)) != 0;

        public void ClearAll() => Edit(() => Array.Clear(_v, 0, _v.Length));

        public void ClearLayer() => Edit(() =>
        {
            for (int z = 0; z < ToolLook.SizeZ; z++)
            {
                for (int x = 0; x < ToolLook.SizeX; x++)
                {
                    _v[ToolLook.IndexOf(x, Layer, z)] = 0;
                }
            }
        });

        public void CopyLayerBelow()
        {
            if (Layer == 0)
            {
                return;
            }

            Edit(() =>
            {
                for (int z = 0; z < ToolLook.SizeZ; z++)
                {
                    for (int x = 0; x < ToolLook.SizeX; x++)
                    {
                        _v[ToolLook.IndexOf(x, Layer, z)] = _v[ToolLook.IndexOf(x, Layer - 1, z)];
                    }
                }
            });
        }

        /// <summary>Mirrors the look left–right onto its other half (what is drawn stays).</summary>
        public void MirrorX() => Edit(() =>
        {
            var source = (byte[])_v.Clone();
            for (int y = 0; y < ToolLook.SizeY; y++)
            {
                for (int z = 0; z < ToolLook.SizeZ; z++)
                {
                    for (int x = 0; x < ToolLook.SizeX; x++)
                    {
                        byte mirrored = source[ToolLook.IndexOf(ToolLook.SizeX - 1 - x, y, z)];
                        if (mirrored != 0 && source[ToolLook.IndexOf(x, y, z)] == 0)
                        {
                            _v[ToolLook.IndexOf(x, y, z)] = mirrored;
                        }
                    }
                }
            }
        });

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
            _v = (byte[])snapshot.Voxels.Clone();
            GlowMask = snapshot.Glow;
        }

        private void Edit(Action change)
        {
            EndStroke();
            var before = Take();
            change();
            _history.Record(before, Take());
        }

        private Snapshot Take() => new Snapshot((byte[])_v.Clone(), GlowMask);

        /// <summary>One state of the look: the voxels and the glow mask.</summary>
        private sealed class Snapshot
        {
            public Snapshot(byte[] voxels, int glow)
            {
                Voxels = voxels;
                Glow = glow;
            }

            public byte[] Voxels { get; }

            public int Glow { get; }
        }

        /// <summary>Two snapshots are the same state when the glow mask and every voxel agree.</summary>
        private sealed class SnapshotComparer : IEqualityComparer<Snapshot>
        {
            public static readonly SnapshotComparer Instance = new SnapshotComparer();

            public bool Equals(Snapshot? a, Snapshot? b)
                => ReferenceEquals(a, b) || (a != null && b != null && a.Glow == b.Glow && UndoSnapshots.Bytes.Equals(a.Voxels, b.Voxels));

            public int GetHashCode(Snapshot s) => UndoSnapshots.Bytes.GetHashCode(s.Voxels) ^ s.Glow;
        }
    }
}
