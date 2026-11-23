// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's soft side: "calm when saving", the soft brush, the smoothing brush, the
    /// whole-tile smooth / calm / make-seamless actions and the "brightness only" paint mode. The pixel arithmetic is
    /// <see cref="TileFilters"/>; this part holds the state and makes every action one undo step.
    /// <para>
    /// A soft or smoothing stroke works against the frame as it was when the stroke began: every texel remembers the
    /// strongest brush weight that passed over it (its coverage) and shows the stroke's start blended toward the paint (or
    /// toward its smoothed self) by that coverage. So a stroke never builds up past the brush's own opacity, however long
    /// the mouse rests or however often the stamps overlap — the result depends on the path alone, not on the frame rate.
    /// </para>
    /// </summary>
    public sealed partial class PixelCanvasModel
    {
        /// <summary>The soft brush's opacity at its centre; it falls off to 0 at its rim.</summary>
        public const float SoftCentreOpacity = 0.6f;

        /// <summary>The share "calm all" moves a tile toward its mean colour per press.</summary>
        public const float CalmStep = 0.25f;

        private byte[]? _strokeBase;
        private float[]? _strokeCover;
        private byte[]? _strokeSmooth;
        private int _strokeFrame = -1;

        /// <summary>"Calm when saving", 0..100 %: how far <see cref="SavedFrames"/> moves every texel toward its frame's mean
        /// colour (the calm is baked into the saved pixels; the canvas keeps the painting as painted). Part of
        /// the undo history. 0 after <see cref="Load"/>.</summary>
        public int SaveCalm { get; private set; }

        /// <summary>"Brightness only": brush, soft brush, line, box and fill change only a texel's brightness and keep
        /// its hue — for tiles the world tints. Off unless chosen.</summary>
        public bool LuminanceOnly { get; set; }

        // ---------------------------------------------------------------- calm when saving

        /// <summary>Sets "calm when saving" (clamped to 0..100). One undo step; no step when it does not change.</summary>
        public void SetSaveCalm(int percent)
        {
            int value = Math.Max(0, Math.Min(100, percent));
            if (value != SaveCalm)
            {
                Single(() => SaveCalm = value);
            }
        }

        /// <summary><see cref="Load"/> with a "calm when saving" value — the editor reopening a painting it saved calmed. The
        /// loaded state is the saved one (not dirty, no history).</summary>
        public void LoadWithCalm(IReadOnlyList<byte[]>? frames, int fps, TextureAlphaMode alphaMode, int saveCalm)
        {
            Load(frames, fps, alphaMode);
            SaveCalm = Math.Max(0, Math.Min(100, saveCalm));
        }

        /// <summary><see cref="ReplaceAll(IReadOnlyList{byte[]}, int)"/> together with a "calm when saving" value — a
        /// restored draft. One undo step.</summary>
        public void ReplaceAll(IReadOnlyList<byte[]> frames, int fps, int saveCalm)
        {
            if (frames == null || frames.Count == 0 || frames.Count > TextureTiles.MaxFrames)
            {
                return;
            }

            int value = Math.Max(0, Math.Min(100, saveCalm));
            Single(() =>
            {
                PutFrames(frames, fps);
                SaveCalm = value;
            });
        }

        /// <summary>The frames as they are saved: a deep copy, each frame calmed by <see cref="SaveCalm"/>.</summary>
        public byte[][] SavedFrames()
        {
            var frames = CopyFrames();
            if (SaveCalm > 0)
            {
                foreach (var f in frames)
                {
                    TileFilters.CalmInPlace(f, SaveCalm / 100.0);
                }
            }

            return frames;
        }

        /// <summary>What the canvas shows of frame <paramref name="index"/>: the frame as it will be saved (calmed by
        /// <see cref="SaveCalm"/>), in grey when <see cref="LuminanceOnly"/> is on (the world puts the colour on). The model's
        /// own array when neither applies — read it, do not change it.</summary>
        public byte[] ShownFrame(int index)
        {
            var frame = _frames[Math.Max(0, Math.Min(index, _frames.Count - 1))];
            if (SaveCalm > 0)
            {
                frame = TileFilters.Calm(frame, SaveCalm / 100.0);
            }

            return LuminanceOnly ? TileFilters.Greyscale(frame) : frame;
        }

        // ---------------------------------------------------------------- whole-tile actions

        /// <summary>One pass of the wrapping 3 × 3 smoothing over the active frame. One undo step.</summary>
        public void SmoothAll() => Single(() => _frames[ActiveFrame] = TileFilters.Smooth(_frames[ActiveFrame]));

        /// <summary>Moves the active frame <paramref name="share"/> of the way to its mean colour (alpha kept). One undo step.</summary>
        public void CalmAll(float share = CalmStep) => Single(() => TileFilters.CalmInPlace(_frames[ActiveFrame], share));

        /// <summary>Heals the active frame's edges so it repeats without a seam (<see cref="TileFilters.MakeSeamless"/>). One
        /// undo step; returns the seam measure before and after.</summary>
        public (double Before, double After) MakeSeamless()
        {
            double before = TileFilters.SeamRatio(_frames[ActiveFrame]);
            Single(() => _frames[ActiveFrame] = TileFilters.MakeSeamless(_frames[ActiveFrame]));
            return (before, TileFilters.SeamRatio(_frames[ActiveFrame]));
        }

        // ---------------------------------------------------------------- brightness only

        /// <summary>What every painting tool writes: the colour, or with <see cref="LuminanceOnly"/> the texel's own hue at
        /// the colour's brightness.</summary>
        private void SetPaint(int x, int y, TexelColor c)
            => SetRaw(x, y, LuminanceOnly && InBounds(x, y) ? Normalize(TileFilters.KeepHue(Get(x, y), c)) : c);

        /// <summary>The fill of <see cref="LuminanceOnly"/>: the connected region of the clicked colour (or every texel of it)
        /// is found first and then takes the brightness — a region whose brightness does not change would else be found
        /// again and again. One undo step.</summary>
        private void FillLuminance(int x, int y, TexelColor c, bool everywhere)
        {
            var target = Get(x, y);
            var region = new bool[Size * Size];
            if (everywhere)
            {
                for (int i = 0; i < region.Length; i++)
                {
                    region[i] = Get(i % Size, i / Size) == target;
                }
            }
            else
            {
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                while (stack.Count > 0)
                {
                    var (px, py) = stack.Pop();
                    if (!InBounds(px, py) || region[(py * Size) + px] || Get(px, py) != target)
                    {
                        continue;
                    }

                    region[(py * Size) + px] = true;
                    stack.Push((px + 1, py));
                    stack.Push((px - 1, py));
                    stack.Push((px, py + 1));
                    stack.Push((px, py - 1));
                }
            }

            Single(() =>
            {
                for (int i = 0; i < region.Length; i++)
                {
                    if (region[i])
                    {
                        SetPaint(i % Size, i / Size, c);
                    }
                }
            });
        }

        // ---------------------------------------------------------------- soft and smoothing strokes

        /// <summary>Opens the undo step of a soft or smoothing stroke and remembers the frame as it is (the stroke blends
        /// against it). Call when the button goes down; <see cref="CommitEdit"/> closes it.</summary>
        public void BeginSoftStroke()
        {
            BeginEdit();
            _strokeBase = (byte[])_frames[ActiveFrame].Clone();
            _strokeCover = new float[Size * Size];
            _strokeSmooth = null;
            _strokeFrame = ActiveFrame;
        }

        /// <summary>Forgets the open soft stroke (a new texture, an undo, a restore).</summary>
        private void EndSoftStroke()
        {
            _strokeBase = null;
            _strokeCover = null;
            _strokeSmooth = null;
            _strokeFrame = -1;
        }

        /// <summary>The soft brush from one cell of the stroke to the next: round, <paramref name="size"/> texels across, its
        /// opacity <see cref="SoftCentreOpacity"/> at the centre falling off to 0 at the rim, blending with what is there
        /// (an erasing colour of alpha 0 fades the alpha). Mirrors like the brush. Inside a stroke.</summary>
        public void SoftStrokeTo(int x0, int y0, int x1, int y1, TexelColor color, int size)
        {
            EnsureStroke();
            var c = Normalize(color);
            Walk(x0, y0, x1, y1, (x, y) => Stamp(x, y, size, SoftCentreOpacity, (tx, ty, baseTexel, k) => SoftBlend(baseTexel, c, k)));
        }

        /// <summary>The smoothing brush: where it passes, the frame moves toward its own smoothed self (one wrapping 3 × 3
        /// pass of the stroke's start), fully at the centre and not at all at the rim. The alpha is kept. Inside a stroke.</summary>
        public void SmoothStrokeTo(int x0, int y0, int x1, int y1, int size)
        {
            EnsureStroke();
            var smooth = _strokeSmooth ??= TileFilters.Smooth(_strokeBase!);
            Walk(x0, y0, x1, y1, (x, y) => Stamp(x, y, size, 1f, (tx, ty, baseTexel, k) =>
            {
                int i = Index(tx, ty);
                return Lerp(baseTexel, new TexelColor(smooth[i], smooth[i + 1], smooth[i + 2], baseTexel.A), k);
            }));
        }

        private void EnsureStroke()
        {
            if (_strokeBase == null || _strokeCover == null || _strokeFrame != ActiveFrame || !_history.InGesture)
            {
                BeginSoftStroke();
            }
        }

        /// <summary>One round stamp at (cx, cy) and its mirror images: every texel under it gets the stronger of its coverage
        /// and the stamp's weight, and shows <paramref name="blend"/>(the stroke's start, that coverage).</summary>
        private void Stamp(int cx, int cy, int size, float centre, Func<int, int, TexelColor, float, TexelColor> blend)
        {
            float radius = (Math.Max(1, size) * 0.5f) + 1f;
            int reach = (int)Math.Ceiling(radius);
            var centres = new List<(int X, int Y)> { (cx, cy) };
            if (MirrorX)
            {
                centres.Add((Size - 1 - cx, cy));
            }

            if (MirrorY)
            {
                centres.Add((cx, Size - 1 - cy));
            }

            if (MirrorX && MirrorY)
            {
                centres.Add((Size - 1 - cx, Size - 1 - cy));
            }

            foreach (var (mx, my) in centres)
            {
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        StampTexel(mx + dx, my + dy, Weight(dx, dy, radius) * centre, blend);
                    }
                }
            }
        }

        private void StampTexel(int x, int y, float weight, Func<int, int, TexelColor, float, TexelColor> blend)
        {
            int cell = (y * Size) + x;
            if (!InBounds(x, y) || weight <= _strokeCover![cell])
            {
                return;
            }

            _strokeCover[cell] = weight;
            int i = Index(x, y);
            var baseTexel = new TexelColor(_strokeBase![i], _strokeBase[i + 1], _strokeBase[i + 2], _strokeBase[i + 3]);
            SetRaw(x, y, Normalize(blend(x, y, baseTexel, weight)));
        }

        /// <summary>The brush's falloff: (1 − t²)² at the distance t (in radii) from its centre, 0 beyond the rim.</summary>
        private static float Weight(int dx, int dy, float radius)
        {
            float t2 = ((dx * dx) + (dy * dy)) / (radius * radius);
            return t2 >= 1f ? 0f : (1f - t2) * (1f - t2);
        }

        /// <summary>The soft brush's blend of the stroke's start toward the paint by <paramref name="k"/>. Colours are mixed
        /// weighted by alpha (painting into a hole gives the paint's colour, erasing keeps the colour and fades the alpha);
        /// with <see cref="LuminanceOnly"/> only the brightness moves.</summary>
        private TexelColor SoftBlend(TexelColor baseTexel, TexelColor paint, float k)
        {
            if (LuminanceOnly && paint.A > 0)
            {
                double l0 = TileFilters.Luma(baseTexel.R, baseTexel.G, baseTexel.B);
                double l1 = TileFilters.Luma(paint.R, paint.G, paint.B);
                byte alpha = TileFilters.ToByte(baseTexel.A + ((paint.A - baseTexel.A) * k));
                return TileFilters.WithLuminance(baseTexel, l0 + ((l1 - l0) * k), alpha);
            }

            double a0 = baseTexel.A * (1.0 - k), a1 = paint.A * (double)k, a = a0 + a1;
            if (a <= 0.0)
            {
                return new TexelColor(baseTexel.R, baseTexel.G, baseTexel.B, 0);
            }

            return new TexelColor(
                TileFilters.ToByte(((baseTexel.R * a0) + (paint.R * a1)) / a),
                TileFilters.ToByte(((baseTexel.G * a0) + (paint.G * a1)) / a),
                TileFilters.ToByte(((baseTexel.B * a0) + (paint.B * a1)) / a),
                TileFilters.ToByte(a));
        }

        private static TexelColor Lerp(TexelColor a, TexelColor b, float k) => new TexelColor(
            TileFilters.ToByte(a.R + ((b.R - a.R) * (double)k)),
            TileFilters.ToByte(a.G + ((b.G - a.G) * (double)k)),
            TileFilters.ToByte(a.B + ((b.B - a.B) * (double)k)),
            TileFilters.ToByte(a.A + ((b.A - a.A) * (double)k)));

        /// <summary>Every cell of a straight run from (x0, y0) to (x1, y1), both ends included (Bresenham, as the brush).</summary>
        private static void Walk(int x0, int y0, int x1, int y1, Action<int, int> visit)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                visit(x0, y0);
                if (x0 == x1 && y0 == y1)
                {
                    return;
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
    }
}
