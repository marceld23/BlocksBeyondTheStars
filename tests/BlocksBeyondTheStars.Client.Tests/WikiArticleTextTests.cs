// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2508: the Codex reader — the article list from <c>wiki/articles.json</c>, the texts from the per-language tables
/// <c>wiki/locales/&lt;code&gt;.json</c>. English stands in per article: an article the player's language does not have
/// (title and body) reads English as a whole, and so does one added later that no translation knows yet.
/// </summary>
public sealed class WikiArticleTextTests
{
    private static Dictionary<string, string> Table(params (string Key, string Value)[] entries)
        => entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> English = Table(
        ("wiki.a.title", "Alpha"), ("wiki.a.body", "<p>alpha</p>"),
        ("wiki.b.title", "Beta"), ("wiki.b.body", "<p>beta</p>"),
        ("wiki.c.title", "Gamma"), ("wiki.c.body", "<p>gamma</p>"));

    [Fact]
    public void AnArticleTheLanguageHas_ReadsInThatLanguage()
    {
        var french = Table(("wiki.a.title", "Alpha FR"), ("wiki.a.body", "<p>alpha fr</p>"));
        var text = new WikiArticleText(new[] { "a" }, English, french, "fr");

        Assert.Equal(("Alpha FR", "<p>alpha fr</p>"), text.Article("a"));
        Assert.True(text.IsTranslated("a"));
        Assert.Equal("fr", text.LocaleCode);
    }

    [Fact]
    public void AnArticleTheLanguageLacks_ReadsEnglish_TitleAndBodyTogether()
    {
        // b: only the title translated — never a French title over an English body; c: a blank body counts as missing;
        // d: an article added later, which no table but English knows.
        var french = Table(("wiki.b.title", "Bêta"), ("wiki.c.title", "Gamma FR"), ("wiki.c.body", "  "));
        var english = new Dictionary<string, string>(English, StringComparer.Ordinal) { ["wiki.d.title"] = "Delta", ["wiki.d.body"] = "<p>delta</p>" };
        var text = new WikiArticleText(new[] { "b", "c", "d" }, english, french, "fr");

        Assert.Equal(("Beta", "<p>beta</p>"), text.Article("b"));
        Assert.Equal(("Gamma", "<p>gamma</p>"), text.Article("c"));
        Assert.Equal(("Delta", "<p>delta</p>"), text.Article("d"));
        Assert.False(text.IsTranslated("b"));
        Assert.False(text.IsTranslated("c"));
        Assert.False(text.IsTranslated("d"));
    }

    [Fact]
    public void WithoutALanguageTable_EveryArticleReadsEnglish_AndAnUnknownIdIsEmpty()
    {
        var text = new WikiArticleText(new[] { "a", "zzz" }, English, null, "ja");

        Assert.Equal(("Alpha", "<p>alpha</p>"), text.Article("a"));
        Assert.Equal((string.Empty, string.Empty), text.Article("zzz"));
        Assert.Equal("wiki.a.title", WikiArticleText.TitleKey("a"));
        Assert.Equal("wiki.a.body", WikiArticleText.BodyKey("a"));
        Assert.Equal("wiki/locales/ja.json", WikiArticleText.TablePath("ja"));
    }

    [Fact]
    public void ParseArticleIds_ReadsObjectsAndStrings_InOrder_DroppingBlanksAndRepeats()
    {
        // Other fields of an entry are ignored — an old-style entry with inline title/body texts still lists its id.
        const string json = "[ { \"id\": \"b\" }, \"a\", { \"id\": \"\" }, { \"title\": {} }, { \"id\": \"b\" }, 5,"
                            + " { \"id\": \"c\", \"title\": { \"en\": \"old\" } }, ]";

        Assert.Equal(new[] { "b", "a", "c" }, WikiArticleText.ParseArticleIds(json));
        Assert.Empty(WikiArticleText.ParseArticleIds("{ \"id\": \"a\" }"));
    }

    [Fact]
    public void Load_ReadsTheDataFolder_ToleratingAMissingAndABrokenTable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bbs-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "wiki", "locales"));
            File.WriteAllText(Path.Combine(dir, "wiki", "articles.json"), "[ { \"id\": \"a\" }, { \"id\": \"b\" } ]");
            File.WriteAllText(Path.Combine(dir, "wiki", "locales", "en.json"),
                "{ \"wiki.a.title\": \"Alpha\", \"wiki.a.body\": \"<p>alpha</p>\", \"wiki.b.title\": \"Beta\", \"wiki.b.body\": \"<p>beta</p>\" }");
            File.WriteAllText(Path.Combine(dir, "wiki", "locales", "de.json"), "{ \"wiki.b.title\": \"Beta DE\", \"wiki.b.body\": \"<p>beta de</p>\" }");
            File.WriteAllText(Path.Combine(dir, "wiki", "locales", "fr.json"), "{ \"wiki.a.title\": ");

            var warnings = new List<string>();
            var german = WikiArticleText.Load(dir, "de", warnings.Add);
            Assert.Equal(new[] { "a", "b" }, german.Ids);
            Assert.Equal(("Alpha", "<p>alpha</p>"), german.Article("a"));
            Assert.Equal(("Beta DE", "<p>beta de</p>"), german.Article("b"));

            var korean = WikiArticleText.Load(dir, "ko", warnings.Add); // no table yet: English, and no warning
            Assert.Equal(("Beta", "<p>beta</p>"), korean.Article("b"));
            Assert.Empty(warnings);

            var french = WikiArticleText.Load(dir, "fr", warnings.Add); // a broken table is reported and reads English
            Assert.Equal(("Alpha", "<p>alpha</p>"), french.Article("a"));
            Assert.Single(warnings);
            Assert.Contains("wiki/locales/fr.json", warnings[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_WithoutAnyFile_IsEmpty_AndQuiet()
    {
        var warnings = new List<string>();
        var text = WikiArticleText.Load(Path.Combine(Path.GetTempPath(), "bbs-wiki-missing-" + Guid.NewGuid().ToString("N")), "de", warnings.Add);

        Assert.Empty(text.Ids);
        Assert.Empty(warnings);
    }

    [Fact]
    public void TheRealCodex_ReadsEveryArticle_InEnglishAndGerman_AndEnglishForALanguageWithoutATable()
    {
        string data = ClientTestPaths.DataDir();
        var english = WikiArticleText.Load(data, "en");
        var german = WikiArticleText.Load(data, "de");
        var none = WikiArticleText.Load(data, "xx"); // no such table: every article reads English

        Assert.True(english.Ids.Count >= 30, $"only {english.Ids.Count} Codex articles listed");
        foreach (string id in english.Ids)
        {
            Assert.True(german.IsTranslated(id), $"{id} has no German text");
            Assert.NotEqual(string.Empty, english.Article(id).Title);
            Assert.Equal(english.Article(id), none.Article(id));
        }
    }
}
