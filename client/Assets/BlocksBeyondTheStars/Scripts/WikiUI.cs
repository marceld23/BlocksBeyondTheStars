// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game Wiki ("Codex") — a NATIVE uGUI screen (Stream D: replaces the embedded-browser version so the
    /// client no longer needs the Windows-only CEF engine for the Codex). A left sidebar selects a chapter; the
    /// right pane scrolls the content. The Guide chapter renders the bundled articles (via
    /// <see cref="WikiMarkup"/>); the reference chapters list the game's content straight from the loaded
    /// <see cref="GameContent"/>, localized through the same localizer the rest of the UI uses. Opened from the
    /// in-game menu header; <see cref="GameMenu"/> drives <see cref="Show"/>/<see cref="Hide"/>.
    ///
    /// NOTE: layout (sidebar width, scroll sizing, wrapping) is tuned for 1920×1080 and wants an in-editor
    /// visual pass; the discovery-gated Systems/Worlds chapters are not ported yet.
    /// </summary>
    public sealed class WikiUI : MonoBehaviour
    {
        public GameBootstrap Game;
        public GameMenu Menu;

        private const float W = 1920f, H = 1080f;
        private const float RegionX = 40f, RegionY = 96f, RegionW = W - 80f, RegionH = H - 130f;
        private const float SidebarW = 320f;
        private const float Pad = 24f;

        private Canvas _canvas;
        private Transform _root;
        private bool _builtCanvas;
        private string _chapter = "guide";

        // Guide articles, loaded once from the bundled wiki content (StreamingAssets/data/wiki/articles.json).
        [Serializable] private sealed class LocText { public string en = ""; public string de = ""; }
        [Serializable] private sealed class Article { public string id = ""; public LocText title; public LocText body; }
        [Serializable] private sealed class ArticleList { public Article[] items; }
        private Article[] _articles;

        private static readonly (string Id, string LabelKey)[] Chapters =
        {
            ("guide", "ui.wiki.guide"),
            ("blocks", "ui.wiki.blocks"),
            ("items", "ui.wiki.items"),
            ("tech", "ui.wiki.tech"),
            ("ships", "ui.wiki.ships"),
            ("modules", "ui.wiki.modules"),
            ("planets", "ui.wiki.planets"),
        };

        public void Show()
        {
            EnsureCanvas();

            // Rebuild ONLY on the disabled→enabled transition (mirrors ArcadeUI/CraftingTechShipUI).
            // GameMenu.Update calls Show() every frame while the Codex is open; rebuilding each frame
            // destroyed and recreated every button, so a uGUI click (pointer-down + pointer-up on the
            // SAME surviving object) never completed — sidebar nav, Back and Close were all dead and
            // only Esc (polled in GameMenu) worked. SelectChapter() rebuilds itself on a chapter click.
            if (!_canvas.enabled)
            {
                _canvas.enabled = true;
                Rebuild();
                UiKit.TransitionIn(_canvas.gameObject);
            }
        }

        public void Hide()
        {
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }
        }

        private void EnsureCanvas()
        {
            if (_builtCanvas)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("WikiUI");
            _canvas.sortingOrder = 55;
            _root = _canvas.transform;
            _canvas.enabled = false;
            _builtCanvas = true;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
        private bool De => Game != null && Game.German;

        private void Rebuild()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.97f));
            UiKit.AddLogo(_root, 40, 18, 520, 44, L("ui.wiki.title"), 26);
            UiKit.AddButton(_root, W - 360, 26, 180, 46, L("ui.action.back_menu"), () => Menu?.CloseBrowser());
            UiKit.AddButton(_root, W - 170, 26, 130, 46, L("ui.action.close"), () => Menu?.CloseFromUi());

            // Sidebar: one button per chapter; the active one is tinted.
            float sy = RegionY;
            foreach (var (id, labelKey) in Chapters)
            {
                string cid = id;
                var b = UiKit.AddButton(_root, RegionX, sy, SidebarW - 12f, 48f, L(labelKey), () => SelectChapter(cid));
                if (_chapter == id)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }

                sy += 56f;
            }

            // Content: a framed scroll pane to the right of the sidebar.
            float cx = RegionX + SidebarW, cw = RegionW - SidebarW;
            UiKit.AddPanel(_root, cx, RegionY, cw, RegionH, new Color(0.03f, 0.06f, 0.11f, 0.96f));
            var content = BuildContentScroll(cx, RegionY, cw, RegionH);
            RenderChapter(content, cw - 2f * Pad);
        }

        private void SelectChapter(string id)
        {
            _chapter = id;
            Rebuild();
        }

        /// <summary>A vertical ScrollRect clipped to the content pane (mirrors UiSettings' viewport).</summary>
        private Transform BuildContentScroll(float x, float y, float w, float h)
        {
            var viewGo = new GameObject("WikiScroll", typeof(RectTransform));
            viewGo.transform.SetParent(_root, false);
            UiKit.Place(viewGo, x, y, w, h);

            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            viewGo.AddComponent<RectMask2D>();
            var hit = viewGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, h);

            scroll.viewport = (RectTransform)viewGo.transform;
            scroll.content = content;
            return content;
        }

        /// <summary>Renders the current chapter as a single wrapped rich-text block and sizes the scroll content
        /// to it (one block keeps the layout robust; per-entry rows can come with the in-editor polish pass).</summary>
        private void RenderChapter(Transform content, float textW)
        {
            string body = BuildChapterText(_chapter);
            var t = UiKit.AddText(content, Pad, Pad, textW, 100f, body, 18, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            float textH = t.preferredHeight; // wrapped height for the current rect width
            ((RectTransform)t.transform).sizeDelta = new Vector2(textW, textH + 10f);
            ((RectTransform)content).sizeDelta = new Vector2(0f, Mathf.Max(RegionH, textH + 2f * Pad));
        }

        private string BuildChapterText(string chapter) => chapter switch
        {
            "guide" => BuildGuide(),
            // Items/Tech/Ships/Modules carry a DescriptionKey (shown under each name); Blocks/Planets have no
            // description text in the content data yet, so they list names only (pass a null description selector).
            // Blocks/Planets carry no DescriptionKey in their definitions, but the locale tables now hold
            // block.*.desc / planet.*.desc entries authored alongside the names — derive the key by convention
            // (".name" → ".desc") so they show a description line. Desc() still renders nothing for any key
            // that has no locale entry, so undescribed (cosmetic) blocks stay name-only.
            "blocks" => BuildEntries(L("ui.wiki.blocks"), Entries(Game?.Content?.Blocks?.Values, d => d.NameKey, d => ToDescKey(d.NameKey))),
            "items" => BuildEntries(L("ui.wiki.items"), Entries(Game?.Content?.Items?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "tech" => BuildEntries(L("ui.wiki.tech"), Entries(Game?.Content?.Blueprints?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "ships" => BuildEntries(L("ui.wiki.ships"), Entries(Game?.Content?.Ships?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "modules" => BuildEntries(L("ui.wiki.modules"), Entries(Game?.Content?.ShipModules?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "planets" => BuildEntries(L("ui.wiki.planets"), Entries(Game?.Content?.Planets?.Values, d => d.NameKey, d => ToDescKey(d.NameKey))),
            _ => string.Empty,
        };

        /// <summary>Localized, name-sorted (name, description) entries for a content collection. Every definition
        /// carries a NameKey; <paramref name="descKey"/> is null for collections without description text, and an
        /// individual entry's description is empty when its key is blank or unknown (see <see cref="Desc"/>).</summary>
        private List<(string Name, string Desc)> Entries<T>(IEnumerable<T> defs, Func<T, string> nameKey, Func<T, string> descKey)
        {
            var result = new List<(string Name, string Desc)>();
            if (defs != null)
            {
                foreach (var d in defs)
                {
                    result.Add((L(nameKey(d)), descKey == null ? string.Empty : Desc(descKey(d))));
                }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        /// <summary>Maps a name-key to its sibling description key by the locale convention used throughout the
        /// content tables ("block.stone.name" → "block.stone.desc"). Used for the Blocks and Planets chapters,
        /// whose definitions hold a NameKey but no explicit DescriptionKey.</summary>
        private static string ToDescKey(string nameKey)
            => string.IsNullOrEmpty(nameKey) || !nameKey.EndsWith(".name", StringComparison.Ordinal)
                ? string.Empty
                : nameKey.Substring(0, nameKey.Length - ".name".Length) + ".desc";

        /// <summary>Localized description text for a key, or empty when the key is blank or unknown — so an entry
        /// renders a description line only when one actually exists (Localizer.Get returns "[key]" for a miss).</summary>
        private string Desc(string key)
            => string.IsNullOrEmpty(key) || Game?.Localizer == null || !Game.Localizer.Has(key)
                ? string.Empty
                : Game.Localizer.Get(key);

        private string BuildEntries(string header, List<(string Name, string Desc)> entries)
        {
            var sb = new StringBuilder();
            sb.Append("<b><size=24>").Append(header).Append("</size></b>\n\n");
            if (entries.Count == 0)
            {
                sb.Append(L("ui.wiki.empty"));
            }
            else
            {
                foreach (var (name, desc) in entries)
                {
                    sb.Append("• <b>").Append(name).Append("</b>\n");
                    if (!string.IsNullOrEmpty(desc))
                    {
                        sb.Append("<color=#9fb4c8>").Append(desc).Append("</color>\n");
                    }

                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private string BuildGuide()
        {
            LoadArticles();
            var sb = new StringBuilder();
            foreach (var a in _articles)
            {
                sb.Append("<b><size=24>").Append(Loc(a.title)).Append("</size></b>\n\n");
                sb.Append(WikiMarkup.ToUnityRichText(Loc(a.body))).Append("\n\n\n");
            }

            return sb.ToString();
        }

        private string Loc(LocText t)
        {
            if (t == null)
            {
                return string.Empty;
            }

            return De && !string.IsNullOrEmpty(t.de) ? t.de : (t.en ?? string.Empty);
        }

        private void LoadArticles()
        {
            if (_articles != null)
            {
                return;
            }

            try
            {
                string path = Path.Combine(StreamingAssetsCache.DataDir, "wiki", "articles.json");
                if (File.Exists(path))
                {
                    // JsonUtility can't parse a top-level array, so wrap it as { "items": [...] }.
                    var list = JsonUtility.FromJson<ArticleList>("{\"items\":" + File.ReadAllText(path) + "}");
                    _articles = list?.items ?? Array.Empty<Article>();
                }
                else
                {
                    _articles = Array.Empty<Article>();
                }
            }
            catch
            {
                _articles = Array.Empty<Article>();
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
