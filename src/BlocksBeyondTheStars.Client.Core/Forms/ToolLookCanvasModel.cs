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
    /// Unity-free and unit-tested: what it encodes is exactly what the server accepts.
    /// </summary>
    public sealed class ToolLookCanvasModel
    {
        public const int MaxUndo = 48;

        private readonly List<(byte[] Voxels, int Glow)> _undo = new List<(byte[], int)>();
        private readonly List<(byte[] Voxels, int Glow)> _redo = new List<(byte[], int)>();
        private byte[] _v = new byte[ToolLook.VoxelCount];
        private (byte[] Voxels, int Glow)? _strokeStart;

        /// <summary>The layer (Y slice) the canvas shows: 0 = the bottom of the grip.</summary>
        public int Layer { get; private set; } = 3;

        /// <summary>Bit mask of the palette colours that glow.</summary>
        public int GlowMask { get; private set; }

        public bool Dirty { get; private set; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public void MarkSaved() => Dirty = false;

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

        private string ComposeUnchecked()
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

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
            ResetHistory();
            return true;
        }

        public void New()
        {
            _v = new byte[ToolLook.VoxelCount];
            GlowMask = 0;
            ResetHistory();
        }

        private void ResetHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _strokeStart = null;
            Dirty = false;
        }

        public void SetLayer(int layer) => Layer = Math.Max(0, Math.Min(ToolLook.SizeY - 1, layer));

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

            if (_strokeStart == null)
            {
                Push(Take());
            }

            _v[i] = (byte)color;
            Dirty = true;
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
            Push(Take());
            GlowMask ^= 1 << color;
            Dirty = true;
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

        public bool Undo() => Step(_undo, _redo);

        public bool Redo() => Step(_redo, _undo);

        private bool Step(List<(byte[] Voxels, int Glow)> from, List<(byte[] Voxels, int Glow)> to)
        {
            EndStroke();
            if (from.Count == 0)
            {
                return false;
            }

            to.Add(Take());
            var snapshot = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            _v = (byte[])snapshot.Voxels.Clone();
            GlowMask = snapshot.Glow;
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

        private void Push((byte[] Voxels, int Glow) snapshot)
        {
            _undo.Add(snapshot);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
        }

        private (byte[] Voxels, int Glow) Take() => ((byte[])_v.Clone(), GlowMask);

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
