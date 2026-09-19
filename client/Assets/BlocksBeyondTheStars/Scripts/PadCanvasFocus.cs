// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A gamepad's way onto an editor canvas (#1954): the two focus modes (tool panel ⟷ canvas, Start swaps, B leaves
    /// the canvas), the hand-over to <see cref="UiNav"/>, and the cell cursor with its outline. Extracted from the
    /// pixel editor (#1198), where it was born, so the texture editor and the form editor do not become copies
    /// three and four. The rules live in <see cref="PadCanvasLogic"/> (engine-free, pinned by the headless suite);
    /// this class only reads the pad and draws the cursor. An editor owns one, calls <see cref="Tick"/> once per
    /// frame, and reads <see cref="CellX"/> / <see cref="CellY"/> where its mouse path reads the pointer.
    /// </summary>
    public sealed class PadCanvasFocus
    {
        private readonly GameObject _uiRoot;
        private RectTransform _canvas;
        private RectTransform _cursor;
        private float _nextStepAt;
        private float _nextLayerAt;
        private int _cellX, _cellY;

        /// <param name="uiRoot">The editor's canvas root — the object <see cref="UiNav.Enable"/> was called on.</param>
        /// <param name="canvas">The rect the cells cover (top-left anchored, as <see cref="UiKit.Place"/> makes them).</param>
        public PadCanvasFocus(GameObject uiRoot, RectTransform canvas)
        {
            _uiRoot = uiRoot;
            _canvas = canvas;
        }

        /// <summary>True while the pad owns the canvas (the cell cursor is live, the tool panel is parked).</summary>
        public bool CanvasMode { get; private set; }

        /// <summary>True on the frame the pad entered the canvas — editors recentre their view on it.</summary>
        public bool JustEntered { get; private set; }

        public int CellX => _cellX;

        public int CellY => _cellY;

        /// <summary>Cells across / down the visible canvas. Set whenever the canvas content changes size.</summary>
        public int CellsX { get; private set; } = 1;

        public int CellsY { get; private set; } = 1;

        /// <summary>-1 / +1 on the frame the d-pad stepped down / up, else 0 — the optional third axis of a layered
        /// editor. Only reported in canvas mode; on the panel the d-pad walks the tools.</summary>
        public int LayerStep { get; private set; }

        public void SetCells(int cellsX, int cellsY)
        {
            CellsX = Mathf.Max(1, cellsX);
            CellsY = Mathf.Max(1, cellsY);
            PadCanvasLogic.ClampCursor(ref _cellX, ref _cellY, CellsX, CellsY);
        }

        /// <summary>Follows the editor to a rebuilt canvas rect — a no-op while it is still the same one. The focus
        /// MODE survives the rebuild: switching tabs on a pad must not throw the player back to the tool panel.</summary>
        public void EnsureCanvas(RectTransform canvas)
        {
            if (!ReferenceEquals(_canvas, canvas))
            {
                Rebind(canvas);
            }
        }

        /// <summary>Points the helper at a rebuilt canvas rect (the editor rebuilt its UI). The old cursor object
        /// went down with the old canvas.</summary>
        public void Rebind(RectTransform canvas)
        {
            _canvas = canvas;
            _cursor = null;
        }

        public void SetCell(int cellX, int cellY)
        {
            _cellX = cellX;
            _cellY = cellY;
            PadCanvasLogic.ClampCursor(ref _cellX, ref _cellY, CellsX, CellsY);
        }

        /// <summary>
        /// Once per frame. <paramref name="pad"/> = the gamepad is the active device. Handles the mode swap, parks or
        /// releases the menu navigation, walks the cursor and shows or hides its outline. Returns
        /// <see cref="CanvasMode"/> — false for the mouse, whose pointer needs no cursor.
        /// </summary>
        public bool Tick(bool pad)
        {
            JustEntered = false;
            LayerStep = 0;
            if (_uiRoot == null)
            {
                return false;
            }

            if (!pad)
            {
                UiNav.SetSuspended(_uiRoot, false); // mouse in hand — the tools are always live
                RefreshCursor(false);
                return false;
            }

            bool was = CanvasMode;
            CanvasMode = PadCanvasLogic.NextCanvasMode(
                CanvasMode, InputMap.Down(InputAction.UiMenu), InputMap.Down(InputAction.UiCancel));
            if (CanvasMode && !was)
            {
                _cellX = CellsX / 2; // enter at the middle of the canvas, not in a corner
                _cellY = CellsY / 2;
                _nextStepAt = 0f;
                JustEntered = true;
            }

            UiNav.SetSuspended(_uiRoot, CanvasMode);
            if (CanvasMode)
            {
                PadCanvasLogic.StepCursor(
                    InputMap.PadStickX(), InputMap.PadStickY(), Time.unscaledTime,
                    ref _nextStepAt, ref _cellX, ref _cellY, CellsX, CellsY);
                LayerStep = ReadLayerStep();
            }

            RefreshCursor(CanvasMode);
            return CanvasMode;
        }

        /// <summary>D-pad up / down as one repeat-gated step (+1 up, -1 down). The d-pad's left/right already has a
        /// gated reader (<see cref="InputMap.PadDpadStep"/>, the hotbar cycle); up/down only exists raw.</summary>
        private int ReadLayerStep()
        {
            float y = InputMap.PadDpadY();
            if (Mathf.Abs(y) < PadCanvasLogic.StickThreshold)
            {
                _nextLayerAt = 0f;
                return 0;
            }

            if (_nextLayerAt > 0f && Time.unscaledTime < _nextLayerAt)
            {
                return 0;
            }

            _nextLayerAt = Time.unscaledTime + (_nextLayerAt <= 0f ? PadCanvasLogic.StepFirst : 0.18f);
            return y > 0f ? 1 : -1;
        }

        /// <summary>Leaves canvas mode (the editor closed a sub-dialog over it, or is going away).</summary>
        public void Release()
        {
            CanvasMode = false;
            if (_uiRoot != null)
            {
                UiNav.SetSuspended(_uiRoot, false);
            }

            RefreshCursor(false);
        }

        private void RefreshCursor(bool show)
        {
            if (!show || _canvas == null)
            {
                if (_cursor != null && _cursor.gameObject.activeSelf)
                {
                    _cursor.gameObject.SetActive(false);
                }

                return;
            }

            if (_cursor == null)
            {
                var go = new GameObject("PadCursor", typeof(RectTransform));
                go.transform.SetParent(_canvas, false);
                var img = go.AddComponent<Image>();
                img.sprite = UiKit.ButtonSprite; // sliced outline — reads over any pixel colour underneath
                img.type = Image.Type.Sliced;
                img.color = new Color(0.45f, 0.92f, 1f, 0.75f);
                img.raycastTarget = false;
                _cursor = (RectTransform)go.transform;
            }

            if (!_cursor.gameObject.activeSelf)
            {
                _cursor.gameObject.SetActive(true);
            }

            _cursor.SetAsLastSibling(); // above overlays the editor added to the canvas later
            float cw = _canvas.rect.width / CellsX, ch = _canvas.rect.height / CellsY;
            UiKit.Place(_cursor.gameObject, _cellX * cw, _cellY * ch, cw, ch);
        }
    }
}
