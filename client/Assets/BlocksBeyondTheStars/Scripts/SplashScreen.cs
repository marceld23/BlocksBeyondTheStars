// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Splash screen: the animated menu space scene (the corvette before the "varied" world, #3705) shows through behind the
    /// game's official logo (<see cref="GameLogo"/>, #3706) that fades/scales/slides in, with the tagline, the version, the
    /// open-source line and the skip hint. Shown every start, skippable, and bridges content-load time. Modern uGUI on its own
    /// DPI-scaled overlay canvas (no bundled art: the logo is rendered from the block tiles); driven from AppShell.Update. The
    /// old "PROTOTYPE / DEV BUILD" badge is gone (#3705) — the version alone says which build this is.
    /// </summary>
    public sealed class SplashScreen
    {
        private const float Duration = 3.2f;

        // The logo's box (virtual 1080p units), centred a little above the middle of the screen.
        private const float TitleW = 1100f;
        private const float TitleH = 270f;

        private readonly AppShell _shell;
        private float _elapsed;

        private Canvas _canvas;
        private RectTransform _title;
        private CanvasGroup _titleGroup;
        private CanvasGroup _lateGroup;

        // Texts built before the localizer was up (WebGL streams content, so the splash can outrun it and
        // would otherwise show raw "ui.splash.*" keys) — re-localized in Update once content arrives.
        // Suffix covers the build badge, which appends the version to the localized part.
        private readonly List<(Text Text, string Key, string Suffix)> _pendingLocalization = new();

        public SplashScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Splash)
            {
                if (_canvas != null)
                {
                    Object.Destroy(_canvas.gameObject);
                    _canvas = null;
                }

                return;
            }

            EnsureBuilt();

            if (_pendingLocalization.Count > 0 && _shell.Localizer != null)
            {
                foreach (var (text, key, suffix) in _pendingLocalization)
                {
                    if (text != null)
                    {
                        text.text = _shell.L(key) + suffix;
                    }
                }

                _pendingLocalization.Clear();
            }

            _elapsed += Time.deltaTime;
            Animate(_elapsed);

            if (_elapsed >= Duration || (_elapsed > 0.4f && Input.anyKeyDown))
            {
                _shell.OnTitleSplashDone(); // first launch detours through the intro cinematic (#759)
            }
        }

        private void Animate(float t)
        {
            float intro = Mathf.Clamp01(t / 0.9f);
            float ease = 1f - (1f - intro) * (1f - intro); // ease-out
            float scale = 0.85f + 0.15f * ease + 0.02f * Mathf.Sin(t * 2f); // settle + gentle breathe

            _titleGroup.alpha = ease;
            _title.localScale = new Vector3(scale, scale, 1f);
            _title.anchoredPosition = new Vector2(0f, 70f + 30f * ease); // slide up into place

            _lateGroup.alpha = Mathf.Clamp01((t - 0.8f) / 0.8f);
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _elapsed = 0f;
            _canvas = UiKit.CreateCanvas("Splash");
            _canvas.sortingOrder = 40;
            var root = _canvas.transform;

            // Faint darkening for title readability (the menu space scene shows through behind).
            var ov = new GameObject("Overlay", typeof(RectTransform));
            ov.transform.SetParent(root, false);
            var ort = ov.GetComponent<RectTransform>();
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;
            var ovImg = ov.AddComponent<Image>();
            ovImg.sprite = UiKit.SolidSprite;
            ovImg.color = new Color(0.02f, 0.04f, 0.09f, 0.35f);
            ovImg.raycastTarget = false;

            // The official logo (#3706), animated as a group (scaled/faded/slid).
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(root, false);
            _title = titleGo.GetComponent<RectTransform>();
            _title.anchorMin = _title.anchorMax = _title.pivot = new Vector2(0.5f, 0.5f);
            _title.sizeDelta = new Vector2(TitleW, TitleH);
            _titleGroup = titleGo.AddComponent<CanvasGroup>();
            UiKit.AddGameLogo(_title, 0f, 0f, TitleW, TitleH, centered: true);

            // Tagline + build badge + skip hint (fade in slightly later).
            var lateGo = new GameObject("Late", typeof(RectTransform));
            lateGo.transform.SetParent(root, false);
            var lrt = lateGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            _lateGroup = lateGo.AddComponent<CanvasGroup>();

            LocalizedCentered(lateGo.transform, new Vector2(0f, -86f), 20, new Color(0.75f, 0.85f, 0.95f), "ui.splash.tagline", false);
            Centered(lateGo.transform, new Vector2(0f, -124f), 16, new Color(0.55f, 0.68f, 0.82f), "v" + AppShell.Version, false);
            LocalizedCentered(lateGo.transform, new Vector2(0f, -162f), 17, new Color(0.5f, 0.85f, 1f), "ui.splash.contribute", true);
            LocalizedCentered(lateGo.transform, new Vector2(0f, -432f), 18, new Color(0.7f, 0.8f, 0.9f, 0.6f), "ui.splash.skip", false);

            Animate(0f);
        }

        /// <summary>A <see cref="Centered"/> whose text comes from the localizer (plus an optional literal
        /// suffix, e.g. the version on the build badge); queued for re-localization when the localizer
        /// isn't loaded yet (WebGL streams content after the splash starts).</summary>
        private void LocalizedCentered(Transform parent, Vector2 pos, int fontSize, Color color, string key, bool bold, string suffix = "")
        {
            var text = Centered(parent, pos, fontSize, color, _shell.L(key) + suffix, bold);
            if (_shell.Localizer == null)
            {
                _pendingLocalization.Add((text, key, suffix));
            }
        }

        private static Text Centered(Transform parent, Vector2 pos, int fontSize, Color color, string text, bool bold)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, fontSize + 24f);
            rt.anchoredPosition = pos;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }
    }
}
