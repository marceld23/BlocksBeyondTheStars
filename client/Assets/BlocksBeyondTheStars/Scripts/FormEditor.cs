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
    /// large canvas, undo, a material and dye preview, a gamepad, and a library you can tidy up. In the world the
    /// shape designer (<see cref="ShapeEditor"/>) works on the same local library (<see cref="CustomShapeLibrary"/>), so a
    /// form designed here appears under "My forms" there.
    ///
    /// What only this editor can do is forms over several blocks (#1961): a wardrobe two blocks high, a table two
    /// blocks long. The footprint steppers grow the canvas; the layer canvas then shows the whole form from above
    /// with the block borders drawn in, and the budget line counts per block — exactly the rule the server checks.
    ///
    /// The model (<see cref="FormCanvasModel"/>) is Unity-free and unit-tested; this class is its screen. Since #3693 it
    /// sits in the editor shell (<see cref="LayerCanvasEditor"/>): Save puts the form into "My forms" (a form saved under
    /// a new name is a new entry — the old one stays), New starts an empty one (the shell asks over unsaved work), and
    /// unsaved work is kept as a draft. The cards are built in <c>FormEditor.Ui.cs</c>.
    /// </summary>
    public sealed partial class FormEditor : LayerCanvasEditor
    {
        private readonly FormCanvasModel _model = new FormCanvasModel();
        private GameContent _content;
        private string _name = string.Empty;
        private string _loadedName = string.Empty;   // the library entry on the canvas ("" = a new form)
        private string _savedName = string.Empty;    // the name as last loaded or saved (a rename is unsaved work)

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

        protected override int LayerCount => _model.SizeY;

        protected override float SpinDegreesPerSecond => 22f;

        protected override string HintKeyMouse => "ui.form.hint";

        protected override string HintKeyPadCanvas => "ui.form.hint_pad";

        protected override string HintKeyPadPanel => "ui.form.hint_pad_panel";

        protected override string DraftKind => "form";

        public override string DocumentName => (_name ?? string.Empty).Trim();

        /// <summary>A renamed form is unsaved work too.</summary>
        protected override bool ExtraDirty => !string.Equals(DocumentName, _savedName, StringComparison.Ordinal);

        /// <summary>Place adds, Remove (or the right button / X) removes.</summary>
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
                CollectMaterials();
            }

            var firstFocus = BuildUi();
            BuildStage();
            RebuildCanvasTexture();
            RefreshAll();
            FinishFrame(firstFocus, "ui.form.tip_remove");
        }

        /// <summary>Every shapeable block with a texture — by section, then by the name the player READS (the key order
        /// looked random in every language but English) — and the material of the last visit, else plain stone.</summary>
        private void CollectMaterials()
        {
            foreach (var block in _content.Blocks.Values)
            {
                if (block.Shapeable && GameTextures.Resolve(block.Key) != null)
                {
                    _materials.Add(block);
                }
            }

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

            _materialAtOpen = Shell?.Settings != null ? Shell.Settings.FormEditorMaterial ?? string.Empty : string.Empty;
            int start = _materials.FindIndex(b => b.Key == _materialAtOpen);
            _materialIndex = Mathf.Max(0, start >= 0 ? start : _materials.FindIndex(b => b.Key == DefaultMaterial));
            _atlas = BlockTextureAtlas.Acquire(_content);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EditorSceneKit.Discard(_previewTile);
            EditorSceneKit.Discard(_previewMesh);
            EditorSceneKit.Discard(_previewMaterial);
            DestroyLookPreview();
            foreach (var row in _materialRows)
            {
                EditorSceneKit.Discard(row.Icon); // cut out of the shared atlas for this list only
            }

            _atlas?.Release();

            // Remember the material for the next visit — once, on the way out, not on every click.
            string chosen = ChosenMaterialKey();
            if (Shell?.Settings != null && chosen.Length > 0 && chosen != _materialAtOpen)
            {
                Shell.Settings.FormEditorMaterial = chosen;
                Shell.Settings.Save();
            }
        }

        private string ChosenMaterialKey()
            => _materials.Count > 0 ? _materials[Mathf.Clamp(_materialIndex, 0, _materials.Count - 1)].Key : string.Empty;

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
                Changed();
                Frame?.ClearStatus();
            }
            else
            {
                SetStatus(string.Format(L("ui.form.footprint_limit"), CustomShape.MaxFootprint, CustomShape.MaxCells), EditorStatusSeverity.Warning);
            }
        }

        private void ToggleGrid()
        {
            if (_model.SetGrid(_model.Grid == CustomShape.GridLarge ? CustomShape.GridSmall : CustomShape.GridLarge))
            {
                AfterStructuralChange();
                Changed();
            }
        }

        /// <summary>The header's New (the shell has asked over unsaved work): an empty one-block canvas, no name.</summary>
        private void NewForm()
        {
            _model.New();
            SetNames(string.Empty, string.Empty);
            AfterStructuralChange();
            Frame?.ClearStatus();
        }

        private void LoadEntry(string name, string voxels)
        {
            if (!_model.Load(voxels))
            {
                SetStatus(L("ui.form.load_failed"), EditorStatusSeverity.Error);
                return;
            }

            SetNames(name ?? string.Empty, name ?? string.Empty);
            AfterStructuralChange();
            Frame?.ClearStatus();
        }

        /// <summary>The name on the canvas and the library entry it stands for (also the saved name).</summary>
        private void SetNames(string name, string loaded)
        {
            _name = name;
            _loadedName = _savedName = loaded;
            _nameInput?.SetTextWithoutNotify(_name);
        }

        /// <summary>Unattended editor screenshots (-captureEditors, ScreenshotDirector.Editors.cs, #3687): an in-memory sample form
        /// on the canvas, loaded like a library row (nothing is saved). Keep it working when the editor is restyled.</summary>
        internal void CaptureLoadSample(string name, string voxels) => LoadEntry(name, voxels);

        /// <summary>Save (the shell's primary button, Ctrl+S): into "My forms". A form saved under a new name is a new
        /// entry and the old one stays — saving never deletes (#3693); saving under the name of another entry overwrites
        /// that one, as in the world.</summary>
        private bool Save()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), EditorStatusSeverity.Warning);
                return false;
            }

            string name = DocumentName;
            if (name.Length == 0)
            {
                SetStatus(L("ui.form.need_name"), EditorStatusSeverity.Warning);
                return false;
            }

            CustomShapeLibrary.Save(voxels, name);
            _loadedName = _savedName = name;
            _model.MarkSaved();
            RebuildLibrary();
            RefreshLabels();
            EditorPlaytest.Saved(EditorPlaytest.Form);
            SetStatus(string.Format(L("ui.form.saved"), name), EditorStatusSeverity.Success);
            return true;
        }

        private void Duplicate()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), EditorStatusSeverity.Warning);
                return;
            }

            string baseName = DocumentName.Length > 0 ? DocumentName : L("ui.form.unnamed");
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
            SetNames(copy, copy);
            _model.MarkSaved();
            RebuildLibrary();
            EditorPlaytest.Saved(EditorPlaytest.Form);
            SetStatus(string.Format(L("ui.form.saved"), copy), EditorStatusSeverity.Success);
        }

        /// <summary>A library row's ✕ — permanent, so it asks first (#3690).</summary>
        private void AskDeleteEntry(string name) => Frame?.ConfirmDelete(name, () => DeleteEntry(name));

        private void DeleteEntry(string name)
        {
            CustomShapeLibrary.Delete(name);
            if (string.Equals(_loadedName, name, StringComparison.OrdinalIgnoreCase))
            {
                _loadedName = string.Empty; // the canvas keeps the form — saving it again brings it back
            }

            RebuildLibrary();
            SetStatus(string.Format(L("ui.form.deleted"), name), EditorStatusSeverity.Info);
        }

        /// <summary>A library row over unsaved work: the shell asks first.</summary>
        private void PickEntry(string name, string voxels) => Frame?.ConfirmDiscard(() => LoadEntry(name, voxels));

        private void CopyCode()
        {
            string voxels = _model.Encode();
            if (voxels.Length == 0)
            {
                SetStatus(ProblemText(), EditorStatusSeverity.Warning);
                return;
            }

            GUIUtility.systemCopyBuffer = ShareCode.EncodeForm(voxels, DocumentName);
            EditorPlaytest.Exported(EditorPlaytest.Form);
            SetStatus(L("ui.shape.custom.exported"), EditorStatusSeverity.Success);
        }

        /// <summary>A pasted share code lands on the canvas as ONE undoable step (it is a new form until it is saved).</summary>
        private void PasteCode()
        {
            if (ShareCode.TryDecodeForm(GUIUtility.systemCopyBuffer, out string voxels, out string name) && _model.Replace(voxels))
            {
                _name = name ?? string.Empty;
                _loadedName = string.Empty;
                _nameInput?.SetTextWithoutNotify(_name);
                AfterStructuralChange();
                Changed();
                SetStatus(L("ui.form.pasted"), EditorStatusSeverity.Info);
            }
            else
            {
                SetStatus(L("ui.shape.custom.import_failed"), EditorStatusSeverity.Warning);
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

        // ---------------------------------------------------------------- drafts (#3689)

        [Serializable]
        private sealed class FormDraft
        {
            public string name;
            public string loaded;
            public string raw;
            public string material;
            public int dye;
        }

        /// <summary>The canvas as it stands (also half-drawn), its name, the entry it came from and the preview's
        /// material and dye. Nothing when nothing is drawn.</summary>
        protected override string WriteDraftPayload()
        {
            if (_model.Problem(out _) == FormProblem.Empty)
            {
                return null;
            }

            return JsonUtility.ToJson(new FormDraft
            {
                name = _name ?? string.Empty,
                loaded = _loadedName,
                raw = _model.Raw(),
                material = ChosenMaterialKey(),
                dye = _dyeIndex,
            });
        }

        protected override bool ReadDraftPayload(string payload)
        {
            var draft = JsonUtility.FromJson<FormDraft>(payload);
            if (draft == null)
            {
                return false;
            }

            _model.New();
            if (!_model.ReplaceRaw(draft.raw))
            {
                return false;
            }

            _name = draft.name ?? string.Empty;
            _loadedName = _savedName = draft.loaded ?? string.Empty;
            _nameInput?.SetTextWithoutNotify(_name);
            int material = _materials.FindIndex(b => b.Key == draft.material);
            if (material >= 0)
            {
                _materialIndex = material;
            }

            _dyeIndex = Mathf.Clamp(draft.dye, 0, Dyes.Length - 1);
            ApplyMaterial();
            AfterStructuralChange();
            return true;
        }

        // ---------------------------------------------------------------- canvas

        private void RebuildCanvasTexture()
        {
            if (Tex != null)
            {
                EditorSceneKit.Discard(Tex);
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
                        c = ShapeSlotPalette.CanvasColour(_model.ValueAt(x, _model.Layer, z));
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
            AttachLookPreview();
            ApplyMaterial();
        }

        private void ApplyMaterial()
        {
            if (_previewMaterial == null)
            {
                return;
            }

            EditorSceneKit.Discard(_previewTile);
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

            MarkLookPreviewDirty(); // The look preview carries material and dye in its mesh
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

            EditorSceneKit.Discard(_previewMesh);
            _previewMesh = new Mesh { name = "FormEditorPreview" };
            _previewMesh.SetVertices(verts);
            _previewMesh.SetUVs(0, uvs);
            _previewMesh.SetTriangles(tris, 0);
            _previewMesh.RecalculateNormals();
            _previewMesh.RecalculateBounds();
            _previewFilter.sharedMesh = _previewMesh;
            ApplyLookPreview(); // The block-shader form in its place while the look shows

            // Centre the footprint on the turntable and frame it, whatever its size.
            var size = new Vector3(_model.Width, _model.Height, _model.Length);
            _previewFilter.transform.localPosition = -size * 0.5f;
            float reach = size.magnitude * 0.62f + 0.35f;
            PreviewCamera.orthographicSize = reach;
            PreviewCamera.transform.localPosition = new Vector3(1f, 0.8f, -1f).normalized * (reach * 3f + 2f);
            PreviewCamera.transform.LookAt(Stage.transform.position);
        }

        private string MaterialLabel(BlockDefinition block) =>
            Shell != null && !string.IsNullOrEmpty(block.NameKey) ? Shell.L(block.NameKey) : block.Key;

        private static string MaterialGroup(BlockDefinition block) =>
            string.IsNullOrEmpty(block.Category) ? "building" : block.Category;
    }
}
