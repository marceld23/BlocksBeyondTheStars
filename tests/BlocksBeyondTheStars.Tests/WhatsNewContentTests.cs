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
