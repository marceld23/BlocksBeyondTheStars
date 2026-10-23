// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The base of the modal in-world panels that WorldRig creates next to the player (#2913): it owns what every one of
    /// them used to hand-roll — the <see cref="Game"/> link (which also registers the panel in
    /// <see cref="GameBootstrap.Context"/>, the replacement for the old <c>static Instance</c>), the lazily created
    /// <c>UiKit.CreateCanvas</c> with its sort order and gamepad focus (<c>UiNav.Enable</c>), the shown/open-frame
    /// state, the standard modal overlay, the menu-owner arbiter calls, the Esc / pad-B test, the localizer shortcut and
    /// the canvas cleanup on destroy. No Awake, Update or Start here — each panel keeps its own.
    /// <para>Three shapes use it (docs/developer/UI_PANELS.md):</para>
    /// <list type="bullet">
    /// <item><b>B — build once, toggle active</b> (e.g. BeamPadUi): the static chrome goes into
    /// <see cref="BuildChrome"/>; Open = <see cref="EnsureCanvas"/> + <see cref="ShowCanvas"/>, Close =
    /// <see cref="HideCanvas"/>.</item>
    /// <item><b>B2 — canvas once, overlay per open</b> (e.g. TrainCabUi): an empty canvas from <see cref="EnsureCanvas"/>,
    /// each open builds a fresh <see cref="AddModal"/> overlay and plays <see cref="PlayOpenEffect"/>; Close =
    /// <see cref="DestroyOverlay"/> + <see cref="HideCanvas"/>.</item>
    /// <item><b>A — build on open, destroy on close</b> (e.g. ContainerFilterUi): <see cref="CreateCanvasNow"/> on every
    /// open, <see cref="DestroyCanvas"/> on close; <see cref="IsOpen"/> is <see cref="IsBuilt"/>.</item>
    /// </list>
    /// <para>Rules carried over from the panels: an open panel registers as a menu owner through
    /// <see cref="SetMenuOwned"/> (#413 — the arbiter frees the cursor and pauses control while ANY owner is open, and
    /// re-locks only when the last one closes); the open effect plays once per open, never from a rebuild (#2302).</para>
    /// </summary>
    public abstract class UiPanel : MonoBehaviour
    {
        private GameBootstrap _game;
        private Canvas _canvas;
        private GameObject _overlay;

        /// <summary>The world rig's bootstrap. Setting it moves the panel's registration in
        /// <see cref="GameBootstrap.Context"/> from the old rig to the new one (WorldRig's <c>x.Game = boot;</c>).</summary>
        public GameBootstrap Game
        {
            get => _game;
            set
            {
                if (ReferenceEquals(_game, value))
                {
                    return;
                }

                _game?.Context.Unregister(this);
                _game = value;
                _game?.Context.Register(this);
            }
        }

        /// <summary>The panel's own canvas, or null before it is built (shape A: while closed).</summary>
        protected Canvas Canvas => _canvas;

        /// <summary>Test seam (like <c>InputMap.DeviceOverrideForTest</c>): the canvas, so an EditMode test can inspect it
        /// and clean it up with <c>DestroyImmediate</c>.</summary>
        public Canvas CanvasForTest => _canvas;

        /// <summary>True once the canvas exists.</summary>
        public bool IsBuilt => _canvas != null;

        /// <summary>The scrim of the current <see cref="AddModal"/> overlay, or null.</summary>
        protected GameObject Overlay => _overlay;

        /// <summary>True while the panel is open.</summary>
        protected bool Shown { get; set; }

        /// <summary>The frame the panel last opened in — the key press that opened it must not also close it.</summary>
        protected int OpenFrame { get; private set; } = -1;

        /// <summary>True while the panel is open (gameplay hotkeys stand down).</summary>
        public virtual bool IsOpen => Shown;

        /// <summary>The name of the canvas GameObject.</summary>
        protected abstract string CanvasName { get; }

        /// <summary>The canvas's sorting order (the HUD/chat sit below, the world map at 60).</summary>
        protected abstract int CanvasSortingOrder { get; }

        /// <summary>Shape B: builds the static chrome under the fresh canvas, once (before it is hidden).</summary>
        protected virtual void BuildChrome(Transform root)
        {
        }

        /// <summary>Creates the canvas once: sort order, pad focus, then <see cref="BuildChrome"/>; hidden unless
        /// <paramref name="hidden"/> is false. A no-op once built.</summary>
        protected void EnsureCanvas(bool hidden = true)
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas(CanvasName);
            _canvas.sortingOrder = CanvasSortingOrder;
            UiNav.Enable(_canvas.gameObject);
            BuildChrome(_canvas.transform);
            if (hidden)
            {
                _canvas.gameObject.SetActive(false);
            }
        }

        /// <summary>Shape A: a fresh, active canvas for this open (no hide/show cycle); marks the panel shown.</summary>
        protected void CreateCanvasNow()
        {
            _canvas = UiKit.CreateCanvas(CanvasName);
            _canvas.sortingOrder = CanvasSortingOrder;
            UiNav.Enable(_canvas.gameObject);
            Shown = true;
            OpenFrame = Time.frameCount;
        }

        /// <summary>Shapes B/B2: builds the canvas if needed, marks the panel shown in this frame and activates it.</summary>
        protected void ShowCanvas()
        {
            EnsureCanvas();
            Shown = true;
            OpenFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
        }

        /// <summary>Shapes B/B2: marks the panel closed and deactivates the canvas (kept for the next open).</summary>
        protected void HideCanvas()
        {
            Shown = false;
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }
        }

        /// <summary>Shape A: marks the panel closed and destroys its canvas (and with it the overlay).</summary>
        protected void DestroyCanvas()
        {
            Shown = false;
            _overlay = null;
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
        }

        /// <summary>The standard modal (<c>UiKit.AddModalOverlay</c>) on this panel's canvas; remembers the scrim as
        /// <see cref="Overlay"/> and returns the dialog panel to fill.</summary>
        protected Transform AddModal(float x, float y, float w, float h, float scrim = UiKit.ScrimDialog)
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, x, y, w, h, scrim);
            _overlay = overlay;
            return panel;
        }

        /// <summary>Destroys the current <see cref="Overlay"/>, if any.</summary>
        protected void DestroyOverlay()
        {
            if (_overlay != null)
            {
                Destroy(_overlay);
            }

            _overlay = null;
        }

        /// <summary>#2302: the open effect of the current overlay — call it once when the panel OPENS, never from a
        /// rebuild of an open one.</summary>
        protected void PlayOpenEffect() => UiKit.OpenModal(_overlay);

        /// <summary>#413: registers (or releases) this panel as a menu owner with the arbiter — control freezes and the
        /// cursor is free while any owner is open; it re-locks only once none is.</summary>
        protected void SetMenuOwned(bool open) => Game?.SetMenuOwner(this, open);

        /// <summary>Marks this frame's menu key as consumed, so the same Esc does not also pop the quit prompt (#413 N1).</summary>
        protected void ConsumeMenuInput() => Game?.MarkMenuInputHandled();

        /// <summary>The menu verb (Escape or pad B) — by default not in the frame the panel opened in.</summary>
        protected bool CancelPressed(bool skipOpenFrame = true)
            => (!skipOpenFrame || Time.frameCount != OpenFrame) && InputMap.Down(InputAction.UiCancel);

        /// <summary>The localized text for <paramref name="key"/>, or the key itself without a localizer.</summary>
        protected string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>Unregisters the panel and destroys its canvas — a canvas is a top-level object of its own and would
        /// otherwise outlive the rig, one more hidden canvas per world join (#2303, #2925). Overrides call this last.</summary>
        protected virtual void OnDestroy()
        {
            _game?.Context.Unregister(this);
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
