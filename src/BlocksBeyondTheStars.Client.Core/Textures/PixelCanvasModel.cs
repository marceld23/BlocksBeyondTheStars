// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One RGBA colour of the texture editor's canvas.</summary>
    public readonly struct TexelColor : IEquatable<TexelColor>
    {
        public TexelColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public byte A { get; }

        public static TexelColor Clear => new TexelColor(0, 0, 0, 0);

        public bool Equals(TexelColor other) => R == other.R && G == other.G && B == other.B && A == other.A;

        public override bool Equals(object? obj) => obj is TexelColor other && Equals(other);

        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

        public static bool operator ==(TexelColor a, TexelColor b) => a.Equals(b);

        public static bool operator !=(TexelColor a, TexelColor b) => !a.Equals(b);
    }

    /// <summary>
    /// The texture editor's canvas (#1955): the true-colour frames of one texture and every tool that changes them —
    /// brush, eraser, fill, line, rectangle, mirror, shift, colour adjustment, frame handling — with undo. Engine-free,
    /// so the headless suite pins the tools; the Unity side only draws <see cref="Frames"/> and feeds it clicks.
    /// <para>
    /// Coordinates are screen-like: x grows to the right, y grows DOWN, (0,0) is the top-left texel. The frames
    /// themselves are stored the way the game stores tiles — raw RGBA32, rows bottom-up — so they go to the atlas,
    /// the texture pack and the export without conversion.
    /// </para>
    /// <para>
    /// The alpha rule (<see cref="TextureTiles.AlphaModeOf"/>) is part of the model: on an opaque tile nothing can
    /// make a pixel see-through — the eraser paints the secondary colour instead, and a colour picked with alpha is
    /// stored opaque. A block tile therefore cannot leave this editor as an x-ray.
    /// </para>
    /// </summary>
    public sealed class PixelCanvasModel
    {
        public const int Size = TextureTiles.Size;
        private const int MaxUndo = 48;

        private readonly List<byte[]> _frames = new List<byte[]>();
        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private Snapshot? _pending;

        private sealed class Snapshot
        {
            public byte[][] Frames = Array.Empty<byte[]>();
            public int Fps;
            public int Active;
        }

        public PixelCanvasModel(TextureAlphaMode alphaMode, IReadOnlyList<byte[]>? frames = null, int fps = 0)
        {
            AlphaMode = alphaMode;
            Load(frames, fps);
        }

        public TextureAlphaMode AlphaMode { get; private set; }

        /// <summary>The frames, raw RGBA32 rows bottom-up. Never empty. Treat as read-only outside the model.</summary>
        public IReadOnlyList<byte[]> Frames => _frames;

        public int FrameCount => _frames.Count;

        public int ActiveFrame { get; private set; }

        /// <summary>Animation speed; 0 for a still texture.</summary>
        public int Fps { get; private set; }

        /// <summary>Mirror every stroke left↔right / top↔bottom — symmetric tiles in half the strokes.</summary>
        public bool MirrorX { get; set; }

        public bool MirrorY { get; set; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>True once anything was changed since <see cref="Load"/> / <see cref="MarkSaved"/>.</summary>
        public bool Dirty { get; private set; }

        public void MarkSaved() => Dirty = false;

        /// <summary>Replaces the whole content (another texture was opened). Clears the history.</summary>
        public void Load(IReadOnlyList<byte[]>? frames, int fps, TextureAlphaMode? alphaMode = null)
        {
            if (alphaMode.HasValue)
            {
                AlphaMode = alphaMode.Value;
            }

            _frames.Clear();
            if (frames != null)
            {
                foreach (var f in frames)
                {
                    if (f != null && f.Length == TextureTiles.BytesPerFrame && _frames.Count < TextureTiles.MaxFrames)
                    {
                        var copy = (byte[])f.Clone();
                        TextureTiles.EnforceAlpha(copy, AlphaMode);
                        _frames.Add(copy);
                    }
                }
            }

            if (_frames.Count == 0)
            {
                _frames.Add(BlankFrame());
            }

            Fps = _frames.Count > 1 ? (TextureTiles.IsValidAnimation(_frames.Count, fps) ? fps : 8) : 0;
            ActiveFrame = 0;
            _undo.Clear();
            _redo.Clear();
            _pending = null;
            Dirty = false;
        }

        private byte[] BlankFrame()
        {
            var f = new byte[TextureTiles.BytesPerFrame];
            if (AlphaMode == TextureAlphaMode.Opaque)
            {
                for (int i = 0; i < f.Length; i += 4)
                {
                    f[i] = f[i + 1] = f[i + 2] = 128;
                    f[i + 3] = 255;
                }
            }

            return f;
        }

        // ---------------------------------------------------------------- pixels

        private static int Index(int x, int y) => (((Size - 1 - y) * Size) + x) * 4;

        public static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

        public TexelColor Get(int x, int y) => Get(ActiveFrame, x, y);

        public TexelColor Get(int frame, int x, int y)
        {
            if (!InBounds(x, y) || frame < 0 || frame >= _frames.Count)
            {
                return TexelColor.Clear;
            }

            var f = _frames[frame];
            int i = Index(x, y);
            return new TexelColor(f[i], f[i + 1], f[i + 2], f[i + 3]);
        }

        /// <summary>The colour a tool really writes: on an opaque tile always fully opaque.</summary>
        public TexelColor Normalize(TexelColor c) => AlphaMode == TextureAlphaMode.Opaque ? new TexelColor(c.R, c.G, c.B, 255) : c;

        private void SetRaw(int x, int y, TexelColor c)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            var f = _frames[ActiveFrame];
            int i = Index(x, y);
            f[i] = c.R;
            f[i + 1] = c.G;
            f[i + 2] = c.B;
            f[i + 3] = c.A;
        }

        private void Plot(int x, int y, TexelColor c)
        {
            SetRaw(x, y, c);
            if (MirrorX)
            {
                SetRaw(Size - 1 - x, y, c);
            }

            if (MirrorY)
            {
                SetRaw(x, Size - 1 - y, c);
            }

            if (MirrorX && MirrorY)
            {
                SetRaw(Size - 1 - x, Size - 1 - y, c);
            }
        }

        // ---------------------------------------------------------------- history

        /// <summary>Opens one undo step — a whole stroke, not a pixel. Call before the first change of a gesture;
        /// <see cref="CommitEdit"/> after the last. Tools that are a single gesture (fill, adjust…) do both themselves.</summary>
        public void BeginEdit()
        {
            if (_pending == null)
            {
                _pending = Capture();
            }
        }

        public void CommitEdit()
        {
            if (_pending == null)
            {
                return;
            }

            var before = _pending;
            _pending = null;
            if (SameAsNow(before))
            {
                return; // a click that changed nothing is not a step
            }

            _undo.Add(before);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
            Dirty = true;
        }

        public bool Undo()
        {
            CommitEdit();
            if (_undo.Count == 0)
            {
                return false;
            }

            _redo.Add(Capture());
            Restore(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            Dirty = true;
            return true;
        }

        public bool Redo()
        {
            CommitEdit();
            if (_redo.Count == 0)
            {
                return false;
            }

            _undo.Add(Capture());
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            Dirty = true;
            return true;
        }

        private Snapshot Capture()
        {
            var frames = new byte[_frames.Count][];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = (byte[])_frames[i].Clone();
            }

            return new Snapshot { Frames = frames, Fps = Fps, Active = ActiveFrame };
        }

        private void Restore(Snapshot s)
        {
            _frames.Clear();
            foreach (var f in s.Frames)
            {
                _frames.Add((byte[])f.Clone());
            }

            Fps = s.Fps;
            ActiveFrame = Math.Min(s.Active, _frames.Count - 1);
        }

        private bool SameAsNow(Snapshot s)
        {
            if (s.Fps != Fps || s.Frames.Length != _frames.Count)
            {
                return false;
            }

            for (int i = 0; i < s.Frames.Length; i++)
            {
                if (!s.Frames[i].AsSpan().SequenceEqual(_frames[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void Single(Action change)
        {
            BeginEdit();
            change();
            CommitEdit();
        }

        // ---------------------------------------------------------------- tools

        /// <summary>A square brush of <paramref name="size"/> texels centred on (x, y). Inside an open edit.</summary>
        public void Paint(int x, int y, TexelColor color, int size = 1)
        {
            var c = Normalize(color);
            int lo = -(size / 2), hi = lo + Math.Max(1, size) - 1;
            for (int dy = lo; dy <= hi; dy++)
            {
                for (int dx = lo; dx <= hi; dx++)
                {
                    Plot(x + dx, y + dy, c);
                }
            }
        }

        /// <summary>The eraser: see-through on cutout / free textures, the given colour on an opaque tile (which
        /// cannot have holes).</summary>
        public void Erase(int x, int y, TexelColor opaqueFallback, int size = 1)
            => Paint(x, y, AlphaMode == TextureAlphaMode.Opaque ? opaqueFallback : TexelColor.Clear, size);

        /// <summary>A straight run of brush stamps from the last cell of a stroke to this one, so a fast drag leaves
        /// no gaps. Inside an open edit.</summary>
        public void StrokeTo(int x0, int y0, int x1, int y1, TexelColor color, int size = 1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Paint(x0, y0, color, size);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>A line as one undo step.</summary>
        public void Line(int x0, int y0, int x1, int y1, TexelColor color, int size = 1)
            => Single(() => StrokeTo(x0, y0, x1, y1, color, size));

        /// <summary>A rectangle (outline or filled) as one undo step.</summary>
        public void Rectangle(int x0, int y0, int x1, int y1, TexelColor color, bool filled, int size = 1)
        {
            Single(() =>
            {
                int lx = Math.Min(x0, x1), hx = Math.Max(x0, x1), ly = Math.Min(y0, y1), hy = Math.Max(y0, y1);
                if (filled)
                {
                    for (int y = ly; y <= hy; y++)
                    {
                        for (int x = lx; x <= hx; x++)
                        {
                            Paint(x, y, color, 1);
                        }
                    }

                    return;
                }

                StrokeTo(lx, ly, hx, ly, color, size);
                StrokeTo(hx, ly, hx, hy, color, size);
                StrokeTo(hx, hy, lx, hy, color, size);
                StrokeTo(lx, hy, lx, ly, color, size);
            });
        }

        /// <summary>Flood fill from (x, y): the connected area of that colour, or — with
        /// <paramref name="everywhere"/> — every texel of that colour. One undo step.</summary>
        public void Fill(int x, int y, TexelColor color, bool everywhere = false)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            var target = Get(x, y);
            var c = Normalize(color);
            if (target == c)
            {
                return;
            }

            Single(() =>
            {
                if (everywhere)
                {
                    for (int yy = 0; yy < Size; yy++)
                    {
                        for (int xx = 0; xx < Size; xx++)
                        {
                            if (Get(xx, yy) == target)
                            {
                                SetRaw(xx, yy, c);
                            }
                        }
                    }

                    return;
                }

                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                while (stack.Count > 0)
                {
                    var (px, py) = stack.Pop();
                    if (!InBounds(px, py) || Get(px, py) != target)
                    {
                        continue;
                    }

                    SetRaw(px, py, c);
                    stack.Push((px + 1, py));
                    stack.Push((px - 1, py));
                    stack.Push((px, py + 1));
                    stack.Push((px, py - 1));
                }
            });
        }

        /// <summary>Fills the whole frame with one colour. One undo step.</summary>
        public void Clear(TexelColor color)
        {
            var c = Normalize(color);
            Single(() =>
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        SetRaw(x, y, c);
                    }
                }
            });
        }

        /// <summary>Rolls the frame around its edges — the way to bring a tile's SEAM into the middle, fix it, and
        /// roll it back. One undo step.</summary>
        public void Shift(int dx, int dy)
        {
            Single(() =>
            {
                var src = (byte[])_frames[ActiveFrame].Clone();
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int fx = (((x - dx) % Size) + Size) % Size, fy = (((y - dy) % Size) + Size) % Size;
                        int i = Index(fx, fy);
                        SetRaw(x, y, new TexelColor(src[i], src[i + 1], src[i + 2], src[i + 3]));
                    }
                }
            });
        }

        /// <summary>Mirrors the frame left↔right or top↔bottom. One undo step.</summary>
        public void Flip(bool horizontal)
        {
            Single(() =>
            {
                var src = (byte[])_frames[ActiveFrame].Clone();
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int i = Index(horizontal ? Size - 1 - x : x, horizontal ? y : Size - 1 - y);
                        SetRaw(x, y, new TexelColor(src[i], src[i + 1], src[i + 2], src[i + 3]));
                    }
                }
            });
        }

        /// <summary>Brightness (−1..1), contrast (−1..1) and saturation (−1..1) on the whole frame — the quick way
        /// to make an existing tile darker, punchier or greyer without repainting it. One undo step.</summary>
        public void Adjust(float brightness, float contrast, float saturation)
        {
            Single(() =>
            {
                var f = _frames[ActiveFrame];
                float cFactor = 1f + contrast, sFactor = 1f + saturation;
                for (int i = 0; i < f.Length; i += 4)
                {
                    float r = f[i] / 255f, g = f[i + 1] / 255f, b = f[i + 2] / 255f;
                    float lum = (0.299f * r) + (0.587f * g) + (0.114f * b);
                    r = lum + ((r - lum) * sFactor);
                    g = lum + ((g - lum) * sFactor);
                    b = lum + ((b - lum) * sFactor);
                    r = (((r - 0.5f) * cFactor) + 0.5f) + brightness;
                    g = (((g - 0.5f) * cFactor) + 0.5f) + brightness;
                    b = (((b - 0.5f) * cFactor) + 0.5f) + brightness;
                    f[i] = ToByte(r);
                    f[i + 1] = ToByte(g);
                    f[i + 2] = ToByte(b);
                }
            });
        }

        private static byte ToByte(float v) => (byte)(v <= 0f ? 0 : (v >= 1f ? 255 : (int)((v * 255f) + 0.5f)));

        /// <summary>Replaces the active frame (an imported PNG, "reset to the official texture"). One undo step.</summary>
        public void ReplaceFrame(byte[] raw)
        {
            if (raw == null || raw.Length != TextureTiles.BytesPerFrame)
            {
                return;
            }

            Single(() =>
            {
                var copy = (byte[])raw.Clone();
                TextureTiles.EnforceAlpha(copy, AlphaMode);
                _frames[ActiveFrame] = copy;
            });
        }

        /// <summary>Replaces every frame at once (import of a strip, reset of an animated texture). One undo step.</summary>
        public void ReplaceAll(IReadOnlyList<byte[]> frames, int fps)
        {
            if (frames == null || frames.Count == 0 || frames.Count > TextureTiles.MaxFrames)
            {
                return;
            }

            Single(() =>
            {
                _frames.Clear();
                foreach (var f in frames)
                {
                    var copy = (byte[])f.Clone();
                    TextureTiles.EnforceAlpha(copy, AlphaMode);
                    _frames.Add(copy);
                }

                Fps = _frames.Count > 1 ? (TextureTiles.IsValidAnimation(_frames.Count, fps) ? fps : 8) : 0;
                ActiveFrame = Math.Min(ActiveFrame, _frames.Count - 1);
            });
        }

        // ---------------------------------------------------------------- frames

        public void SelectFrame(int index)
        {
            CommitEdit();
            ActiveFrame = Math.Max(0, Math.Min(index, _frames.Count - 1));
        }

        /// <summary>Adds a copy of the active frame right after it (animation is drawn by changing a copy a little).
        /// False at the frame limit. One undo step.</summary>
        public bool DuplicateFrame()
        {
            if (_frames.Count >= TextureTiles.MaxFrames)
            {
                return false;
            }

            Single(() =>
            {
                _frames.Insert(ActiveFrame + 1, (byte[])_frames[ActiveFrame].Clone());
                ActiveFrame++;
                if (Fps == 0)
                {
                    Fps = 8;
                }
            });
            return true;
        }

        /// <summary>Removes the active frame; the last one stays. One undo step.</summary>
        public bool DeleteFrame()
        {
            if (_frames.Count <= 1)
            {
                return false;
            }

            Single(() =>
            {
                _frames.RemoveAt(ActiveFrame);
                ActiveFrame = Math.Min(ActiveFrame, _frames.Count - 1);
                if (_frames.Count == 1)
                {
                    Fps = 0;
                }
            });
            return true;
        }

        /// <summary>Moves the active frame one place earlier / later. One undo step.</summary>
        public bool MoveFrame(int direction)
        {
            int target = ActiveFrame + Math.Sign(direction);
            if (direction == 0 || target < 0 || target >= _frames.Count)
            {
                return false;
            }

            Single(() =>
            {
                var f = _frames[ActiveFrame];
                _frames.RemoveAt(ActiveFrame);
                _frames.Insert(target, f);
                ActiveFrame = target;
            });
            return true;
        }

        /// <summary>Steps through the allowed animation speeds. One undo step.</summary>
        public void SetFps(int fps)
        {
            if (_frames.Count > 1 && TextureTiles.IsValidAnimation(_frames.Count, fps) && fps != Fps)
            {
                Single(() => Fps = fps);
            }
        }

        // ---------------------------------------------------------------- helpers for the UI

        /// <summary>The most used colours of the active frame, most frequent first — offered as swatches so a
        /// repaint stays in the tile's own palette. Fully transparent texels are skipped.</summary>
        public List<TexelColor> DominantColors(int max)
        {
            var counts = new Dictionary<int, int>();
            var f = _frames[ActiveFrame];
            for (int i = 0; i < f.Length; i += 4)
            {
                if (f[i + 3] == 0)
                {
                    continue;
                }

                // 4 bits per channel: shades that differ by a hair share a swatch.
                int key = ((f[i] >> 4) << 8) | ((f[i + 1] >> 4) << 4) | (f[i + 2] >> 4);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }

            var keys = new List<int>(counts.Keys);
            keys.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));
            var result = new List<TexelColor>();
            for (int i = 0; i < keys.Count && result.Count < max; i++)
            {
                int k = keys[i];
                result.Add(new TexelColor((byte)((((k >> 8) & 15) * 17)), (byte)((((k >> 4) & 15) * 17)), (byte)(((k & 15) * 17)), 255));
            }

            return result;
        }

        /// <summary>A deep copy of the frames for saving.</summary>
        public byte[][] CopyFrames()
        {
            CommitEdit();
            var copy = new byte[_frames.Count][];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = (byte[])_frames[i].Clone();
            }

            return copy;
        }
    }
}
