// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's screen (#1955). Laid out so every label fits in all 14 languages (#3263): the tools sit three
    /// to a row under the canvas, the frame buttons on a row of their own, and undo/redo, flip, shift and the colour
    /// adjustments in a column of the right-hand panel; every button fits its label by <see cref="UiKit.AddFitButton"/>.
    /// The boxes are mirrored by the headless <c>EditorTextFitTests</c> — move a button, update its entry there.
    /// </summary>
    public sealed partial class TextureEditor
    {
        /// <summary>The tool buttons under the canvas: three to a row (#3263).</summary>
        internal const float ToolButtonW = 208f, ToolButtonH = 40f, ToolGap = 8f;

        /// <summary>The frame buttons under the thumbnails.</summary>
        internal const float FrameButtonW = 150f, FrameButtonH = 32f;

        /// <summary>The edit column of the right-hand panel: two buttons to a row.</summary>
        internal const float EditColumnX = 480f, EditButtonW = 150f, EditButtonH = 40f;

        /// <summary>The colour-alpha hint under the colour picker: as wide as the picker and the tile colours together.</summary>
        internal const float AlphaHintW = 456f;

        /// <summary>The swap button and the hex field beside the swatches.</summary>
        internal const float SwapW = 120f;

        /// <summary>The tile's own colours: six to a row, four rows.</summary>
        private const int TileColourColumns = 6;

        /// <summary>The status line above the Submit / Back buttons.</summary>
        internal const float StatusW = 772f;

        private Texture2D _checker;

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Texture Editor UI");
            _ui.sortingOrder = WorldHost != null ? 40 : 5;
            var root = _ui.transform;
            if (WorldHost != null)
            {
                UiKit.AddModalDim(root); // over a running world: the scene behind must not shine through the panels
            }

            UiKit.AddText(root, 16f, 2f, 1400f, 26f, L("ui.tex.title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            BuildBrowser(root);
            BuildCanvasPanel(root);
            BuildRightPanel(root);

            // An instruction, so in the normal text colour (#3263).
            _hint = UiKit.AddText(root, 16f, 1080f - 30f, 1880f, 24f, L("ui.tex.hint"), 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiNav.Enable(_ui.gameObject, padHints: false); // this editor draws its own hint line
            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
            SetTool(Tool.Brush);
            SetBrushSize(1);
            SetPrimary(_primary);
        }

        private void BuildBrowser(Transform root)
        {
            var panel = UiKit.AddPanel(root, 16f, 32f, 360f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(panel, 14f, 10f, 330f, 24f, L("ui.tex.browser"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(panel, 14f, 40f, 332f, 34f, string.Empty, v =>
            {
                _search = v;
                _list?.Rebuild(_search);
            }, L("ui.tex.search"), 40, 15);

            _listContent = UiKit.ScrollList(panel, 14f, 82f, 332f, 914f);
            _rows.Clear();
            foreach (var e in _entries)
            {
                _rows.Add(new EditorPaletteKit.Entry
                {
                    Id = e.Key,
                    Label = e.Label,
                    Kind = "block",
                    Group = e.Group,
                    Color = new Color(0.4f, 0.5f, 0.6f),
                    Icon = e.Block != null ? EditorPaletteKit.TileSprite(_atlas, e.Block.NumericId.Value) : null,
                });
            }

            _list = new PaletteListUi(Shell != null ? Shell : FindAnyObjectByType<AppShell>(), _listContent, _rows);
            bool first = true;
            _list.OnSelected = index =>
            {
                if (first)
                {
                    first = false; // Rebuild reports the initial selection; Start opens it itself
                    return;
                }

                if (index >= 0 && index < _entries.Count && !ReferenceEquals(_entries[index], _entry))
                {
                    Open(_entries[index]);
                }
            };
            _list.Rebuild(string.Empty);
        }

        private void BuildCanvasPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 388f, 32f, 700f, 1010f, UiKit.PanelFill).transform;
            _title = UiKit.AddText(panel, 30f, 8f, 640f, 24f, string.Empty, 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _layerLabel = UiKit.AddText(panel, 30f, 30f, 640f, 20f, string.Empty, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // A checkerboard under the canvas, so see-through texels read as see-through and not as black.
            var checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            checker.SetPixels32(new[] { new Color32(70, 74, 84, 255), new Color32(104, 108, 118, 255), new Color32(104, 108, 118, 255), new Color32(70, 74, 84, 255) });
            checker.Apply(false);
            var checkerGo = new GameObject("Checker", typeof(RectTransform));
            checkerGo.transform.SetParent(panel, false);
            UiKit.Place(checkerGo, 30f, 52f, CanvasPx, CanvasPx);
            var checkerImg = checkerGo.AddComponent<RawImage>();
            checkerImg.texture = checker;
            checkerImg.uvRect = new Rect(0f, 0f, 16f, 16f);
            checkerImg.raycastTarget = false;
            _checker = checker;

            var canvasGo = new GameObject("TextureCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 30f, 52f, CanvasPx, CanvasPx);
            _canvasImg = canvasGo.AddComponent<RawImage>();
            _canvasImg.texture = _tex;

            var slotGo = new GameObject("FaceSlots", typeof(RectTransform));
            slotGo.transform.SetParent(_canvasRt, false);
            _slotLayer = UiKit.Place(slotGo, 0f, 0f, CanvasPx, CanvasPx);

            float y = BuildFrameStrip(panel, 52f + CanvasPx + 8f);
            BuildToolRows(panel, y);
        }

        /// <summary>The frame label, the thumbnails and, on a row of their own, the frame buttons. Returns the next free y.</summary>
        private float BuildFrameStrip(Transform panel, float y)
        {
            _frameLabel = UiKit.AddText(panel, 30f, y, 640f, 20f, string.Empty, 13, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 22f;
            for (int i = 0; i < TextureTiles.MaxFrames; i++)
            {
                int index = i;
                float x = 30f + (i * 60f);
                _thumbFrames[i] = UiKit.AddImage(panel, x - 2f, y - 2f, 56f, 56f, UiKit.SolidSprite, Color.white);
                var go = new GameObject("Frame" + i, typeof(RectTransform));
                go.transform.SetParent(panel, false);
                UiKit.Place(go, x, y, 52f, 52f);
                _thumbImgs[i] = go.AddComponent<RawImage>();
                _thumbImgs[i].texture = _thumbs[i];
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SelectFrame(index));
            }

            y += 58f;
            float x0 = 30f;
            UiKit.AddFitButton(panel, x0, y, FrameButtonW, FrameButtonH, L("ui.tex.frame_add"), AddFrame);
            UiKit.AddFitButton(panel, x0 + FrameButtonW + 6f, y, FrameButtonW, FrameButtonH, L("ui.tex.frame_del"), () => { _model.DeleteFrame(); AfterChange(); });
            float arrows = x0 + (2f * (FrameButtonW + 6f));
            UiKit.AddFitButton(panel, arrows, y, 44f, FrameButtonH, "◀", () => { _model.MoveFrame(-1); AfterChange(); });
            UiKit.AddFitButton(panel, arrows + 50f, y, 44f, FrameButtonH, "▶", () => { _model.MoveFrame(1); AfterChange(); });
            UiKit.AddFitButton(panel, arrows + 100f, y, FrameButtonW, FrameButtonH, L("ui.tex.fps"), CycleFps);
            return y + FrameButtonH + 8f;
        }

        private void AddFrame()
        {
            if (_entry != null && _entry.IsIcon)
            {
                SetStatus(L("ui.tex.icon_one_frame"), UiKit.Warn);
                return;
            }

            if (!_model.DuplicateFrame())
            {
                SetStatus(L("ui.tex.frame_limit"), UiKit.Warn);
            }

            AfterChange();
        }

        /// <summary>The tools, the brush size with the mirrors, and slots / play / fill-all — three to a row (#3263).</summary>
        private void BuildToolRows(Transform panel, float y)
        {
            float Col(int c) => 30f + (c * (ToolButtonW + ToolGap));
            const float step = ToolButtonH + 6f;
            AddToolButton(panel, Col(0), y, Tool.Brush, "ui.tex.tool.brush");
            AddToolButton(panel, Col(1), y, Tool.Eraser, "ui.tex.tool.eraser");
            AddToolButton(panel, Col(2), y, Tool.Fill, "ui.tex.tool.fill");
            y += step;
            AddToolButton(panel, Col(0), y, Tool.Pipette, "ui.tex.tool.pipette");
            AddToolButton(panel, Col(1), y, Tool.Line, "ui.tex.tool.line");
            AddToolButton(panel, Col(2), y, Tool.Rectangle, "ui.tex.tool.rect");
            y += step;

            UiKit.AddFitButton(panel, Col(0), y, 32f, ToolButtonH, "−", () => SetBrushSize(_brushSize - 1));
            _sizeLabel = UiKit.AddText(panel, Col(0) + 36f, y, ToolButtonW - 72f, ToolButtonH, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.AddFitButton(panel, Col(0) + ToolButtonW - 32f, y, 32f, ToolButtonH, "+", () => SetBrushSize(_brushSize + 1));
            var mirrorX = UiKit.AddFitButton(panel, Col(1), y, ToolButtonW, ToolButtonH, L("ui.tex.mirror_x"), null);
            mirrorX.onClick.AddListener(() => { _model.MirrorX = !_model.MirrorX; Tint(mirrorX, _model.MirrorX); });
            var mirrorY = UiKit.AddFitButton(panel, Col(2), y, ToolButtonW, ToolButtonH, L("ui.tex.mirror_y"), null);
            mirrorY.onClick.AddListener(() => { _model.MirrorY = !_model.MirrorY; Tint(mirrorY, _model.MirrorY); });
            y += step;

            var slots = UiKit.AddFitButton(panel, Col(0), y, ToolButtonW, ToolButtonH, L("ui.tex.slots"), null);
            Tint(slots, _showSlots);
            slots.onClick.AddListener(() => { _showSlots = !_showSlots; Tint(slots, _showSlots); RebuildSlotOverlay(); });
            var play = UiKit.AddFitButton(panel, Col(1), y, ToolButtonW, ToolButtonH, L("ui.tex.play"), null);
            play.onClick.AddListener(() =>
            {
                _playing = !_playing && _model.FrameCount > 1;
                _playFrame = _model.ActiveFrame;
                _playClock = 0f;
                Tint(play, _playing);
                UploadActiveFrame();
            });
            UiKit.AddFitButton(panel, Col(2), y, ToolButtonW, ToolButtonH, L("ui.tex.fill_all"), () => { _model.Clear(_primary); AfterChange(); });
        }

        private void AddToolButton(Transform panel, float x, float y, Tool tool, string key)
        {
            var button = UiKit.AddFitButton(panel, x, y, ToolButtonW, ToolButtonH, L(key), () => SetTool(tool));
            _toolButtons[tool] = button.GetComponent<Image>();
        }

        private static void Tint(Button button, bool on)
        {
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = on ? new Color(0.45f, 0.82f, 1f, 1f) : Color.white;
            }
        }

        private void BuildRightPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 1100f, 32f, 804f, 1010f, UiKit.PanelFill).transform;

            // 3-D preview and the tiled 3×3 view (seams!) side by side.
            UiKit.AddText(panel, 16f, 8f, 360f, 22f, L("ui.tex.preview"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var previewGo = new GameObject("Preview3D", typeof(RectTransform));
            previewGo.transform.SetParent(panel, false);
            UiKit.Place(previewGo, 16f, 34f, 360f, 360f);
            previewGo.AddComponent<RawImage>().texture = _preview.Target;

            UiKit.AddText(panel, 408f, 8f, 380f, 22f, L("ui.tex.tiled"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var tiledGo = new GameObject("Tiled", typeof(RectTransform));
            tiledGo.transform.SetParent(panel, false);
            UiKit.Place(tiledGo, 408f, 34f, 360f, 360f);
            var tiled = tiledGo.AddComponent<RawImage>();
            tiled.texture = _tex;
            tiled.uvRect = new Rect(0f, 0f, 3f, 3f);
            tiled.raycastTarget = false;

            float cy = 410f;
            UiKit.AddText(panel, 16f, cy, 300f, 22f, L("ui.tex.color"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            cy += 26f;
            float below = BuildColourPicker(panel, cy);
            BuildEditColumn(panel, cy);
            BuildSaveSection(panel, Mathf.Max(756f, below + 10f));
        }

        /// <summary>Saturation/value square, hue and alpha bars with their hint, swatches, hex and the tile's own colours.
        /// Returns the bottom of the alpha hint.</summary>
        private float BuildColourPicker(Transform panel, float cy)
        {
            _svTex = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _svRt = AddRaw(panel, 16f, cy, 220f, 220f, _svTex);
            _hueTex = new Texture2D(96, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var hue = new Color32[96];
            for (int i = 0; i < hue.Length; i++)
            {
                hue[i] = Color.HSVToRGB(i / 95f, 1f, 1f);
            }

            _hueTex.SetPixels32(hue);
            _hueTex.Apply(false);
            _hueRt = AddRaw(panel, 16f, cy + 228f, 220f, 22f, _hueTex);
            var alphaBar = UiKit.AddImage(panel, 16f, cy + 256f, 220f, 18f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.55f));
            _alphaRt = (RectTransform)alphaBar.transform;
            // How to make texels see-through — an instruction: 13 px, normal colour, as many lines as it needs (#3263).
            UiKit.AddMeasuredText(panel, 16f, cy + 278f, AlphaHintW, L("ui.tex.alpha_bar"), 13, UiKit.TextCol, out float hintH);

            // Primary / secondary swatch, swap, hex.
            float sx = 252f;
            _secondarySwatch = UiKit.AddImage(panel, sx + 34f, cy + 28f, 56f, 56f, UiKit.SolidSprite, Color.black);
            _primarySwatch = UiKit.AddImage(panel, sx, cy, 56f, 56f, UiKit.SolidSprite, Color.white);
            UiKit.AddFitButton(panel, sx + 100f, cy, SwapW, 32f, L("ui.tex.swap"), SwapColors);
            _hexField = UiKit.AddInput(panel, sx + 100f, cy + 38f, SwapW, 30f, "ffffff", null, "rrggbb", 9, 14);
            _hexField.onEndEdit.AddListener(OnHexEntered);

            // The tile's own colours — a repaint stays in the tile's palette (and a pad can pick from these).
            UiKit.AddText(panel, sx, cy + 94f, 220f, 20f, L("ui.tex.tile_colors"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            for (int i = 0; i < 24; i++)
            {
                int index = i;
                var sw = UiKit.AddImage(panel, sx + ((i % TileColourColumns) * 34f), cy + 118f + ((i / TileColourColumns) * 34f), 30f, 30f,
                    UiKit.SolidSprite, Color.white);
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

            return cy + 278f + hintH;
        }

        /// <summary>Undo/redo, flip, shift and the colour adjustments: two to a row beside the colour picker (#3263).</summary>
        private void BuildEditColumn(Transform panel, float y)
        {
            const float x0 = EditColumnX, x1 = EditColumnX + EditButtonW + 8f, step = EditButtonH + 4f;
            UiKit.AddFitButton(panel, x0, y, EditButtonW, EditButtonH, L("ui.tex.undo"), DoUndo);
            UiKit.AddFitButton(panel, x1, y, EditButtonW, EditButtonH, L("ui.tex.redo"), DoRedo);
            y += step;
            UiKit.AddFitButton(panel, x0, y, EditButtonW, EditButtonH, L("ui.tex.flip_h"), () => { _model.Flip(true); AfterChange(); });
            UiKit.AddFitButton(panel, x1, y, EditButtonW, EditButtonH, L("ui.tex.flip_v"), () => { _model.Flip(false); AfterChange(); });
            y += step;
            const float arrowW = ((2f * EditButtonW) + 8f - 18f) / 4f;
            UiKit.AddFitButton(panel, x0, y, arrowW, EditButtonH, "←", () => { _model.Shift(-8, 0); AfterChange(); });
            UiKit.AddFitButton(panel, x0 + arrowW + 6f, y, arrowW, EditButtonH, "→", () => { _model.Shift(8, 0); AfterChange(); });
            UiKit.AddFitButton(panel, x0 + (2f * (arrowW + 6f)), y, arrowW, EditButtonH, "↑", () => { _model.Shift(0, -8); AfterChange(); });
            UiKit.AddFitButton(panel, x0 + (3f * (arrowW + 6f)), y, arrowW, EditButtonH, "↓", () => { _model.Shift(0, 8); AfterChange(); });
            y += step;
            UiKit.AddFitButton(panel, x0, y, EditButtonW, EditButtonH, L("ui.tex.brighter"), () => { _model.Adjust(0.06f, 0f, 0f); AfterChange(); });
            UiKit.AddFitButton(panel, x1, y, EditButtonW, EditButtonH, L("ui.tex.darker"), () => { _model.Adjust(-0.06f, 0f, 0f); AfterChange(); });
            y += step;
            UiKit.AddFitButton(panel, x0, y, EditButtonW, EditButtonH, L("ui.tex.contrast_up"), () => { _model.Adjust(0f, 0.12f, 0f); AfterChange(); });
            UiKit.AddFitButton(panel, x1, y, EditButtonW, EditButtonH, L("ui.tex.contrast_down"), () => { _model.Adjust(0f, -0.12f, 0f); AfterChange(); });
            y += step;
            UiKit.AddFitButton(panel, x0, y, EditButtonW, EditButtonH, L("ui.tex.saturate"), () => { _model.Adjust(0f, 0f, 0.15f); AfterChange(); });
            UiKit.AddFitButton(panel, x1, y, EditButtonW, EditButtonH, L("ui.tex.desaturate"), () => { _model.Adjust(0f, 0f, -0.15f); AfterChange(); });
        }

        /// <summary>Use for me / remove / export, reset / before / code, folder / publish, the status line, Submit and Back.</summary>
        private void BuildSaveSection(Transform panel, float by)
        {
            UiKit.AddText(panel, 16f, by, 770f, 22f, L("ui.tex.save_title"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            by += 26f;
            UiKit.AddFitButton(panel, 16f, by, 250f, 38f, L("ui.tex.save_local"), SaveForMe);
            UiKit.AddFitButton(panel, 274f, by, 250f, 38f, L("ui.tex.remove_local"), RemoveMine);
            UiKit.AddFitButton(panel, 532f, by, 256f, 38f, L("ui.tex.export"), ExportForGame);
            by += 42f;
            UiKit.AddFitButton(panel, 16f, by, 250f, 34f, L("ui.tex.reset"), ResetToOfficial);
            var before = UiKit.AddFitButton(panel, 274f, by, 250f, 34f, L("ui.tex.before"), null);
            var hold = before.gameObject.AddComponent<HoldButton>();
            hold.OnHold = ShowOfficial;
            UiKit.AddFitButton(panel, 532f, by, 126f, 34f, L("ui.tex.copy_code"), CopyCode);
            UiKit.AddFitButton(panel, 662f, by, 126f, 34f, L("ui.tex.paste_code"), PasteCode);
            by += 38f;
            UiKit.AddFitButton(panel, 16f, by, 250f, 34f, L("ui.tex.open_folder"), OpenFolder);
            UiKit.AddFitButton(panel, 274f, by, 250f, 34f, L("ui.tex.reload_folder"), ReloadFolder);
            _publishButton = UiKit.AddFitButton(panel, 532f, by, 126f, 34f, L("ui.tex.publish"), Publish);
            _unpublishButton = UiKit.AddFitButton(panel, 662f, by, 126f, 34f, L("ui.tex.unpublish"), Unpublish);
            by += 40f;

            _status = UiKit.AddText(panel, 16f, by, StatusW, 1010f - 54f - by, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddFitButton(panel, 628f, 1010f - 52f, 160f, 40f, L("ui.menu.back"), Close);
            // Offer the texture for the game itself (#1965) — its own dialog with the three consents.
            UiKit.AddFitButton(panel, 398f, 1010f - 52f, 222f, 40f, L("ui.tex.submit.open"), OpenSubmit, "btn_feedback");
        }

        private static RectTransform AddRaw(Transform parent, float x, float y, float w, float h, Texture tex)
        {
            var go = new GameObject("Raw", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = UiKit.Place(go, x, y, w, h);
            go.AddComponent<RawImage>().texture = tex;
            return rt;
        }
    }
}
