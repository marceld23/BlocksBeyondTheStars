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
    /// The model (<see cref="FormCanvasModel"/>) is Unity-free and unit-tested; this class is its screen. The frame
    /// loop, keys, undo/redo, preview stage and screen shell it shares with "My tools" live in
    /// <see cref="LayerCanvasEditor"/>.
    /// </summary>
    public sealed class FormEditor : LayerCanvasEditor
    {
        private const float CanvasPx = 640f;

        private readonly FormCanvasModel _model = new FormCanvasModel();
        private GameContent _content;
        private string _name = string.Empty;
        private string _loadedName = string.Empty;   // the library entry on the canvas ("" = a new form)

        private RawImage _canvasImg;
        private InputField _nameInput;
        private Text _layerLabel, _budgetLabel, _problemLabel, _footprintLabel, _materialLabel;
        private RectTransform _libraryContent;
        private Button _gridButton;

        // 3-D preview.
        private MeshFilter _previewFilter;
        private Material _previewMaterial;
        private Texture2D _previewTile;
        private Mesh _previewMesh;
        private readonly List<BlockDefinition> _materials = new List<BlockDefinition>();
        private int _materialIndex;
        private int _dyeIndex; // 0 = undyed

        // The material list (#1969): the same searchable palette list the texture editor browses with.
        private const string DefaultMaterial = "stone";
        private BlockTextureAtlas _atlas;
        private readonly List<EditorPaletteKit.Entry> _materialRows = new List<EditorPaletteKit.Entry>();
        private PaletteListUi _materialList;
        private string _materialAtOpen = string.Empty;

        private static readonly Color[] Dyes =
        {
            Color.white, new Color(0.85f, 0.25f, 0.22f), new Color(0.95f, 0.62f, 0.18f), new Color(0.93f, 0.85f, 0.30f),
            new Color(0.35f, 0.72f, 0.32f), new Color(0.25f, 0.55f, 0.90f), new Color(0.58f, 0.36f, 0.80f), new Color(0.20f, 0.20f, 0.24f),
        };

        protected override ILayerCanvas Layers => _model;

        protected override int CellsX => _model.SizeX;

        protected override int CellsZ => _model.SizeZ;

        protected override float SpinDegreesPerSecond => 22f;

        protected override string HintKeyMouse => "ui.form.hint";

        protected override string HintKeyPadCanvas => "ui.form.hint_pad";

        protected override string HintKeyPadPanel => "ui.form.hint_pad_panel";

        /// <summary>Left button / A adds, right button / X removes (both at once remove).</summary>
        protected override bool PaintCell(int x, int z, bool primary, bool secondary) => _model.Paint(x, z, primary && !secondary);

        /// <summary>The budget and problem lines follow every painted cell.</summary>
        protected override void OnCellPainted() => RefreshLabels();

        /// <summary>An undo, a redo or a helper may have changed the canvas size (footprint, grid).</summary>
        protected override void AfterCanvasEdit() => AfterStructuralChange();

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

                // By section, then by the name the player READS — the key order looked random in every
                // language but English.
                _materials.Sort((a, b) =>
                {
                    int rank = EditorPaletteKit.CategoryRank(MaterialGroup(a)).CompareTo(EditorPaletteKit.CategoryRank(MaterialGroup(b)));
                    if (rank != 0)
                    {
                        return rank;
                    }

                    int group = string.CompareOrdinal(MaterialGroup(a), MaterialGroup(b));
                    return group != 0 ? group : string.Compare(MaterialLabel(a), MaterialLabel(b), StringComparison.CurrentCultureIgnoreCase);
                });

                // The material of the last visit, else plain stone.
                _materialAtOpen = Shell?.Settings != null ? Shell.Settings.FormEditorMaterial ?? string.Empty : string.Empty;
                int start = _materials.FindIndex(b => b.Key == _materialAtOpen);
                _materialIndex = Mathf.Max(0, start >= 0 ? start : _materials.FindIndex(b => b.Key == DefaultMaterial));
                _atlas = BlockTextureAtlas.Acquire(_content);
            }

            BuildUi();
            BuildStage();
            RebuildCanvasTexture();
            RefreshAll();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Destroy(_previewTile);
            Destroy(_previewMesh);
            Destroy(_previewMaterial);
            foreach (var row in _materialRows)
            {
                Destroy(row.Icon); // cut out of the shared atlas for this list only
            }

            _atlas?.Release();

            // Remember the material for the next visit — once, on the way out, not on every click.
            string chosen = _materials.Count > 0 ? _materials[Mathf.Clamp(_materialIndex, 0, _materials.Count - 1)].Key : string.Empty;
            if (Shell?.Settings != null && chosen.Length > 0 && chosen != _materialAtOpen)
            {
                Shell.Settings.FormEditorMaterial = chosen;
                Shell.Settings.Save();
            }
        }

        // ---------------------------------------------------------------- commands

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
            if (Tex != null)
            {
                Destroy(Tex);
            }

            Tex = new Texture2D(_model.SizeX, _model.SizeZ, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            if (_canvasImg != null)
            {
                _canvasImg.texture = Tex;
            }

            // The canvas keeps the footprint's proportions inside its square frame.
            if (CanvasRt != null)
            {
                float longest = Mathf.Max(_model.SizeX, _model.SizeZ);
                float w = CanvasPx * _model.SizeX / longest, h = CanvasPx * _model.SizeZ / longest;
                CanvasRt.sizeDelta = new Vector2(w, h);
                CanvasRt.anchoredPosition = new Vector2((CanvasPx - w) * 0.5f, -(CanvasPx - h) * 0.5f);
                Pad?.Rebind(CanvasRt);
            }
        }

        /// <summary>The current layer from above, the layer below shining through dimly, and the block borders of
        /// a form over several blocks as a lighter grid — so it is always clear which block a cell belongs to.</summary>
        protected override void RenderLayer()
        {
            if (Tex == null)
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

                    Tex.SetPixel(x, z, c); // texture row 0 is the bottom = the near edge (z 0)
                }
            }

            Tex.Apply();
        }

        // ---------------------------------------------------------------- preview

        private void BuildStage()
        {
            var target = BeginPreviewStage("FormPreviewRT", "FormEditorStage", -7000f);

            var model = new GameObject("Form", typeof(MeshFilter), typeof(MeshRenderer));
            model.transform.SetParent(Spin, false);
            _previewFilter = model.GetComponent<MeshFilter>();
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Texture");
            _previewMaterial = new Material(shader) { color = Color.white };
            if (_previewMaterial.HasProperty("_Floor"))
            {
                _previewMaterial.SetFloat("_Floor", 0.5f);
            }

            model.GetComponent<MeshRenderer>().sharedMaterial = _previewMaterial;

            AddPreviewCamera(target, "FormEditorCam", 40f);
            ApplyMaterial();
        }

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
                label = MaterialLabel(block);
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
        protected override void RebuildPreview()
        {
            PreviewDirty = false;
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
            PreviewCamera.orthographicSize = reach;
            PreviewCamera.transform.localPosition = new Vector3(1f, 0.8f, -1f).normalized * (reach * 3f + 2f);
            PreviewCamera.transform.LookAt(Stage.transform.position);
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

        protected override void RefreshLabels()
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

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            var root = BeginShell("Form Editor UI", "ui.form.title");
            BuildLibraryPanel(root);
            BuildCanvasPanel(root);
            BuildRightPanel(root);
            EndShell(root, "ui.form.hint");
        }

        private void BuildLibraryPanel(Transform root)
        {
            var panel = AddColumn(root, 16f, 360f);
            UiKit.AddText(panel, 14f, 10f, 330f, 24f, L("ui.shape.custom.library"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, 14f, 42f, 162f, 40f, L("ui.form.new"), NewForm);
            UiKit.AddButton(panel, 184f, 42f, 162f, 40f, L("ui.form.duplicate"), Duplicate);
            UiKit.AddButton(panel, 14f, 88f, 162f, 40f, L("ui.shape.custom.export"), CopyCode);
            UiKit.AddButton(panel, 184f, 88f, 162f, 40f, L("ui.shape.custom.import"), PasteCode);
            // The column is shared (#1969): the forms above, the preview material below — each scrolls alone.
            _libraryContent = UiKit.ScrollList(panel, 14f, 138f, 332f, 362f, 4f);
            RebuildLibrary();
            UiKit.AddImage(panel, 14f, 510f, 332f, 2f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.12f));
            BuildMaterialList(panel, 522f, 474f);
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
                var row = AddListRow(_libraryContent);

                string name = entry.Name, voxels = entry.Voxels;
                int cells = CustomShape.CellCount(voxels);
                string label = cells > 1 ? name + "  · " + string.Format(L("ui.form.blocks"), cells) : name;
                UiKit.AddButton(row, 0f, 0f, 262f, 42f, label, () => LoadEntry(name, voxels));
                UiKit.AddButton(row, 268f, 0f, 46f, 42f, "✕", () => DeleteEntry(name));
            }
        }

        private void BuildCanvasPanel(Transform root)
        {
            var panel = AddColumn(root, 392f, 700f);
            _layerLabel = UiKit.AddText(panel, 30f, 12f, 320f, 30f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(panel, 400f, 8f, 80f, 38f, "▲", () => StepLayer(1));
            UiKit.AddButton(panel, 488f, 8f, 80f, 38f, "▼", () => StepLayer(-1));

            var frame = new GameObject("CanvasFrame", typeof(RectTransform));
            frame.transform.SetParent(panel, false);
            UiKit.Place(frame, 30f, 56f, CanvasPx, CanvasPx);
            frame.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var canvasGo = new GameObject("LayerCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(frame.transform, false);
            CanvasRt = UiKit.Place(canvasGo, 0f, 0f, CanvasPx, CanvasPx);
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
            var panel = AddColumn(root, 1108f, 796f);
            UiKit.AddText(panel, 16f, 10f, 400f, 24f, L("ui.shape.custom.preview"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            var view = new GameObject("FormPreviewView", typeof(RectTransform));
            view.transform.SetParent(panel, false);
            UiKit.Place(view, 16f, 40f, 460f, 460f);
            PreviewView = view.AddComponent<RawImage>();

            // Material + dye: what the form will look like — it takes the material it is made of in the world.
            // The material is picked in the list on the left (#1969); here stands only its name, which stays
            // readable while a search hides its row.
            float x = 492f, y = 40f;
            UiKit.AddText(panel, x, y, 288f, 22f, L("ui.form.material"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 26f;
            _materialLabel = UiKit.AddText(panel, x, y, 288f, 28f, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 40f;
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
            StatusLabel = UiKit.AddText(panel, 16f, y, 600f, 48f, string.Empty, 14, UiKit.Ok, TextAnchor.UpperLeft);
            StatusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel, 620f, 1010f - 52f, 160f, 40f, L("ui.menu.back"), Close);
        }

        private void FootprintRow(Transform panel, float y, string labelKey, int dw, int dh, int dl)
        {
            UiKit.AddText(panel, 16f, y, 240f, 40f, L(labelKey), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddButton(panel, 270f, y, 80f, 40f, "−", () => StepFootprint(-dw, -dh, -dl));
            UiKit.AddButton(panel, 358f, y, 80f, 40f, "+", () => StepFootprint(dw, dh, dl));
        }

        private string MaterialLabel(BlockDefinition block) =>
            Shell != null && !string.IsNullOrEmpty(block.NameKey) ? Shell.L(block.NameKey) : block.Key;

        private static string MaterialGroup(BlockDefinition block) =>
            string.IsNullOrEmpty(block.Category) ? "building" : block.Category;

        /// <summary>The lower half of the left column (#1969): every material a form can be made of, with its
        /// real tile in front of the name, under section headers, filtered by the search field — the list the
        /// texture editor browses with. It only dresses the PREVIEW: in the world a form takes the material it
        /// is made from.</summary>
        private void BuildMaterialList(Transform panel, float top, float height)
        {
            UiKit.AddText(panel, 14f, top, 330f, 24f, L("ui.form.material"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddInput(panel, 14f, top + 30f, 332f, 34f, string.Empty, v => _materialList?.Rebuild(v), L("ui.tex.search"), 40, 15);
            var content = UiKit.ScrollList(panel, 14f, top + 72f, 332f, height - 72f);

            _materialRows.Clear();
            foreach (var block in _materials)
            {
                _materialRows.Add(new EditorPaletteKit.Entry
                {
                    Id = block.Key,
                    Label = MaterialLabel(block),
                    Kind = "block",
                    Group = MaterialGroup(block),
                    Color = new Color(0.4f, 0.5f, 0.6f),
                    Icon = EditorPaletteKit.TileSprite(_atlas, block.NumericId.Value),
                });
            }

            _materialList = new PaletteListUi(Shell != null ? Shell : FindAnyObjectByType<AppShell>(), content, _materialRows, _materialIndex);
            _materialList.OnSelected = index =>
            {
                // Rebuild reports the standing selection on every keystroke of the search; only a real change
                // loads a tile.
                if (index >= 0 && index < _materials.Count && index != _materialIndex)
                {
                    _materialIndex = index;
                    ApplyMaterial();
                }
            };
            _materialList.Rebuild(string.Empty);
        }
    }
}
