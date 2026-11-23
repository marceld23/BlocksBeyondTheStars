// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's preview "as in the world": the camera gets the shared fixed look
    /// light (<see cref="LookPreview"/>), and a block tile is shown on a small wall drawn with the block shader
    /// (<see cref="LookBlockWall"/>) instead of the one cube. Tiles that are no block tile (creature hides, the avatar's
    /// fabrics, tools, icons), the picture tiles on their built-in form, and see-through blocks keep the classic model,
    /// lit by the look light. "Pixels only" is the classic preview.
    /// </summary>
    internal sealed partial class TexturePreviewRig
    {
        private const float ClassicFrame = 1.05f;

        private Texture2D _tile;
        private LookPreviewLight _look;
        private LookBlockWall _wall;
        private TextureEntry _lookEntry;
        private GameContent _lookContent;
        private Button _exampleButton;
        private Func<string, string> _localize;

        private void AttachLook(Texture2D tile)
        {
            _tile = tile;
            _look = LookPreview.Attach(_camera);
            if (_look == null)
            {
                return;
            }

            _wall = new LookBlockWall(_spin);
            LookPreview.Changed += OnLookChanged;
        }

        /// <summary>After the classic model is built: the wall in its place when the look shows and the tile is a block's.</summary>
        private void ShowLook(TextureEntry entry, GameContent content)
        {
            _lookEntry = entry;
            _lookContent = content;
            if (_wall == null)
            {
                return;
            }

            bool wall = LookPreview.AsInWorld && _wall.Show(entry, content, _tile);
            if (!wall)
            {
                _wall.Hide(); // "pixels only", or a tile the wall cannot show: the classic model alone
            }

            _renderer.enabled = !wall;
            _camera.orthographicSize = wall ? _wall.FrameSize : ClassicFrame;
            _look.Blocks = wall;
            _look.PaintedTile = wall; // what is painted here stays as painted in the world
            _look.MeanMip = _wall.MeanMip;
            _look.FloraTint = wall && _wall.WorldTinted ? _wall.FloraTint : Vector4.zero;
            RefreshExampleButton();
        }

        private void OnLookChanged() => ShowLook(_lookEntry, _lookContent);

        private void DestroyLook()
        {
            LookPreview.Changed -= OnLookChanged;
            _wall?.Destroy();
            _wall = null;
        }

        /// <summary>The look's two buttons over the 3-D preview whose rect on <paramref name="parent"/> is given: the
        /// "as in the world" / "pixels only" switch at the bottom and, for a tile the world tints, "World colour n of 3" at
        /// the top.</summary>
        public void AddLookButtons(Transform parent, float x, float y, float w, float h, Func<string, string> localize)
        {
            if (_wall == null || parent == null || localize == null)
            {
                return;
            }

            _localize = localize;
            LookPreview.AddToggle(parent, x, y, w, h, localize);
            float bw = Mathf.Min(w - 12f, 220f);
            _exampleButton = UiKit.AddTierButton(parent, x + ((w - bw) * 0.5f), y + 6f, bw, 32f, string.Empty, () =>
            {
                _wall?.NextExample();
                ShowLook(_lookEntry, _lookContent);
            }, ButtonTier.Quiet);
            RefreshExampleButton();
        }

        private void RefreshExampleButton()
        {
            if (_exampleButton == null)
            {
                return;
            }

            bool show = _wall != null && _wall.Shown && _wall.WorldTinted;
            _exampleButton.gameObject.SetActive(show);
            var text = _exampleButton.GetComponentInChildren<Text>(true);
            if (show && text != null && _localize != null)
            {
                text.text = string.Format(_localize("ui.tex.look_tint"), _wall.Example + 1, LookPreviewCells.ExampleCount);
                UiKit.FitButtonLabel(text);
            }
        }
    }
}
