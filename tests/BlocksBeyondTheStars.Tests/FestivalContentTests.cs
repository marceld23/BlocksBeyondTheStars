// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary><c>data/festivals.json</c> (#3002): the shape the validator cannot see — the English and German texts of every
/// festival, the kid-friendly framing, the glyphs the game font can draw, the theme set, and the yearly reminder to extend
/// the dated festivals.</summary>
public sealed class FestivalContentTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>Words that would make a festival text a statement of faith rather than a description of what people do.</summary>
    private static readonly Regex Rite = new(@"\b(god|gods|goddess|pray|prayer|prayers|worship|sacrifice|holy|sacred|sin|devil|blood)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    [Fact]
    public void TheFile_ListsManyFestivals_WithUniqueKeys_KnownThemes_AndDrawableGlyphs()
    {
        var festivals = Content.Value.Festivals;
        Assert.True(festivals.Count >= 50, $"only {festivals.Count} festivals");
        Assert.Equal(festivals.Count, festivals.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var f in festivals)
        {
            Assert.Contains(f.Theme, FestivalThemes.All);
            Assert.False(string.IsNullOrWhiteSpace(f.Glyph), $"{f.Key} has no glyph");
            // The game font draws the weather chip's symbols (☀ ❄ ★ …) but no colour emoji: one BMP symbol per festival.
            Assert.True(f.Glyph.Length == 1 && !char.IsSurrogate(f.Glyph[0]), $"{f.Key}: glyph '{f.Glyph}' is not a single plain symbol");
            Assert.NotNull(f.Effects);
        }

        foreach (string theme in FestivalThemes.All)
        {
            Assert.Contains(festivals, f => f.Theme == theme);
        }
    }

    [Fact]
    public void EveryFestival_HasNameExplanationAndGreeting_InEnglishAndGerman()
    {
        var en = Content.Value.CreateLocalizer(GameLocale.English);
        var de = Content.Value.CreateLocalizer(GameLocale.German);
        foreach (var f in Content.Value.Festivals)
        {
            foreach (string key in new[] { f.NameKey, f.ExplainKey, f.VegaKey })
            {
                Assert.True(en.Has(key), $"en.json lacks {key}");
                Assert.True(de.Has(key), $"de.json lacks {key}");
                Assert.False(string.IsNullOrWhiteSpace(en.Get(key)), $"en {key} is blank");
                Assert.False(string.IsNullOrWhiteSpace(de.Get(key)), $"de {key} is blank");
            }

            // The explanation says what people do and where — two sentences, not a one-liner.
            Assert.True(en.Get(f.ExplainKey).Count(c => c == '.') >= 2, $"{f.ExplainKey} (en) should be two sentences");
        }

        foreach (string theme in FestivalThemes.All)
        {
            Assert.True(en.Has("festival.theme." + theme) && de.Has("festival.theme." + theme), $"theme name missing for {theme}");
        }
    }

    [Fact]
    public void TheTexts_DescribeWhatPeopleDo_NotARite()
    {
        var en = Content.Value.CreateLocalizer(GameLocale.English);
        foreach (var f in Content.Value.Festivals)
        {
            foreach (string key in new[] { f.ExplainKey, f.VegaKey })
            {
                var hit = Rite.Match(en.Get(key));
                Assert.False(hit.Success, $"{key}: '{hit.Value}' — festival texts stay secular (epic #3000, decision 3)");
            }
        }
    }

    [Fact]
    public void DatedFestivals_CoverNextYear_OrTheFileNeedsExtending()
    {
        // The movable calendars (lunar, Hebrew, Islamic, Easter) carry explicit dates. This test turns red on 1 January of
        // the year datesThrough: then add the next year's ranges to data/festivals.json and raise datesThrough.
        int needed = DateTime.UtcNow.Year + 1;
        Assert.True(Content.Value.FestivalsDatesThrough >= needed,
            $"data/festivals.json covers dated festivals through {Content.Value.FestivalsDatesThrough}; extend it through {needed} (Lunar New Year, Holi, Diwali, Hanukkah, Ramadan, Chuseok …).");
        Assert.True(Content.Value.FestivalsDatesFrom <= DateTime.UtcNow.Year || Content.Value.FestivalsDatesFrom == needed);
    }

    [Fact]
    public void SharedDates_StayConsistent_BetweenSiblingFestivals()
    {
        // Qixi and Chilseok fall on the same lunar night; Chuseok's middle day is the Mid-Autumn Festival.
        var c = Content.Value;
        Assert.Equal(Ranges("qixi"), Ranges("chilseok"));
        foreach (var midAutumn in c.FindFestival("mid_autumn")!.Window.Ranges)
        {
            Assert.True(FestivalCalendar.TryParseDate(midAutumn[0], out var day));
            var chuseok = FestivalCalendar.SpansAround(c.FindFestival("chuseok")!, day.Year);
            Assert.Contains(chuseok, s => s.Contains(day));
        }

        string[] Ranges(string key) => c.FindFestival(key)!.Window.Ranges.Select(r => r[0] + ".." + r[1]).ToArray();
    }
}
