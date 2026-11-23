// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shape designer's screen on the editor shell (#3693 — the mock's "Formen &amp; Werkzeuge" view, which "Form
    /// entwerfen" shares): "My forms" with the share codes on the left, the layer canvas with its layer strip, the Place |
    /// Remove switch and the helpers in the centre, the preview, the name and — in the crafting tab — "Craft it" on the
    /// right. Above the console in a world: an opaque frame of its own.
    /// </summary>
    public sealed partial class ShapeEditor
    {
        private const float CanvasPx = 640f, CanvasY = 64f;
        private const float ToolRowY = CanvasY + CanvasPx + 12f, HelperRow1Y = ToolRowY + RowH + RowGap, HelperRow2Y = HelperRow1Y + RowH + RowGap;

        /// <summary>The centre card's row width and a helper in a row of three.</summary>
        internal const float CentreRowW = CentreW - (2f * Inset), ThirdW = (CentreRowW - (2f * RowGap)) / 3f;

        private RawImage _canvasImg;
        private InputField _nameInput;
        private Text _budgetLabel;
        private RectTransform _libList;

        /// <summary>Builds the three cards; returns the pad's first stop (the Place tool).</summary>
        private Selectable BuildUi()
        {
            BuildFrame("Shape Editor UI", new EditorShellOptions
            {
                TitleKey = "ui.shape.custom.title",
                SaveSublineKey = "ui.editor.save_sub.shape",
                OnBack = Close,
                OnSave = Save,
            }, inWorld: true);
            BuildLibraryCard(Frame.AddCard(LeftX, CardY, LeftW, CardH));
            var first = BuildCanvasCard(Frame.AddCard(CentreX, CardY, CentreW, CardH));
            BuildRightCard(Frame.AddCard(RightX, CardY, RightW, CardH));
            return first;
        }

        /// <summary>"My forms" — the same idea as the paint editor's "my designs" — and the share codes (#846), which only
        /// work where the clipboard does (not in the browser).</summary>
        private void BuildLibraryCard(Transform card)
        {
            const float w = LeftW - (2f * Inset);
            Kicker(card, Inset, 14f, w, "ui.shape.custom.library");
            float y = 48f;
            if (EditorShell.DesktopFeatures)
            {
                TierButton(card, Inset, y, w, RowH, L("ui.shape.custom.export"), ExportCode);
                TierButton(card, Inset, y + RowH + RowGap, w, RowH, L("ui.shape.custom.import"), ImportCode);
                y += (2f * (RowH + RowGap)) + 8f;
            }

            _libList = UiKit.ScrollList(card, Inset, y, w, CardH - Inset - y, 4f);
            RebuildLibraryList();
        }

        private void RebuildLibraryList()
        {
            if (_libList == null || LibraryProvider == null)
            {
                return;
            }

            for (int i = _libList.childCount - 1; i >= 0; i--)
            {
                Destroy(_libList.GetChild(i).gameObject);
            }

            const float rowW = LeftW - (2f * Inset) - 16f;
            foreach (var entry in LibraryProvider() ?? new List<(string, string)>())
            {
                int blocks = CustomShape.CellCount(entry.Voxels);
                string label = blocks > 1 ? entry.Name + " ×" + blocks : entry.Name; // a form over several blocks (#1961)
                string name = entry.Name, voxels = entry.Voxels;
                TierButton(AddListRow(_libList, rowW), 0f, 0f, rowW, RowH, label, () => PickEntry(name, voxels));
            }
        }

        private Selectable BuildCanvasCard(Transform card)
        {
            BuildLayerHeader(card, CentreW);
            var canvasGo = new GameObject("LayerCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(card, false);
            CanvasRt = UiKit.Place(canvasGo, Inset, CanvasY, CanvasPx, CanvasPx);
            _canvasImg = canvasGo.AddComponent<RawImage>();
            float stripX = Inset + CanvasPx + 16f;
            BuildLayerStrip(card, stripX, CanvasY, CentreW - Inset - stripX, CanvasPx);

            var tools = BuildToolSwitch(card, Inset, ToolRowY, (2f * ThirdW) + RowGap);
            TierButton(card, Inset + (2f * (ThirdW + RowGap)), ToolRowY, ThirdW, RowH, L("ui.shape.custom.grid_toggle"), ToggleGrid);
            TierButton(card, Inset, HelperRow1Y, ThirdW, RowH, L("ui.shape.custom.copy_below"), () => Do(_model.CopyLayerBelow));
            TierButton(card, Inset + ThirdW + RowGap, HelperRow1Y, ThirdW, RowH, L("ui.shape.custom.mirror_x"), () => Do(() => _model.Mirror(alongX: true)));
            TierButton(card, Inset + (2f * (ThirdW + RowGap)), HelperRow1Y, ThirdW, RowH, L("ui.shape.custom.mirror_z"), () => Do(() => _model.Mirror(alongX: false)));
            TierButton(card, Inset, HelperRow2Y, ThirdW, RowH, L("ui.shape.custom.clear_layer"), () => Do(_model.ClearLayer));
            TierButton(card, Inset + ThirdW + RowGap, HelperRow2Y, ThirdW, RowH, L("ui.shape.custom.clear_all"), () => Do(_model.ClearAll));
            _budgetLabel = UiKit.AddText(card, Inset + (2f * (ThirdW + RowGap)), HelperRow2Y, ThirdW, RowH, string.Empty, 16, UiKit.CyanDim,
                TextAnchor.MiddleLeft);
            return tools.Buttons[0];
        }

        /// <summary>The preview, the name and — where the host crafts — "Craft it".</summary>
        private void BuildRightCard(Transform card)
        {
            const float w = RightW - (2f * Inset);
            Kicker(card, Inset, 14f, w, "ui.shape.custom.preview");
            var view = new GameObject("FormPreviewView", typeof(RectTransform));
            view.transform.SetParent(card, false);
            UiKit.Place(view, Inset, 48f, w, 480f);
            PreviewView = view.AddComponent<RawImage>();
            LookPreview.AddToggle(card, Inset, 48f, w, 480f, L); // "as in the world" / "pixels only"

            Kicker(card, Inset, 548f, w, "ui.shape.custom.name");
            _nameInput = UiKit.AddInput(card, Inset, 576f, w, RowH, _name, v =>
            {
                _name = v;
                Changed();
            }, L("ui.shape.custom.name_hint"), 24, 18);
            if (HostCrafts && OnApply != null)
            {
                TierButton(card, Inset, 636f, w, RowH, L(CraftLabelKey), Craft, ButtonTier.Secondary);
            }

            BuildSlotPalette(card, Inset, 700f, w); // ShapeEditor.Slots.cs
        }

        protected override void RefreshLabels()
        {
            ShowLayerLine(_model.Layer, _model.SizeY);
            if (_budgetLabel != null)
            {
                int boxes = _model.MaxBoxesUsed();
                _budgetLabel.text = string.Format(L("ui.shape.custom.budget"), boxes, CustomShape.MaxBoxes);
                _budgetLabel.color = boxes > CustomShape.MaxBoxes ? UiKit.DangerCol : UiKit.CyanDim;
                UiKit.FitLabelSingleLine(_budgetLabel, ThirdW, UiKit.EditorMinFontSize, 16);
            }
        }

        // ---------------------------------------------------------------- canvas

        private void RebuildCanvasTexture()
        {
            if (Tex != null && Tex.width == _model.SizeX)
            {
                return;
            }

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
        }

        /// <summary>Draws the current layer, with the layer BELOW showing through dimly — that ghost is what makes stacking
        /// layers into a shape possible without constantly flipping back and forth.</summary>
        protected override void RenderLayer()
        {
            if (Tex == null)
            {
                return;
            }

            var below = new Color(UiKit.Cyan.r * 0.32f, UiKit.Cyan.g * 0.32f, UiKit.Cyan.b * 0.32f, 1f);
            for (int z = 0; z < _model.SizeZ; z++)
            {
                for (int x = 0; x < _model.SizeX; x++)
                {
                    Color c = _model.Get(x, _model.Layer, z) ? ShapeSlotPalette.CanvasColour(_model.ValueAt(x, _model.Layer, z)) // the slot's colour
                        : _model.Layer > 0 && _model.Get(x, _model.Layer - 1, z) ? below
                        : FacePalette.EditorBackground;
                    Tex.SetPixel(x, z, c); // texture row 0 is the BOTTOM: grid z 0 is the near edge, drawn at the bottom
                }
            }

            Tex.Apply();
        }

        // ---------------------------------------------------------------- 3-D preview

        /// <summary>A render-texture stage parked far from the world, so the preview cannot pick up what is behind it.</summary>
        private void BuildStage()
        {
            var target = BeginPreviewStage("FormPreviewRT", "FormPreviewStage", -5000f);
            AddPreviewCamera(target, "FormPreviewCam", 20f);
            PreviewCamera.orthographicSize = 0.95f;
            PreviewCamera.transform.localPosition = new Vector3(2.2f, 1.7f, -2.2f);
            PreviewCamera.transform.LookAt(Stage.transform.position);
        }

        /// <summary>Rebuilds the turning preview from the SAME geometry the world mesher would emit, so what the player sees
        /// here is what they will place. A form that is not valid yet (empty, a full cube) shows nothing.</summary>
        protected override void RebuildPreview()
        {
            PreviewDirty = false;
            if (Spin == null)
            {
                return;
            }

            for (int i = Spin.childCount - 1; i >= 0; i--)
            {
                Destroy(Spin.GetChild(i).gameObject);
            }

            Destroy(_previewMesh);
            _previewMesh = null;
            if (TryLookPreview())
            {
                return; // the form with its block's tile and the block shader (ShapeEditor.LookPreview.cs)
            }

            _previewMesh = EditorVoxelPreview.CustomShapeMesh(_model.CellBitmaps()[0].Bitmap);
            var material = EditorVoxelPreview.PreviewMaterial();
            if (_previewMesh == null || material == null)
            {
                return;
            }

            var go = new GameObject("FormPreview", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(Spin, false);
            go.transform.localPosition = new Vector3(-0.5f, -0.5f, -0.5f); // centre the unit cell on the pivot
            go.GetComponent<MeshFilter>().sharedMesh = _previewMesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.layer = Spin.gameObject.layer;
        }
    }
}
