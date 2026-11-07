// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.HudFrames;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's own frame around the view (#3317) — a garland of Christmas lights, a slim helmet rim, whatever they
    /// painted in the character editor's HUD mode. It is drawn on its own diegetic canvas BELOW every HUD widget (hotbar,
    /// vitals, chat and toasts are never covered), so it rides the visor pipeline when that is on (kept clear of its corner
    /// marker by <see cref="HudFrameLayout.VisorInset"/>), works flat on Low and in the browser, and — like the whole HUD —
    /// never appears in a photo (the camera renders a clone of the view camera without any canvas).
    /// <para>
    /// It shows in first person, third person and space flight; it hides with the HUD in menus and cutscenes
    /// (<c>ScreenMenuOpen</c>, the prologue, the cinematic camera, a scene's <c>HudHidden</c>) and while the binoculars are
    /// raised (their optic owns the screen; <see cref="HudUi.SuppressCrosshair"/> is their flag). The canvas is pixel-exact
    /// (constant pixel size) so the 32-pixel art stays crisp at any resolution. With the setting off or no design, nothing
    /// is built and each frame costs a few comparisons; while it shows, it re-reads the design only when the settings
    /// string changes and relays out only when the screen, the size or the visor does.
    /// </para>
    /// <para>
    /// A new <c>hudframe:</c> unlock of the world (a calendar door, #3315) is announced once per installation, and put on
    /// right away when the player wears no frame yet — the toast says where to change it or switch it off.
    /// </para>
    /// </summary>
    public sealed class HudFrame : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        /// <summary>Below the nameplates (8) and the HUD (10): the frame is the backmost layer of the HUD.</summary>
        private const int SortingOrder = 6;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private HudFrameView _view;
        private string _designText;
        private int _width, _height, _size = -1;
        private bool _visor;
        private NetworkClient _network;
        private string _pendingToast;
        private WorldLoadingOverlay _veil;

        private void LateUpdate()
        {
            if (Game == null || Settings == null)
            {
                return;
            }

            WatchUnlocks();
            bool hudShown = HudShown();
            if (hudShown && _pendingToast != null)
            {
                ShowPendingToast();
            }

            bool want = hudShown && Settings.HudFrameOn && !string.IsNullOrEmpty(Settings.HudFrameDesign);
            if (!want)
            {
                Hide();
                return;
            }

            EnsureBuilt();
            if (!ReferenceEquals(_designText, Settings.HudFrameDesign))
            {
                _designText = Settings.HudFrameDesign;
                _view.SetDesign(HudFrameDesign.Parse(_designText));
            }

            if (!_view.HasFrame)
            {
                Hide();
                return;
            }

            Relayout();
            _view.SetAlpha(Settings.HudFrameAlpha);
            _view.Still = Settings.ReducedEffects;
            if (!_canvas.enabled)
            {
                _canvas.enabled = true;
            }
        }

        /// <summary>The same moments the HUD itself shows (see <c>HudUi.LateUpdate</c>), minus the raised binoculars.</summary>
        private bool HudShown()
            => !Game.ScreenMenuOpen && !Game.VegaPrologueActive && !Game.CinematicCameraActive && !GameBootstrap.HudHidden
               && !HudUi.SuppressCrosshair;

        private void Hide()
        {
            if (_canvas != null && _canvas.enabled)
            {
                _canvas.enabled = false;
            }
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateDiegeticCanvas("HudFrame", UiKit.HudRefW, UiKit.HudRefH);
            _canvas.sortingOrder = SortingOrder;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // one unit = one pixel: crisp art pixels
                scaler.scaleFactor = 1f;
            }

            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false; // nothing on it is clickable
            }

            _canvasRect = _canvas.GetComponent<RectTransform>();
            _view = HudFrameView.Create(_canvas.transform, "Frame", FilterMode.Point);
        }

        /// <summary>The unlock toast waits until the HUD is up and the world's loading veil has gone (the unlock list
        /// arrives with the join).</summary>
        private void ShowPendingToast()
        {
            if (_veil == null)
            {
                _veil = FindAnyObjectByType<WorldLoadingOverlay>();
            }

            if (_veil != null && _veil.VeilActive)
            {
                return;
            }

            Game.ShowMessage(_pendingToast);
            _pendingToast = null;
        }

        /// <summary>Lays the frame out again when the screen size, the size setting or the visor pipeline changed.</summary>
        private void Relayout()
        {
            var rect = _canvasRect.rect;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
            int size = Settings.HudFrameSizeLevel;
            bool visor = UiKit.HudCamera != null && UiKit.HudLayer >= 0;
            if (width == _width && height == _height && size == _size && visor == _visor)
            {
                return;
            }

            _width = width;
            _height = height;
            _size = size;
            _visor = visor;
            if (width > 0 && height > 0)
            {
                int inset = visor ? HudFrameLayout.VisorInset(width, height) : 0;
                _view.Layout(width, height, HudFrameLayout.TilePixels(width, height, size), inset);
            }
        }

        // ── unlocks (#3315 → #3317) ──────────────────────────────────────────────────────────────

        /// <summary>Follows the session's network client (a reconnect brings a new one) and its unlock list.</summary>
        private void WatchUnlocks()
        {
            var network = Game.Network;
            if (ReferenceEquals(network, _network))
            {
                return;
            }

            if (_network != null)
            {
                _network.CosmeticUnlocksChanged -= OnUnlocksChanged;
            }

            _network = network;
            if (_network != null)
            {
                _network.CosmeticUnlocksChanged += OnUnlocksChanged;
                OnUnlocksChanged(); // the list may have arrived before this component looked
            }
        }

        private void OnUnlocksChanged()
        {
            if (_network == null || Settings == null || Game?.Content == null)
            {
                return;
            }

            bool changed = false;
            foreach (string key in _network.CosmeticUnlockKeys)
            {
                if (key == null || !key.StartsWith(CosmeticKeys.HudFramePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var template = Game.Content.FindHudFrame(key.Substring(CosmeticKeys.HudFramePrefix.Length));
                if (template == null || !Settings.MarkHudFrameAnnounced(template.Key))
                {
                    continue;
                }

                changed = true;
                bool putOn = false;
                if (string.IsNullOrEmpty(Settings.HudFrameDesign) && HudFrameDesign.TryFromTemplate(template, out var design))
                {
                    Settings.HudFrameDesign = design.Encode();
                    putOn = true;
                }

                bool shows = putOn && Settings.HudFrameOn; // a player who switched frames off keeps them off
                _pendingToast = L(shows ? "ui.hudframe.unlocked_worn" : "ui.hudframe.unlocked").Replace("{name}", L(template.NameKey));
            }

            if (changed)
            {
                Settings.Save();
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.CosmeticUnlocksChanged -= OnUnlocksChanged;
                _network = null;
            }

            // The canvas is a top-level object (not under the world root), like the HUD's own.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
