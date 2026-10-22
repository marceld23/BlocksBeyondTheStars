// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The crosshair of <see cref="HudUi"/>: the reticle with its hostile / rope tint and the hit-marker flash (#693).
    /// </summary>
    public sealed partial class HudUi
    {
        private GameObject _crosshair;

        // Crosshair state (#693): hostile tint while an enemy is under the reticle + the hit-marker flash.
        private static readonly Color HostileAim = new Color(1f, 0.4f, 0.35f, 0.95f);
        private static readonly Color RopeAim = new Color(0.62f, 1f, 0.8f, 0.95f); // #2321: the rope can hold on what is under the reticle
        private static readonly Color HitMarkerCol = new Color(1f, 0.85f, 0.4f, 0.95f);
        private Image _crossV, _crossH;
        private GameObject _hitMarker;
        private float _hitMarkerTimer;

        /// <summary>Set while a scope draws its own reticle (see <see cref="BinocularOptic"/>); hides the HUD
        /// crosshair for as long as it is up.</summary>
        public static bool SuppressCrosshair;

        private void BuildCrosshair(Transform root)
        {
            // Crosshair.
            _crosshair = new GameObject("Crosshair", typeof(RectTransform));
            _crosshair.transform.SetParent(root, false);
            var ch = _crosshair.GetComponent<RectTransform>();
            ch.anchorMin = ch.anchorMax = new Vector2(0.5f, 0.5f);
            ch.sizeDelta = Vector2.zero;
            MakeCrosshair(ch);
        }

        private void MakeCrosshair(RectTransform parent)
        {
            var v = new GameObject("v", typeof(RectTransform)); v.transform.SetParent(parent, false);
            var vr = v.GetComponent<RectTransform>(); vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 0.5f); vr.sizeDelta = new Vector2(2, 18);
            _crossV = v.AddComponent<Image>(); _crossV.color = UiKit.Cyan;
            var hh = new GameObject("h", typeof(RectTransform)); hh.transform.SetParent(parent, false);
            var hr = hh.GetComponent<RectTransform>(); hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f); hr.sizeDelta = new Vector2(18, 2);
            _crossH = hh.AddComponent<Image>(); _crossH.color = UiKit.Cyan;

            // Hit marker (#693): four diagonal ticks around the reticle, flashed briefly when one of the
            // local player's shots visibly lands (the entity views attribute the hull drop and call
            // ShowHitMarker). Inactive by default.
            _hitMarker = new GameObject("hits", typeof(RectTransform));
            _hitMarker.transform.SetParent(parent, false);
            var hm = _hitMarker.GetComponent<RectTransform>();
            hm.anchorMin = hm.anchorMax = new Vector2(0.5f, 0.5f);
            hm.sizeDelta = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var tick = new GameObject("t" + i, typeof(RectTransform));
                tick.transform.SetParent(_hitMarker.transform, false);
                var tr = tick.GetComponent<RectTransform>();
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(2.5f, 9f);
                float ang = 45f + i * 90f;
                tr.localRotation = Quaternion.Euler(0f, 0f, ang);
                tr.anchoredPosition = Quaternion.Euler(0f, 0f, ang) * new Vector2(0f, 13f);
                tick.AddComponent<Image>().color = HitMarkerCol;
            }

            _hitMarker.SetActive(false);
        }

        /// <summary>Tints the reticle hostile-red while an enemy sits under it, and runs the hit-marker
        /// flash timer. Called every frame from <see cref="LateUpdate"/>.</summary>
        private void UpdateCrosshairState(float dt)
        {
            if (_crossV == null)
            {
                return;
            }

            var col = Game.AimedEnemyId != null ? HostileAim : Game.RopeAimValid ? RopeAim : UiKit.Cyan;
            if (_crossV.color != col)
            {
                _crossV.color = col;
                _crossH.color = col;
            }

            if (_hitMarkerTimer > 0f)
            {
                _hitMarkerTimer -= dt;
                if (_hitMarkerTimer <= 0f && _hitMarker != null)
                {
                    _hitMarker.SetActive(false);
                }
            }
        }

        /// <summary>Flashes the crosshair hit marker (#693) — called by the entity views when a hull drop is
        /// attributable to the local player's latest shot.</summary>
        public void ShowHitMarker()
        {
            if (_hitMarker == null)
            {
                return;
            }

            _hitMarkerTimer = 0.25f;
            _hitMarker.SetActive(true);
            UiTween.Scale(_hitMarker.transform, 1.7f, 1f, 0.18f, UiTween.Ease.OutCubic); // recoil: snaps in from wide
        }
    }
}
