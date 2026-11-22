// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The form editor's preview "as in the world": the form with its material's tile on the
    /// world atlas and the block shader, the dye as the world's player-dye tint (<see cref="LookShapePreview"/>), under
    /// the shared fixed look light (<see cref="LookPreview"/>). The plain mesh and material stay built underneath and
    /// come back for "pixels only".
    /// </summary>
    public sealed partial class FormEditor
    {
        private LookPreviewLight _lookLight;
        private Material _lookMaterial;
        private Mesh _lookMesh;

        private void AttachLookPreview()
        {
            _lookLight = LookPreview.Attach(PreviewCamera);
            if (_lookLight != null)
            {
                LookPreview.Changed += MarkLookPreviewDirty;
            }
        }

        private void MarkLookPreviewDirty()
        {
            if (_lookLight != null)
            {
                PreviewDirty = true;
            }
        }

        private void DestroyLookPreview()
        {
            LookPreview.Changed -= MarkLookPreviewDirty;
            EditorSceneKit.Discard(_lookMesh);
            EditorSceneKit.Discard(_lookMaterial);
            _lookMesh = null;
            _lookMaterial = null;
        }

        /// <summary>After the classic mesh is built: puts the look's mesh and material on the preview while the look shows,
        /// the classic material back otherwise.</summary>
        private void ApplyLookPreview()
        {
            if (_lookLight == null || _previewFilter == null)
            {
                return;
            }

            var renderer = _previewFilter.GetComponent<MeshRenderer>();
            EditorSceneKit.Discard(_lookMesh);
            _lookMesh = null;
            _lookLight.Blocks = false;
            if (LookPreview.AsInWorld && _content != null && _atlas != null && _materials.Count > 0)
            {
                _lookMaterial ??= LookShapePreview.CreateMaterial(_atlas);
                var block = _materials[Mathf.Clamp(_materialIndex, 0, _materials.Count - 1)];
                int dye = Mathf.Clamp(_dyeIndex, 0, Dyes.Length - 1);
                Color tint = dye > 0 ? new Color(Dyes[dye].r, Dyes[dye].g, Dyes[dye].b, 1f) : Color.clear;
                _lookMesh = _lookMaterial != null
                    ? LookShapePreview.Build(_model.CellBitmaps(), _content, _atlas, block.NumericId, tint)
                    : null;
            }

            if (_lookMesh != null)
            {
                _previewFilter.sharedMesh = _lookMesh;
                renderer.sharedMaterial = _lookMaterial;
                _lookLight.Blocks = true;
                _lookLight.MeanMip = DistanceSoftLook.TileMeanMip(BlockTextureAtlas.Tile, QualitySettings.globalTextureMipmapLimit);
            }
            else
            {
                renderer.sharedMaterial = _previewMaterial; // the classic mesh is already on the filter
            }
        }
    }
}
