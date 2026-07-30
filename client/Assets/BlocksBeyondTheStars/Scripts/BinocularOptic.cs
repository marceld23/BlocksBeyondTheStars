// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The handheld optic (items <c>binoculars</c> / <c>thermal_binoculars</c>). Right-clicking the held item
    /// steps the magnification (2× → 3.3× → 6× → down again); the thermal key toggles <see cref="ThermalVision"/>
    /// on the upgraded pair. Entirely client-side — like <see cref="CameraTool"/> there is no server round-trip
    /// and no suit-energy cost.
    ///
    /// The zoom itself is just a field-of-view target: <see cref="PlayerController.UpdateCameraFeel"/> owns
    /// <c>Camera.fieldOfView</c> and rewrites it every frame (head-bob + walking FOV kick), so the optic
    /// publishes <see cref="TargetFov"/>/<see cref="SensitivityScale"/>/<see cref="MotionScale"/> and lets the
    /// controller apply them. Writing the FOV from here would simply be undone on the next frame.
    ///
    /// The overlay (scope mask, reticle, magnification readout) lives on its own diegetic canvas above the HUD,
    /// and the HUD crosshair is suppressed while raised so the two reticles never stack.
    /// </summary>
    public sealed class BinocularOptic : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The thermal overlay driven by the upgraded optic (wired from <see cref="WorldRig"/> through
        /// <see cref="PlayerController"/>). Null = no thermal mode available at all.</summary>
        public ThermalVision Thermal;

        /// <summary>Magnification per step; index 0 is "lowered" (unaided view).</summary>
        private static readonly float[] Steps = { 1f, 2f, 3.3f, 6f };

        private int _step;
        private bool _thermalCapable;   // the held item is the upgraded pair
        private bool _thermalOn;
        private Canvas _canvas;
        private GameObject _root;
        private Text _magText;
        private Text _hintText;
        private static Sprite _maskSprite;

        /// <summary>True while the player is looking through the optic.</summary>
        public bool Raised => _step > 0;

        /// <summary>Current magnification (1 = lowered).</summary>
        public float Magnification => Steps[_step];

        /// <summary>The field of view the camera should ease to, given the player's unzoomed base FOV.</summary>
        public float TargetFov(float baseFov) => baseFov / Magnification;

        /// <summary>Look sensitivity multiplier — without it a 6× view is uncontrollable.</summary>
        public float SensitivityScale => 1f / Magnification;

        /// <summary>Head-bob / shake multiplier: a magnified view amplifies every wobble, so damp it.</summary>
        public float MotionScale => 1f / Magnification;

        /// <summary>Right-click on a held optic: raise it, or step the magnification, wrapping back to lowered
        /// after the strongest step.</summary>
        public void Step()
        {
            SetStep((_step + 1) % Steps.Length);
            ClientAudio.Instance?.Cue(Raised ? "ui_click" : "ui_back");
        }

        /// <summary>Puts the optic away (hotbar change, menu, death, going third-person, …). Safe to spam.</summary>
        public void Lower()
        {
            if (_step != 0)
            {
                SetStep(0);
            }
        }

        /// <summary>Tells the optic which item is in hand; a non-optic key lowers it. Called on every held-item
        /// change so swapping away from the binoculars can never strand the player in a zoomed view.</summary>
        public void SetHeldItem(string itemKey)
        {
            bool plain = itemKey == "binoculars";
            _thermalCapable = itemKey == "thermal_binoculars";
            if (!plain && !_thermalCapable)
            {
                Lower();
            }
        }

        private void SetStep(int step)
        {
            _step = Mathf.Clamp(step, 0, Steps.Length - 1);
            if (!Raised && _thermalOn)
            {
                SetThermal(false); // lowering the optic always ends thermal mode
            }

            EnsureOverlay();
            _root.SetActive(Raised);
            HudUi.SuppressCrosshair = Raised;
            RefreshOverlayText();
        }

        private void SetThermal(bool on)
        {
            _thermalOn = on;
            if (Thermal != null)
            {
                Thermal.Active = on;
            }

            RefreshOverlayText();
        }

        private void Update()
        {
            if (!Raised)
            {
                return;
            }

            // Situations where a raised optic makes no sense — a menu over the scope, the ship view, or the
            // player having died. Cheap to test and it keeps every caller from having to remember to lower it.
            if (Game == null || Game.MenuOpen || Game.InSpace || Game.SpaceViewActive || Game.DrivenSpeeder != null)
            {
                Lower();
                return;
            }

            if (InputMap.Down(InputAction.ToggleThermal))
            {
                var loc = Game.Localizer;
                if (!_thermalCapable)
                {
                    Game.ShowMessage(loc?.Get("ui.optic.thermal_needs_upgrade") ?? "These binoculars have no thermal sensor.");
                }
                else
                {
                    SetThermal(!_thermalOn);
                    Game.ShowMessage(loc?.Get(_thermalOn ? "ui.optic.thermal_on" : "ui.optic.thermal_off") ?? string.Empty);
                    ClientAudio.Instance?.Cue("ui_click");
                }
            }
        }

        private void RefreshOverlayText()
        {
            if (_magText == null)
            {
                return;
            }

            _magText.text = Raised ? "×" + Magnification.ToString("0.#") : string.Empty;

            string hint = string.Empty;
            if (Raised && _thermalCapable && !_thermalOn)
            {
                string key = InputMap.Glyph(InputAction.ToggleThermal);
                string fmt = Game?.Localizer?.Get("ui.optic.hint_thermal") ?? "{0}: thermal vision";
                hint = string.Format(fmt, key);
            }

            _hintText.text = hint;
        }

        private void EnsureOverlay()
        {
            if (_root != null)
            {
                return;
            }

            _canvas = UiKit.CreateDiegeticCanvas("BinocularOptic");
            _canvas.sortingOrder = 12; // above the HUD (10) — the scope surround frames everything
            _root = new GameObject("Scope", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

            // Scope surround: a stretched, feathered mask that blacks out everything outside the eyepiece.
            var maskGo = new GameObject("Mask", typeof(RectTransform));
            maskGo.transform.SetParent(_root.transform, false);
            var maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = maskRt.offsetMax = Vector2.zero;
            var mask = maskGo.AddComponent<Image>();
            mask.sprite = MaskSprite();
            mask.color = new Color(0f, 0f, 0f, 0.96f);
            mask.raycastTarget = false;

            // A thin centre reticle (the HUD crosshair is hidden while we are up).
            AddReticleBar(2f, 26f);
            AddReticleBar(26f, 2f);

            _magText = UiKit.AddText(_root.transform, 0f, 0f, 200f, 26f, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            CentreBottom(_magText.rectTransform, 128f);
            _hintText = UiKit.AddText(_root.transform, 0f, 0f, 420f, 22f, string.Empty, 15, new Color(0.75f, 0.85f, 0.9f, 0.9f), TextAnchor.MiddleCenter);
            CentreBottom(_hintText.rectTransform, 100f);

            _root.SetActive(false);
        }

        private void AddReticleBar(float w, float h)
        {
            var go = new GameObject("Reticle", typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = new Color(0.55f, 0.9f, 1f, 0.65f);
            img.raycastTarget = false;
        }

        /// <summary>Re-anchors a UiKit text (which lays out from the top-left) to the bottom centre, so the
        /// readouts sit under the eyepiece at any aspect ratio.</summary>
        private static void CentreBottom(RectTransform rt, float bottomOffset)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, bottomOffset);
        }

        /// <summary>The eyepiece mask: transparent in the middle, feathered out to opaque at the rim. Built once
        /// and shared; stretched over the screen, so the eyepiece reads as a wide oval.</summary>
        private static Sprite MaskSprite()
        {
            if (_maskSprite != null)
            {
                return _maskSprite;
            }

            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0 centre .. 1 edge
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.86f, d));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            _maskSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f));
            return _maskSprite;
        }

        private void OnDisable()
        {
            HudUi.SuppressCrosshair = false; // never strand the HUD without its crosshair
            if (Thermal != null)
            {
                Thermal.Active = false;
            }
        }

        private void OnDestroy()
        {
            HudUi.SuppressCrosshair = false;
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
