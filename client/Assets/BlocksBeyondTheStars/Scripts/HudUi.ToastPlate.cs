// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The plate under the status toast (#3769). The toast was a bare outlined line at the top centre; the HUD draws over
    /// the name tags, so "This station is protected." ran straight through an NPC's name. It now sits on the same rounded
    /// dark plate with a light edge as the speech bubbles (<see cref="NpcBubbleFrame"/>), as wide as its text.
    /// </summary>
    public sealed partial class HudUi
    {
        private Image _toastPlate;

        /// <summary>How much higher than the toast's line the plate is (half above, half below).</summary>
        private const float ToastPlatePad = 8f;

        /// <summary>Builds the plate — before the toast's text, so it draws behind it.</summary>
        private void BuildToastPlate(Transform root)
        {
            _toastPlate = UiKit.AddImage(root, (W / 2f) - (ToastW / 2f), ToastY, ToastW, ToastH, UiKit.PanelSprite, Color.white, Image.Type.Sliced);
            _toastPlate.gameObject.name = "ToastPlate";
            _toastPlate.gameObject.SetActive(false);
        }

        /// <summary>Every frame: the plate is up exactly while a toast is, centred on the toast's line (it follows the
        /// slide-in and the touch lane), as wide as the text, and fades with it.</summary>
        private void SyncToastPlate()
        {
            if (_toastPlate == null || _toast == null)
            {
                return;
            }

            bool up = _toastAge >= 0f && !string.IsNullOrEmpty(_toast.text);
            if (_toastPlate.gameObject.activeSelf != up)
            {
                _toastPlate.gameObject.SetActive(up);
            }

            if (!up)
            {
                return;
            }

            var line = _toast.rectTransform;
            float w = NpcBubbleFrame.Plate(_toast.preferredWidth, line.rect.height, line.rect.width, ToastH).Width;
            var rt = _toastPlate.rectTransform;
            rt.anchorMin = line.anchorMin;
            rt.anchorMax = line.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, line.rect.height + ToastPlatePad); // slim: the lane above it (y 54…74) stays clear
            rt.anchoredPosition = line.anchoredPosition + Vector2.Scale(new Vector2(0.5f, 0.5f) - line.pivot, line.rect.size);
            _toastPlate.color = new Color(1f, 1f, 1f, _toastGroup != null ? _toastGroup.alpha : 1f);
        }
    }
}
