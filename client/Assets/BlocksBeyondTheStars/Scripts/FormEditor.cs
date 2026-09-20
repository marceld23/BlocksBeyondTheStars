// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The form editor of the main menu (#1960): design the forms you later place in a world — in peace, with a
    /// large canvas, undo, a material and dye preview, a gamepad, and a library you can tidy up. In the world
    /// nothing changes: the form tool and its small editor stay as they are, and BOTH write the same local
    /// library (<see cref="CustomShapeLibrary"/>), so a form designed here appears under "My forms" there.
    ///
    /// What only this editor can do is forms over several blocks (#1961): a wardrobe two blocks high, a table two
    /// blocks long. The footprint steppers grow the canvas; the layer canvas then shows the whole form from above
    /// with the block borders drawn in, and the budget line counts per block — exactly the rule the server checks.
    ///
    /// The model (<see cref="FormCanvasModel"/>) is Unity-free and unit-tested; this class is its screen.
    /// </summary>
    public sealed class FormEditor : MonoBehaviour
    {
        public AppShell Shell;

        private const float CanvasPx = 640f;

        private readonly FormCanvasModel _model = new FormCanvasModel();
        private GameContent _content;
        private string _name = string.Empty;
        private string _loadedName = string.Empty;   // the library entry on the canvas ("" = a new form)

        private Canvas _ui;
        private Texture2D _tex;
        private RectTransform _canvasRt;
        private RawImage _canvasImg;
        private PadCanvasFocus _pad;
        private InputField _nameInput;
        private Text _layerLabel, _budgetLabel, _problemLabel, _footprintLabel, _status, _hint, _materialLabel;
        private RectTransform _libraryContent;
        private Button _gridButton;
        private bool _stroking;

        // 3-D preview.
        private GameObject _stage;
        private Transform _spin;
        private MeshFilter _previewFilter;
        private Material _previewMaterial;
        private Texture2D _previewTile;
        private Mesh _previewMesh;
        private Camera _previewCamera;
        private readonly List<BlockDefinition> _materials = new List<BlockDefinition>();
        private int _materialIndex;
        private int _dyeIndex; // 0 = undyed
        private bool _previewDirty = true;

        private static readonly Color[] Dyes =
        {
            Color.white, new Color(0.85f, 0.25f, 0.22f), new Color(0.95f, 0.62f, 0.18f), new Color(0.93f, 0.85f, 0.30f),
            new Color(0.35f, 0.72f, 0.32f), new Color(0.25f, 0.55f, 0.90f), new Color(0.58f, 0.36f, 0.80f), new Color(0.20f, 0.20f, 0.24f),
        };

        private static string L(string key) => UiKit.L(key);

        // ---------------------------------------------------------------- life cycle

        private void Start()
        {
            _content = Shell != null ? Shell.Content : null;
            if (_content != null)
            {
                foreach (var block in _content.Blocks.Values)
                {
                    if (block.Shapeable && GameTextures.Resolve(block.Key) != null)
                    {
                        _materials.Add(block);
                    }
                }

                _materials.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                _materialIndex = Mathf.Max(0, _materials.FindIndex(b => b.Key == "planks"));
            }

            BuildUi();
            BuildStage();
            RebuildCanvasTexture();
            RefreshAll();
        }

        private void OnDestroy()
        {
            _pad?.Release();
            if (_ui != null)
            {
                Destroy(_ui.gameObject);
            }

            if (_stage != null)
            {
                Destroy(_stage);
            }

            if (_previewCamera != null && _previewCamera.targetTexture != null)
            {
                _previewCamera.targetTexture.Release();
            }

            Destroy(_tex);
            Destroy(_previewTile);
            Destroy(_previewMesh);
            Destroy(_previewMaterial);
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (_canvasRt == null)
            {
                return;
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            _spin?.Rotate(0f, (pad ? InputMap.PadLookX() * 90f : 22f) * Time.unscaledDeltaTime, 0f, Space.Self);

            HandleKeys();
            _pad.SetCells(_model.SizeX, _model.SizeZ);
            bool padCanvas = _pad.Tick(pad);
            SetHint(!pad ? "ui.form.hint" : padCanvas ? "ui.form.hint_pad" : "ui.form.hint_pad_panel");

            bool add, remove;
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

                add = InputMap.PadHeld(PadButton.A);
                remove = InputMap.PadHeld(PadButton.X);
                x = _pad.CellX;
                z = _model.SizeZ - 1 - _pad.CellY; // the canvas looks from above: its top row is the far edge (+Z)
            }
            else
            {
                add = Input.GetMouseButton(0);
                remove = Input.GetMouseButton(1);
                if (!TryCanvasCell(out x, out z))
                {
                    add = remove = false;
                }
            }

            if (add || remove)
            {
                if (!_stroking)
                {
                    _stroking = true;
                    _model.BeginStroke();
                }

                if (_model.Paint(x, z, add && !remove))
                {
                    RenderLayer();
                    _previewDirty = true;
                    RefreshLabels();
                }
            }
            else if (_stroking)
            {
                _stroking = false;
                _model.EndStroke();
            }

            if (_previewDirty && !_stroking)
            {
                RebuildPreview(); // once per stroke, not once per cell — merging eight blocks is not free
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

            // Place() anchors top-left with pivot (0,1): local x ∈ [0,w], y ∈ [-h,0].
            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(lp.x / w) * _model.SizeX), 0, _model.SizeX - 1);
            int fromTop = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(-lp.y / h) * _model.SizeZ), 0, _model.SizeZ - 1);
            z = _model.SizeZ - 1 - fromTop;
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
                AfterStructuralChange();
            }
        }

        private void DoRedo()
        {
            if (_model.Redo())
            {
                AfterStructuralChange();
            }
        }

        private void Do(Action change)
        {
            change();
            AfterStructuralChange();
        }

        /// <summary>After anything that may have changed the canvas SIZE (footprint, grid, undo across them).</summary>
        private void AfterStructuralChange()
        {
            RebuildCanvasTexture();
            RefreshAll();
        }

        private void StepFootprint(int dw, int dh, int dl)
        {
            if (_model.SetFootprint(_model.Width + dw, _model.Height + dh, _model.Length + dl))
            {
                AfterStructuralChange();
                SetStatus(string.Empty, UiKit.Ok);
            }
            else
            {
                SetStatus(string.Format(L("ui.form.footprint_limit"), CustomShape.MaxFootprint, CustomShape.MaxCells), UiKit.Warn);
            }
        }

        private void ToggleGrid()
        {
            if (_model.SetGrid(_model.Grid == CustomShape.GridLarge ? CustomShape.GridSmall : CustomShape.GridLarge))
            {
                AfterStructuralChange();
            }
        }

        private void NewForm()
        {
            _model.New();
            _name = _loadedName = string.Empty;
            if (_nameInput != null)
            {
                _nameInput.SetTextWithoutNotify(string.Empty);
            }

            AfterStructuralChange();
            SetStatus(string.Empty, UiKit.Ok);
        }

        private void LoadEntry(string name, string voxels)
        {
            if (!_model.Load(voxels))
            {
                SetStatus(L("ui.form.load_failed"), UiKit.Warn);
                return;
            }

            _name = _loadedName = name ?? string.Empty;
            if (_nameInput != null)
            {
                _nameInput.SetTextWithoutNotify(_name);
            }

            AfterStructuralChange();
            SetStatus(string.Empty, UiKit.Ok);
        }

        private void Save()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), UiKit.Warn);
                return;
            }

            string name = (_name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                SetStatus(L("ui.form.need_name"), UiKit.Warn);
                return;
            }

            // Saving under a NEW name while an entry is open renames it (a trail of near-duplicates is what
            // "Duplicate" is for); saving under the name of another entry overwrites that one, as in the world.
            if (_loadedName.Length > 0 && !string.Equals(_loadedName, name, StringComparison.OrdinalIgnoreCase))
            {
                CustomShapeLibrary.Delete(_loadedName);
            }

            CustomShapeLibrary.Save(voxels, name);
            _loadedName = name;
            _model.MarkSaved();
            RebuildLibrary();
            RefreshLabels();
            SetStatus(string.Format(L("ui.form.saved"), name), UiKit.Ok);
        }

        private void Duplicate()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), UiKit.Warn);
                return;
            }

            string baseName = ((_name ?? string.Empty).Trim().Length > 0 ? _name.Trim() : L("ui.form.unnamed"));
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in CustomShapeLibrary.List())
            {
                taken.Add(e.Name);
            }

            string copy = baseName;
            for (int n = 2; taken.Contains(copy) && n < 100; n++)
            {
                string suffix = " " + n;
                copy = (baseName.Length + suffix.Length > 24 ? baseName.Substring(0, 24 - suffix.Length) : baseName) + suffix;
            }

            CustomShapeLibrary.Save(voxels, copy);
            _name = _loadedName = copy;
            _nameInput?.SetTextWithoutNotify(copy);
            _model.MarkSaved();
            RebuildLibrary();
            SetStatus(string.Format(L("ui.form.saved"), copy), UiKit.Ok);
        }

        private void DeleteEntry(string name)
        {
            CustomShapeLibrary.Delete(name);
            if (string.Equals(_loadedName, name, StringComparison.OrdinalIgnoreCase))
            {
                _loadedName = string.Empty; // the canvas keeps the form — saving it again brings it back
            }

            RebuildLibrary();
            SetStatus(string.Format(L("ui.form.deleted"), name), UiKit.Ok);
        }

        private void CopyCode()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), UiKit.Warn);
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeForm(voxels, _name ?? string.Empty);
            SetStatus(L("ui.shape.custom.exported"), UiKit.Ok);
        }

        private void PasteCode()
        {
            if (ShareCode.TryDecodeForm(GUIUtility.systemCopyBuffer, out string voxels, out string name))
            {
                LoadEntry(name, voxels);
                _loadedName = string.Empty; // an import is a new form until it is saved
                SetStatus(L("ui.shape.custom.imported"), UiKit.Ok);
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
                Shell.CloseFormEditor();
            }
        }

        private string ProblemText()
        {
            switch (_model.Problem(out int cell))
            {
                case FormProblem.Empty: return L("ui.form.problem_empty");
                case FormProblem.SolidCubes: return L("ui.form.problem_solid");
                case FormProblem.EmptyCell: return string.Format(L("ui.form.problem_empty_cell"), cell + 1);
                case FormProblem.TooDetailed: return string.Format(L("ui.form.problem_detailed"), cell + 1, CustomShape.MaxBoxes);
                default: return string.Empty;
            }
        }

        // ---------------------------------------------------------------- canvas

        private void RebuildCanvasTexture()
        {
            if (_tex != null)
            {
                Destroy(_tex);
            }

            _tex = new Texture2D(_model.SizeX, _model.SizeZ, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            if (_canvasImg != null)
            {
                _canvasImg.texture = _tex;
            }

            // The canvas keeps the footprint's proportions inside its square frame.
            if (_canvasRt != null)
            {
                float longest = Mathf.Max(_model.SizeX, _model.SizeZ);
                float w = CanvasPx * _model.SizeX / longest, h = CanvasPx * _model.SizeZ / longest;
                _canvasRt.sizeDelta = new Vector2(w, h);
                _canvasRt.anchoredPosition = new Vector2((CanvasPx - w) * 0.5f, -(CanvasPx - h) * 0.5f);
                _pad?.Rebind(_canvasRt);
            }
        }

        /// <summary>The current layer from above, the layer below shining through dimly, and the block borders of
        /// a form over several blocks as a lighter grid — so it is always clear which block a cell belongs to.</summary>
        private void RenderLayer()
        {
            if (_tex == null)
            {
                return;
            }

            var background = FacePalette.EditorBackground;
            var other = new Color(background.r * 1.35f + 0.02f, background.g * 1.35f + 0.02f, background.b * 1.35f + 0.03f, 1f);
            var below = new Color(UiKit.Cyan.r * 0.32f, UiKit.Cyan.g * 0.32f, UiKit.Cyan.b * 0.32f, 1f);
            for (int z = 0; z < _model.SizeZ; z++)
            {
                for (int x = 0; x < _model.SizeX; x++)
                {
                    Color c;
                    if (_model.Get(x, _model.Layer, z))
                    {
                        c = UiKit.Cyan;
                    }
                    else if (_model.Layer > 0 && _model.Get(x, _model.Layer - 1, z))
                    {
                        c = below;
                    }
                    else
                    {
                        // a checkerboard of BLOCKS, not of cells: neighbouring blocks differ in shade
                        bool odd = (((x / _model.Grid) + (z / _model.Grid)) & 1) == 1;
                        c = odd ? other : background;
                    }

                    _tex.SetPixel(x, z, c); // texture row 0 is the bottom = the near edge (z 0)
                }
            }

            _tex.Apply();
        }

        // ---------------------------------------------------------------- preview

        private void BuildStage()
        {
            var target = new RenderTexture(512, 512, 16) { name = "FormPreviewRT" };
            _stage = new GameObject("FormEditorStage");
            _stage.transform.position = new Vector3(0f, -7000f, 0f); // parked far from the menu backdrop
            _spin = new GameObject("Spin").transform;
            _spin.SetParent(_stage.transform, false);

            var model = new GameObject("Form", typeof(MeshFilter), typeof(MeshRenderer));
            model.transform.SetParent(_spin, false);
            _previewFilter = model.GetComponent<MeshFilter>();
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Texture");
            _previewMaterial = new Material(shader) { color = Color.white };
            if (_previewMaterial.HasProperty("_Floor"))
            {
                _previewMaterial.SetFloat("_Floor", 0.5f);
            }

            model.GetComponent<MeshRenderer>().sharedMaterial = _previewMaterial;

            var camGo = new GameObject("FormEditorCam", typeof(Camera));
            camGo.transform.SetParent(_stage.transform, false);
            _previewCamera = camGo.GetComponent<Camera>();
            _previewCamera.orthographic = true;
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = new Color(0.03f, 0.07f, 0.14f, 1f);
            _previewCamera.targetTexture = target;
            _previewCamera.nearClipPlane = 0.05f;
            _previewCamera.farClipPlane = 40f;
            if (_previewView != null)
            {
                _previewView.texture = target;
            }

            ApplyMaterial();
        }

        private RawImage _previewView;

        private void ApplyMaterial()
        {
            if (_previewMaterial == null)
            {
                return;
            }

            Destroy(_previewTile);
            _previewTile = null;
            string label = "—";
            if (_materials.Count > 0)
            {
                var block = _materials[Mathf.Clamp(_materialIndex, 0, _materials.Count - 1)];
                _previewTile = GameTextures.LoadTileTexture(block.Key);
                label = Shell != null && !string.IsNullOrEmpty(block.NameKey) ? Shell.L(block.NameKey) : block.Key;
            }

            _previewMaterial.mainTexture = _previewTile;
            _previewMaterial.color = Dyes[Mathf.Clamp(_dyeIndex, 0, Dyes.Length - 1)];
            if (_materialLabel != null)
            {
                _materialLabel.text = label;
            }
        }

        /// <summary>Rebuilds the preview from the SAME merged boxes the world mesher draws — also for a form that
        /// is not valid yet, because a half-drawn form must still show.</summary>
        private void RebuildPreview()
        {
            _previewDirty = false;
            if (_previewFilter == null)
            {
                return;
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var (cx, cy, cz, bitmap) in _model.CellBitmaps())
            {
                var offset = new Vector3(cx, cy, cz);
                foreach (var box in CustomShape.Merge(bitmap))
                {
                    float g = box.Grid;
                    AddBox(verts, uvs, tris,
                        offset + new Vector3(box.X0 / g, box.Y0 / g, box.Z0 / g),
                        offset + new Vector3(box.X1 / g, box.Y1 / g, box.Z1 / g));
                }
            }

            Destroy(_previewMesh);
            _previewMesh = new Mesh { name = "FormEditorPreview" };
            _previewMesh.SetVertices(verts);
            _previewMesh.SetUVs(0, uvs);
            _previewMesh.SetTriangles(tris, 0);
            _previewMesh.RecalculateNormals();
            _previewMesh.RecalculateBounds();
            _previewFilter.sharedMesh = _previewMesh;

            // Centre the footprint on the turntable and frame it, whatever its size.
            var size = new Vector3(_model.Width, _model.Height, _model.Length);
            _previewFilter.transform.localPosition = -size * 0.5f;
            float reach = size.magnitude * 0.62f + 0.35f;
            _previewCamera.orthographicSize = reach;
            _previewCamera.transform.localPosition = new Vector3(1f, 0.8f, -1f).normalized * (reach * 3f + 2f);
            _previewCamera.transform.LookAt(_stage.transform.position);
        }

        /// <summary>Six outward quads with planar texture coordinates in BLOCK units — the tile repeats once per
        /// block, exactly as the form shows its material in the world.</summary>
        private static void AddBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 lo, Vector3 hi)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int axis)
            {
                int i = verts.Count;
                foreach (var p in new[] { a, b, c, d })
                {
                    verts.Add(p);
                    uvs.Add(axis == 1 ? new Vector2(p.x, p.z) : axis == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                }

                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Quad(new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z), 1);
            Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), 1);
            Quad(new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, lo.y, hi.z), 0);
            Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, lo.y, lo.z), 0);
            Quad(new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, lo.y, hi.z), 2);
            Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, lo.y, lo.z), 2);
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
            if (_layerLabel != null)
            {
                _layerLabel.text = string.Format(L("ui.shape.custom.layer"), _model.Layer + 1, _model.SizeY);
            }

            if (_footprintLabel != null)
            {
                _footprintLabel.text = string.Format(L("ui.form.footprint"), _model.Width, _model.Height, _model.Length, _model.Cells);
            }

            if (_budgetLabel != null)
            {
                int boxes = _model.MaxBoxesUsed();
                _budgetLabel.text = string.Format(L(_model.Cells > 1 ? "ui.form.budget_per_block" : "ui.shape.custom.budget"), boxes, CustomShape.MaxBoxes);
                _budgetLabel.color = boxes > CustomShape.MaxBoxes ? new Color(1f, 0.45f, 0.35f) : UiKit.CyanDim;
            }

            if (_problemLabel != null)
            {
                _problemLabel.text = ProblemText();
            }

            if (_gridButton != null)
            {
                _gridButton.interactable = _model.Cells == 1; // a form over several blocks is always fine-grained
            }
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
            _ui = UiKit.CreateCanvas("Form Editor UI");
            _ui.sortingOrder = 5;
            var root = _ui.transform;
            UiKit.AddText(root, 16f, 2f, 1400f, 26f, L("ui.form.title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            BuildLibraryPanel(root);
            BuildCanvasPanel(root);
            BuildRightPanel(root);

            _hint = UiKit.AddText(root, 16f, 1080f - 30f, 1880f, 24f, L("ui.form.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiNav.Enable(_ui.gameObject, padHints: false); // this editor draws its own hint line
            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
        }

        private void BuildLibraryPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 16f, 32f, 360f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(panel, 14f, 10f, 330f, 24f, L("ui.shape.custom.library"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, 14f, 42f, 162f, 40f, L("ui.form.new"), NewForm);
            UiKit.AddButton(panel, 184f, 42f, 162f, 40f, L("ui.form.duplicate"), Duplicate);
            UiKit.AddButton(panel, 14f, 88f, 162f, 40f, L("ui.shape.custom.export"), CopyCode);
            UiKit.AddButton(panel, 184f, 88f, 162f, 40f, L("ui.shape.custom.import"), PasteCode);
            _libraryContent = UiKit.ScrollList(panel, 14f, 138f, 332f, 858f, 4f);
            RebuildLibrary();
        }

        /// <summary>EVERY saved form (the in-world editor shows the first fourteen), each with its size and a
        /// delete button — the library had no way to lose a form before.</summary>
        private void RebuildLibrary()
        {
            if (_libraryContent == null)
            {
                return;
            }

            for (int i = _libraryContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_libraryContent.GetChild(i).gameObject);
            }

            foreach (var entry in CustomShapeLibrary.List())
            {
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(_libraryContent, false);
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(316f, 44f);
                row.AddComponent<LayoutElement>().preferredHeight = 44f;

                string name = entry.Name, voxels = entry.Voxels;
                int cells = CustomShape.CellCount(voxels);
                string label = cells > 1 ? name + "  · " + string.Format(L("ui.form.blocks"), cells) : name;
                UiKit.AddButton(row.transform, 0f, 0f, 262f, 42f, label, () => LoadEntry(name, voxels));
                UiKit.AddButton(row.transform, 268f, 0f, 46f, 42f, "✕", () => DeleteEntry(name));
            }
        }

        private void BuildCanvasPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 392f, 32f, 700f, 1010f, UiKit.PanelFill).transform;
            _layerLabel = UiKit.AddText(panel, 30f, 12f, 320f, 30f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, 400f, 8f, 80f, 38f, "▲", () => StepLayer(1));
            UiKit.AddButton(panel, 488f, 8f, 80f, 38f, "▼", () => StepLayer(-1));

            var frame = new GameObject("CanvasFrame", typeof(RectTransform));
            frame.transform.SetParent(panel, false);
            UiKit.Place(frame, 30f, 56f, CanvasPx, CanvasPx);
            frame.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var canvasGo = new GameObject("LayerCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(frame.transform, false);
            _canvasRt = UiKit.Place(canvasGo, 0f, 0f, CanvasPx, CanvasPx);
            _canvasImg = canvasGo.AddComponent<RawImage>();

            float y = 56f + CanvasPx + 14f;
            UiKit.AddButton(panel, 30f, y, 208f, 40f, L("ui.shape.custom.copy_below"), () => Do(_model.CopyLayerBelow));
            UiKit.AddButton(panel, 246f, y, 208f, 40f, L("ui.shape.custom.clear_layer"), () => Do(_model.ClearLayer));
            UiKit.AddButton(panel, 462f, y, 208f, 40f, L("ui.form.fill_layer"), () => Do(_model.FillLayer));
            y += 46f;
            UiKit.AddButton(panel, 30f, y, 208f, 40f, L("ui.shape.custom.mirror_x"), () => Do(() => _model.Mirror(alongX: true)));
            UiKit.AddButton(panel, 246f, y, 208f, 40f, L("ui.shape.custom.mirror_z"), () => Do(() => _model.Mirror(alongX: false)));
            UiKit.AddButton(panel, 462f, y, 208f, 40f, L("ui.shape.custom.clear_all"), () => Do(_model.ClearAll));
            y += 46f;
            UiKit.AddButton(panel, 30f, y, 100f, 40f, "←", () => Do(() => _model.Shift(-1, 0, 0)));
            UiKit.AddButton(panel, 136f, y, 100f, 40f, "→", () => Do(() => _model.Shift(1, 0, 0)));
            UiKit.AddButton(panel, 246f, y, 100f, 40f, "↑", () => Do(() => _model.Shift(0, 0, 1)));
            UiKit.AddButton(panel, 352f, y, 100f, 40f, "↓", () => Do(() => _model.Shift(0, 0, -1)));
            UiKit.AddButton(panel, 462f, y, 100f, 40f, "⤒", () => Do(() => _model.Shift(0, 1, 0)));
            UiKit.AddButton(panel, 568f, y, 102f, 40f, "⤓", () => Do(() => _model.Shift(0, -1, 0)));
            y += 46f;
            UiKit.AddButton(panel, 30f, y, 208f, 40f, L("ui.tex.undo"), DoUndo);
            UiKit.AddButton(panel, 246f, y, 208f, 40f, L("ui.tex.redo"), DoRedo);
            _gridButton = UiKit.AddButton(panel, 462f, y, 208f, 40f, L("ui.shape.custom.grid_toggle"), ToggleGrid);
            y += 50f;
            _budgetLabel = UiKit.AddText(panel, 30f, y, 640f, 24f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 26f;
            _problemLabel = UiKit.AddText(panel, 30f, y, 640f, 44f, string.Empty, 15, UiKit.Warn, TextAnchor.UpperLeft);
            _problemLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void BuildRightPanel(Transform root)
        {
            var panel = UiKit.AddPanel(root, 1108f, 32f, 796f, 1010f, UiKit.PanelFill).transform;
            UiKit.AddText(panel, 16f, 10f, 400f, 24f, L("ui.shape.custom.preview"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            var view = new GameObject("FormPreviewView", typeof(RectTransform));
            view.transform.SetParent(panel, false);
            UiKit.Place(view, 16f, 40f, 460f, 460f);
            _previewView = view.AddComponent<RawImage>();

            // Material + dye: what the form will look like — it takes the material it is made of in the world.
            float x = 492f, y = 40f;
            UiKit.AddText(panel, x, y, 288f, 22f, L("ui.form.material"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 26f;
            UiKit.AddButton(panel, x, y, 50f, 40f, "◀", () => StepMaterial(-1));
            _materialLabel = UiKit.AddText(panel, x + 56f, y, 176f, 40f, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.AddButton(panel, x + 238f, y, 50f, 40f, "▶", () => StepMaterial(1));
            y += 52f;
            UiKit.AddText(panel, x, y, 288f, 22f, L("ui.form.dye"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 26f;
            for (int i = 0; i < Dyes.Length; i++)
            {
                int index = i;
                var swatch = UiKit.AddImage(panel, x + ((i % 4) * 72f), y + ((i / 4) * 44f), 66f, 38f, UiKit.SolidSprite, Dyes[i]);
                var button = swatch.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                button.onClick.AddListener(() =>
                {
                    _dyeIndex = index;
                    ApplyMaterial();
                });
            }

            // Footprint: how many blocks the form spans.
            y = 520f;
            UiKit.AddText(panel, 16f, y, 764f, 24f, L("ui.form.footprint_title"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            _footprintLabel = UiKit.AddText(panel, 16f, y, 764f, 24f, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 32f;
            FootprintRow(panel, y, "ui.form.width", 1, 0, 0);
            FootprintRow(panel, y + 46f, "ui.form.height", 0, 1, 0);
            FootprintRow(panel, y + 92f, "ui.form.length", 0, 0, 1);
            y += 146f;
            var note = UiKit.AddText(panel, 16f, y, 764f, 66f, L("ui.form.footprint_note"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Name + save.
            y = 810f;
            UiKit.AddText(panel, 16f, y, 764f, 22f, L("ui.shape.custom.name"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 26f;
            _nameInput = UiKit.AddInput(panel, 16f, y, 470f, 44f, _name, v => _name = v, L("ui.shape.custom.name_hint"), 24);
            UiKit.AddButton(panel, 494f, y, 286f, 44f, L("ui.form.save"), Save, "btn_singleplayer");
            y += 54f;
            _status = UiKit.AddText(panel, 16f, y, 600f, 48f, string.Empty, 14, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel, 620f, 1010f - 52f, 160f, 40f, L("ui.menu.back"), Close);
        }

        private void FootprintRow(Transform panel, float y, string labelKey, int dw, int dh, int dl)
        {
            UiKit.AddText(panel, 16f, y, 240f, 40f, L(labelKey), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(panel, 270f, y, 80f, 40f, "−", () => StepFootprint(-dw, -dh, -dl));
            UiKit.AddButton(panel, 358f, y, 80f, 40f, "+", () => StepFootprint(dw, dh, dl));
        }

        private void StepMaterial(int delta)
        {
            if (_materials.Count == 0)
            {
                return;
            }

            _materialIndex = (_materialIndex + delta + _materials.Count) % _materials.Count;
            ApplyMaterial();
        }
    }
}
