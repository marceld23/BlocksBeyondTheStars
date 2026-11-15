// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3643: the G.D.S., the Sounders and the SPS as LEGACY civilizations — tags only. The shipped content loads all three
/// (a legacy file needs no scope and no sites), every civilization tag in the shipped content names a loaded civilization
/// (the loader warns about no tag that groups nothing), and spot checks pin the tags on G.D.S., Sounder and SPS content —
/// which still sits in its own pools: a tagged landmark stays a landmark, a tagged giant ship a giant ship.
/// </summary>
public sealed class LegacyCivilizationTagsTests
{
    private static readonly string[] LegacyKeys = { "gds", "sounders", "sps" };

    /// <summary>The start of the loader's warning about tags that name no loaded civilization.</summary>
    private const string UnknownTagsWarning = "Content names unknown civilization(s) ";

    private static readonly (GameContent Content, List<string> Warnings) Shipped = LoadShipped();

    private static (GameContent, List<string>) LoadShipped()
    {
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), warn: warnings.Add);
        return (content, warnings);
    }

    [Fact]
    public void TheShippedContent_LoadsTheThreeLegacyCivilizations_AsTagsOnly()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (string key in LegacyKeys)
        {
            var civ = Shipped.Content.GetCivilization(key);
            Assert.True(civ != null, $"civilization '{key}' did not load: {string.Join(" | ", Shipped.Warnings)}");
            Assert.False(civ!.IsFramework);
            Assert.Equal(CivilizationRules.PlacementLegacy, civ.Placement);
            Assert.Equal(CivilizationRules.StateLost, civ.State);
            Assert.Equal($"civ.{key}.name", civ.NameKey);
            Assert.True(en.ContainsKey(civ.NameKey) && de.ContainsKey(civ.NameKey), $"'{civ.NameKey}' needs en + de");
            Assert.Empty(civ.Sites);
            Assert.DoesNotContain(Shipped.Content.CivilizationTemplates, t => t.Civilization == key);
            Assert.DoesNotContain(Shipped.Warnings, w => w.Contains($"'{key}'", System.StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryCivilizationTag_InTheShippedContent_NamesALoadedCivilization()
    {
        var c = Shipped.Content;
        var templates = c.SettlementTemplates.Concat(c.StationTemplates).Concat(c.Landmarks).Concat(c.GiantShips).Concat(c.CivilizationTemplates);
        var tags = c.Blocks.Values.Select(b => (What: "block " + b.Key, Tag: b.Civilization))
            .Concat(c.Items.Values.Select(i => (What: "item " + i.Key, Tag: i.Civilization)))
            .Concat(c.LootTables.Values.Select(t => (What: "loot table " + t.Key, Tag: t.Civilization)))
            .Concat(c.StructureKits.Select(k => (What: "kit " + k.Key, Tag: k.Civilization)))
            .Concat(templates.Select(t => (What: "template " + t.Key, Tag: t.Civilization)))
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .ToList();

        var unknown = tags.Where(x => c.GetCivilization(x.Tag) is null).Select(x => $"{x.What} → '{x.Tag}'").ToList();
        Assert.True(unknown.Count == 0, "tags naming no loaded civilization: " + string.Join(", ", unknown));
        foreach (string key in LegacyKeys)
        {
            Assert.Contains(tags, x => x.Tag == key);
        }

        // The loader's own check (blocks, items, loot tables) reports through the warn callback. Only the three legacy
        // tags are asserted here: a framework civilization whose blocks live on another branch is dropped with its own warning.
        var namedUnknown = Shipped.Warnings
            .Where(w => w.StartsWith(UnknownTagsWarning, System.StringComparison.Ordinal))
            .SelectMany(w => w.Substring(UnknownTagsWarning.Length).Split(" — ")[0].Split(", "))
            .ToList();
        Assert.Empty(namedUnknown.Intersect(LegacyKeys));
    }

    [Fact]
    public void SpotChecks_GdsSoundersAndSpsContent_IsTagged_AndStaysInItsOwnPools()
    {
        var c = Shipped.Content;

        // The G.D.S.: their blocks, the Ressaw, their logs, the buried ruin's kit and modules.
        Assert.Equal("gds", c.CivilizationOfBlock("gds_guardian_head")?.Key);
        Assert.Equal("gds", c.CivilizationOfBlock("ressaw_block")?.Key);
        Assert.Equal("gds", c.Items["gds_log"].Civilization);
        Assert.Equal("gds", c.StructureKits.Single(k => k.Key == "ruin_gds_1").Civilization);
        Assert.Equal("gds", c.SettlementTemplates.Single(t => t.Key == "gds_ruin_pumphall_1").Civilization);

        // The Sounders: the singing blooms, the Song Spindle, the Castle of Soaniri and its chests.
        Assert.Equal("sounders", c.CivilizationOfBlock("bell_bloom")?.Key);
        Assert.Equal("sounders", c.CivilizationOfBlock("bell_bloom_open")?.Key);
        Assert.Equal("sounders", c.Items["song_spindle"].Civilization);
        Assert.Equal("sounders", c.Landmarks.Single(t => t.Key == "soaniri_castle").Civilization);
        Assert.Equal("sounders", c.LootTable("soaniri_castle")?.Civilization);

        // The SPS: the giant ships of its survey fleet Meridian and their loot; the Crystal Squid was never theirs.
        Assert.Equal("sps", c.GiantShips.Single(t => t.Key == "carrier").Civilization);
        Assert.Equal("sps", c.LootTable("giant_ark")?.Civilization);
        Assert.Equal("sps", c.Items["access_code"].Civilization);
        Assert.Empty(c.GiantShips.Single(t => t.Key == "squid").Civilization);
    }
}
