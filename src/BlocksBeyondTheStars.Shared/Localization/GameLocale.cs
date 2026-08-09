// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// Supported in-game languages. The game must be playable in both German and English; English is the
/// fallback locale and every locale falls back to it per missing key (see <c>GameContent.CreateLocalizer</c>),
/// so a partially translated language is mechanically safe to ship.
/// <para>
/// Community languages beyond DE/EN are added here as soon as their locale file exists, so contributors can
/// see their own strings in the game. Whether a language is <em>offered in the settings menu</em> is a
/// separate decision gated on translation coverage — <c>data/locales/it.json</c> (Italian, contributed by
/// @alessandroquirino-lab) is loadable but not yet listed in the picker.
/// </para>
/// </summary>
public enum GameLocale
{
    English,
    German,
    Italian,
    French,
    Spanish,
    Portuguese,
    Polish,
    Turkish,
    Dutch,
    Russian,
    Ukrainian,
    ChineseSimplified,
    Japanese,
    Korean,
}

public static class GameLocaleExtensions
{
    /// <summary>The file-name code used for locale resource files, e.g. "en", "de", "it".
    /// <c>ContentLoader</c> enumerates this enum and loads <c>data/locales/{Code()}.json</c> when present,
    /// so adding a member here is all it takes to make a locale file load.</summary>
    public static string Code(this GameLocale locale) => locale switch
    {
        GameLocale.English => "en",
        GameLocale.German => "de",
        GameLocale.Italian => "it",
        GameLocale.French => "fr",
        GameLocale.Spanish => "es",
        GameLocale.Portuguese => "pt",
        GameLocale.Polish => "pl",
        GameLocale.Turkish => "tr",
        GameLocale.Dutch => "nl",
        GameLocale.Russian => "ru",
        GameLocale.Ukrainian => "uk",
        GameLocale.ChineseSimplified => "zh",
        GameLocale.Japanese => "ja",
        GameLocale.Korean => "ko",
        _ => "en",
    };

    /// <summary>The language's name in itself, for the settings picker — a French player should
    /// find "Français" without having to read the current language first.</summary>
    public static string NativeName(this GameLocale locale) => locale switch
    {
        GameLocale.English => "English",
        GameLocale.German => "Deutsch",
        GameLocale.Italian => "Italiano",
        GameLocale.French => "Français",
        GameLocale.Spanish => "Español",
        GameLocale.Portuguese => "Português",
        GameLocale.Polish => "Polski",
        GameLocale.Turkish => "Türkçe",
        GameLocale.Dutch => "Nederlands",
        GameLocale.Russian => "Русский",
        GameLocale.Ukrainian => "Українська",
        GameLocale.ChineseSimplified => "简体中文",
        GameLocale.Japanese => "日本語",
        GameLocale.Korean => "한국어",
        _ => locale.ToString(),
    };

    /// <summary>Parses a locale code (or language name) as sent by clients and written to
    /// <c>client_settings.json</c>. Returns false for unknown codes; <paramref name="locale"/> is set to
    /// English in that case, so callers that don't care about the difference can ignore the result.</summary>
    public static bool TryParse(string code, out GameLocale locale)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "en":
            case "en-us":
            case "english":
                locale = GameLocale.English;
                return true;
            case "de":
            case "de-de":
            case "german":
            case "deutsch":
                locale = GameLocale.German;
                return true;
            case "it":
            case "it-it":
            case "italian":
            case "italiano":
                locale = GameLocale.Italian;
                return true;
            case "fr":
            case "fr-fr":
            case "french":
            case "francais":
            case "français":
                locale = GameLocale.French;
                return true;
            case "es":
            case "es-es":
            case "spanish":
            case "espanol":
            case "español":
                locale = GameLocale.Spanish;
                return true;
            case "pt":
            case "pt-br":
            case "pt-pt":
            case "portuguese":
            case "portugues":
            case "português":
                locale = GameLocale.Portuguese;
                return true;
            case "pl":
            case "pl-pl":
            case "polish":
            case "polski":
                locale = GameLocale.Polish;
                return true;
            case "tr":
            case "tr-tr":
            case "turkish":
            case "türkçe":
            case "turkce":
                locale = GameLocale.Turkish;
                return true;
            case "nl":
            case "nl-nl":
            case "dutch":
            case "nederlands":
                locale = GameLocale.Dutch;
                return true;
            case "ru":
            case "ru-ru":
            case "russian":
            case "русский":
                locale = GameLocale.Russian;
                return true;
            case "uk":
            case "uk-ua":
            case "ukrainian":
            case "українська":
                locale = GameLocale.Ukrainian;
                return true;
            case "zh":
            case "zh-cn":
            case "zh-hans":
            case "chinese":
            case "中文":
            case "简体中文":
                locale = GameLocale.ChineseSimplified;
                return true;
            case "ja":
            case "ja-jp":
            case "japanese":
            case "日本語":
                locale = GameLocale.Japanese;
                return true;
            case "ko":
            case "ko-kr":
            case "korean":
            case "한국어":
                locale = GameLocale.Korean;
                return true;
            default:
                locale = GameLocale.English;
                return false;
        }
    }

    /// <summary>Convenience for the many call sites that only want a locale and treat anything unknown as
    /// English — replaces the <c>== "de" ? German : English</c> pattern that could never see a third language.</summary>
    public static GameLocale Parse(string? code)
    {
        TryParse(code ?? string.Empty, out var locale);
        return locale;
    }
}
