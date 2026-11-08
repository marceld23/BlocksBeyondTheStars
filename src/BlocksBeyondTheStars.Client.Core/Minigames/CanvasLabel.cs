// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>How a <see cref="CanvasLabel"/> hangs off its anchor x.</summary>
    public enum LabelAlign
    {
        /// <summary>The anchor is the left edge of the line (like <see cref="Canvas2D.DrawText"/>).</summary>
        Left,

        /// <summary>The anchor is the centre of the line (like <see cref="Canvas2D.DrawTextCentered"/>).</summary>
        Center,

        /// <summary>The anchor is the right edge of the line.</summary>
        Right,
    }

    /// <summary>
    /// One line of text on the label layer of a <see cref="Canvas2D"/> (#3382). A label is NOT rasterised into the canvas'
    /// pixels: the Unity host draws it over the canvas texture with the real UI font (Rajdhani with the Noto fallbacks), so
    /// a localized word reads in every language — the 5×7 <see cref="BitmapFont"/> knows only A–Z, digits and a few signs
    /// and left Cyrillic, CJK and even the German umlauts blank.
    ///
    /// Coordinates are canvas pixels with the canvas' own top-left origin. <see cref="Scale"/> has the meaning of
    /// <see cref="Canvas2D.DrawText"/>'s scale, so a call moves over unchanged: the capitals stand
    /// <see cref="CapHeight"/> = 7 × scale pixels tall, their tops at <see cref="Y"/>.
    /// </summary>
    public readonly struct CanvasLabel : System.IEquatable<CanvasLabel>
    {
        /// <summary>Font size (em) per capital height: Rajdhani's capitals are 0.643 em tall, the Noto fallbacks' about
        /// 0.71 — 1.5 makes the real capitals about as tall as the bitmap glyphs were.</summary>
        public const float FontPerCap = 1.5f;

        /// <summary>The line box is this many capital heights tall — room for accents and descenders around the caps.</summary>
        public const int BoxCaps = 2;

        public CanvasLabel(int x, int y, string text, Rgba color, int scale, LabelAlign align, int maxWidth)
        {
            X = x;
            Y = y;
            Text = text ?? string.Empty;
            Color = color;
            Scale = scale < 1 ? 1 : scale;
            Align = align;
            MaxWidth = maxWidth < 1 ? 1 : maxWidth;
        }

        /// <summary>The anchor x: the left edge, the centre or the right edge of the line, by <see cref="Align"/>.</summary>
        public int X { get; }

        /// <summary>The top of the capitals (where <see cref="Canvas2D.DrawText"/> put the top glyph row).</summary>
        public int Y { get; }

        public string Text { get; }

        public Rgba Color { get; }

        /// <summary>The size, in <see cref="Canvas2D.DrawText"/> scale units (≥ 1).</summary>
        public int Scale { get; }

        public LabelAlign Align { get; }

        /// <summary>The widest the line may get, in canvas pixels; the host shrinks a longer text to fit (a button caption
        /// stays inside its box, a word in a long language stays on the canvas).</summary>
        public int MaxWidth { get; }

        /// <summary>The height of the capitals in canvas pixels — the bitmap glyph height at this scale.</summary>
        public int CapHeight => BitmapFont.GlyphHeight * Scale;

        /// <summary>The box (canvas pixels, top-left origin) the host lays the text into: <see cref="MaxWidth"/> wide on the
        /// anchor's side, <see cref="BoxCaps"/> capital heights tall and centred on the middle of the capitals, so the host
        /// centres the line vertically in it.</summary>
        public (float X, float Y, float W, float H) Box()
        {
            float w = MaxWidth;
            float h = CapHeight * BoxCaps;
            float x = Align == LabelAlign.Center ? X - (w / 2f) : Align == LabelAlign.Right ? X - w : X;
            float y = Y + (CapHeight / 2f) - (h / 2f);
            return (x, y, w, h);
        }

        /// <summary>The font size for the line when the canvas is shown <paramref name="displayScale"/> times its pixel
        /// size (display units per canvas pixel).</summary>
        public float FontSize(float displayScale) => CapHeight * FontPerCap * displayScale;

        /// <summary>The room a label has from its anchor to the canvas edge on its side(s) — the default
        /// <see cref="MaxWidth"/>, so a long word shrinks instead of running off the canvas.</summary>
        public static int Room(int canvasWidth, int x, LabelAlign align)
        {
            int room = align == LabelAlign.Center ? 2 * System.Math.Min(x, canvasWidth - x)
                : align == LabelAlign.Right ? x
                : canvasWidth - x;
            return room < 1 ? 1 : room;
        }

        public bool Equals(CanvasLabel other)
            => X == other.X && Y == other.Y && Scale == other.Scale && Align == other.Align && MaxWidth == other.MaxWidth
               && Color.R == other.Color.R && Color.G == other.Color.G && Color.B == other.Color.B && Color.A == other.Color.A
               && string.Equals(Text, other.Text, System.StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CanvasLabel other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (X * 397) ^ Y;
                h = (h * 397) ^ Scale;
                h = (h * 397) ^ (int)Align;
                h = (h * 397) ^ MaxWidth;
                h = (h * 397) ^ ((Color.R << 24) | (Color.G << 16) | (Color.B << 8) | Color.A);
                return (h * 397) ^ System.StringComparer.Ordinal.GetHashCode(Text);
            }
        }

        public static bool operator ==(CanvasLabel left, CanvasLabel right) => left.Equals(right);

        public static bool operator !=(CanvasLabel left, CanvasLabel right) => !left.Equals(right);

        public override string ToString() => Text;
    }
}
