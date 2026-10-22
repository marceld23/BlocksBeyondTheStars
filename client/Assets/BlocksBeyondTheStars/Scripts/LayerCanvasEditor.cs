// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shared base of the main menu's layer-canvas editors (<see cref="FormEditor"/>, <see cref="ToolLookEditor"/>):
    /// a small voxel model painted one horizontal layer at a time on a flat canvas, with a turning 3-D preview beside
    /// it. The base owns the frame loop (mouse or gamepad onto the canvas, a stroke as one undo step, the preview
    /// rebuilt once per stroke), the keys (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z, PageUp/PageDown or W/S for the layer),
    /// layer stepping, undo/redo, the status and hint lines, the preview stage and the screen shell (title, columns,
    /// hint line, gamepad focus on the canvas). A derived editor supplies its Unity-free model
    /// (<see cref="ILayerCanvas"/>), what painting one cell means, how the layer, the labels and the preview are drawn,
    /// and its few extras (<see cref="OnPadCanvasFrame"/>, <see cref="OnCellPainted"/>, <see cref="OnStrokeEnded"/>).
    /// </summary>
    public abstract class LayerCanvasEditor : MonoBehaviour
    {
        public AppShell Shell;

        protected Canvas Ui;
        protected Texture2D Tex;               // the layer canvas, one pixel per cell
        protected RectTransform CanvasRt;
        protected PadCanvasFocus Pad;
        protected Text StatusLabel;
        protected Text HintLabel;
        protected bool Stroking;
        protected bool PreviewDirty = true;

        // 3-D preview.
        protected GameObject Stage;
        protected Transform Spin;
        protected Camera PreviewCamera;
        protected RawImage PreviewView;

        /// <summary>The model the canvas paints.</summary>
        protected abstract ILayerCanvas Layers { get; }

        /// <summary>Canvas cells across (X).</summary>
        protected abstract int CellsX { get; }

        /// <summary>Canvas cells up the canvas (Z, the far edge at the top).</summary>
        protected abstract int CellsZ { get; }

        /// <summary>How fast the preview turns on its own (degrees per second) while no gamepad steers it.</summary>
        protected abstract float SpinDegreesPerSecond { get; }

        /// <summary>Hint line key while the mouse works.</summary>
        protected abstract string HintKeyMouse { get; }

        /// <summary>Hint line key while the gamepad is on the canvas.</summary>
        protected abstract string HintKeyPadCanvas { get; }

        /// <summary>Hint line key while the gamepad is on the panels.</summary>
        protected abstract string HintKeyPadPanel { get; }

        protected static string L(string key) => UiKit.L(key);

        /// <summary>Draws the current layer into <see cref="Tex"/>.</summary>
        protected abstract void RenderLayer();

        /// <summary>Brings the layer, budget and status labels up to date.</summary>
        protected abstract void RefreshLabels();

        /// <summary>Rebuilds the 3-D preview; clears <see cref="PreviewDirty"/>.</summary>
        protected abstract void RebuildPreview();

        /// <summary>After an undo, a redo or a helper action (<see cref="Do"/>).</summary>
        protected abstract void AfterCanvasEdit();

        /// <summary>Paints one cell of the current layer: <paramref name="primary"/> is the left mouse button or A
        /// (paint), <paramref name="secondary"/> the right mouse button or X (erase). True when something changed.</summary>
        protected abstract bool PaintCell(int x, int z, bool primary, bool secondary);

        /// <summary>Once per frame while the gamepad is on the canvas, after the layer step and the undo button and
        /// before the paint buttons are read.</summary>
        protected virtual void OnPadCanvasFrame()
        {
        }

        /// <summary>After a painted cell changed the layer.</summary>
        protected virtual void OnCellPainted()
        {
        }

        /// <summary>After a stroke ended (the buttons were released).</summary>
        protected virtual void OnStrokeEnded()
        {
        }

        // ---------------------------------------------------------------- life cycle

        /// <summary>Takes the screen, the preview stage and the canvas texture down with the editor.</summary>
        protected virtual void OnDestroy()
        {
            Pad?.Release();
            if (Ui != null)
            {
                Destroy(Ui.gameObject);
            }

            if (Stage != null)
            {
                Destroy(Stage);
            }

            ReleasePreviewTarget();
            Destroy(Tex);
        }

        protected void ReleasePreviewTarget()
        {
            if (PreviewCamera != null && PreviewCamera.targetTexture != null)
            {
                PreviewCamera.targetTexture.Release();
            }
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (CanvasRt == null)
            {
                return;
            }

            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            Spin?.Rotate(0f, (pad ? InputMap.PadLookX() * 90f : SpinDegreesPerSecond) * Time.unscaledDeltaTime, 0f, Space.Self);

            HandleKeys();
            Pad.SetCells(CellsX, CellsZ);
            bool padCanvas = Pad.Tick(pad);
            SetHint(!pad ? HintKeyMouse : padCanvas ? HintKeyPadCanvas : HintKeyPadPanel);

            bool primary, secondary;
            int x, z;
            if (padCanvas)
            {
                if (Pad.LayerStep != 0)
                {
                    StepLayer(Pad.LayerStep);
                }

                if (InputMap.PadDown(PadButton.Rb))
                {
                    DoUndo();
                }

                OnPadCanvasFrame();
                primary = InputMap.PadHeld(PadButton.A);
                secondary = InputMap.PadHeld(PadButton.X);
                x = Pad.CellX;
                z = CellsZ - 1 - Pad.CellY; // the canvas looks from above: its top row is the far edge (+Z), where a tool points
            }
            else
            {
                primary = Input.GetMouseButton(0);
                secondary = Input.GetMouseButton(1);
                if (!TryCanvasCell(out x, out z))
                {
                    primary = secondary = false;
                }
            }

            ApplyStroke(primary, secondary, x, z);
            if (PreviewDirty && !Stroking)
            {
                RebuildPreview(); // once per stroke, not once per cell — merging eight blocks is not free
            }
        }

        /// <summary>While a button is held, paints the cell under the cursor — the first cell opens a stroke, which is
        /// ONE undo step; when the buttons are released, closes the stroke.</summary>
        private void ApplyStroke(bool primary, bool secondary, int x, int z)
        {
            if (primary || secondary)
            {
                if (!Stroking)
                {
                    Stroking = true;
                    Layers.BeginStroke();
                }

                if (PaintCell(x, z, primary, secondary))
                {
                    RenderLayer();
                    PreviewDirty = true;
                    OnCellPainted();
                }
            }
            else if (Stroking)
            {
                Stroking = false;
                Layers.EndStroke();
                OnStrokeEnded();
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
            if (!RectTransformUtility.RectangleContainsScreenPoint(CanvasRt, Input.mousePosition, null)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(CanvasRt, Input.mousePosition, null, out var lp))
            {
                return false;
            }

            // Place() anchors top-left with pivot (0,1): local x ∈ [0,w], y ∈ [-h,0].
            float w = CanvasRt.rect.width, h = CanvasRt.rect.height;
            x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(lp.x / w) * CellsX), 0, CellsX - 1);
            int fromTop = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(-lp.y / h) * CellsZ), 0, CellsZ - 1);
            z = CellsZ - 1 - fromTop;
            return true;
        }

        // ---------------------------------------------------------------- commands

        protected void StepLayer(int delta)
        {
            Layers.SetLayer(Layers.Layer + delta);
            RenderLayer();
            RefreshLabels();
        }

        protected void DoUndo()
        {
            if (Layers.Undo())
            {
                AfterCanvasEdit();
            }
        }

        protected void DoRedo()
        {
            if (Layers.Redo())
            {
                AfterCanvasEdit();
            }
        }

        protected void Do(System.Action change)
        {
            change();
            AfterCanvasEdit();
        }

        // ---------------------------------------------------------------- labels

        protected void RefreshAll()
        {
            RenderLayer();
            PreviewDirty = true;
            RefreshLabels();
        }

        protected void SetStatus(string text, Color color)
        {
            if (StatusLabel != null)
            {
                StatusLabel.text = text;
                StatusLabel.color = color;
            }
        }

        private void SetHint(string key)
        {
            if (HintLabel != null)
            {
                HintLabel.text = L(key);
            }
        }

        // ---------------------------------------------------------------- preview stage

        /// <summary>The preview stage, parked far below the menu backdrop, with its turntable (<see cref="Spin"/>);
        /// returns the render target <see cref="AddPreviewCamera"/> then draws into.</summary>
        protected RenderTexture BeginPreviewStage(string targetName, string stageName, float parkY)
        {
            var target = new RenderTexture(512, 512, 16) { name = targetName };
            Stage = new GameObject(stageName);
            Stage.transform.position = new Vector3(0f, parkY, 0f); // parked far from the menu backdrop
            Spin = new GameObject("Spin").transform;
            Spin.SetParent(Stage.transform, false);
            return target;
        }

        /// <summary>The orthographic preview camera on the stage, drawing into <paramref name="target"/>, which the
        /// preview view (<see cref="PreviewView"/>) shows.</summary>
        protected void AddPreviewCamera(RenderTexture target, string cameraName, float farClip)
        {
            var camGo = new GameObject(cameraName, typeof(Camera));
            camGo.transform.SetParent(Stage.transform, false);
            PreviewCamera = camGo.GetComponent<Camera>();
            PreviewCamera.orthographic = true;
            PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            PreviewCamera.backgroundColor = new Color(0.03f, 0.07f, 0.14f, 1f);
            PreviewCamera.targetTexture = target;
            PreviewCamera.nearClipPlane = 0.05f;
            PreviewCamera.farClipPlane = farClip;
            if (PreviewView != null)
            {
                PreviewView.texture = target;
            }
        }

        // ---------------------------------------------------------------- screen shell

        /// <summary>The editor's screen canvas with its title line; returns the root the columns go into.
        /// <see cref="EndShell"/> closes it.</summary>
        protected Transform BeginShell(string canvasName, string titleKey)
        {
            Ui = UiKit.CreateCanvas(canvasName);
            Ui.sortingOrder = 5;
            var root = Ui.transform;
            UiKit.AddText(root, 16f, 2f, 1400f, 26f, L(titleKey), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            return root;
        }

        /// <summary>The hint line at the bottom, menu navigation and the gamepad focus on the layer canvas — so
        /// <see cref="CanvasRt"/> must exist by now.</summary>
        protected void EndShell(Transform root, string hintKey)
        {
            HintLabel = UiKit.AddText(root, 16f, 1080f - 30f, 1880f, 24f, L(hintKey), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiNav.Enable(Ui.gameObject, padHints: false); // this editor draws its own hint line
            Pad = new PadCanvasFocus(Ui.gameObject, CanvasRt);
        }

        /// <summary>One of the three full-height columns of the screen.</summary>
        protected static Transform AddColumn(Transform root, float x, float w)
            => UiKit.AddPanel(root, x, 32f, w, 1010f, UiKit.PanelFill).transform;

        /// <summary>A row of a scrolling list (the form library, the tool list): 316 × 44.</summary>
        protected static RectTransform AddListRow(Transform parent)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(316f, 44f);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
            return rt;
        }
    }
}
