// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "My tools" (#1963): give your drill, your pistol, your blade or your scanner a look of your own. Like the
    /// pixel face and the body paint the look belongs to YOU — it is chosen here in the main menu, stored in the
    /// settings, announced when you enter a world and shown to everyone who sees you. It changes nothing about what
    /// the tool does, and it is not handed over with the tool.
    ///
    /// A look is a small coloured voxel model, painted one horizontal layer at a time (the form editor's way of
    /// working) from a fixed palette of fifteen colours, each of which can glow. The canvas looks down on the tool:
    /// the bottom edge is at the hand, the tool points up the canvas.
    ///
    /// The model (<see cref="ToolLookCanvasModel"/>) is Unity-free and unit-tested; this class is its screen.
    /// </summary>
    public sealed class ToolLookEditor : MonoBehaviour
    {
        public AppShell Shell;

        private const float CellPx = 40f;

        private readonly ToolLookCanvasModel _model = new ToolLookCanvasModel();
        private readonly List<ItemDefinition> _tools = new List<ItemDefinition>();
        private GameContent _content;
        private ItemDefinition _tool;
        private int _color = 3;

        private Canvas _ui;
        private Texture2D _tex;
        private RectTransform _canvasRt, _toolList;
        private PadCanvasFocus _pad;
        private Text _title, _layerLabel, _budgetLabel, _status, _hint, _glowLabel;
        private readonly List<Image> _swatchFrames = new List<Image>();
        private bool _stroking;

        private GameObject _stage, _previewModel;
        private Transform _spin;
        private Camera _previewCamera;
        private RawImage _previewView;
        private bool _previewDirty = true;

        private static string L(string key) => UiKit.L(key);

        // ---------------------------------------------------------------- life cycle

        private void Start()
        {
            _content = Shell != null ? Shell.Content : null;
            if (_content != null)
            {
                foreach (var item in _content.Items.Values)
                {
                    if (item.Tool != null && HeldItemShapes.IsShapedKind(HeldItem.For(_content, item.Key).kind.ToString()))
                    {
                        _tools.Add(item);
                    }
                }

                _tools.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            }

            _tex = new Texture2D(ToolLook.SizeX, ToolLook.SizeZ, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            BuildUi();
            BuildStage();
            Open(_tools.Count > 0 ? _tools[0] : null);
        }

        private void OnDestroy()
        {
            _pad?.Release();
            if (_ui != null)
            {
                Destroy(_ui.gameObject);
            }

            if (_previewCamera != null && _previewCamera.targetTexture != null)
            {
                _previewCamera.targetTexture.Release();
            }

            if (_stage != null)
            {
                Destroy(_stage);
            }

            Destroy(_tex);
        }

        private void Open(ItemDefinition tool)
        {
            _tool = tool;
            _model.New();
            if (tool != null && Shell?.Settings != null)
            {
                _model.Load(Shell.Settings.GetToolLook(tool.Key)); // nothing stored → an empty canvas
            }

            _stroking = false;
            RefreshAll();
            RebuildToolList();
            SetStatus(string.Empty, UiKit.Ok);
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (_canvasRt == null)
            {
                return;
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _spin?.Rotate(0f, (pad ? InputMap.PadLookX() * 90f : 26f) * Time.unscaledDeltaTime, 0f, Space.Self);
            HandleKeys();

            _pad.SetCells(ToolLook.SizeX, ToolLook.SizeZ);
            bool padCanvas = _pad.Tick(pad);
            SetHint(!pad ? "ui.toollook.hint" : padCanvas ? "ui.toollook.hint_pad" : "ui.form.hint_pad_panel");

            bool paint, erase;
            int x, z;
            if (padCanvas)
            {
                if (_pad.LayerStep != 0)
                {
                    StepLayer(_pad.LayerStep);
                }

                if (InputMap.PadDown(PadButton.Rb))
                {
                    DoUndo();
                }

                if (InputMap.PadDown(PadButton.Y))
                {
                    int picked = _model.Get(_pad.CellX, _model.Layer, ToolLook.SizeZ - 1 - _pad.CellY);
                    if (picked != 0)
                    {
                        SetColor(picked);
                    }
                }

                paint = InputMap.PadHeld(PadButton.A);
                erase = InputMap.PadHeld(PadButton.X);
                x = _pad.CellX;
                z = ToolLook.SizeZ - 1 - _pad.CellY; // the tool points UP the canvas
            }
            else
            {
                paint = Input.GetMouseButton(0);
                erase = Input.GetMouseButton(1);
                if (!TryCanvasCell(out x, out z))
                {
                    paint = erase = false;
                }
            }

            if (paint || erase)
            {
                if (!_stroking)
                {
                    _stroking = true;
                    _model.BeginStroke();
                }

                if (_model.Paint(x, z, erase && !paint ? 0 : _color))
                {
                    RenderLayer();
                    _previewDirty = true;
                }
            }
            else if (_stroking)
            {
                _stroking = false;
                _model.EndStroke();
                RefreshLabels();
            }

            if (_previewDirty && !_stroking)
            {
                RebuildPreview();
            }
        }

        private void HandleKeys()
        {
            if (UiKit.TextFieldFocused())
            {
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (shift)
                {
                    DoRedo();
                }
                else
                {
                    DoUndo();
                }
            }
            else if (ctrl && Input.GetKeyDown(KeyCode.Y))
            {
                DoRedo();
            }
            else if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.W))
            {
                StepLayer(1);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.S))
            {
                StepLayer(-1);
            }
        }

        private bool TryCanvasCell(out int x, out int z)
        {
            x = z = 0;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp))
            {
                return false;
            }

            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(lp.x / w) * ToolLook.SizeX), 0, ToolLook.SizeX - 1);
            int fromTop = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(-lp.y / h) * ToolLook.SizeZ), 0, ToolLook.SizeZ - 1);
            z = ToolLook.SizeZ - 1 - fromTop;
            return true;
        }

        // ---------------------------------------------------------------- commands

        private void StepLayer(int delta)
        {
            _model.SetLayer(_model.Layer + delta);
            RenderLayer();
            RefreshLabels();
        }

        private void DoUndo()
        {
            if (_model.Undo())
            {
                RefreshAll();
            }
        }

        private void DoRedo()
        {
            if (_model.Redo())
            {
                RefreshAll();
            }
        }

        private void Do(System.Action change)
        {
            change();
            RefreshAll();
        }

        private void SetColor(int color)
        {
            _color = Mathf.Clamp(color, 1, ToolLook.Palette.Count - 1);
            RefreshLabels();
        }

        private void Save()
        {
            if (_tool == null || Shell?.Settings == null)
            {
                return;
            }

            string look = _model.Encode();
            if (look.Length == 0)
            {
                SetStatus(L(_model.IsEmpty ? "ui.toollook.problem_empty" : "ui.toollook.problem_parts"), UiKit.Warn);
                return;
            }

            if (!Shell.Settings.SetToolLook(_tool.Key, look))
            {
                SetStatus(string.Format(L("ui.toollook.limit"), ToolLook.MaxLooksPerPlayer), UiKit.Warn);
                return;
            }

            Shell.Settings.Save();
            _model.MarkSaved();
            RebuildToolList();
            SetStatus(L("ui.toollook.saved"), UiKit.Ok);
        }

        private void UseStandard()
        {
            if (_tool == null || Shell?.Settings == null)
            {
                return;
            }

            Shell.Settings.SetToolLook(_tool.Key, string.Empty);
            Shell.Settings.Save();
            _model.New();
            RefreshAll();
            RebuildToolList();
            SetStatus(L("ui.toollook.standard_again"), UiKit.Ok);
        }

        private void CopyCode()
        {
            string look = _model.Encode();
            if (look.Length == 0 || _tool == null)
            {
                SetStatus(L(_model.IsEmpty ? "ui.toollook.problem_empty" : "ui.toollook.problem_parts"), UiKit.Warn);
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeToolLook(look, _tool.Key);
            SetStatus(L("ui.tex.code_copied"), UiKit.Ok);
        }

        private void PasteCode()
        {
            if (ShareCode.TryDecodeToolLook(GUIUtility.systemCopyBuffer, out string look, out _) && _model.Load(look))
            {
                RefreshAll();
                SetStatus(L("ui.toollook.pasted"), UiKit.Ok); // on the canvas — "Save" makes it this tool's look
            }
            else
            {
                SetStatus(L("ui.shape.custom.import_failed"), UiKit.Warn);
            }
        }

        private void Close()
        {
            if (Shell != null)
            {
                Shell.CloseToolLookEditor();
            }
        }

        // ---------------------------------------------------------------- rendering

        private static Color PaletteColor(int index)
            => HeldModelPart.TryParseColor(ToolLook.Palette[index], out float r, out float g, out float b) ? new Color(r, g, b, 1f) : Color.magenta;

        private void RenderLayer()
        {
            var background = FacePalette.EditorBackground;
            for (int z = 0; z < ToolLook.SizeZ; z++)
            {
                for (int x = 0; x < ToolLook.SizeX; x++)
                {
                    int here = _model.Get(x, _model.Layer, z);
                    Color c;
                    if (here != 0)
                    {
                        c = PaletteColor(here);
                    }
                    else
                    {
                        int below = _model.Layer > 0 ? _model.Get(x, _model.Layer - 1, z) : 0;
                        c = below != 0 ? Color.Lerp(background, PaletteColor(below), 0.28f) : background;
                    }

                    _tex.SetPixel(x, z, c); // texture row 0 = the hand end, drawn at the bottom
                }
            }

            _tex.Apply();
        }

        private void BuildStage()
        {
            var target = new RenderTexture(512, 512, 16) { name = "ToolLookPreviewRT" };
            _stage = new GameObject("ToolLookStage");
            _stage.transform.position = new Vector3(0f, -8000f, 0f);
            _spin = new GameObject("Spin").transform;
            _spin.SetParent(_stage.transform, false);

            var camGo = new GameObject("ToolLookCam", typeof(Camera));
            camGo.transform.SetParent(_stage.transform, false);
            _previewCamera = camGo.GetComponent<Camera>();
            _previewCamera.orthographic = true;
            _previewCamera.orthographicSize = 0.42f;
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = new Color(0.03f, 0.07f, 0.14f, 1f);
            _previewCamera.targetTexture = target;
            _previewCamera.nearClipPlane = 0.05f;
            _previewCamera.farClipPlane = 12f;
            camGo.transform.localPosition = new Vector3(1.6f, 1.1f, -1.6f);
            camGo.transform.LookAt(_stage.transform.position);
            if (_previewView != null)
            {
                _previewView.texture = target;
            }
        }

        /// <summary>The preview is built by the SAME code that puts the tool into a hand in the world
        /// (<see cref="HeldItem.Build"/>), so what turns here is what the others will see. While the canvas is empty
        /// it shows the standard model — the thing the look would replace.</summary>
        private void RebuildPreview()
        {
            _previewDirty = false;
            if (_spin == null)
            {
                return;
            }

            if (_previewModel != null)
            {
                Destroy(_previewModel);
                _previewModel = null;
            }

            if (_tool == null)
            {
                return;
            }

            var parts = new List<HeldModelPart>();
            foreach (var box in _model.PreviewBoxes())
            {
                float w = (box.X1 - box.X0) * ToolLook.Voxel, h = (box.Y1 - box.Y0) * ToolLook.Voxel, d = (box.Z1 - box.Z0) * ToolLook.Voxel;
                parts.Add(new HeldModelPart
                {
                    P = new[] { ToolLook.OriginX + (box.X0 * ToolLook.Voxel) + (w / 2f), ToolLook.OriginY + (box.Y0 * ToolLook.Voxel) + (h / 2f), ToolLook.OriginZ + (box.Z0 * ToolLook.Voxel) + (d / 2f) },
                    S = new[] { w, h, d },
                    C = ToolLook.Palette[box.Color],
                    G = _model.Glows(box.Color),
                });
            }

            var (kind, tint, _) = HeldItem.For(_content, _tool.Key);
            var model = parts.Count > 0 ? parts : (IReadOnlyList<HeldModelPart>)_tool.HeldModel; // empty canvas → the standard look
            _previewModel = HeldItem.Build(_spin, kind, tint, null, _tool.Key, model);
            if (_previewModel != null)
            {
                _previewModel.transform.localPosition = new Vector3(0f, 0.04f, -0.2f); // centre the length of the tool on the turntable
            }
        }

        // ---------------------------------------------------------------- labels

        private void RefreshAll()
        {
            RenderLayer();
            _previewDirty = true;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (_title != null)
            {
                _title.text = _tool == null ? L("ui.toollook.no_tools")
                    : ToolName(_tool) + (_model.Dirty ? "  *" : string.Empty);
            }

            if (_layerLabel != null)
            {
                _layerLabel.text = string.Format(L("ui.shape.custom.layer"), _model.Layer + 1, ToolLook.SizeY);
            }

            if (_budgetLabel != null)
            {
                int parts = _model.PartsUsed();
                _budgetLabel.text = string.Format(L("ui.toollook.budget"), parts, ToolLook.MaxParts);
                _budgetLabel.color = parts > ToolLook.MaxParts ? new Color(1f, 0.45f, 0.35f) : UiKit.CyanDim;
            }

            for (int i = 0; i < _swatchFrames.Count; i++)
            {
                _swatchFrames[i].color = i + 1 == _color ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            }

            if (_glowLabel != null)
            {
                _glowLabel.text = L(_model.Glows(_color) ? "ui.toollook.glow_on" : "ui.toollook.glow_off");
            }
        }

        private string ToolName(ItemDefinition tool)
        {
            string name = Shell != null && !string.IsNullOrEmpty(tool.NameKey) ? Shell.L(tool.NameKey) : tool.Key;
            return string.IsNullOrEmpty(name) || name == tool.NameKey ? tool.Key : name;
        }

        private void SetStatus(string text, Color color)
        {
            if (_status != null)
            {
                _status.text = text;
                _status.color = color;
            }
        }

        private void SetHint(string key)
        {
            if (_hint != null)
            {
                _hint.text = L(key);
            }
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Tool Look Editor UI");
            _ui.sortingOrder = 5;
            var root = _ui.transform;
            UiKit.AddText(root, 16f, 2f, 1400f, 26f, L("ui.toollook.title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Tools.
            var left = UiKit.AddPanel(root, 16f, 32f, 360f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(left, 14f, 10f, 330f, 24f, L("ui.toollook.my_tools"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var note = UiKit.AddText(left, 14f, 38f, 332f, 62f, L("ui.toollook.note"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            _toolList = UiKit.ScrollList(left, 14f, 106f, 332f, 890f, 4f);

            // Canvas.
            var mid = UiKit.AddPanel(root, 392f, 32f, 700f, 1010f, UiKit.PanelFill).transform;
            _title = UiKit.AddText(mid, 30f, 10f, 640f, 28f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _layerLabel = UiKit.AddText(mid, 30f, 44f, 300f, 28f, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(mid, 360f, 40f, 70f, 36f, "▲", () => StepLayer(1));
            UiKit.AddButton(mid, 438f, 40f, 70f, 36f, "▼", () => StepLayer(-1));

            var canvasGo = new GameObject("LookCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(mid, false);
            _canvasRt = UiKit.Place(canvasGo, 30f, 86f, ToolLook.SizeX * CellPx, ToolLook.SizeZ * CellPx);
            canvasGo.AddComponent<RawImage>().texture = _tex;
            UiKit.AddText(mid, 30f, 86f + (ToolLook.SizeZ * CellPx) + 4f, 320f, 20f, L("ui.toollook.hand_here"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);

            // Palette: fifteen colours, each can glow.
            float px = 380f, py = 86f;
            UiKit.AddText(mid, px, py, 290f, 22f, L("ui.toollook.colours"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            py += 28f;
            for (int i = 1; i < ToolLook.Palette.Count; i++)
            {
                int index = i;
                float sx = px + (((i - 1) % 5) * 58f), sy = py + (((i - 1) / 5) * 58f);
                var frame = UiKit.AddImage(mid, sx, sy, 52f, 52f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.55f));
                var swatch = UiKit.AddImage(frame.transform, 4f, 4f, 44f, 44f, UiKit.SolidSprite, PaletteColor(i));
                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                button.onClick.AddListener(() => SetColor(index));
                _swatchFrames.Add(frame);
            }

            py += (3 * 58f) + 8f;
            UiKit.AddButton(mid, px, py, 290f, 40f, L("ui.toollook.glow_toggle"), () => Do(() => _model.ToggleGlow(_color)));
            py += 44f;
            _glowLabel = UiKit.AddText(mid, px, py, 290f, 22f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            py += 40f;
            UiKit.AddButton(mid, px, py, 290f, 40f, L("ui.shape.custom.copy_below"), () => Do(_model.CopyLayerBelow));
            py += 46f;
            UiKit.AddButton(mid, px, py, 290f, 40f, L("ui.shape.custom.mirror_x"), () => Do(_model.MirrorX));
            py += 46f;
            UiKit.AddButton(mid, px, py, 290f, 40f, L("ui.shape.custom.clear_layer"), () => Do(_model.ClearLayer));
            py += 46f;
            UiKit.AddButton(mid, px, py, 290f, 40f, L("ui.shape.custom.clear_all"), () => Do(_model.ClearAll));
            py += 46f;
            UiKit.AddButton(mid, px, py, 141f, 40f, L("ui.tex.undo"), DoUndo);
            UiKit.AddButton(mid, px + 149f, py, 141f, 40f, L("ui.tex.redo"), DoRedo);
            py += 50f;
            _budgetLabel = UiKit.AddText(mid, px, py, 290f, 24f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Preview + save.
            var right = UiKit.AddPanel(root, 1108f, 32f, 796f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(right, 16f, 10f, 400f, 24f, L("ui.shape.custom.preview"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var view = new GameObject("ToolLookPreviewView", typeof(RectTransform));
            view.transform.SetParent(right, false);
            UiKit.Place(view, 16f, 40f, 600f, 600f);
            _previewView = view.AddComponent<RawImage>();

            float by = 660f;
            UiKit.AddButton(right, 16f, by, 380f, 52f, L("ui.toollook.save"), Save, "btn_singleplayer");
            UiKit.AddButton(right, 404f, by, 376f, 52f, L("ui.toollook.use_standard"), UseStandard);
            by += 60f;
            UiKit.AddButton(right, 16f, by, 186f, 40f, L("ui.tex.copy_code"), CopyCode);
            UiKit.AddButton(right, 210f, by, 186f, 40f, L("ui.tex.paste_code"), PasteCode);
            by += 50f;
            _status = UiKit.AddText(right, 16f, by, 764f, 60f, string.Empty, 14, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            var rules = UiKit.AddText(right, 16f, by + 70f, 764f, 80f, L("ui.toollook.rules"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            rules.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(right, 620f, 1010f - 52f, 160f, 40f, L("ui.menu.back"), Close);

            _hint = UiKit.AddText(root, 16f, 1080f - 30f, 1880f, 24f, L("ui.toollook.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiNav.Enable(_ui.gameObject, padHints: false);
            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
        }

        private void RebuildToolList()
        {
            if (_toolList == null)
            {
                return;
            }

            for (int i = _toolList.childCount - 1; i >= 0; i--)
            {
                Destroy(_toolList.GetChild(i).gameObject);
            }

            foreach (var tool in _tools)
            {
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(_toolList, false);
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(316f, 44f);
                row.AddComponent<LayoutElement>().preferredHeight = 44f;

                var item = tool;
                bool own = Shell?.Settings != null && Shell.Settings.GetToolLook(tool.Key).Length > 0;
                string label = (own ? "★ " : string.Empty) + ToolName(tool);
                var button = UiKit.AddButton(row.transform, 0f, 0f, 314f, 42f, label, () => Open(item), "item_" + tool.Key);
                if (tool == _tool)
                {
                    UiKit.AddOutline(button.targetGraphic);
                }
            }
        }
    }
}
