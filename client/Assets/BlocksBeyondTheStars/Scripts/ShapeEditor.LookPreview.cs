// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one-block shape designer's preview "as in the world": instead of the form in one flat
    /// colour, the form with the tile of its block on the world's atlas and the block shader (<see cref="LookShapePreview"/>),
    /// under the shared fixed look light (<see cref="LookPreview"/>). The block is the crafting tab's material
    /// (<see cref="PreviewBlockKey"/>), stone where the editor was opened from the shaping tool (no material is chosen
    /// there). "Pixels only" or no world atlas: the classic flat-colour preview.
    /// </summary>
    public sealed partial class ShapeEditor
    {
        private const string LookFallbackBlock = "stone";

        /// <summary>The block whose tile the preview shows (the crafting tab sets its material); null = stone.</summary>
        public string PreviewBlockKey;

        private LookPreviewLight _lookLight;
        private Material _lookMaterial;

        /// <summary>After the stage is built: the look light on the preview camera.</summary>
        private void AttachLookPreview()
        {
            _lookLight = LookPreview.Attach(PreviewCamera);
            if (_lookLight != null)
            {
                LookPreview.Changed += OnLookPreviewChanged;
            }
        }

        private void OnLookPreviewChanged() => PreviewDirty = true;

        private void DestroyLookPreview()
        {
            LookPreview.Changed -= OnLookPreviewChanged;
            EditorSceneKit.Discard(_lookMaterial);
            _lookMaterial = null;
        }

        /// <summary>Builds the look preview of the current form under <see cref="LayerCanvasEditor.Spin"/> into
        /// <see cref="_previewMesh"/>; false when the classic preview is to be built instead.</summary>
        private bool TryLookPreview()
        {
            if (_lookLight == null)
            {
                return false;
            }

            _lookLight.Blocks = false;
            var content = Game != null ? Game.Content : null;
            var atlas = Game != null ? Game.Atlas : null;
            if (!LookPreview.AsInWorld || content == null || atlas == null)
            {
                return false;
            }

            var block = (string.IsNullOrEmpty(PreviewBlockKey) ? null : content.GetBlock(PreviewBlockKey)) ?? content.GetBlock(LookFallbackBlock);
            if (block == null)
            {
                return false;
            }

            _lookMaterial ??= LookShapePreview.CreateMaterial(atlas);
            var cells = _model.CellBitmaps();
            bool valid = cells.Count > 0 && BlocksBeyondTheStars.Shared.World.CustomShape.IsValidVoxels(cells[0].Bitmap); // as the classic preview
            var mesh = _lookMaterial != null && valid ? LookShapePreview.Build(cells, content, atlas, block.NumericId, Color.clear) : null;
            if (mesh == null)
            {
                return _lookMaterial != null; // an empty form shows nothing either way
            }

            _previewMesh = mesh; // destroyed like the classic one (OnDestroy, the next rebuild)
            var go = new GameObject("FormPreview", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(Spin, false);
            go.transform.localPosition = new Vector3(-0.5f, -0.5f, -0.5f); // centre the unit cell on the pivot
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _lookMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.layer = Spin.gameObject.layer;
            _lookLight.Blocks = true;
            _lookLight.MeanMip = DistanceSoftLook.TileMeanMip(BlockTextureAtlas.Tile, QualitySettings.globalTextureMipmapLimit);
            return true;
        }
    }
}
