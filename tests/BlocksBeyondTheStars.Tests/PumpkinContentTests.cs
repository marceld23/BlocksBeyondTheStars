// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pumpkin (#3005) and the festival challenges' data (#3006, #3007): the species, its palette, the blocks,
/// items and recipes, the harvest festivals' spawns, the eight challenge packs and their presents.</summary>
public sealed class PumpkinContentTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    [Fact]
    public void ThePumpkin_IsASolidWildSpeciesOfGeneration32_WithAPalette_BeforeTheCrops()
    {
        var all = FloraCatalog.All;
        var pumpkin = FloraCatalog.Find(FloraCatalog.PumpkinKey);
        Assert.NotNull(pumpkin);
        Assert.True(pumpkin!.Solid);
        Assert.False(pumpkin.Cultivated); // wild — rolled into rosters like every species, toxic on a few
        Assert.Equal(WorldDescription.PumpkinGeneration, pumpkin.MinGeneration);
        Assert.Equal(32, WorldDescription.PumpkinGeneration);
        Assert.Equal(WorldDescription.PumpkinGeneration, WorldDescription.CurrentTerrainGeneration);
        Assert.NotNull(FloraCatalog.PaletteOf(FloraCatalog.PumpkinKey));
        Assert.Null(FloraCatalog.PaletteOf("flora_berrybush"));
        Assert.Contains("hydro_tray", pumpkin.LateHosts); // sowable in a tray like the crops — a late host, so the roster coverage ignores it
        // The crops stay at the tail: the pumpkin sits before the first cultivated species.
        int pumpkinIndex = all.ToList().FindIndex(s => s.Key == FloraCatalog.PumpkinKey);
        int firstCrop = all.ToList().FindIndex(s => s.Cultivated);
        Assert.True(pumpkinIndex >= 0 && firstCrop > pumpkinIndex);
        Assert.Contains(FloraCatalog.PumpkinKey, FloraCatalog.SolidKeys());
    }

    [Fact]
    public void ThePalette_GivesEveryCellOneOfItsColours_Deterministically_TheFirstOnesMostOften()
    {
        var palette = FloraPalettes.Pumpkin;
        var counts = new int[palette.Count];
        for (int i = 0; i < 2000; i++)
        {
            var c = FloraTints.PaletteAt(i * 7, 64, i * 13, palette);
            var again = FloraTints.PaletteAt(i * 7, 64, i * 13, palette);
            Assert.Equal(c, again);
            // One of the palette's colours, brightness-jittered by at most 8 % (the same factor on every channel).
            int index = Enumerable.Range(0, palette.Count).First(k =>
            {
                float j = c.G / palette[k].G;
                return j >= 0.91f && j <= 1.09f
                       && Math.Abs(c.R - Math.Min(1f, palette[k].R * j)) < 0.01f
                       && Math.Abs(c.B - Math.Min(1f, palette[k].B * j)) < 0.01f;
            });
            counts[index]++;
        }

        Assert.True(counts[0] > counts[palette.Count - 1] * 3, $"orange {counts[0]} should be far more common than blue-grey {counts[^1]}");
        Assert.All(counts, n => Assert.True(n > 0));
        Assert.Equal((1f, 1f, 1f), FloraTints.PaletteAt(1, 2, 3, Array.Empty<(float, float, float)>()));
    }

    [Fact]
    public void Blocks_Items_Recipes_AndTexts_AreAllThere()
    {
        var c = Content.Value;
        var block = c.GetBlock("flora_pumpkin");
        Assert.NotNull(block);
        Assert.Contains(block!.Drops, d => d.Item == "pumpkin");
        Assert.Contains(block.Drops, d => d.Item == "pumpkin_seed");
        var lantern = c.GetBlock("pumpkin_lantern");
        Assert.NotNull(lantern);
        Assert.True(lantern!.Tintable);
        Assert.True(lantern.Emission > 0f);
        Assert.Equal("flora_pumpkin", c.GetItem("pumpkin_seed")?.PlacesBlock);
        Assert.Equal("pumpkin_lantern", c.GetItem("pumpkin_lantern")?.PlacesBlock);
        Assert.True(c.GetItem("pumpkin")?.ConsumeHunger > 0);
        Assert.True(c.GetItem("toxic_pumpkin")?.ConsumeHealth < 0);
        foreach (string recipe in new[] { "seed_pumpkin", "pumpkin_lantern", "wash_pumpkin" })
        {
            Assert.NotNull(c.GetRecipe(recipe));
        }

        var en = c.CreateLocalizer(GameLocale.English);
        var de = c.CreateLocalizer(GameLocale.German);
        foreach (string key in new[] { "block.flora_pumpkin.name", "block.pumpkin_lantern.name", "item.pumpkin.name", "item.pumpkin.desc",
                     "item.toxic_pumpkin.name", "item.pumpkin_seed.name", "item.pumpkin_lantern.desc", "achv.category.festival", "achv.pumpkin_500.name" })
        {
            Assert.True(en.Has(key) && de.Has(key), key);
        }
    }

    [Fact]
    public void TheHarvestFestivals_SpawnPumpkins_OnRealHosts_WithinBudget()
    {
        var c = Content.Value;
        var spawning = c.Festivals.Where(f => f.Spawn is not null).ToList();
        Assert.Contains(spawning, f => f.Key == "halloween");
        Assert.Contains(spawning, f => f.Key == "erntedank");
        foreach (var f in spawning)
        {
            Assert.Equal(FestivalThemes.Harvest, f.Theme);
            Assert.NotNull(c.GetBlock(f.Spawn!.Block));
            Assert.All(f.Spawn.Hosts, h => Assert.NotNull(c.GetBlock(h)));
            Assert.InRange(f.Spawn.PerPass, 1, 8);
            Assert.InRange(f.Spawn.Cap, 100, 2000); // the weather snow's ceiling is 2000
            Assert.InRange(f.Spawn.Radius, 8, 32);
        }
    }

    [Fact]
    public void TheEightChallengePacks_StartOnTheirTheme_AreNotSkippable_AndGraduateIntoAPresent()
    {
        var c = Content.Value;
        foreach (string theme in FestivalThemes.All)
        {
            string id = "festival_" + theme;
            Assert.True(c.Stories.TryGetValue(id, out var pack), $"pack {id} missing");
            Assert.Equal("side", pack!.Kind);
            Assert.Equal("player", pack.Scope);
            Assert.True(pack.DefaultEnabled);
            Assert.Contains("world:" + FestivalThemes.Predicate(theme), pack.Start.After);
            Assert.Equal("festival:" + theme, pack.Graduation.Counter);
            var chapter = Assert.Single(pack.Chapters);
            Assert.False(chapter.Skippable ?? true);
            Assert.All(chapter.Steps, s => Assert.False(s.Skippable ?? true));
            Assert.Contains(chapter.Steps, s => s.Objective is { Required: > 1 });
            // The present: an achievement watching the graduation counter, paying real decoration items.
            var achievement = Assert.Single(c.Achievements, a => a.Counter == "festival:" + theme);
            Assert.Equal("festival", achievement.Category);
            Assert.NotEmpty(achievement.Rewards);
            Assert.All(achievement.Rewards, r => Assert.NotNull(c.GetItem(r.Item)));
        }

        Assert.Equal(3, c.Achievements.Count(a => a.Counter == "mine:flora_pumpkin"));
    }
}
