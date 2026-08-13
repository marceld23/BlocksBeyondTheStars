// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Server-side localization for the public portal pages (issue #970). The portal used to be a pair of
/// inline <c>T(de, en)</c> literals per string, which structurally could never hold a third language —
/// while the game itself ships fourteen. Text now lives in one JSON table per language under
/// <c>Locales/</c>, EMBEDDED in the assembly: the WorldHost container image carries no <c>data/</c>
/// folder, so an embedded resource is the only form that survives a plain <c>dotnet publish</c> without
/// a new volume mount or Dockerfile COPY.
/// <para>
/// Missing keys fall back to English through <see cref="Localizer"/>, so a partially translated language
/// is mechanically safe to ship — exactly like the in-game locales.
/// </para>
/// </summary>
public static class PortalLocales
{
    /// <summary>The portal's default language: the service's primary audience, and what anything
    /// unrecognized falls back to.</summary>
    public const string DefaultLang = "de";

    /// <summary>Languages offered in the switcher, in display order: the two defaults first, then the
    /// community languages by code — the same set the game ships in <c>data/locales/</c>.</summary>
    public static IReadOnlyList<GameLocale> Supported { get; } = new[]
    {
        GameLocale.German,
        GameLocale.English,
        GameLocale.Spanish,
        GameLocale.French,
        GameLocale.Italian,
        GameLocale.Japanese,
        GameLocale.Korean,
        GameLocale.Dutch,
        GameLocale.Polish,
        GameLocale.Portuguese,
        GameLocale.Russian,
        GameLocale.Turkish,
        GameLocale.Ukrainian,
        GameLocale.ChineseSimplified,
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables =
        LoadTables();

    private static readonly Dictionary<string, PortalText> Texts =
        Supported.ToDictionary(
            locale => locale.Code(),
            locale => new PortalText(locale, Table(locale.Code()), Table("en")),
            StringComparer.Ordinal);

    /// <summary>Every key the portal knows about — the English table IS the contract, since that is
    /// what every other language falls back to.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = Table("en").Keys.ToArray();

    /// <summary>Clamps a request's language choice to a supported portal language. Unlike the old
    /// two-language version this accepts every locale the game ships; German still wins for anything
    /// unknown, and matching stays exact (the switcher and the cookie only ever emit our own codes).</summary>
    public static string Normalize(string? lang)
        => lang is not null && Texts.ContainsKey(lang) ? lang : DefaultLang;

    /// <summary>True when <paramref name="lang"/> is a code the portal serves — used by the request
    /// pipeline to decide whether a <c>?lang=</c> value is worth remembering in the cookie.</summary>
    public static bool IsSupported(string? lang) => lang is not null && Texts.ContainsKey(lang);

    /// <summary>The text table for a language (unknown codes get the German default).</summary>
    public static PortalText For(string? lang) => Texts[Normalize(lang)];

    /// <summary>First-visit language from the browser's <c>Accept-Language</c> header: the first
    /// supported primary tag wins (browsers list tags in preference order, so full q-value parsing would
    /// only complicate this). A browser that asks for a language the portal does NOT serve gets English —
    /// the game's fallback language everywhere else too. Only a missing header keeps the German default.
    /// Only consulted when neither <c>?lang=</c> nor the <c>bbs_lang</c> cookie carries an explicit
    /// choice — auto-detection never persists, so a deliberate switch always outranks it.</summary>
    public static string LangFromAcceptHeader(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return DefaultLang;
        }

        foreach (string part in acceptLanguage.Split(','))
        {
            string tag = part.Split(';')[0].Trim();
            // "de-DE" → "de"; a bare "*" or a three-letter tag like "eng" matches nothing on purpose.
            string primary = tag.Length >= 3 && tag[2] == '-' ? tag[..2] : tag;
            if (GameLocaleExtensions.TryParse(primary, out var locale) && Texts.ContainsKey(locale.Code()))
            {
                return locale.Code();
            }
        }

        return "en";
    }

    private static IReadOnlyDictionary<string, string> Table(string code)
        => Tables.TryGetValue(code, out var table)
            ? table
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Reads every <c>Locales/*.json</c> embedded resource once at startup. A language whose
    /// file is missing simply resolves everything through the English fallback.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadTables()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        const string prefix = "BlocksBeyondTheStars.WorldHost.Locales.";

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (parsed is not null)
            {
                tables[name[prefix.Length..^".json".Length]] = parsed;
            }
        }

        return tables;
    }
}

/// <summary>
/// The localized text of one portal language, plus the link/attribute bits every page needs to keep that
/// language across navigations.
/// </summary>
public sealed class PortalText
{
    private readonly Localizer _localizer;
    private readonly IReadOnlyDictionary<string, string> _own;

    internal PortalText(
        GameLocale locale,
        IReadOnlyDictionary<string, string> ownTable,
        IReadOnlyDictionary<string, string> englishFallback)
    {
        Locale = locale;
        Lang = locale.Code();
        _own = ownTable;
        _localizer = new Localizer(locale, ownTable, englishFallback);
    }

    public GameLocale Locale { get; }

    /// <summary>The two-letter code, e.g. "de" — also what goes into <c>&lt;html lang&gt;</c>.</summary>
    public string Lang { get; }

    /// <summary>The language's name in itself, for the switcher.</summary>
    public string NativeName => Locale.NativeName();

    /// <summary>True for the German pages, whose legal texts are the authoritative ones.</summary>
    public bool IsGerman => Locale == GameLocale.German;

    /// <summary>Query string that pins this language on a plain link. Always emitted, German included —
    /// the DE/EN version left German links bare and a visitor whose <c>bbs_lang</c> cookie said English
    /// could never walk back to German through the footer.</summary>
    public string Query => "?lang=" + Lang;

    /// <summary>Localized text for a key (English fallback, then <c>[key]</c>).</summary>
    public string T(string key) => _localizer.Get(key);

    /// <summary>True when THIS language carries the key itself, rather than borrowing the English
    /// fallback — the coverage check the locale tests assert on.</summary>
    public bool IsTranslated(string key) => _own.ContainsKey(key);

    /// <summary>Localized text with <c>{placeholder}</c> substitution. Placeholders carry the parts a
    /// translator must not touch — mostly link markup whose <c>href</c> has to stay machine-readable.</summary>
    public string T(string key, params (string Name, string Value)[] values)
    {
        string text = _localizer.Get(key);
        foreach (var (name, value) in values)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }
}
