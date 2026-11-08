// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3382: draws a minigame canvas' label layer (<see cref="Canvas2D.Labels"/>) over the game's surface with the real UI
    /// font (<see cref="UiKit.Font"/>: Rajdhani with the Noto fallbacks), so the localized words of the arcade games read in
    /// every language — the 5×7 bitmap font of the canvas knows only A–Z and digits. The labels sit in canvas pixels; this
    /// layer covers the surface's on-screen rect and scales them with it. The uGUI texts are POOLED: a slot is created the
    /// first time that many labels are shown and only re-laid when its label changed, so a game that redraws every frame
    /// costs no GameObjects and no text layout per frame.
    /// </summary>
    internal sealed class MinigameLabelLayer
    {
        private readonly RectTransform _root;
        private readonly List<Slot> _slots = new List<Slot>();
        private Canvas2D _canvas;
        private int _version = -1;
        private float _x, _y, _w, _h, _scale;

        public MinigameLabelLayer(RectTransform parent)
        {
            var go = new GameObject("MgLabels", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = UiKit.Place(go, 0f, 0f, 0f, 0f);
        }

        /// <summary>Puts the layer over the surface's rect (parent space) — called with every surface layout, so it only
        /// re-lays the labels when the rect really moved.</summary>
        public void Layout(float x, float y, float w, float h, int canvasWidth)
        {
            float scale = canvasWidth > 0 ? w / canvasWidth : 1f;
            if (x == _x && y == _y && w == _w && h == _h && scale == _scale)
            {
                return;
            }

            _x = x;
            _y = y;
            _w = w;
            _h = h;
            _scale = scale;
            UiKit.Place(_root.gameObject, x, y, w, h);
            _version = -1; // the next Sync re-lays every label at the new scale
        }

        /// <summary>Shows the labels of <paramref name="canvas"/> — a no-op while neither the canvas, its labels nor the
        /// layout changed.</summary>
        public void Sync(Canvas2D canvas)
        {
            if (canvas == null || (ReferenceEquals(canvas, _canvas) && canvas.LabelVersion == _version))
            {
                return;
            }

            _canvas = canvas;
            _version = canvas.LabelVersion;
            var labels = canvas.Labels;
            for (int i = 0; i < labels.Count; i++)
            {
                if (i == _slots.Count)
                {
                    _slots.Add(new Slot(_root));
                }

                _slots[i].Show(labels[i], _scale);
            }

            for (int i = labels.Count; i < _slots.Count; i++)
            {
                _slots[i].Hide();
            }
        }

        /// <summary>Hides every label (the host stopped, or a new game is about to start).</summary>
        public void Hide()
        {
            _canvas = null;
            _version = -1;
            foreach (var slot in _slots)
            {
                slot.Hide();
            }
        }

        /// <summary>One pooled uGUI text and the label it shows.</summary>
        private sealed class Slot
        {
            private const int MinFontSize = 9;

            private readonly Text _text;
            private CanvasLabel _shown;
            private float _shownScale = -1f;
            private bool _active;

            public Slot(RectTransform parent)
            {
                var go = new GameObject("MgLabel", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _text = go.AddComponent<Text>();
                _text.font = UiKit.Font;
                _text.fontStyle = FontStyle.Bold; // the bitmap glyphs were heavy strokes — bold keeps the look
                _text.supportRichText = false;    // a "<" in a translation is text, not a tag
                _text.horizontalOverflow = HorizontalWrapMode.Overflow;
                _text.verticalOverflow = VerticalWrapMode.Overflow;
                _text.raycastTarget = false;
            }

            public void Show(CanvasLabel label, float scale)
            {
                if (!_active)
                {
                    _text.gameObject.SetActive(true);
                    _active = true;
                }

                bool sameText = _shownScale == scale && label.Text == _shown.Text && label.Scale == _shown.Scale
                                && label.MaxWidth == _shown.MaxWidth;
                var (bx, by, bw, bh) = label.Box();
                if (!sameText || label.X != _shown.X || label.Y != _shown.Y || label.Align != _shown.Align)
                {
                    UiKit.Place(_text.gameObject, bx * scale, by * scale, bw * scale, bh * scale);
                    _text.alignment = label.Align == LabelAlign.Center ? TextAnchor.MiddleCenter
                        : label.Align == LabelAlign.Right ? TextAnchor.MiddleRight
                        : TextAnchor.MiddleLeft;
                }

                if (!sameText)
                {
                    _text.text = label.Text;
                    int size = Mathf.Max(MinFontSize, Mathf.RoundToInt(label.FontSize(scale)));
                    UiKit.FitLabelSingleLine(_text, bw * scale, Mathf.Min(size, Mathf.Max(MinFontSize, Mathf.RoundToInt(size * 0.55f))), size);
                    _text.verticalOverflow = VerticalWrapMode.Overflow; // the fit truncates; the box is only a centring aid
                }

                _text.color = new Color32(label.Color.R, label.Color.G, label.Color.B, label.Color.A);
                _shown = label;
                _shownScale = scale;
            }

            public void Hide()
            {
                if (_active)
                {
                    _text.gameObject.SetActive(false);
                    _active = false;
                }
            }
        }
    }
}
