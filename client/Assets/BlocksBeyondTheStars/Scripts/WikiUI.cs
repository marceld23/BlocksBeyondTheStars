// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game Wiki ("Codex") screen — a full-screen menu page that hosts the bundled wiki SPA in the
    /// shared <see cref="EmbeddedBrowser"/>. Opened from the in-game menu header (a "Codex" button). The wiki
    /// renders the game's content reference plus discovery-gated Systems/Worlds chapters; the active language
    /// is passed through the URL. On a build without the UWB browser package it shows a placeholder explaining
    /// how to enable it (everything else in the game still works).
    /// </summary>
    public sealed class WikiUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        private const float W = 1920f, H = 1080f;
        private const float RegionX = 40f, RegionY = 96f, RegionW = W - 80f, RegionH = H - 130f;

        private Canvas _canvas;
        private RectTransform _root;
        private GameObject _placeholder;
        private bool _built;
        private bool _open;

        private void EnsureBuilt()
        {
            if (_built) return;

            _canvas = UiKit.CreateCanvas("WikiUI");
            _canvas.sortingOrder = 55;
            _root = (RectTransform)_canvas.transform;

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.96f));
            UiKit.AddLogo(_root, 40, 18, 520, 44, L("ui.wiki.title"), 26);
            UiKit.AddButton(_root, W - 360, 26, 180, 46, L("ui.action.back_menu"), () => Menu?.CloseBrowser());
            UiKit.AddButton(_root, W - 170, 26, 130, 46, L("ui.action.close"), () => Menu?.CloseFromUi());

            UiKit.AddPanel(_root, RegionX, RegionY, RegionW, RegionH, new Color(0.03f, 0.06f, 0.11f, 0.96f));
            _placeholder = BuildPlaceholder(_root);
            _canvas.enabled = false; // first Show() enables it + runs OnOpen()
            _built = true;
        }

        private GameObject BuildPlaceholder(Transform root)
        {
            var go = new GameObject("WikiPlaceholder", typeof(RectTransform));
            go.transform.SetParent(root, false);
            UiKit.Place(go, RegionX, RegionY, RegionW, RegionH);
            UiKit.AddText(go.transform, 0, RegionH * 0.5f - 70, RegionW, 60, L("ui.browser.unavailable_title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(go.transform, 0, RegionH * 0.5f, RegionW, 60, L("ui.browser.unavailable_body"), 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
            go.SetActive(false);
            return go;
        }

        public void Show()
        {
            EnsureBuilt();
            if (!_canvas.enabled)
            {
                _canvas.enabled = true;
                OnOpen();
                UiKit.TransitionIn(_canvas.gameObject);
            }
        }

        public void Hide()
        {
            if (!_built || !_canvas.enabled) return;
            _canvas.enabled = false;
            if (_open)
            {
                EmbeddedBrowser.Instance?.Park();
                _open = false;
            }
        }

        private void OnOpen()
        {
            var host = Menu != null ? Menu.EnsureBrowserHost() : EmbeddedBrowser.Instance;
            string lang = (Game != null && Game.German) ? "de" : "en";
            if (host != null && host.Available && host.Content != null && host.Content.Running
                && host.MountInto(_root, RegionX, RegionY, RegionW, RegionH))
            {
                host.SetLoadingLabel(L("ui.browser.loading"));
                host.Navigate("wiki/index.html?lang=" + lang);
                _placeholder.SetActive(false);
                _open = true;
            }
            else
            {
                _placeholder.SetActive(true);
            }
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}
