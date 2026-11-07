// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The stamina bar of <see cref="HudUi"/> (#3375): a thin bar a little below the crosshair, filled with the body's
    /// stamina that running and climbing share (<see cref="PlayerController.Stamina"/>, #3373). It shows only while the
    /// stamina is not full and fades out a moment after it is full again (<see cref="StaminaBarFade"/>); a run-dry stamina
    /// wears a warm tint until a new run is possible. It lives under the crosshair object, so it hides with the reticle
    /// (scope, chat, cutscene) and shares its sub-canvas: one float a frame, no allocations, no layout pass.
    /// </summary>
    public sealed partial class HudUi
    {
        private const float StaminaBarWidth = 64f;
        private const float StaminaBarHeight = 4f;
        private const float StaminaBarDrop = 26f; // below the reticle's centre, clear of the hit-marker ticks

        /// <summary>Seconds between two searches for the player rig while there is none (the menu, a world switch).</summary>
        private const float StaminaRigLookupPause = 0.5f;

        private static readonly Color StaminaTrack = new Color(0.02f, 0.06f, 0.12f, 0.55f);
        private static readonly Color StaminaSpentTint = UiKit.Warn;

        private readonly StaminaBarFade _staminaFade = new StaminaBarFade();
        private CanvasGroup _staminaGroup;
        private RectTransform _staminaFillRt;
        private Image _staminaFill;
        private float _staminaShown = -1f;
        private bool _staminaSpentShown;
        private float _staminaRigLookupAt;

        /// <summary>Builds the bar (hidden) under the crosshair's root.</summary>
        private void BuildStaminaBar(RectTransform parent)
        {
            var go = new GameObject("Stamina", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(StaminaBarWidth + 2f, StaminaBarHeight + 2f);
            rt.anchoredPosition = new Vector2(0f, -StaminaBarDrop);
            var track = go.AddComponent<Image>();
            track.color = StaminaTrack;
            track.raycastTarget = false;

            var fillGo = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(rt, false);
            _staminaFillRt = (RectTransform)fillGo.transform;
            _staminaFillRt.anchorMin = _staminaFillRt.anchorMax = new Vector2(0f, 0.5f);
            _staminaFillRt.pivot = new Vector2(0f, 0.5f);
            _staminaFillRt.sizeDelta = new Vector2(StaminaBarWidth, StaminaBarHeight);
            _staminaFillRt.anchoredPosition = new Vector2(1f, 0f);
            _staminaFill = fillGo.GetComponent<Image>();
            _staminaFill.color = UiKit.Cyan;
            _staminaFill.raycastTarget = false;

            _staminaGroup = go.GetComponent<CanvasGroup>();
            _staminaGroup.alpha = 0f;
            _staminaGroup.interactable = false;
            _staminaGroup.blocksRaycasts = false;
        }

        /// <summary>Per frame while the HUD shows: the fade, the fill and the spent tint.</summary>
        private void UpdateStaminaBar(float dt)
        {
            if (_staminaGroup == null)
            {
                return;
            }

            if (_playerRig == null && Time.unscaledTime >= _staminaRigLookupAt)
            {
                _staminaRigLookupAt = Time.unscaledTime + StaminaRigLookupPause;
                _playerRig = FindAnyObjectByType<PlayerController>(); // the rig WorldRig creates; null in the menu
            }

            float value = _playerRig != null ? _playerRig.Stamina : 1f;
            float alpha = _staminaFade.Step(dt, value, UiKit.ReducedMotion);
            if (_staminaGroup.alpha != alpha)
            {
                _staminaGroup.alpha = alpha;
            }

            if (alpha <= 0f)
            {
                return;
            }

            if (Mathf.Abs(value - _staminaShown) > 0.002f)
            {
                _staminaShown = value;
                _staminaFillRt.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
            }

            bool spent = _playerRig != null && _playerRig.StaminaSpent;
            if (spent != _staminaSpentShown)
            {
                _staminaSpentShown = spent;
                _staminaFill.color = spent ? StaminaSpentTint : UiKit.Cyan;
            }
        }
    }
}
