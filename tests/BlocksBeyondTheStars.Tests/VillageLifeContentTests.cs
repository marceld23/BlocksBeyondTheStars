// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2599/#2600: the data of the villagers' chatter and the deed news. The game is bilingual by rule and a speech bubble
/// is no paragraph — every chatter line resolves in English AND German (read raw: the German localizer would fall back to
/// English silently), stays within <see cref="VillageChatterTopic.MaxLineChars"/>, and uses only the placeholders its
/// topic's need provides (a <c>{player}</c> in an everyday topic would have nobody to name). The content loader checks
/// the structure (<c>when</c>, <c>needs</c>, line counts) at load.
/// </summary>
public sealed class VillageLifeContentTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static IEnumerable<string> PlaceholdersIn(string text) => Placeholder.Matches(text).Select(m => m.Value);

    [Fact]
    public void Chatter_HasEnoughTopics_ForEveryTimeOfDay()
    {
        var topics = Content.Value.VillageLife.Chatter;
        Assert.True(topics.Count >= 16, $"only {topics.Count} chatter topics");
        Assert.Contains(topics, t => t.When == "day" && t.Needs.Length == 0);
        Assert.Contains(topics, t => t.When == "evening" && t.Needs.Length == 0);
        Assert.Contains(topics, t => t.When == "any" && t.Needs.Length == 0);

        // Every need the server knows has a topic, and every deed kind can be talked about.
        foreach (var need in VillageChatterNeeds.Plain)
        {
            Assert.Contains(topics, t => t.Needs == need);
        }

        foreach (var kind in DeedKinds.All)
        {
            Assert.Contains(topics, t => t.Needs == VillageChatterNeeds.DeedPrefix + kind);
        }
    }

    [Fact]
    public void ChatterLines_ResolveInEnglishAndGerman_AreShort_AndUseOnlyTheirPlaceholders()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (var topic in Content.Value.VillageLife.Chatter)
        {
            Assert.True(VillageChatterNeeds.IsKnown(topic.Needs), $"'{topic.Key}' needs '{topic.Needs}'");
            Assert.InRange(topic.Lines.Count, VillageChatterTopic.MinLines, VillageChatterTopic.MaxLines);
            var allowed = VillageChatterNeeds.TokensFor(topic.Needs);
            foreach (var key in topic.Lines)
            {
                foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
                {
                    Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                    Assert.True(text!.Length <= VillageChatterTopic.MaxLineChars,
                        $"{lang} '{key}' is {text.Length} characters (max {VillageChatterTopic.MaxLineChars}): {text}");
                    foreach (var token in PlaceholdersIn(text))
                    {
                        Assert.True(allowed.Contains(token), $"{lang} '{key}' uses {token}, which topic '{topic.Key}' (needs '{topic.Needs}') cannot fill");
                    }

                    Assert.DoesNotMatch(@"\d", Placeholder.Replace(text, string.Empty)); // numbers would be facts
                }
            }

            // The fact the need provides is actually SAID: a deed topic names its hero, a trader topic its trader.
            var tokens = topic.Lines.SelectMany(k => PlaceholdersIn(en[k])).ToList();
            if (topic.Needs.StartsWith(VillageChatterNeeds.DeedPrefix, StringComparison.Ordinal)
                || topic.Needs is VillageChatterNeeds.PlayerKnown or VillageChatterNeeds.FestivalPending)
            {
                Assert.Contains("{player}", tokens);
            }

            if (topic.Needs == VillageChatterNeeds.Trader)
            {
                Assert.Contains("{trader}", tokens);
            }
        }
    }

    [Fact]
    public void ChatterNeeds_Vocabulary_RejectsTypos()
    {
        Assert.True(VillageChatterNeeds.IsKnown(""));
        Assert.True(VillageChatterNeeds.IsKnown("weather_wet"));
        Assert.True(VillageChatterNeeds.IsKnown("deed:camp_cleared"));
        Assert.False(VillageChatterNeeds.IsKnown("weather_rain"));
        Assert.False(VillageChatterNeeds.IsKnown("deed:dragon"));
        Assert.Equal(new[] { "{a}", "{b}", "{place}", "{player}" }, VillageChatterNeeds.TokensFor("deed:giant"));
        Assert.Equal(new[] { "{a}", "{b}", "{place}", "{trader}" }, VillageChatterNeeds.TokensFor("trader"));
        Assert.Equal(new[] { "{a}", "{b}", "{place}" }, VillageChatterNeeds.TokensFor(""));
    }

    [Fact]
    public void ContentValidation_CatchesABrokenChatterTopic()
    {
        var bad = new VillageLifeDefinition
        {
            Chatter =
            {
                new VillageChatterTopic { Key = "x", When = "noon", Needs = "weather_rain", Weight = 0, Lines = { "a" } },
            },
        };
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetVillageLife(bad);
        var ex = Assert.Throws<ContentValidationException>(() => content.Validate());
        Assert.Contains("unknown when 'noon'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("needs something unknown: 'weather_rain'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("weight below 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2–4 non-empty line keys", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeedNews_ResolveInEnglishAndGerman_WithTheirPlaceholders()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var keys = DeedKinds.All.Select(k => "news.deed." + k)
            .Concat(new[] { "news.thing.giant", "news.thing.colossus", "news.thing.sandworm", "news.thing.leviathan", "news.thing.sky_giant" })
            .Concat(new[] { "dlg.innkeeper.prompt", "dlg.innkeeper.c_news", "dlg.innkeeper.r_news", "dlg.innkeeper.c_bye", "dlg.innkeeper.r_bye", "dlg.innkeeper.no_news" });
        foreach (var key in keys)
        {
            foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                Assert.All(PlaceholdersIn(text!), t => Assert.Contains(t, new[] { "{0}", "{1}", "{2}" }));
                if (key.StartsWith("news.deed.", StringComparison.Ordinal))
                {
                    Assert.Contains("{0}", text, StringComparison.Ordinal); // the hero is always named
                }
            }
        }

        // The giant's headline names the giant; the German "thing" is accusative (… hat bei X einen Koloss besiegt).
        Assert.Contains("{2}", en["news.deed.giant"], StringComparison.Ordinal);
        Assert.Contains("{2}", de["news.deed.giant"], StringComparison.Ordinal);
        Assert.StartsWith("einen ", de["news.thing.colossus"], StringComparison.Ordinal);
    }

    [Fact]
    public void TheInnkeeper_HasANewsDialogue_BeforeTheVillagersSmalltalk()
    {
        var dialogs = Content.Value.Dialogs.ToList();
        int inn = dialogs.FindIndex(d => d.Key == "innkeeper_news");
        int smalltalk = dialogs.FindIndex(d => d.Key == "settler_neighbours");
        Assert.True(inn >= 0, "innkeeper_news missing");
        Assert.True(inn < smalltalk, "the innkeeper's news must win over the villagers' smalltalk");
        Assert.Equal("innkeeper", dialogs[inn].Job);
        Assert.Contains(dialogs[inn].Nodes[0].Choices, c => c.Consequence == "news");
    }
}
