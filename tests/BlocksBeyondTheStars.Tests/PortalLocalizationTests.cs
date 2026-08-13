// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The portal's own locale tables (issue #970): the hosted-worlds site used to exist in German and
/// English only while the game shipped fourteen languages. These tests pin the properties a hand-edited
/// or machine-generated table could quietly break — full key coverage, intact placeholders and markup,
/// and pages that really render in the language that was asked for.
/// </summary>
public sealed class PortalLocalizationTests
{
    private static readonly WorldHostConfig Config = new();

    /// <summary>Substitution slots the pages fill in ({rules} carries a whole anchor, %s a runtime
    /// value in the browser) — a translation that drops one renders a broken sentence.</summary>
    private static readonly Regex Placeholder = new(@"\{[a-zA-Z0-9_]+\}", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Inline markup the pages emit as HTML rather than text.</summary>
    private static readonly Regex Tag = new(@"</?[a-zA-Z]+>", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static IEnumerable<string> Codes => PortalLocales.Supported.Select(l => l.Code());

    [Fact]
    public void EveryGameLanguage_IsAPortalLanguage()
    {
        // The whole point of #970: the two lists must not drift apart again.
        Assert.Equal(
            Enum.GetValues<GameLocale>().Select(l => l.Code()).OrderBy(c => c, StringComparer.Ordinal),
            Codes.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLanguage_TranslatesEveryKey()
    {
        var gaps = new List<string>();
        foreach (string code in Codes)
        {
            var text = PortalLocales.For(code);
            gaps.AddRange(PortalLocales.Keys.Where(key => !text.IsTranslated(key)).Select(key => $"{code}: {key}"));
        }

        // A missing key silently falls back to English — safe, but it is exactly the half-translated
        // page this issue set out to remove.
        Assert.Empty(gaps);
    }

    [Fact]
    public void EveryTranslation_KeepsItsPlaceholdersAndMarkup()
    {
        var english = PortalLocales.For("en");
        var broken = new List<string>();

        foreach (string code in Codes.Where(c => c != "en"))
        {
            var text = PortalLocales.For(code);
            foreach (string key in PortalLocales.Keys)
            {
                string source = english.T(key), translated = text.T(key);

                if (!Slots(source).SetEquals(Slots(translated)))
                {
                    broken.Add($"{code} [{key}]: placeholders {string.Join(",", Slots(source))} → {string.Join(",", Slots(translated))}");
                }

                if (CountOf(source, "%s") != CountOf(translated, "%s"))
                {
                    broken.Add($"{code} [{key}]: %s count {CountOf(source, "%s")} → {CountOf(translated, "%s")}");
                }

                if (!Tags(source).SequenceEqual(Tags(translated)))
                {
                    broken.Add($"{code} [{key}]: markup {string.Join(",", Tags(source))} → {string.Join(",", Tags(translated))}");
                }
            }
        }

        Assert.Empty(broken);

        static HashSet<string> Slots(string s)
            => Placeholder.Matches(s).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        static IEnumerable<string> Tags(string s)
            => Tag.Matches(s).Select(m => m.Value.ToLowerInvariant()).Order(StringComparer.Ordinal);

        static int CountOf(string s, string needle)
        {
            int count = 0, at = 0;
            while ((at = s.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }

            return count;
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryPage_RendersInEveryLanguage(string code)
    {
        foreach (string html in Pages(code))
        {
            Assert.Contains($"<html lang='{code}'>", html, StringComparison.Ordinal);

            // Localizer marks an unknown key as "[the.key]" — the pages must never ship one.
            Assert.DoesNotContain("[landing.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[worlds.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[shell.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[rules.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[err.", html, StringComparison.Ordinal);

            // Unfilled substitution slots would render as literal braces.
            Assert.DoesNotContain("{rules}", html, StringComparison.Ordinal);
            Assert.DoesNotContain("{worlds}", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryPage_KeepsItsLanguage_InEveryLink(string code)
    {
        // Losing ?lang= on a link drops the visitor back to German (or to whatever the cookie says) —
        // the DE/EN version left German links bare, so a switched-away visitor could not walk back.
        foreach (string html in Pages(code))
        {
            Assert.Contains($"href='/rules?lang={code}'", html, StringComparison.Ordinal);
            Assert.Contains($"href='/impressum?lang={code}'", html, StringComparison.Ordinal);
            Assert.Contains($"href='/datenschutz?lang={code}'", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("ja")]
    [InlineData("pl")]
    public void ANonGermanPage_ShowsNoGermanUiText(string code)
    {
        // Sample strings that only the German table can produce. The German legal bodies on
        // /impressum and /datenschutz are deliberately exempt — they are the authoritative texts.
        foreach (string html in new[]
        {
            WorldHostPortalPages.Landing(Config, code),
            WorldHostPortalPages.Worlds(Config, code),
            WorldHostPortalPages.Rules(Config, code),
        })
        {
            Assert.DoesNotContain("Konto erstellen", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Neue Welt", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Sei freundlich", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Welt wird gestartet", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlayPage_NotInstalledNotice_SpeaksEveryLanguage()
    {
        foreach (string code in Codes)
        {
            string html = PlayPage.NotInstalledHtml(code);
            Assert.Contains($"<html lang='{code}'>", html, StringComparison.Ordinal);
            Assert.Contains($"href='/?lang={code}'", html, StringComparison.Ordinal);
            Assert.DoesNotContain("[play.", html, StringComparison.Ordinal);
        }

        // Unknown codes still land on the German default rather than an empty page.
        Assert.Contains("noch nicht installiert", PlayPage.NotInstalledHtml("whatever"), StringComparison.Ordinal);
    }

    public static TheoryData<string> AllLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var locale in PortalLocales.Supported)
        {
            data.Add(locale.Code());
        }

        return data;
    }

    private static IEnumerable<string> Pages(string code)
    {
        yield return WorldHostPortalPages.Landing(Config, code);
        yield return WorldHostPortalPages.Worlds(Config, code);
        yield return WorldHostPortalPages.Rules(Config, code);
        yield return WorldHostPortalPages.Impressum(Config, code);
        yield return WorldHostPortalPages.Privacy(Config, code);
    }
}
