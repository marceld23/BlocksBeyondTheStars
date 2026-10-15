// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2601: the villagers' wish templates in <c>data/wishes.json</c>. The content loader checks the structure (keys, kinds,
/// items, scan targets, jobs, world conditions, tiers, presents) at load; here: the starter pool has every kind and a
/// first-wish template for every eligible job, and every text resolves in English AND German (read raw — the German
/// localizer would fall back to English silently) with only the placeholders the server fills.
/// </summary>
public sealed class WishContentTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IEnumerable<string> PlaceholdersIn(string text) => Placeholder.Matches(text).Select(m => m.Value);

    [Fact]
    public void StarterPool_HasEveryKind_AndAFirstWishForEveryJob()
    {
        var wishes = Content.Value.Wishes;
        Assert.True(wishes.Count >= 11, $"only {wishes.Count} wish templates");
        Assert.Equal(wishes.Count, wishes.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(wishes, w => w.Kind == WishTemplate.KindDeliver);
        Assert.Contains(wishes, w => w.Kind == WishTemplate.KindShow);
        Assert.Contains(wishes, w => w.Kind == WishTemplate.KindScan);

        // Every job that has wishes can be asked a first wish that needs nothing special from the world.
        foreach (string job in WishTemplate.EligibleJobs)
        {
            Assert.Contains(wishes, w => w.Tier == 1 && w.Requires.Length == 0 && (w.Jobs.Count == 0 || w.Jobs.Contains(job)));
        }
    }

    [Fact]
    public void EveryTemplate_NamesRealThings_AndSaysThankYouWithSomething()
    {
        var content = Content.Value;
        foreach (var w in content.Wishes)
        {
            if (w.Kind == WishTemplate.KindScan)
            {
                Assert.True(ScanTargets.IsValid(w.Item, k => content.GetBlock(k) is not null), $"'{w.Key}': scan target '{w.Item}'");
            }
            else
            {
                Assert.NotNull(content.GetItem(w.Item));
            }

            Assert.True(w.Count - w.Jitter >= 1, $"'{w.Key}': {w.Count} ± {w.Jitter}");
            Assert.Contains(w.Requires, WishTemplate.KnownRequirements);
            Assert.InRange(w.Tier, 1, WishTemplate.MaxTier);
            Assert.True(w.Gift.Count > 0 || w.Knowledge > 0, $"'{w.Key}' gives nothing");
            Assert.All(w.Gift, g => Assert.NotNull(content.GetItem(g.Item)));
            Assert.All(w.Jobs, j => Assert.Contains(j, WishTemplate.EligibleJobs));
            Assert.False(string.IsNullOrWhiteSpace(w.Reason), $"'{w.Key}' has no reason for the AI wording");
        }
    }

    [Fact]
    public void EveryText_ResolvesInEnglishAndGerman_WithOnlyTheServersPlaceholders()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var keys = new List<(string Key, string[] Allowed)>
        {
            ("wish.title", new[] { "{npc}" }),
            ("wish.target.creature", Array.Empty<string>()),
            ("dlg.wish.c_yes", Array.Empty<string>()),
            ("dlg.wish.c_later", Array.Empty<string>()),
            ("dlg.wish.r_yes", Array.Empty<string>()),
            ("dlg.wish.r_yes_ready", Array.Empty<string>()),
            ("dlg.wish.r_later", Array.Empty<string>()),
            ("dlg.wish.fact", new[] { "{count}", "{item}" }),
            ("dlg.wish.fact_show", new[] { "{count}", "{item}" }),
            ("dlg.wish.fact_scan", new[] { "{count}", "{item}" }),
            ("dlg.wish.remind", new[] { "{have}", "{count}", "{item}" }),
            ("dlg.wish.remind_scan", new[] { "{have}", "{count}" }),
            ("dlg.wish.full", Array.Empty<string>()),
            ("dlg.wish.gift", new[] { "{npc}", "{gift}" }),
            ("dlg.wish.knowledge", new[] { "{count}" }),
            ("srv.wish.ready", new[] { "{name}" }),
            ("srv.wish.talk", Array.Empty<string>()),
            ("srv.wish.return_giver", new[] { "{name}" }),
        };
        foreach (var w in Content.Value.Wishes)
        {
            keys.Add((w.AskKey, new[] { "{count}", "{item}" }));
            keys.Add((w.ThanksKey, new[] { "{count}", "{item}" }));
        }

        foreach (var (key, allowed) in keys)
        {
            foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                foreach (var token in PlaceholdersIn(text!))
                {
                    Assert.True(allowed.Contains(token), $"{lang} '{key}' uses {token}, which the server does not fill there");
                }

                Assert.DoesNotMatch(@"\d", Placeholder.Replace(text!, string.Empty)); // numbers are facts — the server fills them
            }
        }

        // The fact line and the reminder actually SAY the facts.
        foreach (var lang in new[] { en, de })
        {
            Assert.Contains("{count}", lang["dlg.wish.fact"]);
            Assert.Contains("{item}", lang["dlg.wish.fact"]);
            Assert.Contains("{have}", lang["dlg.wish.remind"]);
        }
    }

    [Fact]
    public void TheWishFile_IsInTheClientsFallbackManifest()
    {
        // The browser build reads data/ through StreamingAssetsCache's fallback list; a data file missing there would never
        // reach a WebGL client (the Client.Tests check the whole list — this pins the wish file by name).
        string cache = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "StreamingAssetsCache.cs"));
        Assert.Contains("\"wishes.json\"", cache);
    }
}
