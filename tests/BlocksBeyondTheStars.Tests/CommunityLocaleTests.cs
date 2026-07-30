// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Guards for community-contributed languages beyond the mandatory DE/EN pair (currently Italian,
/// <c>data/locales/it.json</c>). These are translated incrementally, one key group per pull request, so they
/// are deliberately NOT held to the completeness bar that <see cref="ContentTests"/> enforces for en/de —
/// missing keys fall back to English per key (<c>GameContent.CreateLocalizer</c>) and that is a supported state.
/// <para>
/// What DOES break the game is caught here instead: a key that exists in no other language (a typo nothing
/// will ever read), a lost or invented <c>{0}</c>/<c>{item}</c> placeholder (a format hole in the middle of a
/// sentence), an empty string rendering as blank UI, and a locale file no <see cref="GameLocale"/> member
/// loads. Each failure message names the exact keys so a contributor can fix them without a local checkout.
/// </para>
/// </summary>
public class CommunityLocaleTests
{
    /// <summary>Both placeholder styles the locale tables use: positional (<c>{0}</c>) and named
    /// (<c>{item}</c>, <c>{player}</c>). Matched as whole tokens so a stray brace is reported, not ignored.</summary>
    private static readonly Regex PlaceholderPattern = new(
        @"\{[A-Za-z0-9_]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    /// <summary>The languages this file governs: every <see cref="GameLocale"/> whose file exists on disk,
    /// minus the two that have their own stricter completeness tests.</summary>
    public static TheoryData<string> CommunityLocales()
    {
        var data = new TheoryData<string>();
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            if (locale is GameLocale.English or GameLocale.German)
            {
                continue;
            }

            string code = locale.Code();
            if (File.Exists(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")))
            {
                data.Add(code);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CommunityLocales))]
    public void CommunityLocale_InventsNoKeys(string code)
    {
        var table = TestLocales.Load(code);
        var en = TestLocales.Load("en");

        var orphans = table.Keys.Where(k => !en.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(
            orphans.Count == 0,
            $"{code}.json has {orphans.Count} key(s) that do not exist in en.json — a typo or a key removed "
            + $"from the game since the translation was written; nothing reads them: {string.Join(", ", orphans.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(CommunityLocales))]
    public void CommunityLocale_PreservesPlaceholders(string code)
    {
        var table = TestLocales.Load(code);
        var en = TestLocales.Load("en");
        var broken = new List<string>();

        foreach (var (key, translated) in table)
        {
            if (!en.TryGetValue(key, out var source))
            {
                continue; // reported by CommunityLocale_InventsNoKeys — don't fail twice for one mistake
            }

            // Order may legitimately differ (word order moves between languages, the placeholder moves with
            // it), so compare the SET of placeholders, not the sequence.
            var expected = Placeholders(source);
            var actual = Placeholders(translated);
            if (!expected.SetEquals(actual))
            {
                broken.Add($"{key} (en: {Format(expected)} / {code}: {Format(actual)})");
            }
        }

        Assert.True(
            broken.Count == 0,
            $"{code}.json changes the placeholder set of {broken.Count} key(s) — the game substitutes values "
            + $"into these at runtime, so a dropped or renamed placeholder leaves a hole in the sentence: "
            + string.Join("; ", broken.Take(20)));
    }

    [Theory]
    [MemberData(nameof(CommunityLocales))]
    public void CommunityLocale_HasNoBlankValues(string code)
    {
        var table = TestLocales.Load(code);

        var blanks = table.Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        // A blank value SHADOWS the English fallback (the key is present, so the fallback never runs) and
        // renders as empty UI — strictly worse than leaving the key out of the file entirely.
        Assert.True(
            blanks.Count == 0,
            $"{code}.json has {blanks.Count} empty value(s); an empty string shadows the English fallback and "
            + $"renders as blank UI — remove the key instead: {string.Join(", ", blanks.Take(20))}");
    }

    [Fact]
    public void EveryLocaleFile_IsLoadedBySomeGameLocale()
    {
        var known = Enum.GetValues<GameLocale>().Select(l => l.Code()).ToHashSet(StringComparer.Ordinal);

        var stray = Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name) && !known.Contains(name!))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // ContentLoader enumerates the GameLocale enum, so a file whose code has no enum member is dead
        // weight that ships to players and never loads — exactly the state it.json was in before this PR.
        Assert.True(
            stray.Count == 0,
            $"data/locales contains {stray.Count} file(s) no GameLocale member loads — add the language to the "
            + $"enum (GameLocale.cs) or delete the file: {string.Join(", ", stray)}");
    }

    [Fact]
    public void EveryGameLocale_LoadsThroughContentLoader()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            string code = locale.Code();
            if (!File.Exists(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")))
            {
                continue; // a declared-but-not-yet-written language is fine; the loader skips it
            }

            // Round-trip through the real loader: proves the enum member, its Code() and the file name agree,
            // and that a partial table still answers with the English fallback rather than a raw key.
            var localizer = content.CreateLocalizer(locale);
            Assert.False(
                string.IsNullOrWhiteSpace(localizer.Get("ui.menu.play")),
                $"locale '{code}' produced no text for 'ui.menu.play' — neither its own table nor the English fallback resolved");
        }
    }

    private static HashSet<string> Placeholders(string text)
        => PlaceholderPattern.Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    private static string Format(HashSet<string> placeholders)
        => placeholders.Count == 0 ? "none" : string.Join(" ", placeholders.OrderBy(p => p, StringComparer.Ordinal));
}
