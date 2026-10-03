// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One release post of the in-game "What's new?" feed — the devblog release post in German
    /// and English, exported by <c>tools/devblog/export_whatsnew.py</c> into <c>data/whatsnew.json</c>.
    /// Field names mirror the JSON (JsonUtility maps by name). Every other language comes from its own
    /// language file — ask <see cref="WhatsNew.Title"/> / <see cref="WhatsNew.Body"/> for display text.</summary>
    [Serializable]
    public sealed class WhatsNewEntry
    {
        public string version = "";
        public string date = "";
        public string title_de = "";
        public string title_en = "";
        public string body_de = "";
        public string body_en = "";

        /// <summary>Title from the feed itself: German for "de", English for every other code (and German
        /// for the odd entry authored DE-only).</summary>
        public string BaseTitle(string code) => Pick(code, title_en, title_de);

        /// <summary>Body from the feed itself, same rule as <see cref="BaseTitle"/>.</summary>
        public string BaseBody(string code) => Pick(code, body_en, body_de);

        private static string Pick(string code, string en, string de)
        {
            string chosen = code == "de" ? de : en;
            if (!string.IsNullOrEmpty(chosen))
            {
                return chosen;
            }

            return !string.IsNullOrEmpty(en) ? en : de;
        }
    }

    /// <summary>JsonUtility wrapper for the whatsnew.json root object.</summary>
    [Serializable]
    public sealed class WhatsNewFile
    {
        public List<WhatsNewEntry> entries = new List<WhatsNewEntry>();
    }

    /// <summary>One release post in ONE community language — an entry of
    /// <c>data-online/whatsnew/&lt;code&gt;.json</c>, matched to the feed by <see cref="version"/>.</summary>
    [Serializable]
    public sealed class WhatsNewLanguageEntry
    {
        public string version = "";
        public string title = "";
        public string body = "";
    }

    /// <summary>JsonUtility wrapper for a per-language file.</summary>
    [Serializable]
    public sealed class WhatsNewLanguageFile
    {
        public string language = "";
        public List<WhatsNewLanguageEntry> entries = new List<WhatsNewLanguageEntry>();
    }

    /// <summary>
    /// Loads the "What's new?" feed once per session: the committed <c>data/whatsnew.json</c> fetched
    /// raw from the repository's main branch (so the feed can be NEWER than the installed build —
    /// the interesting case next to the update notice #543), falling back to the copy bundled into
    /// StreamingAssets by the data/ sync when offline. Presentation-only data, same trust model as
    /// the rest of the bundled content.
    /// <para>
    /// The feed carries German and English. The other twelve languages live in one file each under
    /// <c>data-online/whatsnew/</c>, which is deliberately NOT bundled (twelve more copies of the notes
    /// would be megabytes in every download and in the browser's eager data prefetch): the player's
    /// language file is fetched online and laid over the feed by version. A language without a file, an
    /// entry the file does not have yet, or no connection all read English — as they always did.
    /// </para>
    /// </summary>
    public static class WhatsNew
    {
        private const string OnlineUrl =
            "https://raw.githubusercontent.com/marceld23/BlocksBeyondTheStars/main/data/whatsnew.json";

        private const string LanguageUrlFormat =
            "https://raw.githubusercontent.com/marceld23/BlocksBeyondTheStars/main/data-online/whatsnew/{0}.json";

        // Language code -> (version -> text). A code is present once its file was asked for, with an
        // empty map when the file is missing or failed, so no language is fetched twice per session.
        private static readonly Dictionary<string, Dictionary<string, WhatsNewLanguageEntry>> LanguageTexts =
            new Dictionary<string, Dictionary<string, WhatsNewLanguageEntry>>();

        private static readonly HashSet<string> LanguagesLoading = new HashSet<string>();

        /// <summary>Loaded entries, newest first; null while no load has finished yet. An empty list
        /// means both the online fetch and the bundled fallback came up dry.</summary>
        public static List<WhatsNewEntry> Entries { get; private set; }

        /// <summary>True when the online fetch failed and <see cref="Entries"/> is the bundled copy —
        /// the dialog shows a small "offline" hint, because bundled notes end at the installed version.</summary>
        public static bool FromBundled { get; private set; }

        private static bool _started;

        /// <summary>Starts the one-per-session background load (no-op on repeat calls). Called when the
        /// main menu first builds, so the data is usually in before anyone opens the dialog.
        /// <paramref name="languageCode"/> is the player's language at that moment; its language file is
        /// loaded along with the feed.</summary>
        public static void BeginFetch(MonoBehaviour runner, string languageCode)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            runner.StartCoroutine(Fetch(languageCode));
        }

        /// <summary>True for every language that is not in the feed itself, i.e. all but German and English.</summary>
        public static bool HasLanguageFile(string code)
            => !string.IsNullOrEmpty(code) && code != "de" && code != "en";

        /// <summary>Whether the texts for <paramref name="code"/> are settled: German and English always
        /// are, another language once its file was fetched (or found missing), and every language while
        /// the feed is the bundled offline copy — there is nothing to fetch then.</summary>
        public static bool LanguageReady(string code)
            => !HasLanguageFile(code) || FromBundled || LanguageTexts.ContainsKey(code);

        /// <summary>Asks for the language file of <paramref name="code"/> if this session has not done so
        /// yet — the player can switch language after the startup load. Safe to call every frame.</summary>
        public static void EnsureLanguage(MonoBehaviour runner, string code)
        {
            if (Entries == null || LanguageReady(code) || LanguagesLoading.Contains(code))
            {
                return;
            }

            runner.StartCoroutine(FetchLanguage(code));
        }

        /// <summary>Title of a release post for a locale code: the language file's text when it has this
        /// version, otherwise the feed's English (German for "de").</summary>
        public static string Title(WhatsNewEntry entry, string code)
            => Localized(entry, code)?.title ?? entry.BaseTitle(code);

        /// <summary>Body of a release post for a locale code, same rule as <see cref="Title"/>.</summary>
        public static string Body(WhatsNewEntry entry, string code)
            => Localized(entry, code)?.body ?? entry.BaseBody(code);

        private static WhatsNewLanguageEntry Localized(WhatsNewEntry entry, string code)
            => code != null
               && LanguageTexts.TryGetValue(code, out var texts)
               && texts.TryGetValue(entry.version, out var text)
                ? text
                : null;

        /// <summary>Stores the texts of one language (null = forget the language, so it is fetched again).
        /// The loader's own sink, and the seam the edit-mode tests use.</summary>
        public static void SetLanguageTexts(string code, Dictionary<string, WhatsNewLanguageEntry> texts)
        {
            if (texts == null)
            {
                LanguageTexts.Remove(code);
            }
            else
            {
                LanguageTexts[code] = texts;
            }
        }

        /// <summary>Parses a per-language file into version -> text. Entries without a version, title or
        /// body are dropped (they would only blank out the English fallback). Null when the JSON is unusable.</summary>
        public static Dictionary<string, WhatsNewLanguageEntry> ParseLanguageFile(string json)
        {
            try
            {
                var file = JsonUtility.FromJson<WhatsNewLanguageFile>(json);
                if (file?.entries == null)
                {
                    return null;
                }

                var texts = new Dictionary<string, WhatsNewLanguageEntry>();
                foreach (var e in file.entries)
                {
                    if (e != null && !string.IsNullOrEmpty(e.version)
                        && !string.IsNullOrEmpty(e.title) && !string.IsNullOrEmpty(e.body))
                    {
                        texts[e.version] = e;
                    }
                }

                return texts;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"What's-new language file parse failed: {e.Message}");
                return null;
            }
        }

        private static IEnumerator Fetch(string languageCode)
        {
            List<WhatsNewEntry> entries = null;
            using (var req = UnityWebRequest.Get(OnlineUrl))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    entries = Parse(req.downloadHandler.text);
                }
            }

            if (entries != null)
            {
                // The player's language first, the entries second: the first paint — and the auto-open
                // after an update — is then already in that language instead of flipping from English.
                if (HasLanguageFile(languageCode))
                {
                    yield return FetchLanguage(languageCode);
                }

                Entries = entries;
                yield break;
            }

            // Offline / rate-limited / malformed: fall back to the copy bundled with this build. By the
            // time the menu is up the StreamingAssets cache is ready on WebGL too, so plain File IO works.
            FromBundled = true;
            try
            {
                string path = Path.Combine(StreamingAssetsCache.DataDir, "whatsnew.json");
                entries = File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"What's-new bundled fallback failed: {e.Message}");
            }

            Entries = entries ?? new List<WhatsNewEntry>();
        }

        private static IEnumerator FetchLanguage(string code)
        {
            LanguagesLoading.Add(code);
            Dictionary<string, WhatsNewLanguageEntry> texts = null;
            using (var req = UnityWebRequest.Get(string.Format(LanguageUrlFormat, code)))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    texts = ParseLanguageFile(req.downloadHandler.text);
                }
            }

            // No file for this language yet (HTTP 404), no connection, or unusable JSON: remember the
            // empty result. The language then reads English and is not asked for again this session.
            SetLanguageTexts(code, texts ?? new Dictionary<string, WhatsNewLanguageEntry>());
            LanguagesLoading.Remove(code);
        }

        private static List<WhatsNewEntry> Parse(string json)
        {
            try
            {
                var file = JsonUtility.FromJson<WhatsNewFile>(json);
                return file?.entries == null || file.entries.Count == 0 ? null : file.entries;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"What's-new parse failed: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// The "What's new?" dialog over the main menu: the devblog release posts in the player's language
    /// (English where a post has no translation yet), newest first in a scrollable list. Opened from the menu's bottom-bar button, and automatically
    /// ONCE after an update (AppShell tracks <see cref="ClientSettings.LastSeenVersion"/> and queues
    /// the auto-open behind the update notice #543). AppShell spawns/destroys it with the MainMenu phase.
    /// </summary>
    public static class UiWhatsNew
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("WhatsNewUI");
            canvas.sortingOrder = UiKit.ShellModalSortingOrder; // above the menu even when it is rebuilt later (#2163)
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject);

            var dim = UiKit.AddModalDim(root);
            var dlg = UiKit.AddDialogPanel(dim.transform, 260f, 80f, 1400f, 920f);
            UiKit.AddText(dlg, 40f, 22f, 1320f, 34f, shell.L("ui.whatsnew.title"), 26,
                UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var hint = UiKit.AddText(dlg, 40f, 60f, 1320f, 24f, "", 14, UiKit.CyanDim, TextAnchor.MiddleCenter);

            var content = BuildScrollList(dlg, out var scroll);
            UiKit.AddVerticalScrollbar(dlg, scroll, 1366f, 92f, 16f, 740f);
            Populate(shell, content, hint);

            // Opened before the background load finished (or it is still falling back), or in a language
            // the player switched to after it: a tiny refresher fills the list in as soon as the data
            // lands, then removes itself.
            string code = LanguageCode(shell);
            if (WhatsNew.Entries == null || !WhatsNew.LanguageReady(code))
            {
                var refresher = canvas.gameObject.AddComponent<WhatsNewRefresher>();
                refresher.Runner = shell; // outlives the dialog, so a fetch survives closing it
                refresher.Code = code;
                refresher.OnReady = () => { if (content != null) { Populate(shell, content, hint); } };
            }

            UiKit.AddButton(dlg, 570f, 846f, 260f, 52f, shell.L("ui.menu.back"), shell.CloseWhatsNew, "btn_exit");
            return canvas.gameObject;
        }

        /// <summary>The player's locale code ("en" for a shell that has no settings yet).</summary>
        private static string LanguageCode(AppShell shell)
            => GameLocaleExtensions.Parse(shell.Settings?.Language).Code();

        /// <summary>Row width inside the scroll content: viewport 1320 minus text margins and the
        /// scrollbar lane on the right.</summary>
        private const float RowW = 1260f;

        /// <summary>Vertical ScrollRect clipped to the dialog — the settings screen's proven pattern
        /// (absolute top-left rows on a content rect whose height <see cref="Populate"/> sets last).</summary>
        private static Transform BuildScrollList(Transform dlg, out ScrollRect scroll)
        {
            var viewGo = new GameObject("WhatsNewScroll", typeof(RectTransform));
            viewGo.transform.SetParent(dlg, false);
            UiKit.Place(viewGo, 40f, 92f, 1320f, 740f);

            scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            viewGo.AddComponent<RectMask2D>();

            // Near-transparent hit surface so the wheel/drag works over empty areas (same trick as the
            // settings screen's scroll viewport).
            var hit = viewGo.AddComponent<Image>();
            hit.sprite = UiKit.SolidSprite;
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(viewGo.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 100f);

            scroll.viewport = (RectTransform)viewGo.transform;
            scroll.content = content;
            return content;
        }

        private static void Populate(AppShell shell, Transform content, Text hint)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            }

            float y = 8f;
            var entries = WhatsNew.Entries;
            if (entries == null)
            {
                AddRow(content, ref y, shell.L("ui.portal.working"), 18, UiKit.CyanDim, FontStyle.Normal);
            }
            else if (entries.Count == 0)
            {
                hint.text = WhatsNew.FromBundled ? shell.L("ui.whatsnew.offline") : "";
                AddRow(content, ref y, shell.L("ui.whatsnew.empty"), 18, UiKit.CyanDim, FontStyle.Normal);
            }
            else
            {
                hint.text = WhatsNew.FromBundled ? shell.L("ui.whatsnew.offline") : "";
                string code = LanguageCode(shell);
                foreach (var e in entries)
                {
                    AddRow(content, ref y, $"Version {e.version} — {WhatsNew.Title(e, code)}", 22, UiKit.Cyan, FontStyle.Bold);
                    AddRow(content, ref y, MarkdownToRich(WhatsNew.Body(e, code)), 17, UiKit.TextCol, FontStyle.Normal);
                    y += 18f; // breathing room between releases
                }
            }

            ((RectTransform)content).sizeDelta = new Vector2(0f, y + 16f);
        }

        /// <summary>One wrapped text row placed absolutely at the running y offset, which advances by the
        /// measured text height. Measured with an explicit scaleFactor of 1, so the height comes back in
        /// the same 1920×1080 reference units the row is placed in — independent of canvas scaling.</summary>
        private static void AddRow(Transform content, ref float y, string text, int size, Color col, FontStyle style)
        {
            float h = MeasureHeight(text, size, style) + 6f;
            var t = UiKit.AddText(content, 24f, y, RowW, h, text, size, col, TextAnchor.UpperLeft, style);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            y += h + 8f;
        }

        private static readonly TextGenerator Measurer = new TextGenerator();

        private static float MeasureHeight(string text, int size, FontStyle style)
        {
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = size,
                fontStyle = style,
                richText = true,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(RowW, 0f),
                textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            return Measurer.GetPreferredHeight(text, settings);
        }

        /// <summary>Devblog markdown → uGUI rich text: bold/italic map to tags, bullets to •, links keep
        /// their text, headings turn bold. Raw angle brackets are neutralized FIRST so post content can
        /// never inject tags (same concern ChatMarkup.RichSafe covers for chat).</summary>
        private static string MarkdownToRich(string md)
        {
            string s = md.Replace("<", "‹").Replace(">", "›");
            s = Regex.Replace(s, @"^#{2,4}\s*(.+)$", "<b>$1</b>", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");           // [text](url) → text
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            s = Regex.Replace(s, @"(?<![*\w])\*([^*\n]+)\*(?![*\w])", "<i>$1</i>");
            s = s.Replace("`", string.Empty);
            s = Regex.Replace(s, @"^[-*]\s+", "• ", RegexOptions.Multiline); // list bullets
            s = Regex.Replace(s, @"^›\s?", string.Empty, RegexOptions.Multiline); // '>' quotes (already ›)
            return s.Trim();
        }

        /// <summary>Fills the list in once the feed AND the player's language have landed, then removes
        /// itself. Asks for the language file itself when nobody has yet (a language switched mid-session).</summary>
        private sealed class WhatsNewRefresher : MonoBehaviour
        {
            public System.Action OnReady;
            public MonoBehaviour Runner;
            public string Code = "en";

            private void Update()
            {
                if (WhatsNew.Entries == null)
                {
                    return;
                }

                if (!WhatsNew.LanguageReady(Code))
                {
                    WhatsNew.EnsureLanguage(Runner != null ? Runner : this, Code);
                    return;
                }

                OnReady?.Invoke();
                Destroy(this);
            }
        }
    }
}
