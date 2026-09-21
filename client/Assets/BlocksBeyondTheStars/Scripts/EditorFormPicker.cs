// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The build editors' FORM picker (#1975): the in-game Shape action's grid of forms — icon + name over the
    /// shared <see cref="BuiltInForms"/> list — with <b>Automatic</b> (the block's own form: a bed as head + foot,
    /// a campfire as a slab, a ladder against its wall) in front. Replaces the −/+ stepper that reached only the
    /// first nine forms. A modal like <see cref="EditorLoadPicker"/>, so the pad walks it the same way.
    /// </summary>
    internal sealed class EditorFormPicker
    {
        private const float W = 640f, H = 620f;
        private const int Cols = 4;
        private const float CellW = 140f, CellH = 108f, Gap = 8f;

        private readonly AppShell _shell;
        private readonly GameObject _overlay;
        private readonly Transform _panel;
        private readonly Action _onClosed;

        private EditorFormPicker(AppShell shell, Transform canvas, Action onClosed)
        {
            _shell = shell;
            _onClosed = onClosed;
            var (overlay, panel) = UiKit.AddModalOverlay(canvas, 640f, 220f, W, H);
            _overlay = overlay;
            _panel = panel;
        }

        private string L(string key) => _shell != null ? _shell.L(key) : key;

        /// <summary>Opens the picker. <paramref name="tileId"/> is the atlas tile the icons are drawn on (the selected
        /// block's); <paramref name="current"/> the brush's form (<see cref="EditorPlacementRules.AutoForm"/> for
        /// Automatic). <paramref name="onPick"/> gets the chosen form; the picker closes afterwards.</summary>
        public static EditorFormPicker Show(AppShell shell, Transform canvas, BlockTextureAtlas atlas, ushort tileId, int current,
            Action<int> onPick, Action onClosed = null)
        {
            var picker = new EditorFormPicker(shell, canvas, onClosed);
            picker.Build(atlas, tileId, current, onPick);
            return picker;
        }

        public void Close()
        {
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
            }

            _onClosed?.Invoke();
        }

        private void Build(BlockTextureAtlas atlas, ushort tileId, int current, Action<int> onPick)
        {
            UiKit.AddText(_panel, 20f, 14f, W - 40f, 28f, L("ui.ed.form_title"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            var list = UiKit.ScrollList(_panel, 16f, 52f, W - 32f, H - 52f - 64f);
            int count = BuiltInForms.Options.Count + 1; // Automatic first
            int rows = (count + Cols - 1) / Cols;
            for (int r = 0; r < rows; r++)
            {
                var row = Row(list, CellH + Gap);
                for (int c = 0; c < Cols; c++)
                {
                    int i = r * Cols + c;
                    if (i >= count)
                    {
                        break;
                    }

                    int shape = i == 0 ? EditorPlacementRules.AutoForm : BuiltInForms.Options[i - 1].Shape;
                    string name = i == 0 ? L("ui.ed.form_auto") : L(BuiltInForms.Options[i - 1].LocKey);
                    float x = c * (CellW + Gap);
                    var btn = UiKit.AddButton(row, x, 0f, CellW, CellH, string.Empty, () => { onPick?.Invoke(shape); Close(); });
                    var icon = new GameObject("FormIcon", typeof(RectTransform));
                    icon.transform.SetParent(btn.transform, false);
                    UiKit.Place(icon, (CellW - 64f) * 0.5f, 8f, 64f, 64f);
                    var raw = icon.AddComponent<RawImage>();
                    raw.raycastTarget = false;
                    PaintIcon(raw, atlas, tileId, shape);
                    var cap = UiKit.AddText(btn.transform, 2f, CellH - 30f, CellW - 4f, 26f, name, 12, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                    cap.raycastTarget = false;
                    UiKit.AddOutline(cap);
                    if (shape == current)
                    {
                        btn.interactable = false;
                        var img = btn.GetComponent<Image>();
                        if (img != null)
                        {
                            img.color = UiKit.Cyan; // "this is the brush's form"
                        }
                    }
                }
            }

            UiKit.AddButton(_panel, 20f, H - 54f, W - 40f, 40f, L("ui.menu.back"), Close);
        }

        /// <summary>Draws a form's icon into <paramref name="raw"/>: the form on the block's tile (the in-game
        /// <see cref="ShapeIconFactory"/>), the plain tile for the cube and for Automatic. Without an atlas the
        /// image stays empty and the caption carries the meaning.</summary>
        public static void PaintIcon(RawImage raw, BlockTextureAtlas atlas, ushort tileId, int shape)
        {
            if (raw == null)
            {
                return;
            }

            Texture2D tex = shape > 0 && atlas != null ? ShapeIconFactory.ForBlock(atlas, tileId, shape) : null;
            if (tex != null)
            {
                raw.enabled = true;
                raw.texture = tex;
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            else if (atlas != null && atlas.Texture != null)
            {
                raw.enabled = true;
                raw.texture = atlas.Texture;
                raw.uvRect = atlas.TileUv(tileId);
            }
            else
            {
                raw.enabled = false;
            }
        }

        /// <summary>Row height under <see cref="UiKit.ScrollList"/> must be set on the rect itself (#1386).</summary>
        private static RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return rt;
        }
    }
}
