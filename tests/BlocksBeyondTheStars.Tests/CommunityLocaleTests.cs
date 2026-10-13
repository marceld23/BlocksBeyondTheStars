// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
/// sentence), an empty string rendering as blank UI, a brace that belongs to no token (rendered literally),
/// and a locale file no <see cref="GameLocale"/> member loads. Each failure message names the exact keys so a
/// contributor can fix them without a local checkout.
/// </para>
/// <para>
/// A few terminology guards (#2501) sit here too: no word that mixes Latin and Cyrillic letters, no combining accent,
/// and a short per-language table of terms that once slipped in and must not come back (<see cref="ForbiddenTerms"/>).
/// </para>
/// </summary>
public class CommunityLocaleTests
{
    /// <summary>Every token the locale tables use: positional (<c>{0}</c>), named (<c>{item}</c>, <c>{player}</c>)
    /// and the key-binding control token (<c>{key:ToggleLamp}</c>). Named tokens are filled by plain string
    /// replacement; positional ones go through <c>string.Format</c> after the key tokens are expanded
    /// (<c>VegaText.Compose</c>, #2363 — the order matters). There is no conditional or plural syntax, so anything
    /// else in braces is a defect.</summary>
    private static readonly Regex PlaceholderPattern = new(
        @"\{(?:key:)?[A-Za-z0-9_]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    /// <summary>A run of letters — a word, split at spaces, digits, hyphens and punctuation. Used by the mixed-script guard,
    /// so "SPS-реле", "Mk2" or a placeholder next to a Cyrillic word never count as one word.</summary>
    private static readonly Regex LetterRun = new(
        @"\p{L}+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    /// <summary>Terms that once slipped into a community table and must not come back (#2501): an English name a machine
    /// pass left untranslated, or a word that means something else in the language. <c>Codes</c> is a comma list, "*" for
    /// all twelve languages; <c>Use</c> names the canonical wording the failure message suggests. Checked in the base table
    /// and every story pack's table of that language. The canonical terms per language are listed in
    /// TRANSLATION_GUIDE.md. (fr and it keep the English name "AI Core" on purpose — their names say so too.)</summary>
    private static readonly (string Codes, Regex Term, string Use)[] ForbiddenTerms =
    [
        ("*", Term(@"(?i)\bguardian\b"), "the language's own name for the Guardian (Koruyucu, Strażnik, Bewaker, Guardiano, ガーディアン …)"),
        ("es,ja,ko,nl,pl,pt,ru,tr,uk,zh", Term(@"\bAI Core\b"), "the language's own name of the AI core, as in module.ai_core_mk3.name"),
        ("*", Term(@"(?i)\bship (?:keel|helm|engine)\b|\bkeel\b|\bshipwright\b|\bself-built ship\b|\bstarter ship\b"),
            "the language's own ship-part names (item.ship_core/ship_helm/ship_engine.name, blueprint.ship_builder.name, ship.*.name)"),
        ("tr", Term(@"(?i)hiperspace|hipermekan"), "Hiperuzay"),
        ("pl", Term(@"(?i)fabryfikator"), "Fabrykator"),
        ("it", Term(@"(?i)\bfabricator"), "Fabbricatore"),
        ("fr", Term(@"(?i)\bétrier"), "Poste de pilotage (the ship's helm)"),
        ("nl", Term(@"(?i)\bafme(?:ren|ert|erde|erden)\b|\bafgemeerd\b"), "loskoppelen (undock); aanmeren / aangemeerd (dock)"),
        ("uk", Term(@"(?i)\bстраж(?:а|у|ем|і)?\b|\bсторож(?:а|у|ем)?\b"), "Охоронець / Серце Охоронця"),
    ];

    private static Regex Term(string pattern)
        => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Every translated table the game and its web portal ship — the places the machine pass
    /// (<c>tools/translate_locale.py</c>) writes to: the base table, every story pack's <c>locales/</c> (#2465 — the
    /// packs are enumerated, not hard-coded to VEGA) and the portal's own tables.</summary>
    private static IEnumerable<string> LocaleDirs()
    {
        yield return Path.Combine("data", "locales");
        foreach (string pack in StoryPackFolders())
        {
            yield return Path.Combine("data", "stories", pack, "locales");
        }

        yield return Path.Combine("src", "BlocksBeyondTheStars.WorldHost", "Locales");
    }

    /// <summary>The story-pack folders under <c>data/stories</c> that carry a <c>locales/</c> table, in ordinal order.</summary>
    private static IEnumerable<string> StoryPackFolders()
    {
        string stories = Path.Combine(TestPaths.DataDir(), "stories");
        if (!Directory.Exists(stories))
        {
            yield break;
        }

        foreach (string dir in Directory.GetDirectories(stories).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (Directory.Exists(Path.Combine(dir, "locales")))
            {
                yield return Path.GetFileName(dir);
            }
        }
    }

    /// <summary>(pack, language) for every community-language table a story pack ships (#2465): the same guards as the
    /// base tables, measured against the pack's own <c>en.json</c>.</summary>
    public static TheoryData<string, string> CommunityPackLocales()
    {
        var data = new TheoryData<string, string>();
        foreach (string pack in StoryPackFolders())
        {
            foreach (GameLocale locale in Enum.GetValues<GameLocale>())
            {
                if (locale is GameLocale.English or GameLocale.German)
                {
                    continue;
                }

                string code = locale.Code();
                if (File.Exists(Path.Combine(TestPaths.DataDir(), "stories", pack, "locales", code + ".json")))
                {
                    data.Add(pack, code);
                }
            }
        }

        return data;
    }

    private static Dictionary<string, string> PackTable(string pack, string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
               File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", pack, "locales", code + ".json"))) ?? [];

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

    /// <summary>A pack's community table holds only keys of the pack's own English table (#2465).</summary>
    [Theory]
    [MemberData(nameof(CommunityPackLocales))]
    public void CommunityPackLocale_InventsNoKeys(string pack, string code)
    {
        var table = PackTable(pack, code);
        var en = PackTable(pack, "en");

        var orphans = table.Keys.Where(k => !en.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(
            orphans.Count == 0,
            $"stories/{pack}/locales/{code}.json has {orphans.Count} key(s) that are not in the pack's en.json: {string.Join(", ", orphans.Take(20))}");
    }

    /// <summary>A pack's community table keeps the placeholder set of the pack's English text (#2465).</summary>
    [Theory]
    [MemberData(nameof(CommunityPackLocales))]
    public void CommunityPackLocale_PreservesPlaceholders(string pack, string code)
    {
        var table = PackTable(pack, code);
        var en = PackTable(pack, "en");
        var broken = new List<string>();
        foreach (var (key, translated) in table)
        {
            if (en.TryGetValue(key, out var source) && !Placeholders(source).SetEquals(Placeholders(translated)))
            {
                broken.Add($"{key} (en: {Format(Placeholders(source))} / {code}: {Format(Placeholders(translated))})");
            }
        }

        Assert.True(
            broken.Count == 0,
            $"stories/{pack}/locales/{code}.json changes the placeholder set of {broken.Count} key(s): " + string.Join("; ", broken.Take(20)));
    }

    /// <summary>A blank pack value shadows the English fallback just like a blank base value (#2465).</summary>
    [Theory]
    [MemberData(nameof(CommunityPackLocales))]
    public void CommunityPackLocale_HasNoBlankValues(string pack, string code)
    {
        var blanks = PackTable(pack, code).Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(
            blanks.Count == 0,
            $"stories/{pack}/locales/{code}.json has {blanks.Count} empty value(s) that shadow the English fallback: {string.Join(", ", blanks.Take(20))}");
    }

    /// <summary>A pack's locale file must be one a <see cref="GameLocale"/> loads, like the base tables (#2465).</summary>
    [Fact]
    public void EveryPackLocaleFile_IsLoadedBySomeGameLocale()
    {
        var known = Enum.GetValues<GameLocale>().Select(l => l.Code()).ToHashSet(StringComparer.Ordinal);
        var stray = StoryPackFolders()
            .SelectMany(pack => Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "stories", pack, "locales"), "*.json")
                .Select(f => (Pack: pack, Code: Path.GetFileNameWithoutExtension(f))))
            .Where(f => !known.Contains(f.Code))
            .Select(f => $"stories/{f.Pack}/locales/{f.Code}.json")
            .ToList();

        Assert.True(stray.Count == 0, $"story-pack locale file(s) no GameLocale member loads: {string.Join(", ", stray)}");
    }

    [Fact]
    public void EveryLocaleFile_HasNoStrayBraces()
    {
        var stray = new List<string>();
        foreach (string dir in LocaleDirs())
        {
            foreach (string path in Directory.GetFiles(Path.Combine(TestPaths.RepoRoot(), dir), "*.json"))
            {
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
                foreach (var (key, value) in table)
                {
                    string rest = PlaceholderPattern.Replace(value, string.Empty);
                    if (rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal))
                    {
                        stray.Add($"{Path.GetRelativePath(TestPaths.RepoRoot(), path).Replace('\\', '/')}: {key} (\"{value}\")");
                    }
                }
            }
        }

        // The placeholder-set check above only sees well-formed tokens, so a mangled one — a machine pass once
        // wrote "{count?100:100}" where English has the plain number 100 (#1973) — compares as "no placeholders"
        // on both sides and slips through, then renders literally in game. Any brace outside a token is caught here.
        Assert.True(
            stray.Count == 0,
            $"{stray.Count} locale value(s) contain a brace that is not part of a {{0}}/{{name}}/{{key:Action}} token — "
            + $"the game does no conditional or plural formatting, so it would show up literally: {string.Join("; ", stray.Take(20))}");
    }

    /// <summary>A word that mixes Latin and Cyrillic letters — "Uплінк", "вырастet", or an Italian "Rottа" whose last
    /// letter is a Cyrillic а (#2501). Machine passes and keyboard-layout slips leave these behind; they look almost right
    /// on screen but read as typos and break search. Placeholders are blanked first and words split at every non-letter,
    /// so "SPS-реле", "Mk2" or a <c>{key:…}</c> token next to Cyrillic text stay legal.</summary>
    [Fact]
    public void EveryLocaleFile_HasNoMixedScriptWords()
    {
        var mixed = new List<string>();
        foreach (var (file, key, value) in LocaleValues())
        {
            foreach (Match word in LetterRun.Matches(PlaceholderPattern.Replace(value, " ")))
            {
                if (word.Value.Any(IsLatinLetter) && word.Value.Any(IsCyrillicLetter))
                {
                    mixed.Add($"{file}: {key} (\"{word.Value}\")");
                }
            }
        }

        Assert.True(
            mixed.Count == 0,
            $"{mixed.Count} locale word(s) mix Latin and Cyrillic letters — retype the word in one script: "
            + string.Join("; ", mixed.Take(20)));
    }

    /// <summary>A combining accent (U+0300–U+036F) in a locale value: a stray stress mark ("Ста́йный") or a decomposed
    /// letter ("s" + U+0301 instead of "ś", #2501). Game text never needs either, and fonts render them as a detached
    /// accent.</summary>
    [Fact]
    public void EveryLocaleFile_HasNoCombiningAccents()
    {
        var marked = LocaleValues()
            .Where(v => v.Value.Any(c => c is >= '̀' and <= 'ͯ'))
            .Select(v => $"{v.File}: {v.Key}")
            .ToList();

        Assert.True(
            marked.Count == 0,
            $"{marked.Count} locale value(s) contain a combining accent — a stress mark or a decomposed letter; write the "
            + $"precomposed letter (ś, é) or drop the mark: {string.Join("; ", marked.Take(20))}");
    }

    /// <summary>Each community language keeps its canonical terms (<see cref="ForbiddenTerms"/>, #2501) in its base table
    /// and in every story pack's table.</summary>
    [Theory]
    [MemberData(nameof(CommunityLocales))]
    public void CommunityLocale_KeepsItsCanonicalTerms(string code)
    {
        var tables = new List<(string Name, Dictionary<string, string> Table)> { ($"locales/{code}.json", TestLocales.Load(code)) };
        foreach (string pack in StoryPackFolders())
        {
            if (File.Exists(Path.Combine(TestPaths.DataDir(), "stories", pack, "locales", code + ".json")))
            {
                tables.Add(($"stories/{pack}/locales/{code}.json", PackTable(pack, code)));
            }
        }

        var hits = new List<string>();
        foreach (var (codes, term, use) in ForbiddenTerms)
        {
            if (codes != "*" && !codes.Split(',').Contains(code, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var (name, table) in tables)
            {
                foreach (var (key, value) in table)
                {
                    Match found = term.Match(value);
                    if (found.Success)
                    {
                        hits.Add($"{name}: {key} (\"{found.Value}\" — use {use})");
                    }
                }
            }
        }

        Assert.True(
            hits.Count == 0,
            $"{code}: {hits.Count} value(s) use a term this language replaced with its canonical wording: "
            + string.Join("; ", hits.Take(20)));
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

    /// <summary>Every (file, key, value) of every table in <see cref="LocaleDirs"/>, the file as a repo-relative path.</summary>
    private static IEnumerable<(string File, string Key, string Value)> LocaleValues()
    {
        foreach (string dir in LocaleDirs())
        {
            foreach (string path in Directory.GetFiles(Path.Combine(TestPaths.RepoRoot(), dir), "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                string file = Path.GetRelativePath(TestPaths.RepoRoot(), path).Replace('\\', '/');
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
                foreach (var (key, value) in table)
                {
                    yield return (file, key, value);
                }
            }
        }
    }

    private static bool IsLatinLetter(char c)
        => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 'À' and <= 'ɏ');

    private static bool IsCyrillicLetter(char c)
        => c is >= 'Ѐ' and <= 'ӿ';

    private static HashSet<string> Placeholders(string text)
        => PlaceholderPattern.Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    private static string Format(HashSet<string> placeholders)
        => placeholders.Count == 0 ? "none" : string.Join(" ", placeholders.OrderBy(p => p, StringComparer.Ordinal));
}
