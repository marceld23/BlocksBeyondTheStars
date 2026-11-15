// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The material editor's texture card (#3694): the 64×64 tile, the texture editor's paint tools as one bar — brush, fill,
    /// pick a colour, eraser (keys B / G / I / E, as there) — a palette with the base colour, and the whole-tile fills. The
    /// pixels live in a <see cref="PixelCanvasModel"/> (opaque: the eraser paints the base colour, as the right button
    /// always did); a stroke, a fill and a whole-tile fill are each one undo step. A pad paints too
    /// (<see cref="PadCanvasFocus"/>: Start onto the tile, A paints, X erases, Y picks, B back to the buttons).
    /// </summary>
    public sealed partial class MaterialEditor
    {
        /// <summary>The card and the tile on it: 8 screen px per texel.</summary>
        internal const float CardY = EditorShellLayout.HeaderH + EditorShellLayout.Margin;

        internal const float CardH = EditorShellLayout.CanvasH - EditorShellLayout.HeaderH - EditorShellLayout.StatusH
                                     - 2f * EditorShellLayout.Margin;

        internal const float TexX = 16f, TexW = 700f, Pad = 24f, TileSize = 512f, ToolsY = 584f, ToolsH = 48f;

        /// <summary>The palette: 2 rows of 6 swatches, the brush colour and the base colour as big squares beside them.</summary>
        internal const float PaletteY = 648f, SwatchSize = 44f, SwatchStep = 52f, BigSwatch = 96f, BrushSquareX = 344f, BaseSquareX = 456f;

        /// <summary>The whole-tile fills under the palette.</summary>
        internal const float FillsY = 788f;

        /// <summary>The paint tool bar's buttons are named this plus their index.</summary>
        public const string PaintToolName = "PaintTool";

        /// <summary>The brush is 3 × 3 texels (as before).</summary>
        private const int BrushSize = 3;

        // A small preset palette to paint with (plus the live base colour).
        private static readonly Color[] Swatches =
        {
            new Color(0.55f, 0.55f, 0.57f), new Color(0.32f, 0.22f, 0.14f), new Color(0.16f, 0.16f, 0.18f),
            new Color(0.70f, 0.50f, 0.35f), new Color(0.45f, 0.62f, 0.45f), new Color(0.40f, 0.70f, 0.90f),
            new Color(0.85f, 0.78f, 0.45f), new Color(0.80f, 0.30f, 0.25f), new Color(0.55f, 0.35f, 0.75f),
            new Color(0.95f, 0.95f, 0.92f), new Color(0.10f, 0.10f, 0.12f), new Color(0.30f, 0.85f, 0.60f),
        };

        /// <summary>The paint tools, in the bar's order (the mock: brush, fill, colour, eraser).</summary>
        private enum PaintTool
        {
            Brush,
            Fill,
            Pick,
            Eraser,
        }

        private readonly List<Image> _swatchImages = new List<Image>();
        private Texture2D _tex;
        private RectTransform _canvasRt;
        private UiKit.Segmented _toolBar;
        private PaintTool _tool = PaintTool.Brush;
        private Color _paint = Swatches[0];
        private Image _activeSwatch, _brushSquare, _baseSquare;
        private PadCanvasFocus _pad;
        private bool _stroking, _strokeErase;
        private int _lastX = -1, _lastY = -1;
        private bool? _hintPad, _hintCanvas;

        private void BuildTextureCard()
        {
            var card = _shell.AddCard(TexX, CardY, TexW, CardH);
            UiKit.AddKicker(card, Pad, 20f, TexW - 2f * Pad, L("ui.material.texture"));
            _tex = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var canvasGo = new GameObject("PaintCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(card, false);
            _canvasRt = UiKit.Place(canvasGo, (TexW - TileSize) / 2f, 56f, TileSize, TileSize);
            canvasGo.AddComponent<RawImage>().texture = _tex;
            Upload();

            var labels = new[] { L("ui.tex.tool.brush"), L("ui.tex.tool.fill"), L("ui.tex.tool.pipette"), L("ui.tex.tool.eraser") };
            _toolBar = UiKit.AddSegmented(card, Pad, ToolsY, TexW - 2f * Pad, ToolsH, labels, (int)_tool, i => SetTool((PaintTool)i));
            for (int i = 0; i < _toolBar.Buttons.Length; i++)
            {
                // The selected tool is drawn like a primary (the template's tabs, the mock's bar) — a state, not an action.
                _toolBar.Buttons[i].name = PaintToolName + i;
                UiKit.FitButtonLabel(_toolBar.Buttons[i].GetComponentInChildren<Text>(true), 20);
            }

            BuildPalette(card);
            float fillW = (TexW - 2f * Pad - 24f) / 3f;
            DevEditorForm.ActionButton(card, Pad, FillsY, fillW, L("ui.material.fill"), () => FillTile(true), ButtonTier.Quiet);
            DevEditorForm.ActionButton(card, Pad + fillW + 12f, FillsY, fillW, L("ui.material.flat"), () => FillTile(false), ButtonTier.Quiet);
            DevEditorForm.ActionButton(card, Pad + 2f * (fillW + 12f), FillsY, fillW, L("ui.material.clear"), ClearTile, ButtonTier.Quiet);
        }

        private void BuildPalette(Transform card)
        {
            DevEditorForm.LabelAbove(card, Pad, PaletteY, BaseSquareX - Pad - 16f, L("ui.material.brush"));
            DevEditorForm.LabelAbove(card, BaseSquareX, PaletteY, TexW - Pad - BaseSquareX, L("ui.material.base_color"));
            float top = PaletteY + DevEditorForm.AboveH + 6f;
            for (int i = 0; i < Swatches.Length; i++)
            {
                int index = i;
                var swatch = MakeSwatch(card, Pad + (i % 6) * SwatchStep, top + (i / 6) * SwatchStep, SwatchSize, Swatches[i], null);
                swatch.GetComponent<Button>().onClick.AddListener(() => SetPaint(Swatches[index], _swatchImages[index]));
                _swatchImages.Add(swatch);
            }

            _brushSquare = UiKit.AddImage(card, BrushSquareX, top, BigSwatch, BigSwatch, UiKit.SolidSprite, _paint);
            _brushSquare.name = "BrushColour";
            _baseSquare = MakeSwatch(card, BaseSquareX, top, BigSwatch, BaseColor(), () => SetPaint(BaseColor(), _baseSquare));
            _baseSquare.name = "BaseColour";
            SetPaint(_paint, _swatchImages[0]);
        }

        private static Image MakeSwatch(Transform parent, float x, float y, float size, Color color, System.Action onClick)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(UiSound.Click);
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            return img;
        }

        // ---- tools and colours -------------------------------------------------------------------------------------------

        private void SetTool(PaintTool tool)
        {
            _tool = tool;
            _toolBar?.Select((int)tool);
        }

        private void SetPaint(Color c, Image swatch)
        {
            _paint = c;
            if (_activeSwatch != null)
            {
                _activeSwatch.transform.localScale = Vector3.one;
            }

            _activeSwatch = swatch;
            if (_activeSwatch != null)
            {
                _activeSwatch.transform.localScale = Vector3.one * 1.12f;
            }

            if (_brushSquare != null)
            {
                _brushSquare.color = c;
            }
        }

        private Color BaseColor() => new Color32((byte)((_b.colorRgb >> 16) & 255), (byte)((_b.colorRgb >> 8) & 255), (byte)(_b.colorRgb & 255), 255);

        /// <summary>The swatches that follow the definition (the base colour) — part of the form's refresh.</summary>
        private void RefreshPaintUi()
        {
            if (_baseSquare != null)
            {
                _baseSquare.color = BaseColor();
            }
        }

        private static TexelColor Texel(Color c)
            => new TexelColor((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), 255);

        /// <summary>The tile in the base colour; <paramref name="grain"/> adds per-pixel noise and a darker edge (the tiled look).</summary>
        private byte[] BaseFrame(bool grain)
        {
            var frame = new byte[Tile * Tile * 4];
            var baseCol = BaseColor();
            var rng = new System.Random(12345);
            for (int x = 0; x < Tile; x++)
            {
                for (int y = 0; y < Tile; y++)
                {
                    float n = grain ? 0.86f + 0.28f * (float)rng.NextDouble() : 1f;
                    if (grain && (x == 0 || y == 0 || x == Tile - 1 || y == Tile - 1))
                    {
                        n *= 0.7f; // tiled edge
                    }

                    var t = Texel(new Color(baseCol.r * n, baseCol.g * n, baseCol.b * n));
                    int i = ((y * Tile) + x) * 4; // raw RGBA32 rows bottom-up, as the atlas reads them
                    frame[i] = t.R;
                    frame[i + 1] = t.G;
                    frame[i + 2] = t.B;
                    frame[i + 3] = 255;
                }
            }

            return frame;
        }

        /// <summary>Fills the whole tile with the base colour (grainy or flat) — one undo step.</summary>
        private void FillTile(bool grain)
        {
            EndStroke();
            Change(() => _model.Load(new[] { BaseFrame(grain) }, 0));
        }

        /// <summary>Clear: a dark base colour and a flat tile — one undo step (Ctrl+Z brings the work back).</summary>
        private void ClearTile()
        {
            EndStroke();
            Change(() =>
            {
                _b.colorRgb = (20 << 16) | (20 << 8) | 20;
                _model.Load(new[] { BaseFrame(false) }, 0);
            });
        }

        private void Upload()
        {
            if (_tex == null)
            {
                return;
            }

            _tex.LoadRawTextureData(_model.Frames[0]);
            _tex.Apply(false);
        }

        // ---- painting ----------------------------------------------------------------------------------------------------

        private void Update()
        {
            if (_shell == null)
            {
                return;
            }

            if (_shell.IsDialogOpen)
            {
                EndStroke();
                return;
            }

            _form.TickTab();
            HandleToolKeys();
            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _pad.SetCells(Tile, Tile);
            bool padCanvas = _pad.Tick(pad);
            UpdateHint(pad, padCanvas);
            TickPaint(padCanvas);
        }

        private void UpdateHint(bool pad, bool padCanvas)
        {
            if (_hintPad == pad && _hintCanvas == padCanvas)
            {
                return;
            }

            _hintPad = pad;
            _hintCanvas = padCanvas;
            _shell.SetHint(L(!pad ? "ui.material.keys_hint" : padCanvas ? "ui.material.hint_pad" : "ui.material.hint_pad_panel"));
        }

        /// <summary>B / G / I / E pick the tools, as in the texture editor — never while a text field has the keyboard.</summary>
        private void HandleToolKeys()
        {
            if (UiKit.TextFieldFocused())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                SetTool(PaintTool.Brush);
            }
            else if (Input.GetKeyDown(KeyCode.G))
            {
                SetTool(PaintTool.Fill);
            }
            else if (Input.GetKeyDown(KeyCode.I))
            {
                SetTool(PaintTool.Pick);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                SetTool(PaintTool.Eraser);
            }
        }

        private void TickPaint(bool padCanvas)
        {
            bool primary, secondary, primaryDown, secondaryDown, pickDown;
            int x, y;
            if (padCanvas)
            {
                primary = InputMap.PadHeld(PadButton.A);
                secondary = InputMap.PadHeld(PadButton.X);
                primaryDown = InputMap.PadDown(PadButton.A);
                secondaryDown = InputMap.PadDown(PadButton.X);
                pickDown = InputMap.PadDown(PadButton.Y);
                x = _pad.CellX;
                y = _pad.CellY;
            }
            else
            {
                primary = Input.GetMouseButton(0);
                secondary = Input.GetMouseButton(1);
                primaryDown = Input.GetMouseButtonDown(0);
                secondaryDown = Input.GetMouseButtonDown(1);
                pickDown = Input.GetMouseButtonDown(2);
                if (!CanvasCell(out x, out y))
                {
                    if (!primary && !secondary)
                    {
                        EndStroke();
                    }

                    return;
                }
            }

            if (pickDown || (_tool == PaintTool.Pick && primaryDown))
            {
                PickAt(x, y);
            }
            else if (!primary && !secondary)
            {
                EndStroke();
            }
            else if (_stroking)
            {
                ContinueStroke(x, y);
            }
            else if (_tool == PaintTool.Fill && primaryDown)
            {
                FillAt(x, y);
            }
            else if (primaryDown || secondaryDown)
            {
                BeginStroke(x, y, secondaryDown || _tool == PaintTool.Eraser); // the right button (pad: X) always erases
            }
        }

        /// <summary>The texel under the mouse (model coordinates: y down). A drag that started on the tile runs on along its
        /// edge (#3179).</summary>
        private bool CanvasCell(out int x, out int y)
        {
            x = y = -1;
            var mouse = Input.mousePosition;
            if (_canvasRt == null || (!_stroking && !RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, mouse, null))
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out var local))
            {
                return false;
            }

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0]. Every texel the same hit area.
            x = PixelStroke.CellOf(local.x / _canvasRt.rect.width, Tile);
            y = PixelStroke.CellOf(-local.y / _canvasRt.rect.height, Tile);
            return true;
        }

        private TexelColor StrokeColor() => Texel(_strokeErase ? BaseColor() : _paint);

        private void BeginStroke(int x, int y, bool erase)
        {
            _stroking = true;
            _strokeErase = erase;
            _history.Begin(Snapshot());
            _model.BeginEdit();
            _model.Paint(x, y, StrokeColor(), BrushSize);
            _lastX = x;
            _lastY = y;
            Upload();
        }

        private void ContinueStroke(int x, int y)
        {
            if (x == _lastX && y == _lastY)
            {
                return;
            }

            _model.StrokeTo(_lastX, _lastY, x, y, StrokeColor(), BrushSize);
            _lastX = x;
            _lastY = y;
            Upload();
        }

        /// <summary>The stroke is one undo step; a stroke that changed nothing is none.</summary>
        private void EndStroke()
        {
            if (!_stroking)
            {
                return;
            }

            _stroking = false;
            _lastX = _lastY = -1;
            _model.CommitEdit();
            if (_history.Commit(Snapshot()))
            {
                AfterChange();
            }
        }

        private void FillAt(int x, int y) => Change(() => _model.Fill(x, y, Texel(_paint)));

        private void PickAt(int x, int y)
        {
            var t = _model.Get(x, y);
            SetPaint(new Color32(t.R, t.G, t.B, 255), null);
        }

        /// <summary>Tests: one stroke from (x0, y0) to (x1, y1) as the mouse would paint it (model coordinates).</summary>
        public void PaintStrokeForTests(int x0, int y0, int x1, int y1, bool erase = false)
        {
            BeginStroke(x0, y0, erase);
            ContinueStroke(x1, y1);
            EndStroke();
        }
    }
}
