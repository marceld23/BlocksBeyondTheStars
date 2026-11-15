// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How the touch layer and the HUD share the screen (#3668 follow-up). The layer publishes the rectangles its current
    /// context takes (<see cref="HudZones.Touch"/>, from the same <see cref="TouchLayout"/> table the buttons are built
    /// from, measured on the live canvas at the live UI scale), so the HUD's map part starts under the top row, its cards
    /// stop above the action buttons and VEGA's panel finds a free spot. In return it places VEGA's NEXT beside her panel —
    /// the top-centre spot it used to have lay on the toast lane — and lets taps through to the open chat window.
    /// </summary>
    public sealed partial class TouchControlsUi
    {
        private Image _lookPadImage;
        private TouchCluster _publishedCluster = (TouchCluster)(-1);
        private Vector2 _publishedCanvas;
        private float _publishedScale;
        private HudRect _nextPlaced;
        private float _nextK;

        /// <summary>The control context the layer shows: flight (and the EVA), a driven speeder, or on foot.</summary>
        private TouchCluster CurrentCluster()
            => Game.SpaceViewActive ? TouchCluster.Flight
                : Game.DrivenSpeeder != null ? TouchCluster.Speeder
                : TouchCluster.OnFoot;

        /// <summary>Publishes this context's controls in HUD units when the context, the canvas size (aspect, window) or the UI
        /// scale changed — a few compares a frame otherwise.</summary>
        private void PublishReservation(TouchCluster cluster)
        {
            var size = ((RectTransform)_rootPanel.transform).rect.size;
            float scale = UiKit.UserScale;
            if (size.x < 1f || size.y < 1f)
            {
                return; // the canvas scaler has not sized the canvas yet
            }

            if (cluster == _publishedCluster && size == _publishedCanvas && Mathf.Approximately(scale, _publishedScale))
            {
                return;
            }

            _publishedCluster = cluster;
            _publishedCanvas = size;
            _publishedScale = scale;
            HudZones.SetTouch(TouchReservation.For(cluster, size.x, size.y, scale));
        }

        /// <summary>
        /// While the chat window is open the full-screen look pad stops catching taps: it lies over the window (this canvas is
        /// order 100, the chat 25), so its ×, its scrollbar and its input field could not be tapped. The controls freeze while
        /// the player types anyway; the buttons stay — CHAT closes the window again, or asks for the next line in a browser.
        /// </summary>
        private void PassTapsToOpenChat()
        {
            bool chatOpen = Chat != null && Chat.WindowOpen;
            if (_lookPadImage != null && _lookPadImage.raycastTarget == chatOpen)
            {
                _lookPadImage.raycastTarget = !chatOpen;
            }
        }

        /// <summary>
        /// NEXT goes where VEGA's panel (or her folded tab) is: beside it, level with the continue pill
        /// (<see cref="VegaPanel.TouchNextSpot"/>). A child reads the page and finds the button that turns it right there —
        /// at the top centre it was far from the text and lay on the toast lane.
        /// </summary>
        private void PlaceVegaNext()
        {
            if (_vegaNext == null || !_vegaNext.gameObject.activeInHierarchy || Vega == null || !HudZones.Touch.Any)
            {
                return;
            }

            var spot = Vega.TouchNextSpot;
            float k = HudZones.Touch.HudPerTouch; // HUD units per touch unit
            if (spot.W <= 0f || (spot.Equals(_nextPlaced) && Mathf.Approximately(k, _nextK)))
            {
                return;
            }

            _nextPlaced = spot;
            _nextK = k;
            var rt = (RectTransform)_vegaNext.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2((spot.X + (spot.W / 2f)) / k, -(spot.Y + (spot.H / 2f)) / k);
        }
    }
}
