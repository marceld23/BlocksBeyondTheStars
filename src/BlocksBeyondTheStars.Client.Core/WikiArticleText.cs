// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Codex guide's articles (#2508). The article LIST — ids and order — is <c>data/wiki/articles.json</c>; the TEXTS are
    /// flat per-language tables <c>data/wiki/locales/&lt;code&gt;.json</c> with the keys <c>wiki.&lt;id&gt;.title</c> and
    /// <c>wiki.&lt;id&gt;.body</c> — the format of the game's own locale tables, so <c>tools/translate_locale.py</c> translates
    /// them as they are. English and German hold every article; any other language may lack some. An article the player's
    /// language does not have — title AND body — reads English as a whole (never a translated title over an English body),
    /// and so does an article added later that no translation knows yet. A missing table reads as empty, a broken one too
    /// (reported through the loader's warning callback). Unity-free so the fallback rules are unit-tested; <c>WikiUI</c>
    /// renders what this resolves.
    /// </summary>
    public sealed class WikiArticleText
    {
        /// <summary>The article list under the data folder.</summary>
        public const string ArticleListPath = "wiki/articles.json";

        /// <summary>The language every article falls back to.</summary>
        public const string FallbackLocale = "en";

        private readonly IReadOnlyDictionary<string, string> _english;
        private readonly IReadOnlyDictionary<string, string> _language;

        public WikiArticleText(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string>? english,
            IReadOnlyDictionary<string, string>? language, string localeCode)
        {
            Ids = ids ?? Array.Empty<string>();
            _english = english ?? new Dictionary<string, string>(StringComparer.Ordinal);
            _language = language ?? _english;
            LocaleCode = string.IsNullOrEmpty(localeCode) ? FallbackLocale : localeCode;
        }

        /// <summary>The language the tables were read for (the player's locale code).</summary>
        public string LocaleCode { get; }

        /// <summary>The article ids in Codex order (<see cref="ArticleListPath"/>).</summary>
        public IReadOnlyList<string> Ids { get; }

        /// <summary>A language's table under the data folder: <c>wiki/locales/&lt;code&gt;.json</c>.</summary>
        public static string TablePath(string localeCode) => "wiki/locales/" + localeCode + ".json";

        /// <summary>An article's title key: <c>wiki.&lt;id&gt;.title</c>.</summary>
        public static string TitleKey(string id) => "wiki." + id + ".title";

        /// <summary>An article's body key: <c>wiki.&lt;id&gt;.body</c>.</summary>
        public static string BodyKey(string id) => "wiki." + id + ".body";

        /// <summary>Whether the article reads in the player's language: that table holds its title and its body.</summary>
        public bool IsTranslated(string id) => HasBoth(_language, id);

        /// <summary>The article's title and body (the body still in the Codex's HTML subset, <see cref="WikiMarkup"/>): in the
        /// player's language when <see cref="IsTranslated"/>, otherwise both in English; empty strings for an id neither table
        /// knows.</summary>
        public (string Title, string Body) Article(string id)
        {
            var table = HasBoth(_language, id) ? _language : _english;
            return (Text(table, TitleKey(id)), Text(table, BodyKey(id)));
        }

        /// <summary>The ids of an article list: a top-level JSON array of objects carrying an <c>"id"</c> (other fields are
        /// ignored) or of bare id strings, in order. Blank and repeated ids are dropped.</summary>
        public static List<string> ParseArticleIds(string json)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                string? id = entry.ValueKind switch
                {
                    JsonValueKind.String => entry.GetString(),
                    JsonValueKind.Object when entry.TryGetProperty("id", out var p) && p.ValueKind == JsonValueKind.String => p.GetString(),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id!))
                {
                    ids.Add(id!);
                }
            }

            return ids;
        }

        /// <summary>Reads the list and the English table plus the one of <paramref name="localeCode"/> from a data folder
        /// (<c>StreamingAssets/data</c> on a desktop, the download cache in the browser). Never throws: a missing file reads as
        /// empty — a language without Codex translations is a normal state — and a file that cannot be read or parsed is
        /// reported through <paramref name="warn"/> and reads as empty too.</summary>
        public static WikiArticleText Load(string dataDir, string localeCode, Action<string>? warn = null)
        {
            string code = string.IsNullOrEmpty(localeCode) ? FallbackLocale : localeCode;
            var ids = new List<string>();
            string? list = ReadFile(dataDir, ArticleListPath, warn);
            if (list != null)
            {
                try
                {
                    ids = ParseArticleIds(list);
                }
                catch (JsonException e)
                {
                    warn?.Invoke($"{ArticleListPath} is not valid JSON: {e.Message}");
                }
            }

            var english = ReadTable(dataDir, FallbackLocale, warn);
            var language = code == FallbackLocale ? english : ReadTable(dataDir, code, warn);
            return new WikiArticleText(ids, english, language, code);
        }

        private static Dictionary<string, string> ReadTable(string dataDir, string code, Action<string>? warn)
        {
            string? json = ReadFile(dataDir, TablePath(code), warn);
            if (json == null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            try
            {
                return ContentLoader.ParseLocaleTable(json);
            }
            catch (JsonException e)
            {
                warn?.Invoke($"{TablePath(code)} is not valid JSON: {e.Message}");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static string? ReadFile(string dataDir, string relative, Action<string>? warn)
        {
            try
            {
                string path = Path.Combine(dataDir, relative.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e)
            {
                warn?.Invoke($"could not read {relative}: {e.Message}");
                return null;
            }
        }

        private static bool HasBoth(IReadOnlyDictionary<string, string> table, string id)
            => Text(table, TitleKey(id)).Length > 0 && Text(table, BodyKey(id)).Length > 0;

        private static string Text(IReadOnlyDictionary<string, string> table, string key)
            => table.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : string.Empty;
    }
}
