// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Guards the committed in-game "What's new?" feed (<c>data/whatsnew.json</c>, produced by
/// <c>tools/devblog/export_whatsnew.py</c> from the git-ignored devblog drafts at release time).
/// The client fetches this file raw from the repository AND ships it as the offline fallback, so a
/// malformed or half-filled export must fail here — the game itself only logs a warning and shows
/// an empty screen (#543).
/// <para>
/// The feed carries German and English. Every other language has one file under
/// <c>data-online/whatsnew/</c> that the client fetches online and lays over the feed by version;
/// those files are guarded for SHAPE only — a language may cover any subset of the releases
/// (missing ones read English in the game), so there is deliberately no completeness check.
/// </para>
/// </summary>
public class WhatsNewContentTests
{
    private static JsonElement LoadEntries()
    {
        string path = Path.Combine(TestPaths.DataDir(), "whatsnew.json");
        Assert.True(File.Exists(path), $"data/whatsnew.json missing at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("entries", out var entries), "root 'entries' missing");
        return entries.Clone();
    }

    [Fact]
    public void Feed_HasEntries_AllBilingualAndComplete()
    {
        var entries = LoadEntries();
        Assert.True(entries.GetArrayLength() > 0, "whatsnew.json has no entries");
        foreach (var e in entries.EnumerateArray())
        {
            string version = e.GetProperty("version").GetString() ?? "";
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);
            foreach (string field in new[] { "title_de", "title_en", "body_de", "body_en" })
            {
                string value = e.GetProperty(field).GetString() ?? "";
                Assert.False(string.IsNullOrWhiteSpace(value), $"{version}: '{field}' is empty");
            }
        }
    }

    [Fact]
    public void Feed_VersionsAreUniqueAndNewestFirst()
    {
        var entries = LoadEntries();
        var versions = entries.EnumerateArray()
            .Select(e => Version.Parse(e.GetProperty("version").GetString()!))
            .ToList();
        Assert.Equal(versions.Count, versions.Distinct().Count());
        var sorted = versions.OrderByDescending(v => v).ToList();
        Assert.Equal(sorted, versions);
    }

    private static string LanguageDir() => Path.Combine(TestPaths.RepoRoot(), "data-online", "whatsnew");

    /// <summary>The per-language files that exist today. None at all is a valid state.</summary>
    private static string[] LanguageFiles()
        => Directory.Exists(LanguageDir())
            ? Directory.GetFiles(LanguageDir(), "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    [Fact]
    public void LanguageFiles_AreNamedAfterGameLanguages_OtherThanTheFeedsOwn()
    {
        foreach (string file in LanguageFiles())
        {
            string code = Path.GetFileNameWithoutExtension(file);
            Assert.True(File.Exists(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
                $"data-online/whatsnew/{code}.json is not a game language (no data/locales/{code}.json)");
            Assert.False(code is "de" or "en",
                $"'{code}' is part of data/whatsnew.json itself — the client never fetches a language file for it");
        }
    }

    [Fact]
    public void LanguageFiles_AreWellFormed_AndOnlyCoverReleasesOfTheFeed()
    {
        var feedVersions = LoadEntries().EnumerateArray()
            .Select(e => e.GetProperty("version").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string file in LanguageFiles())
        {
            string name = Path.GetFileName(file);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(Path.GetFileNameWithoutExtension(file), doc.RootElement.GetProperty("language").GetString());

            var versions = new List<Version>();
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string version = e.GetProperty("version").GetString() ?? "";
                Assert.True(feedVersions.Contains(version),
                    $"{name}: version '{version}' is not in data/whatsnew.json — the client could never show it");
                foreach (string field in new[] { "title", "body" })
                {
                    string value = e.GetProperty(field).GetString() ?? "";
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{name} {version}: '{field}' is empty");
                }

                // The dialog prints "Version X — <title>" itself: the export strips the post's own prefix.
                Assert.DoesNotMatch(@"^Version \d", e.GetProperty("title").GetString()!);
                versions.Add(Version.Parse(version));
            }

            Assert.True(versions.Count > 0, $"{name} has no entries — delete the file instead");
            Assert.Equal(versions.Count, versions.Distinct().Count());
            Assert.Equal(versions.OrderByDescending(v => v).ToList(), versions);
        }
    }

    [Fact]
    public void Feed_LocaleKeysExistInBothLanguages()
    {
        // The dialog chrome around the feed (#543). Body text itself is bilingual inside the feed.
        foreach (string lang in new[] { "en", "de" })
        {
            var table = TestLocales.Load(lang);
            foreach (string key in new[] { "ui.menu.whatsnew", "ui.whatsnew.title", "ui.whatsnew.offline", "ui.whatsnew.empty" })
            {
                Assert.True(table.ContainsKey(key), $"locale '{lang}' is missing '{key}'");
            }
        }
    }
}
