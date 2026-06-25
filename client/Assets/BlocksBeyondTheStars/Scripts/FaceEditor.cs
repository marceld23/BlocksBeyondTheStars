// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-game pixel-face editor (opened from the Character menu tab). The player paints a
    /// <see cref="FacePalette.Size"/>×<see cref="FacePalette.Size"/> face on a live canvas with a palette +
    /// eraser; <b>Apply</b> stores it in <see cref="ClientSettings.FacePixels"/>, shows it on their own figure
    /// and sends it to the server, which persists it and relays it so other players see the face on this
    /// player's avatar. The large painted canvas is the face preview; the rotating figure in the Character tab
    /// updates once the editor is closed. Modern uGUI, mirroring <see cref="MaterialEditor"/>.
    /// </summary>
    public sealed class FaceEditor : MonoBehaviour
    {
        // Host-supplied hooks so one editor serves both the in-game Character tab and the main-menu Avatar
        // Designer. Set these right after AddComponent (before Start runs, which is the next frame).
        public string InitialFace;              // encoded face to preload onto the canvas
        public Func<string, string> Localizer;  // localization lookup (key → text)
        public Action<string> OnApply;          // receives the encoded face when the player hits Apply

        private const int Size = FacePalette.Size;

        private readonly int[] _grid = new int[FacePalette.Pixels];
        private int _brush = 1; // current palette index (0 = eraser/transparent)

        private Texture2D _tex;
        private RectTransform _canvasRt;
        private RawImage _canvas;

        private Canvas _ui;
        private Image _activeSwatch;
        private readonly Image[] _swatches = new Image[FacePalette.Colors.Length]; // index 0 reused as eraser

        private void Start()
        {
            _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            LoadFrom(InitialFace);
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_tex != null) Destroy(_tex);
        }

        // ── live painting ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_canvasRt == null) return;

            bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
            if (!left && !right) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp)) return;

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0].
            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            float u = Mathf.Clamp01(lp.x / w);
            float fromTop = Mathf.Clamp01(-lp.y / h);
            int gx = Mathf.Clamp(Mathf.RoundToInt(u * (Size - 1)), 0, Size - 1);
            int gy = Mathf.Clamp(Mathf.RoundToInt(fromTop * (Size - 1)), 0, Size - 1); // top row = gy 0

            Paint(gx, gy, right ? 0 : _brush);
        }

        /// <summary>Sets one pixel (grid + display texture) and applies it. Grid row 0 is the TOP; the texture's
        /// row 0 is the BOTTOM, so the display flips vertically.</summary>
        private void Paint(int gx, int gy, int index)
        {
            int cell = gy * Size + gx;
            if (_grid[cell] == index) return;

            _grid[cell] = index;
            _tex.SetPixel(gx, Size - 1 - gy, DisplayColor(index));
            _tex.Apply();
        }

        private static Color DisplayColor(int index)
            => index == 0 ? FacePalette.EditorBackground : (Color)FacePalette.ColorOf(index);

        private void RenderAll()
        {
            for (int gy = 0; gy < Size; gy++)
            for (int gx = 0; gx < Size; gx++)
            {
                _tex.SetPixel(gx, Size - 1 - gy, DisplayColor(_grid[gy * Size + gx]));
            }

            _tex.Apply();
        }

        private void LoadFrom(string face)
        {
            var grid = FacePalette.Decode(face);
            Array.Copy(grid, _grid, FacePalette.Pixels);
            RenderAll();
        }

        // ── UI ───────────────────────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Face Editor UI");
            _ui.sortingOrder = 60; // above the in-game menu (CraftingTechShipUI is sortingOrder 50)
            var root = _ui.transform;

            // Dim backdrop (also blocks clicks reaching the menu behind).
            UiKit.AddPanel(root, 0f, 0f, 1920f, 1080f, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.AddPanel(root, 610f, 60f, 700f, 960f, UiKit.PanelFill).transform;
            UiKit.AddText(panel, 24f, 18f, 652f, 30f, L("ui.face.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Paint surface (point-filtered → crisp big pixels).
            var canvasGo = new GameObject("FaceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 94f, 64f, 512f, 512f);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            // Palette: colours 1..N then an eraser (index 0).
            UiKit.AddText(panel, 24f, 592f, 400f, 24f, L("ui.face.palette"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            float px = 24f, py = 624f;
            int col = 0;
            for (int i = 1; i < FacePalette.Colors.Length; i++)
            {
                int idx = i;
                _swatches[i] = MakeSwatch(panel, px + col * 50f, py, FacePalette.ColorOf(i), () => SetBrush(idx, _swatches[idx]));
                if (++col >= 8) { col = 0; py += 50f; }
            }

            // Eraser swatch (transparent → shown as the editor background colour, labelled "E").
            var eraser = MakeSwatch(panel, px + col * 50f, py, FacePalette.EditorBackground, () => SetBrush(0, _swatches[0]));
            _swatches[0] = eraser;
            UiKit.AddText(eraser.transform, 0f, 0f, 44f, 44f, "E", 18, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Buttons.
            UiKit.AddButton(panel, 24f, 832f, 220f, 56f, L("ui.face.apply"), Apply);
            UiKit.AddButton(panel, 260f, 832f, 180f, 56f, L("ui.face.clear"), () => { Array.Clear(_grid, 0, _grid.Length); RenderAll(); });
            UiKit.AddButton(panel, 456f, 832f, 220f, 56f, L("ui.menu.back"), Close);

            UiKit.AddText(panel, 24f, 904f, 652f, 24f, L("ui.face.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

            SetBrush(_brush, _swatches[_brush]);
        }

        private Image MakeSwatch(Transform parent, float x, float y, Color color, Action onClick)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, 44f, 44f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            return img;
        }

        private void SetBrush(int index, Image swatch)
        {
            _brush = index;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one;
            _activeSwatch = swatch;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one * 1.18f;
        }

        private void Apply()
        {
            OnApply?.Invoke(FacePalette.Encode(_grid));
            Close();
        }

        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }
}
