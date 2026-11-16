// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The in-game Wiki ("Codex") — a NATIVE uGUI screen (Stream D: replaces the embedded-browser version so the
    /// client no longer needs the Windows-only CEF engine for the Codex). A left sidebar selects a chapter; the
    /// right pane scrolls the content. The Guide chapter renders the bundled articles in the player's language
    /// (<see cref="WikiArticleText"/>, via <see cref="WikiMarkup"/>, their <c>{key:Action}</c> tokens filled — #2508); the
    /// reference chapters list the game's content straight from the loaded
    /// <see cref="GameContent"/>, localized through the same localizer the rest of the UI uses. Opened from the
    /// in-game menu header; <see cref="GameMenu"/> drives <see cref="Show"/>/<see cref="Hide"/>.
    ///
    /// NOTE: layout (sidebar width, scroll sizing, wrapping) is tuned for 1920×1080 and wants an in-editor
    /// visual pass; the discovery-gated Systems/Worlds chapters are not ported yet.
    /// </summary>
    public sealed partial class WikiUI : MonoBehaviour
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

        // Guide articles (#2508): the list is StreamingAssets/data/wiki/articles.json, the texts the per-language tables
        // wiki/locales/<code>.json, read through WikiArticleText (Client.Core). An article the player's language does not
        // have reads English as a whole, so a partially translated Codex is a supported state. Read again when the
        // language changes.
        private WikiArticleText _articleText;

        // The template header (#3672): Back top left, the title beside it, Close top right.
        private const float HeaderY = 22f, HeaderH = 52f, BackW = 210f, CloseW = 190f;

        private static readonly (string Id, string LabelKey, string Icon)[] Chapters =
        {
            ("guide", "ui.wiki.guide", "cat_mission"),
            ("blocks", "ui.wiki.blocks", "cat_blocks"),
            ("items", "ui.wiki.items", "cat_inventory"),
            ("tech", "ui.wiki.tech", "cat_tech"),
            ("ships", "ui.wiki.ships", "map_ship"),
            ("modules", "ui.wiki.modules", "cat_modules"),
            ("planets", "ui.wiki.planets", "cat_planet"),
            ("discoveries", "ui.wiki.discoveries", "cat_target"), // what THIS player has scanned (#484)
            ("enemies", "ui.wiki.enemies", "map_mark_danger"),    // every hostile, the ones THIS player has met marked (#2971, #3031)
            ("substances", "ui.wiki.substances", "ov_plants"),    // the bio lab's research book (#2203): species analysed…
            ("compounds", "ui.wiki.compounds", "cat_medicine"),   // …mixes tried…
            ("materials", "ui.wiki.materials", "map_mark_ore"),   // …deposits analysed and tools and gear changed
            ("lore", "ui.wiki.lore", "map_ruin"),                 // found story texts: fragments, memories, field records (#1111)
        };

        /// <summary>Opens the Codex. <paramref name="chapter"/> is a deep link (#1843: the Progress block's
        /// "Discoveries" button lands on that chapter); null keeps whatever the reader looked at last.</summary>
        public void Show(string chapter = null)
        {
            EnsureCanvas();

            bool jump = !string.IsNullOrEmpty(chapter) && chapter != _chapter;
            if (jump)
            {
                _chapter = chapter;
            }

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
            else if (jump)
            {
                Rebuild(); // already open on another chapter: switch like a sidebar click would
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
            UiNav.Enable(_canvas.gameObject); // gamepad can browse the Codex (inert on KB/mouse)
            _canvas.enabled = false;
            _builtCanvas = true;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
        private string LocaleCode => Game?.Localizer?.Locale.Code() ?? "en";

        private void Rebuild()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Destroy(_root.GetChild(i).gameObject);
            }

            UiKit.AddImage(_root, 0, 0, W, H, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.08f, 0.97f));
            var (back, close) = AddHeader();

            // The chapter list as the left navigation: the open chapter drawn primary, the others quiet.
            float sy = RegionY;
            Button open = null;
            foreach (var (id, labelKey, icon) in Chapters)
            {
                string cid = id;
                var chapter = UiKit.AddTierButton(_root, RegionX, sy, SidebarW - 12f, 48f, L(labelKey), () => SelectChapter(cid),
                    _chapter == id ? ButtonTier.Primary : ButtonTier.Quiet, icon);
                open = _chapter == id ? chapter : open;
                sy += 56f;
            }

            // Content: a scroll pane on an opaque card to the right of the chapters — the game must not show through the text.
            float cx = RegionX + SidebarW, cw = RegionW - SidebarW;
            UiKit.AddCard(_root, cx, RegionY, cw, RegionH);
            var content = BuildContentScroll(cx, RegionY, cw, RegionH);
            RenderChapter(content, cw - 2f * Pad);

            // Back and Close after the chapters and the content: a pad's first focus is the first control in hierarchy order,
            // and A on arrival must not leave the Codex (as on the menu screens) — it lands on the open chapter.
            back.transform.SetAsLastSibling();
            close.transform.SetAsLastSibling();
            UiNav.Prefer(_canvas.gameObject, open);
        }

        /// <summary>The menu template's header (#3672): Back to the in-game menu top left (the arrow icon — the label's own
        /// "‹" goes), the title beside it, Close top right. Returns the two buttons.</summary>
        private (Button Back, Button Close) AddHeader()
        {
            string back = L("ui.action.back_menu").TrimStart('‹', ' ');
            var backButton = UiKit.AddTierButton(_root, RegionX, HeaderY, BackW, HeaderH, back, () => Menu?.CloseBrowser(),
                ButtonTier.Quiet, "btn_back");
            UiKit.AddLogo(_root, RegionX + BackW + 28f, HeaderY - 2f, 760f, HeaderH + 4f, L("ui.wiki.title"), 32);
            var closeButton = UiKit.AddTierButton(_root, W - RegionX - CloseW, HeaderY, CloseW, HeaderH, L("ui.action.close"),
                () => Menu?.CloseFromUi(), ButtonTier.Quiet, "btn_close");
            return (backButton, closeButton);
        }

        /// <summary>Test seam (as <c>UiPanel.CanvasForTest</c>): the Codex's canvas once shown, so an EditMode test can check
        /// its pad focus and clean it up.</summary>
        public Canvas CanvasForTest => _canvas;

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
            UiKit.AddInlineScrollbar(scroll);
            return content;
        }

        /// <summary>Renders the current chapter as a column of wrapped rich-text blocks and sizes the scroll
        /// content to their sum. The chapter text is split into chunks of at most
        /// <see cref="UiTextChunks.DefaultMaxChars"/> characters (paragraph boundaries first): a single uGUI Text
        /// past ~16 250 characters trips VertexHelper's 65 000-vertex ArgumentException inside the Graphic rebuild
        /// and renders NOTHING — which is exactly how the Guide and Items chapters went blank once the articles
        /// and item descriptions grew (#1097). Each chunk keeps its trailing newlines, so the paragraph gap
        /// between two chunks survives the split (preferredHeight counts them).</summary>
        private void RenderChapter(Transform content, float textW)
        {
            float y = Pad;
            if (_chapter == "guide")
            {
                y = RenderGuide(content, textW, y); // #2259: the guide's articles may carry pictures
            }
            else if (_chapter == "enemies" && Game?.Content != null)
            {
                y = RenderEnemies(content, textW, y); // #3032: each enemy beside its portrait
            }
            else if (_chapter == "substances")
            {
                y = RenderSubstances(content, textW, y); // #3719: each substance behind its effect's icon
            }
            else if (_chapter == "compounds")
            {
                y = RenderCompounds(content, textW, y); // #3721: the reaction chart above the tried mixes
            }
            else
            {
                y = AddTextBlock(content, textW, y, BuildChapterText(_chapter));
            }

            ((RectTransform)content).sizeDelta = new Vector2(0f, Mathf.Max(RegionH, y + Pad));
        }

        /// <summary>A run of rich text as chunked uGUI Texts (see <see cref="RenderChapter"/>); returns the next y.</summary>
        private float AddTextBlock(Transform content, float textW, float y, string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return y;
            }

            foreach (string chunk in UiTextChunks.Split(body))
            {
                var t = UiKit.AddText(content, Pad, y, textW, 100f, chunk, 18, UiKit.TextCol, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;

                float textH = t.preferredHeight; // wrapped height for the current rect width
                ((RectTransform)t.transform).sizeDelta = new Vector2(textW, textH + 10f);
                y += textH;
            }

            return y;
        }

        /// <summary>#2259: the guide chapter, text and pictures in article order. A picture is a PNG under the data folder
        /// (<c>wiki/img/…</c>, shipped like every other data file), drawn at most at its own size and never wider than
        /// the column; one that cannot be read is skipped.</summary>
        private float RenderGuide(Transform content, float textW, float y)
        {
            var articles = LoadArticles();
            var sb = new StringBuilder();
            foreach (string id in articles.Ids)
            {
                var (title, body) = articles.Article(id);
                if (title.Length == 0 && body.Length == 0)
                {
                    continue; // listed, but no table holds it — nothing to show
                }

                sb.Append("<b><size=24>").Append(ExpandKeys(title)).Append("</size></b>\n\n");
                foreach (var seg in WikiMarkup.Segments(body))
                {
                    if (!seg.IsImage)
                    {
                        sb.Append(WikiMarkup.ToUnityRichText(seg.Text, ExpandKeys)).Append("\n\n");
                        continue;
                    }

                    y = AddTextBlock(content, textW, y, sb.ToString());
                    sb.Clear();
                    y = AddWikiImage(content, textW, y, seg.Image);
                }

                sb.Append('\n');
            }

            return AddTextBlock(content, textW, y, sb.ToString());
        }

        private readonly Dictionary<string, Texture2D> _wikiImages = new Dictionary<string, Texture2D>();

        private float AddWikiImage(Transform content, float textW, float y, string src)
        {
            if (!_wikiImages.TryGetValue(src, out var tex))
            {
                tex = null;
                try
                {
                    string path = Path.Combine(StreamingAssetsCache.DataDir, src.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(path))
                    {
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "wiki:" + src };
                        if (!tex.LoadImage(File.ReadAllBytes(path)))
                        {
                            Destroy(tex);
                            tex = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[WikiUI] failed to load {src}: {e.Message}");
                    tex = null;
                }

                _wikiImages[src] = tex;
            }

            if (tex == null)
            {
                return y;
            }

            float w = Mathf.Min(textW, tex.width), h = w * tex.height / tex.width;
            var go = new GameObject("WikiImage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiKit.Place(go, Pad + (textW - w) * 0.5f, y + 4f, w, h);
            go.AddComponent<RawImage>().texture = tex;
            return y + h + 20f;
        }

        private string BuildChapterText(string chapter) => chapter switch
        {
            "guide" => BuildGuide(),
            // Items/Tech/Ships/Modules carry a DescriptionKey (shown under each name). Blocks/Planets have none in their
            // definitions, but the locale tables hold block.*.desc / planet.*.desc — derived by convention (".name" →
            // ".desc"); Desc() renders nothing for a key without a locale entry, so cosmetic blocks stay name-only.
            "blocks" => BuildEntries(L("ui.wiki.blocks"), Entries(Game?.Content?.Blocks?.Values, d => d.NameKey, d => ToDescKey(d.NameKey))),
            "items" => BuildEntries(L("ui.wiki.items"), Entries(Game?.Content?.Items?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "tech" => BuildEntries(L("ui.wiki.tech"), Entries(Game?.Content?.Blueprints?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "ships" => BuildEntries(L("ui.wiki.ships"), Entries(Game?.Content?.Ships?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "modules" => BuildEntries(L("ui.wiki.modules"), Entries(Game?.Content?.ShipModules?.Values, d => d.NameKey, d => d.DescriptionKey)),
            "planets" => BuildEntries(L("ui.wiki.planets"), PlanetEntries()),
            "discoveries" => BuildDiscoveries(),
            "enemies" => Game?.Content == null ? string.Empty : EnemyCodexText.Build(Game.Content.Enemies, Game.EnemiesMet, L, k => L(Game.Content.GetItem(k)?.NameKey ?? k)),
            "materials" => BuildMaterials(),
            "lore" => BuildLore(),
            _ => string.Empty,
        };

        /// <summary>The name of a species (or a deposit) of the research book by its seed.</summary>
        private string SeedName(uint seed, bool mineral)
        {
            Game.Bio.Species.TryGetValue(seed, out var species);
            return BioLabUi.SpeciesLabel(Game, species, mineral);
        }

        private static void AppendOrigin(StringBuilder line, string origin)
        {
            if (!string.IsNullOrEmpty(origin))
            {
                line.Append("  —  <color=#9fb4c8>").Append(origin).Append("</color>"); // dim, like a discovery's site
            }
        }

        private static void AppendSorted(StringBuilder sb, List<(string Name, string Line)> lines)
        {
            lines.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            foreach (var (_, line) in lines)
            {
                sb.Append(line).Append('\n');
            }
        }

        /// <summary>Found story texts (#1111): the save's net fragments, this player's memories and field
        /// records — full text for what was found, a sealed "???" line for what is still out there. Per-player
        /// progress like Discoveries; the logs rebuild from the join snapshot, so this survives a rejoin.</summary>
        private string BuildLore()
        {
            var sb = new StringBuilder();
            sb.Append("<b><size=24>").Append(L("ui.wiki.lore")).Append("</size></b>\n\n");

            var fragments = Game?.StoryLogFragments;
            var memories = Game?.StoryLogMemories;
            var lore = Game?.StoryLogLore;
            int found = (fragments?.Count ?? 0) + (memories?.Count ?? 0) + (lore?.Count ?? 0);
            if (found == 0)
            {
                sb.Append(L("ui.wiki.lore.empty"));
                return sb.ToString();
            }

            Shared.Story.StoryDefinition pack = null;
            if (Game?.Content != null && Game.Story != null)
            {
                Game.Content.TryGetStory(Game.Story.StoryId, out pack);
            }

            int total = (pack?.Fragments.Count ?? 0) + (pack?.Memories.Count ?? 0) + (pack?.LoreSites.Count ?? 0);
            if (total > 0)
            {
                sb.Append(string.Format(L("ui.wiki.lore.found"), found, total)).Append("\n\n");
            }

            if (fragments is { Count: > 0 })
            {
                sb.Append("<b>").Append(L("ui.story.fragments")).Append(" (").Append(fragments.Count).Append(")</b>\n");
                foreach (var (cat, key) in fragments)
                {
                    sb.Append("  • <b>[").Append(L("lore.cat." + cat)).Append("]</b> ").Append(L(key)).Append('\n');
                }

                sb.Append('\n');
            }

            if (memories is { Count: > 0 })
            {
                sb.Append("<b>").Append(L("ui.story.memories")).Append(" (").Append(memories.Count).Append(")</b>\n");
                foreach (var key in memories)
                {
                    sb.Append("  • ").Append(L(key)).Append('\n');
                }

                sb.Append('\n');
            }

            if (lore is { Count: > 0 })
            {
                sb.Append("<b>").Append(L("ui.story.lore")).Append(" (").Append(lore.Count).Append(")</b>\n");
                foreach (var (site, key) in lore)
                {
                    sb.Append("  • <b>").Append(L("ui.lore.site." + site)).Append(":</b> ").Append(L(key)).Append('\n');
                }

                sb.Append('\n');
            }

            int missing = total - found;
            if (missing > 0)
            {
                sb.Append("<color=#9fb4c8>").Append(new string('?', 3)).Append(" × ").Append(missing).Append("</color>\n");
            }

            return sb.ToString();
        }

        /// <summary>The player's own scan log (#484): every first-time scan, grouped by kind. Unlike the other
        /// chapters this is per-player progress, not static content — before this the first-scan ledger never
        /// left the server, so a scan left no permanent record anywhere in the UI.</summary>
        private string BuildDiscoveries()
        {
            var sb = new StringBuilder();
            sb.Append("<b><size=24>").Append(L("ui.wiki.discoveries")).Append("</size></b>\n\n");

            var log = Game?.Discoveries;
            if (log == null || log.Count == 0)
            {
                sb.Append(HudUi.ExpandKeyTokens(Game?.Localizer, L("ui.wiki.discoveries.empty"))); // "press {key:Scan}" (#2502)
                return sb.ToString();
            }

            sb.Append(string.Format(L("ui.wiki.discoveries.count"), log.Count)).Append("\n\n");

            // Ledger keys are "kind:key" ("asteroid" has no colon); group so creatures/plants/materials read apart.
            // A monument's key carries the body id too ("monument:<location>:<archetype>"), so the same relic
            // found on another planet lists as its own discovery — which is exactly what it is.
            // #3496: a landmark's key is "landmark:<location>:<key>" — one discovery per body and landmark, like a monument's.
            // #3611: the giant ship wrecks — on planets ("giantwreck:<location>:<ship>") and in space ("space:giantwreck:<id>").
            // #3642: a lost civilization ("civ:<key>") — once per player, with its Codex title and text (WikiUI.Civilizations.cs).
            foreach (var kind in DiscoveryKinds)
            {
                var names = new List<(string Name, string Where, string Desc)>();
                foreach (var pair in log)
                {
                    int colon = pair.Key.IndexOf(':');
                    string entryKind = DiscoveryKindOf(pair.Key, colon);
                    if (entryKind != kind)
                    {
                        continue;
                    }

                    string subject = string.IsNullOrEmpty(pair.Value)
                        ? (colon > 0 ? pair.Key.Substring(colon + 1) : pair.Key) // pre-#484 entry: raw key
                        : pair.Value;
                    names.Add(DiscoveryRow(kind, pair.Key, pair.Value, subject));
                }

                if (names.Count == 0)
                {
                    continue;
                }

                names.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                sb.Append("<b>").Append(L("ui.wiki.discoveries." + kind)).Append(" (").Append(names.Count).Append(")</b>\n"); // per-kind tally (#1103)
                foreach (var (name, where, desc) in names)
                {
                    AppendDiscovery(sb, name, where, desc);
                }

                sb.Append('\n');
            }

            AppendFoundLogs(sb, log);
            return sb.ToString();
        }

        /// <summary>#2633/#3341: the old logs this player took out of a crate — one list per log set (station logs
        /// <c>log:&lt;seed&gt;</c>, G.D.S. logs <c>gdslog:&lt;seed&gt;</c>), each written out again from its seed
        /// (WorldGeneration.LogSets, the same code the server and the inventory use), so a log stays readable after the
        /// item itself is gone. Sorted by writer.</summary>
        private void AppendFoundLogs(StringBuilder sb, Dictionary<string, string> ledger)
        {
            foreach (var set in BlocksBeyondTheStars.WorldGeneration.LogSets.All)
            {
                var logs = new List<(string Writer, string Where, string Text)>();
                foreach (var (key, entry) in set.FoundIn(ledger.Keys, Game?.Content))
                {
                    logs.Add((entry.Writer, WhereFound(key, "log"), entry.Render(L)));
                }

                if (logs.Count == 0)
                {
                    continue;
                }

                logs.Sort((a, b) => string.Compare(a.Writer, b.Writer, StringComparison.CurrentCultureIgnoreCase));
                sb.Append("<b>").Append(L(set.CodexHeadingKey)).Append(" (").Append(logs.Count).Append(")</b>\n");
                foreach (var (writer, where, text) in logs)
                {
                    sb.Append("  • <b>").Append(writer).Append("</b>");
                    if (!string.IsNullOrEmpty(where))
                    {
                        sb.Append("  —  <color=#9fb4c8>").Append(where).Append("</color>");
                    }

                    sb.Append('\n').Append(text).Append("\n\n");
                }
            }
        }

        /// <summary>The "found on Kepler, Sol" suffix for a discovery (#1843), or null when the server recorded no
        /// site for it (scanned before sites existed, or somewhere the galaxy cannot place — older entries then
        /// read exactly as before). A PLACE is found where it is, so its line carries only the system name.</summary>
        private string WhereFound(string ledgerKey, string kind)
        {
            var sites = Game?.DiscoveryWhere;
            if (sites == null || !sites.TryGetValue(ledgerKey, out var site))
            {
                return null;
            }

            if (kind == "place")
            {
                return string.IsNullOrEmpty(site.System) ? null : site.System;
            }

            if (string.IsNullOrEmpty(site.Body))
            {
                return null;
            }

            string line = string.Format(L("ui.wiki.discoveries.where"), site.Body, site.System);
            // A body the galaxy filed under no system: drop the dangling ", " the template leaves behind.
            return string.IsNullOrEmpty(site.System) ? line.TrimEnd(' ', ',') : line;
        }

        /// <summary>One discovery: "• name — where found" (the site dim, like the "?" fillers), and — a landmark — its Codex line
        /// indented under it, dim like the chapters' descriptions.</summary>
        private static void AppendDiscovery(StringBuilder sb, string name, string where, string desc)
        {
            sb.Append("  • ").Append(name);
            if (!string.IsNullOrEmpty(where))
            {
                sb.Append("  —  <color=#9fb4c8>").Append(where).Append("</color>");
            }

            sb.Append('\n');
            if (!string.IsNullOrEmpty(desc))
            {
                sb.Append("      <color=#9fb4c8>").Append(desc).Append("</color>\n");
            }
        }

        /// <summary>A discovered landmark's Codex line: <c>landmark.&lt;key&gt;.desc</c>, the key being the part of the ledger key
        /// <c>landmark:&lt;location&gt;:&lt;key&gt;</c> after its last colon — empty when the locale has no such text.</summary>
        private string LandmarkDesc(string ledgerKey)
            => Desc("landmark." + ledgerKey.Substring(ledgerKey.LastIndexOf(':') + 1) + ".desc");

        /// <summary>Localizes a discovery subject: a block/item key resolves through the content tables, a
        /// coined species name is already language-neutral and shows as-is (mirrors <c>HudUi.ScanSubjectName</c>).</summary>
        private string SubjectName(string subject)
        {
            if (string.IsNullOrEmpty(subject))
            {
                return subject;
            }

            if (Game?.Content?.GetBlock(subject) is { } b)
            {
                return L(b.NameKey);
            }

            if (Game?.Content?.GetItem(subject) is { } it)
            {
                return L(it.NameKey);
            }

            string key = "ui.scan.subject." + subject;
            return Game?.Localizer != null && Game.Localizer.Has(key) ? L(key) : subject;
        }

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

        /// <summary>The ores whose absence is worth a plain sentence (#1476): the "deep prizes" the Codex guide
        /// promises on the rough worlds — so a varied-world miner learns that digging deeper here finds none.</summary>
        private static readonly string[] RareOres =
        {
            "gold_ore", "titanium_ore", "cobalt_ore", "uranium_ore", "platinum_ore", "tungsten_ore", "neodymium_ore", "diamond_ore",
        };

        /// <summary>#1476: planet entries carry their ore table — every ore with the depth it starts at, and one
        /// line when the type has none of the rare metals — so "how deep is it worth going?" has an answer.</summary>
        private List<(string Name, string Desc)> PlanetEntries()
        {
            var result = new List<(string Name, string Desc)>();
            var planets = Game?.Content?.Planets?.Values;
            if (planets == null)
            {
                return result;
            }

            foreach (var d in planets)
            {
                if (d.Void)
                {
                    continue; // station decks / ship cabins are not worlds you land on
                }

                var sb = new StringBuilder(Desc(ToDescKey(d.NameKey)));
                var ores = new List<BlocksBeyondTheStars.Shared.Definitions.OreVein>(d.Ores ?? new List<BlocksBeyondTheStars.Shared.Definitions.OreVein>());
                ores.Sort((a, b) => a.MinDepth != b.MinDepth
                    ? a.MinDepth.CompareTo(b.MinDepth)
                    : string.Compare(L("block." + a.Block + ".name"), L("block." + b.Block + ".name"), StringComparison.CurrentCultureIgnoreCase));
                if (ores.Count > 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append(L("ui.wiki.ores_by_depth")).Append(' ');
                    bool rare = false;
                    for (int i = 0; i < ores.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(L("ui.wiki.ore_from_depth")
                            .Replace("{name}", L("block." + ores[i].Block + ".name"))
                            .Replace("{depth}", ores[i].MinDepth.ToString()));
                        rare |= Array.IndexOf(RareOres, ores[i].Block) >= 0;
                    }

                    if (!rare)
                    {
                        sb.Append('\n').Append(L("ui.wiki.no_rare_metals"));
                    }
                }

                // #2374: the living types hold oil pockets (the generator's rule — never a cratered body or a gas giant).
                if (d.HasLife && !d.Cratered && !d.IsGasWorld)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append(L("ui.wiki.oil_pockets"));
                }

                result.Add((L(d.NameKey), sb.ToString()));
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
        /// renders a description line only when one actually exists (Localizer.Get returns "[key]" for a miss).
        /// Its <c>{key:Action}</c> tokens read as the player's own key, pad button or touch button (#2503).</summary>
        private string Desc(string key)
            => string.IsNullOrEmpty(key) || Game?.Localizer == null || !Game.Localizer.Has(key)
                ? string.Empty
                : HudUi.ExpandKeyTokens(Game.Localizer, Game.Localizer.Get(key));

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
            var articles = LoadArticles();
            var sb = new StringBuilder();
            foreach (string id in articles.Ids)
            {
                var (title, body) = articles.Article(id);
                if (title.Length == 0 && body.Length == 0)
                {
                    continue;
                }

                sb.Append("<b><size=24>").Append(ExpandKeys(title)).Append("</size></b>\n\n");
                sb.Append(WikiMarkup.ToUnityRichText(body, ExpandKeys)).Append("\n\n\n");
            }

            return sb.ToString();
        }

        /// <summary>#2508: an article's <c>{key:Action}</c> tokens read as the player's own key, pad button or touch button —
        /// the prompt form of the item descriptions (<see cref="Desc"/>), so a tablet reads ACT for a verb without its own
        /// button. Bodies go through <see cref="WikiMarkup.ToUnityRichText(string, Func{string, string})"/>, which expands
        /// after the markup conversion so a glyph is never mistaken for a tag.</summary>
        private string ExpandKeys(string text) => HudUi.ExpandKeyTokens(Game?.Localizer, text);

        /// <summary>The guide's articles in the player's language (#2508), read once per language. A missing or broken file
        /// used to remove every hand-written article without a trace (#428): the reader reports it here.</summary>
        private WikiArticleText LoadArticles()
        {
            string code = LocaleCode;
            if (_articleText == null || _articleText.LocaleCode != code)
            {
                _articleText = WikiArticleText.Load(StreamingAssetsCache.DataDir, code, problem => Debug.LogWarning("[WikiUI] " + problem));
            }

            return _articleText;
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            foreach (var tex in _wikiImages.Values)
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }
    }
}
