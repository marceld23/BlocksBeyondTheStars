// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's right card (#3693, mock "Textur"): the colour — saturation / value square, hue and alpha bars,
    /// the two swatches, hex and the texture's own colours —, the previews (3-D and tiled 3×3) and "More": hold to see the
    /// original, back to the original (asks first), remove mine (asks first), export / share codes / the folder (desktop
    /// only — hidden in the browser, <see cref="EditorShell.DesktopFeatures"/>) and, for a world admin, publishing for
    /// everyone (removing it asks first).
    /// </summary>
    public sealed partial class TextureEditor
    {
        /// <summary>The colour picker's square and bars.</summary>
        internal const float PickerX = 20f, PickerY = 48f, PickerW = 180f;

        /// <summary>The swatches column right of the picker: swap and hex, the texture's own colours (eight to a row).</summary>
        internal const float SwatchX = 216f, SwapW = 124f, HexW = 148f, TileSwatch = 40f, TileSwatchPitch = 44f;

        /// <summary>The colour-alpha hint under the picker, as wide as the card's inside.</summary>
        internal const float AlphaHintW = 580f;

        /// <summary>The previews: 3-D and tiled side by side.</summary>
        internal const float PreviewSize = 200f, TiledX = 240f;

        /// <summary>"More": three buttons to a row.</summary>
        internal const float MoreButtonW = 188f, MoreGap = 8f;

        private const int TileColourColumns = 8;

        private float _h, _s = 0f, _v = 1f;
        private Texture2D _svTex, _hueTex;
        private RectTransform _svRt, _hueRt, _alphaRt;
        private Image _primarySwatch, _secondarySwatch;
        private InputField _hexField;
        private readonly List<Image> _tileSwatches = new List<Image>();
        private readonly List<TexelColor> _tileColors = new List<TexelColor>();
        private int _dragPicker;          // 0 none, 1 SV square, 2 hue bar, 3 alpha bar
        private Button _publishButton, _unpublishButton;

        private void BuildRightPanel(Transform card)
        {
            Kicker(card, 20f, 16f, 580f, "ui.tex.color");
            float below = BuildColourPicker(card);

            float py = Mathf.Max(352f, below + 10f);
            Kicker(card, PickerX, py, PreviewSize, "ui.tex.preview");
            Kicker(card, TiledX, py, 360f, "ui.tex.tiled");
            var previewGo = new GameObject("Preview3D", typeof(RectTransform));
            previewGo.transform.SetParent(card, false);
            UiKit.Place(previewGo, PickerX, py + 30f, PreviewSize, PreviewSize);
            previewGo.AddComponent<RawImage>().texture = _preview.Target;

            var tiledGo = new GameObject("Tiled", typeof(RectTransform));
            tiledGo.transform.SetParent(card, false);
            UiKit.Place(tiledGo, TiledX, py + 30f, PreviewSize, PreviewSize);
            var tiled = tiledGo.AddComponent<RawImage>();
            tiled.texture = _tex;
            tiled.uvRect = new Rect(0f, 0f, 3f, 3f);
            tiled.raycastTarget = false;

            BuildMore(card, py + 244f);
        }

        /// <summary>Saturation/value square, hue and alpha bars with their hint, swatches, swap, hex and the texture's own
        /// colours. Returns the bottom of the alpha hint.</summary>
        private float BuildColourPicker(Transform card)
        {
            _svTex = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _svRt = AddRaw(card, PickerX, PickerY, PickerW, PickerW, _svTex);
            _hueTex = new Texture2D(96, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var hue = new Color32[96];
            for (int i = 0; i < hue.Length; i++)
            {
                hue[i] = Color.HSVToRGB(i / 95f, 1f, 1f);
            }

            _hueTex.SetPixels32(hue);
            _hueTex.Apply(false);
            _hueRt = AddRaw(card, PickerX, PickerY + PickerW + 8f, PickerW, 22f, _hueTex);
            var alphaBar = UiKit.AddImage(card, PickerX, PickerY + PickerW + 38f, PickerW, 18f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.55f));
            _alphaRt = (RectTransform)alphaBar.transform;

            // Primary / secondary swatch, swap, hex.
            _secondarySwatch = UiKit.AddImage(card, SwatchX + 34f, PickerY + 28f, 56f, 56f, UiKit.SolidSprite, Color.black);
            _primarySwatch = UiKit.AddImage(card, SwatchX, PickerY, 56f, 56f, UiKit.SolidSprite, Color.white);
            TierButton(card, SwatchX + 104f, PickerY, SwapW, RowH, L("ui.tex.swap"), SwapColors);
            _hexField = UiKit.AddInput(card, SwatchX + 104f + SwapW + 8f, PickerY, HexW, RowH, "ffffff", null, "rrggbb", 9, 16);
            _hexField.onEndEdit.AddListener(OnHexEntered);

            // The texture's own colours — a repaint stays in its palette (and a pad can pick from these).
            var tileLabel = UiKit.AddText(card, SwatchX, PickerY + 92f, 384f, 24f, L("ui.tex.tile_colors"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(tileLabel, 384f, UiKit.EditorMinFontSize, 16);
            for (int i = 0; i < 24; i++)
            {
                AddTileSwatch(card, i, SwatchX + ((i % TileColourColumns) * TileSwatchPitch), PickerY + 120f + ((i / TileColourColumns) * TileSwatchPitch));
            }

            // How to make texels see-through — an instruction: normal colour, as many lines as it needs (#3263).
            float hintY = PickerY + 260f;
            UiKit.AddMeasuredText(card, PickerX, hintY, AlphaHintW, L("ui.tex.alpha_bar"), 16, UiKit.TextCol, out float hintH);
            return hintY + hintH;
        }

        private void AddTileSwatch(Transform card, int index, float x, float y)
        {
            var sw = UiKit.AddImage(card, x, y, TileSwatch, TileSwatch, UiKit.SolidSprite, Color.white);
            var btn = sw.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = sw;
            btn.onClick.AddListener(() =>
            {
                if (index < _tileColors.Count)
                {
                    SetPrimary(_tileColors[index]);
                }
            });
            _tileSwatches.Add(sw);
        }

        /// <summary>"More" (mock: Weiteres): the original, removing your own, and the desktop-only and world-only extras. The
        /// permanent ones are red and ask first (#3690).</summary>
        private void BuildMore(Transform card, float y)
        {
            Kicker(card, PickerX, y, 580f, "ui.tex.more");
            y += 32f;
            float Col(int c) => PickerX + (c * (MoreButtonW + MoreGap));
            var before = TierButton(card, Col(0), y, MoreButtonW, RowH, L("ui.tex.before"), null);
            before.gameObject.AddComponent<HoldButton>().OnHold = ShowOfficial;
            TierButton(card, Col(1), y, MoreButtonW, RowH, L("ui.tex.reset"), ConfirmResetToOfficial, ButtonTier.Danger);
            TierButton(card, Col(2), y, MoreButtonW, RowH, L("ui.tex.remove_local"), ConfirmRemoveMine, ButtonTier.Danger);
            y += RowH + 8f;

            if (EditorShell.DesktopFeatures)
            {
                // Desktop only (#3693): the browser has no folder to export to and no clipboard the game can read.
                TierButton(card, Col(0), y, MoreButtonW, RowH, L("ui.tex.export"), ExportForGame);
                TierButton(card, Col(1), y, MoreButtonW, RowH, L("ui.tex.copy_code"), CopyCode);
                TierButton(card, Col(2), y, MoreButtonW, RowH, L("ui.tex.paste_code"), () => _shell.ConfirmDiscard(PasteCode));
                y += RowH + 8f;
                TierButton(card, Col(0), y, MoreButtonW, RowH, L("ui.tex.open_folder"), OpenFolder);
                TierButton(card, Col(1), y, MoreButtonW, RowH, L("ui.tex.reload_folder"), () => _shell.ConfirmDiscard(ReloadFolder));
                y += RowH + 8f;
            }

            if (WorldHost != null)
            {
                _publishButton = TierButton(card, Col(0), y, MoreButtonW, RowH, L("ui.tex.publish"), Publish, ButtonTier.Secondary);
                _unpublishButton = TierButton(card, Col(1), y, MoreButtonW, RowH, L("ui.tex.unpublish"), ConfirmUnpublish, ButtonTier.Danger);
            }
        }

        private static RectTransform AddRaw(Transform parent, float x, float y, float w, float h, Texture tex)
        {
            var go = new GameObject("Raw", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = UiKit.Place(go, x, y, w, h);
            go.AddComponent<RawImage>().texture = tex;
            return rt;
        }

        // ---------------------------------------------------------------- the original

        private void ShowOfficial(bool show)
        {
            if (_showOfficial == show)
            {
                return;
            }

            _showOfficial = show;
            var official = show ? OfficialFrames() : null;
            if (show && official != null)
            {
                Upload(_tex, official.Frames[0]);
            }
            else
            {
                _showOfficial = false;
                UploadActiveFrame();
            }
        }

        // ---------------------------------------------------------------- colours

        private static Color ToColor(TexelColor c) => new Color32(c.R, c.G, c.B, c.A);

        private void SwapColors()
        {
            var p = _primary;
            SetPrimary(_secondary);
            SetSecondary(p);
        }

        private void SetPrimary(TexelColor c)
        {
            _primary = c;
            Color.RGBToHSV(ToColor(c), out _h, out _s, out _v);
            RefreshColorUi();
        }

        private void SetSecondary(TexelColor c)
        {
            _secondary = c;
            RefreshColorUi();
        }

        private void RefreshColorUi()
        {
            if (_primarySwatch != null)
            {
                _primarySwatch.color = ToColor(new TexelColor(_primary.R, _primary.G, _primary.B));
            }

            if (_secondarySwatch != null)
            {
                _secondarySwatch.color = ToColor(new TexelColor(_secondary.R, _secondary.G, _secondary.B));
            }

            if (_hexField != null && !_hexField.isFocused)
            {
                _hexField.SetTextWithoutNotify($"{_primary.R:x2}{_primary.G:x2}{_primary.B:x2}{(_primary.A < 255 ? _primary.A.ToString("x2") : string.Empty)}");
            }

            RepaintSvSquare();
        }

        private void OnHexEntered(string text)
        {
            string hex = (text ?? string.Empty).Trim().TrimStart('#');
            if ((hex.Length == 6 || hex.Length == 8)
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v))
            {
                if (hex.Length == 6)
                {
                    v = (v << 8) | 0xFF;
                }

                SetPrimary(new TexelColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v));
            }
        }

        private void RepaintSvSquare()
        {
            if (_svTex == null)
            {
                return;
            }

            int n = _svTex.width;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    px[(y * n) + x] = Color.HSVToRGB(_h, x / (float)(n - 1), y / (float)(n - 1));
                }
            }

            _svTex.SetPixels32(px);
            _svTex.Apply(false);
        }

        /// <summary>Mouse on the colour controls: the saturation/value square, the hue bar, the alpha bar. Returns true
        /// while it owns the mouse, so a drag that leaves the control does not start painting.</summary>
        private bool UpdateColorPicker()
        {
            if (!Input.GetMouseButton(0))
            {
                _dragPicker = 0;
                return false;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _dragPicker = Over(_svRt) ? 1 : Over(_hueRt) ? 2 : Over(_alphaRt) ? 3 : 0;
            }

            if (_dragPicker == 0)
            {
                return false;
            }

            var rt = _dragPicker == 1 ? _svRt : _dragPicker == 2 ? _hueRt : _alphaRt;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out var lp);
            float u = Mathf.Clamp01(lp.x / rt.rect.width), t = Mathf.Clamp01(1f + (lp.y / rt.rect.height)); // t: 0 bottom … 1 top
            byte a = _primary.A;
            if (_dragPicker == 1)
            {
                _s = u;
                _v = t;
            }
            else if (_dragPicker == 2)
            {
                _h = u;
            }
            else
            {
                a = (byte)Mathf.RoundToInt(u * 255f);
            }

            Color32 rgb = Color.HSVToRGB(_h, _s, _v);
            _primary = new TexelColor(rgb.r, rgb.g, rgb.b, a);
            RefreshColorUi();
            return true;
        }

        private static bool Over(RectTransform rt)
            => rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);

        private void RefreshTileColors()
        {
            _tileColors.Clear();
            _tileColors.AddRange(_model.DominantColors(_tileSwatches.Count));
            for (int i = 0; i < _tileSwatches.Count; i++)
            {
                bool used = i < _tileColors.Count;
                _tileSwatches[i].gameObject.SetActive(used);
                if (used)
                {
                    _tileSwatches[i].color = ToColor(_tileColors[i]);
                }
            }
        }
    }
}
