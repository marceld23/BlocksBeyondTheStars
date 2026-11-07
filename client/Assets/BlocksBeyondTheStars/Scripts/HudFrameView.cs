// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.HudFrames;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws a HUD frame design (#3317) into the rectangle of its parent: eight <see cref="RawImage"/>s over four 32 × 32
    /// textures — the corners show the corner tile mirrored through their <c>uvRect</c>, the edges repeat their tile (wrap
    /// mode Repeat) as <see cref="HudFrameLayout"/> lays them out. The in-world frame (<see cref="HudFrame"/>) and the
    /// editor's little preview screen both use it.
    /// <para>
    /// The palette colours come from <see cref="FacePalette"/>; index 0 is transparent. Twinkling indices take their colour
    /// of the current step about twice a second — only the textures that hold a light are rewritten, into buffers made
    /// once, so nothing allocates per frame. <see cref="Still"/> (reduced effects) freezes the lights on their first colour.
    /// </para>
    /// </summary>
    public sealed class HudFrameView : MonoBehaviour
    {
        private readonly RawImage[] _pieces = new RawImage[HudFrameLayout.PieceCount];
        private readonly RectTransform[] _pieceRects = new RectTransform[HudFrameLayout.PieceCount];
        private readonly Texture2D[] _textures = new Texture2D[HudFrameDesign.TileCount];
        private readonly Color32[][] _pixels = new Color32[HudFrameDesign.TileCount][];
        private readonly int[][] _grids = new int[HudFrameDesign.TileCount][];
        private readonly bool[] _tileTwinkles = new bool[HudFrameDesign.TileCount];
        private readonly HudFramePiece[] _layout = new HudFramePiece[HudFrameLayout.PieceCount];
        private readonly Color32[] _twinkleColors = new Color32[HudFrameDesign.MaxTwinkle];

        private HudFrameDesign _design = HudFrameDesign.Empty;
        private int _twinkleColorCount;
        private bool _anyTwinkle;
        private int _step;
        private float _alpha = 1f;

        /// <summary>Reduced effects: the lights keep their first colour.</summary>
        public bool Still;

        /// <summary>True when the shown design paints at least one pixel.</summary>
        public bool HasFrame => !_design.IsEmpty;

        /// <summary>A view filling <paramref name="parent"/>'s rectangle (stretch anchors), empty until
        /// <see cref="SetDesign"/> and <see cref="Layout"/>. Point filtering keeps the in-world pixels crisp; a scaled-down
        /// preview reads better bilinear.</summary>
        public static HudFrameView Create(Transform parent, string name, FilterMode filter)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<HudFrameView>();
            view.Build(filter);
            return view;
        }

        private void Build(FilterMode filter)
        {
            for (int t = 0; t < HudFrameDesign.TileCount; t++)
            {
                _textures[t] = new Texture2D(HudFrameDesign.TileSide, HudFrameDesign.TileSide, TextureFormat.RGBA32, false)
                {
                    filterMode = filter,
                    wrapMode = TextureWrapMode.Repeat,
                    name = "HudFrameTile" + t,
                };
                _pixels[t] = new Color32[HudFrameDesign.TilePixels];
                _grids[t] = new int[HudFrameDesign.TilePixels];
            }

            for (int i = 0; i < _pieces.Length; i++)
            {
                var go = new GameObject("Piece" + i, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); // laid out from the top-left, y down
                var raw = go.AddComponent<RawImage>();
                raw.raycastTarget = false; // a frame never eats a click
                raw.enabled = false;
                _pieces[i] = raw;
                _pieceRects[i] = rt;
            }
        }

        /// <summary>Shows <paramref name="design"/>: decodes its tiles and uploads the four textures.</summary>
        public void SetDesign(HudFrameDesign design)
        {
            _design = design ?? HudFrameDesign.Empty;
            var colors = HudFrameTwinkle.ColorsOf(_design);
            _twinkleColorCount = 0;
            for (int i = 0; i < colors.Count && i < _twinkleColors.Length; i++)
            {
                if (HudFrameTwinkle.TryParseRgb(colors[i], out int rgb))
                {
                    _twinkleColors[_twinkleColorCount++] = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                }
            }

            _anyTwinkle = false;
            for (int t = 0; t < HudFrameDesign.TileCount; t++)
            {
                string tile = _design.Tile((HudFrameTile)t);
                bool twinkles = false;
                for (int i = 0; i < HudFrameDesign.TilePixels; i++)
                {
                    int index = Mathf.Max(0, HudFrameDesign.ValueOf(tile[i]));
                    _grids[t][i] = index;
                    twinkles |= _design.TwinkleSlot(index) >= 0;
                }

                _tileTwinkles[t] = twinkles && _twinkleColorCount > 0;
                _anyTwinkle |= _tileTwinkles[t];
                Upload(t);
            }
        }

        /// <summary>Lays the pieces out on a <paramref name="width"/> × <paramref name="height"/> rectangle (this view's
        /// units) with tiles of <paramref name="tile"/> units, <paramref name="inset"/> in from every edge.</summary>
        public void Layout(float width, float height, float tile, float inset)
        {
            HudFrameLayout.Compute(width, height, tile, inset, _layout);
            for (int i = 0; i < _layout.Length; i++)
            {
                var piece = _layout[i];
                var raw = _pieces[i];
                raw.enabled = piece.Visible;
                raw.texture = _textures[(int)piece.Tile];
                raw.uvRect = new Rect(piece.U, piece.V, piece.UvW, piece.UvH);
                _pieceRects[i].anchoredPosition = new Vector2(piece.X, -piece.Y);
                _pieceRects[i].sizeDelta = new Vector2(piece.W, piece.H);
            }
        }

        /// <summary>The frame's opacity (0–1).</summary>
        public void SetAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(alpha, _alpha))
            {
                return;
            }

            _alpha = alpha;
            var tint = new Color(1f, 1f, 1f, alpha);
            foreach (var raw in _pieces)
            {
                raw.color = tint;
            }
        }

        private void Update()
        {
            if (!_anyTwinkle)
            {
                return;
            }

            int step = HudFrameTwinkle.Step(Time.unscaledTimeAsDouble, Still);
            if (step == _step)
            {
                return;
            }

            _step = step;
            for (int t = 0; t < HudFrameDesign.TileCount; t++)
            {
                if (_tileTwinkles[t])
                {
                    Upload(t);
                }
            }
        }

        /// <summary>Writes tile <paramref name="t"/> into its texture: grid row 0 is the TOP, a texture's row 0 the bottom.</summary>
        private void Upload(int t)
        {
            int[] grid = _grids[t];
            var pixels = _pixels[t];
            const int side = HudFrameDesign.TileSide;
            for (int y = 0; y < side; y++)
            {
                int row = (side - 1 - y) * side;
                for (int x = 0; x < side; x++)
                {
                    pixels[row + x] = ColorOf(grid[(y * side) + x]);
                }
            }

            _textures[t].SetPixels32(pixels);
            _textures[t].Apply(false);
        }

        private Color32 ColorOf(int index)
        {
            if (index <= 0)
            {
                return new Color32(0, 0, 0, 0);
            }

            int slot = _twinkleColorCount > 0 ? HudFrameTwinkle.ColorSlot(_design, index, _step) : -1;
            return slot >= 0 && slot < _twinkleColorCount ? _twinkleColors[slot] : FacePalette.ColorOf(index);
        }

        private void OnDestroy()
        {
            foreach (var texture in _textures)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            Array.Clear(_textures, 0, _textures.Length);
        }
    }
}
