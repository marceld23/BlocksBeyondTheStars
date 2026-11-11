// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The data side of the landmarks (#3484, #3485, #3488): the landmark pool with its filters and its validation, the
/// compact template body, the marker settings line and the loot tables.
/// </summary>
public sealed class LandmarkTemplateTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static List<(int X, int Y, int Z, bool Cavity, string Id, bool Keep)> Plain(StructureTemplate t)
    {
        var list = new List<(int, int, int, bool, string, bool)>();
        TemplateBody.ForEachPlain(t, p => list.Add((p.X, p.Y, p.Z, p.Cavity, p.Id, p.Keep)));
        list.Sort();
        return list;
    }

    [Fact]
    public void CompactBody_RoundTrips_CellForCell()
    {
        var listed = LandmarkShapes.Mine();
        listed.Cells.Add(LandmarkShapes.Block(1, 30 - 1, 1, "stone", keep: true));
        var before = Plain(listed);
        int markersBefore = listed.Cells.Count(c => c.Kind == "marker");

        var compact = LandmarkShapes.Compacted(listed);
        Assert.True(TemplateBody.IsCompact(compact));
        Assert.All(compact.Cells, c => Assert.Equal("marker", c.Kind)); // only the cells with settings stay listed
        Assert.Equal(markersBefore, compact.Cells.Count);
        Assert.Equal(before, Plain(compact));
        Assert.Equal(before.Count + markersBefore, TemplateBody.CellCount(compact));

        TemplateBody.Expand(compact);
        Assert.False(TemplateBody.IsCompact(compact));
        Assert.Equal(before, Plain(compact));
        Assert.Equal(markersBefore, compact.Cells.Count(c => c.Kind == "marker"));
    }

    [Fact]
    public void CompactBody_SurvivesJson_AndIsIdempotent()
    {
        var t = LandmarkShapes.Compacted(LandmarkShapes.Tower());
        var before = Plain(t);
        string json = JsonSerializer.Serialize(t, ContentLoader.JsonOptions);
        var back = JsonSerializer.Deserialize<StructureTemplate>(json, ContentLoader.JsonOptions)!;
        Assert.Equal(before, Plain(back));
        Assert.Equal(t.Cells.Count, back.Cells.Count);
        Assert.Equal("items=iron_plate:2-4,gold_ingot:1", back.Cells.First(c => c.Id == LandmarkRules.ChestMarker).Config);

        var rows = new List<string>(back.Rows);
        TemplateBody.Compact(back);
        Assert.Equal(rows, back.Rows);
    }

    [Fact]
    public void CompactBody_KeepsAGiantSmall()
    {
        // A hollow 256 x 128 x 256 hall with a floor every 16 rows: about 600 000 cells.
        var t = new StructureTemplate { Key = "giant", Landmark = true, Width = 256, Height = 128, Length = 256 };
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                for (int z = 0; z < 256; z++)
                {
                    if (y % 16 == 0 || x == 0 || z == 0 || x == 255 || z == 255)
                    {
                        t.Cells.Add(LandmarkShapes.Block(x, y, z, "stone"));
                    }
                }
            }
        }

        int cells = t.Cells.Count;
        TemplateBody.Compact(t);
        string json = JsonSerializer.Serialize(t, ContentLoader.JsonOptions);
        Assert.True(cells > 500_000, $"the test structure has {cells} cells");
        Assert.True(json.Length < 1_000_000, $"compact body is {json.Length} bytes for {cells} cells");
        Assert.Equal(cells, TemplateBody.CellCount(t));
    }

    [Fact]
    public void CompactBody_ACellWithSettings_WinsOverAPlainCellAtItsPlace()
    {
        var t = new StructureTemplate { Key = "port", Width = 3, Height = 2, Length = 3 };
        t.Cells.Add(LandmarkShapes.Block(1, 0, 1, "stone"));
        t.Cells.Add(new TemplateCell { X = 1, Y = 0, Z = 1, Kind = "block", Id = "iron_wall", Port = "door" });
        TemplateBody.Compact(t);
        Assert.Empty(t.Rows);
        Assert.Single(t.Cells);
        Assert.Equal("door", t.Cells[0].Port);
    }

    [Fact]
    public void MalformedRows_AreSkipped_NotFatal()
    {
        var t = new StructureTemplate
        {
            Palette = new List<TemplatePaletteEntry> { new() { Id = "stone" } },
            Rows = new List<string> { "garbage", "1,2:3.2.0 x", "4,5:0.1.7", "0,0:5.3.0" },
        };
        var cells = Plain(t);
        Assert.Equal(5, cells.Count); // 2 from the second row (its bad run ends it), 3 from the last; palette 7 does not exist
        Assert.Contains((3, 1, 2, false, "stone", false), cells);
        Assert.Contains((7, 0, 0, false, "stone", false), cells);
    }

    [Fact]
    public void MarkerSettings_ParseFormatAndStripSeparators()
    {
        var s = MarkerSettings.Parse("table=ruin_common;chance=0.5;block=wood_crate;junk;=x");
        Assert.Equal("ruin_common", s.Get("table"));
        Assert.Equal(0.5, s.GetDouble("chance", 1.0));
        Assert.Equal(7, s.GetInt("missing", 7));
        Assert.False(s.Has("junk"));
        Assert.Equal("table=ruin_common;chance=0.5;block=wood_crate", s.ToString());

        s.Set("label", "a;b|c\nd").Set("table", string.Empty).Set("radius", 12.5);
        Assert.Equal("chance=0.5;block=wood_crate;label=abcd;radius=12.5", s.ToString());
        Assert.True(MarkerSettings.Parse(null).IsEmpty);

        var items = MarkerSettings.ParseItems("iron_plate:2-4, gold_ingot:1,cable,bad:x,:3");
        Assert.Equal(new[] { "iron_plate", "gold_ingot", "cable", "bad" }, items.Select(i => i.Item));
        Assert.Equal((2, 4), (items[0].Min, items[0].Max));
        Assert.Equal((1, 1), (items[2].Min, items[2].Max));
        Assert.Equal("iron_plate:2-4,gold_ingot:1,cable:1,bad:1", MarkerSettings.FormatItems(items));
    }

    [Fact]
    public void LootTable_RollsFromItsSeedOnly()
    {
        var table = new LootTableDefinition
        {
            Key = "t",
            Rolls =
            {
                new LootRoll
                {
                    Picks = 3,
                    Entries = { new LootEntry { Item = "iron_plate", Min = 1, Max = 4, Weight = 3 }, new LootEntry { Item = "cable", Min = 2, Max = 2 } },
                },
                new LootRoll { Chance = 0.5, Entries = { new LootEntry { Item = "gold_ingot" } } },
                new LootRoll { Entries = { new LootEntry { Item = "no_such_item" } } },
            },
        };

        bool Exists(string item) => Content.GetItem(item) is not null;
        var a = table.Roll(42, Exists);
        var b = table.Roll(42, Exists);
        Assert.Equal(a.Select(s => (s.Item, s.Count)), b.Select(s => (s.Item, s.Count)));
        Assert.DoesNotContain(a, s => s.Item == "no_such_item");
        Assert.All(a.Where(s => s.Item == "iron_plate"), s => Assert.InRange(s.Count, 1, 4));
        Assert.All(a.Where(s => s.Item == "cable"), s => Assert.Equal(2, s.Count));

        int gold = 0, distinct = 0;
        var seen = new HashSet<string>();
        for (ulong seed = 0; seed < 400; seed++)
        {
            var roll = table.Roll(seed, Exists);
            Assert.InRange(roll.Count(s => s.Item != "gold_ingot"), 3, 3);
            gold += roll.Count(s => s.Item == "gold_ingot");
            distinct += seen.Add(string.Join(",", roll.Select(s => s.Item + s.Count))) ? 1 : 0;
        }

        Assert.InRange(gold, 150, 250); // the 50 % roll
        Assert.True(distinct > 40, $"only {distinct} different contents over 400 seeds");
    }

    [Fact]
    public void LandmarkPool_FiltersByPackPlanetAndCondition_AndKeepsOutOfEveryOtherPool()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var everywhere = LandmarkShapes.Tower("lm_everywhere");
        var soaniri = LandmarkShapes.Tower("lm_soaniri");
        soaniri.PlanetTypes.Add("sounder_world");
        var burger = LandmarkShapes.Tower("lm_burger", ruin: false);
        burger.Requires = new List<string> { LandmarkRules.RequiresInhabited };
        burger.Pack = "extras";
        var pinned = LandmarkShapes.Tower("lm_pinned");
        pinned.PinOnly = true;

        var warnings = new List<string>();
        content.SetLandmarks(new[] { everywhere, soaniri, burger, pinned }, warnings.Add);
        Assert.Empty(warnings);
        Assert.Contains("extras", content.StructurePacks);

        string[] Keys(IReadOnlyList<StructureTemplate> l) => l.Select(t => t.Key).ToArray();
        Assert.Equal(new[] { "lm_everywhere", "lm_burger" }, Keys(content.LandmarksFor(null, "meadowlands", atmosphere: true, inhabited: true)));
        Assert.Equal(new[] { "lm_everywhere" }, Keys(content.LandmarksFor(null, "meadowlands", atmosphere: true, inhabited: false)));
        Assert.Equal(new[] { "lm_burger" }, Keys(content.LandmarksFor(null, "meadowlands", atmosphere: false, inhabited: true)));
        Assert.Equal(new[] { "lm_everywhere" }, Keys(content.LandmarksFor(new[] { "default" }, "meadowlands", true, true)));
        Assert.Equal(new[] { "lm_everywhere", "lm_soaniri", "lm_burger" }, Keys(content.LandmarksFor(null, "sounder_world", true, true)));
        Assert.Equal(new[] { "lm_soaniri" }, Keys(content.LandmarksFor(null, "sounder_world", true, true, namedTypeOnly: true)));
        Assert.Same(pinned, content.LandmarkByKey("lm_pinned")); // a pinned replay still finds it

        // No other pool ever offers a landmark.
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            Assert.DoesNotContain(content.CompleteTemplatesFor(StructureKit.KindSettlement, tier, null, null), t => t.Landmark);
        }

        Assert.DoesNotContain(content.SettlementModulesFor(null, null), t => t.Landmark);
        Assert.DoesNotContain(content.UndergroundTemplatesFor(null, null), t => t.Landmark);
        Assert.Null(content.SettlementTemplateByKey("lm_everywhere"));
    }

    [Fact]
    public void LandmarkPool_DropsABrokenLandmark_WithAWarning()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var good = LandmarkShapes.Tower("good");
        var unknownBlock = LandmarkShapes.Tower("unknown_block");
        unknownBlock.Cells.Add(LandmarkShapes.Block(2, 2, 2, "no_such_block"));
        var badWords = LandmarkShapes.Tower("bad_words");
        badWords.Ruin = "sometimes";
        badWords.RuinProfile = "jelly";
        badWords.Requires.Add("moonlight");
        var badRow = LandmarkShapes.Tower("bad_row");
        badRow.GroundRow = 400;
        var tooBig = LandmarkShapes.Tower("too_big");
        tooBig.Width = LandmarkRules.MaxSize + 1;
        var module = LandmarkShapes.Tower("module");
        module.Kit = "station_small_1";
        var twin = LandmarkShapes.Tower("good");

        var warnings = new List<string>();
        content.SetLandmarks(new[] { good, unknownBlock, badWords, badRow, tooBig, module, twin, new StructureTemplate() }, warnings.Add);
        Assert.Equal(new[] { "good" }, content.Landmarks.Select(t => t.Key));
        Assert.Equal(7, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("no_such_block"));
        Assert.Contains(warnings, w => w.Contains("sometimes") && w.Contains("jelly") && w.Contains("moonlight"));
        Assert.Contains(warnings, w => w.Contains("ground row"));
        Assert.Contains(warnings, w => w.Contains("larger than"));
        Assert.Contains(warnings, w => w.Contains("kit module"));
        Assert.Contains(warnings, w => w.Contains("already loaded"));
        Assert.Contains(warnings, w => w.Contains("without a key"));
    }

    [Fact]
    public void SettlementPool_LeavesOutALandmarkThatStrayedIntoIt()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var stray = LandmarkShapes.Tower("stray");
        stray.Tier = "village";
        var warnings = new List<string>();
        content.SetStructureTemplates(content.StationTemplates, content.SettlementTemplates.Append(stray).ToList(), warnings.Add);
        Assert.Contains(warnings, w => w.Contains("stray"));
        Assert.DoesNotContain(content.SettlementTemplates, t => t.Key == "stray");
    }

    [Fact]
    public void ShippedLootTables_NameRealItems_AndEveryShippedLandmarkIsValid()
    {
        // ContentLoader.LoadFromDirectory validates the tables (unknown item = load error) and drops a broken landmark
        // with a warning — so a clean load with no landmark warning is the assertion.
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), warn: warnings.Add);
        Assert.DoesNotContain(warnings, w => w.Contains("landmark", System.StringComparison.OrdinalIgnoreCase));
        Assert.All(content.LootTables.Values, t => Assert.NotEmpty(t.Rolls));
        Assert.All(content.Landmarks, t => Assert.Empty(LandmarkRules.Problems(t)));
    }

    [Fact]
    public void TheShippedSamples_AreCompact_PinOnly_AndBuild()
    {
        // tools/gen_landmarks.py: three small samples for play tests — never rolled, stamped only by /landmark <key>.
        foreach (string key in new[] { "sample_tower", "sample_mine", "sample_store" })
        {
            var t = Content.LandmarkByKey(key);
            Assert.NotNull(t);
            Assert.True(t!.PinOnly, key + " would be rolled for a world");
            Assert.True(TemplateBody.IsCompact(t));
            Assert.All(t.Cells, c => Assert.Equal("marker", c.Kind));
            Assert.StartsWith("landmark." + key, t.NameKey);
            var layout = BlocksBeyondTheStars.WorldGeneration.LandmarkGenerator.Build(t, 1, 0, Content, "grass");
            Assert.Equal(LandmarkRules.IsRuin(t), layout.Ruined);
            Assert.Equal(t.GroundRow, layout.GroundRow);
        }

        Assert.Empty(Content.LandmarksFor(null, "meadowlands", atmosphere: true, inhabited: true)); // nothing is rolled yet
        Assert.True(Content.LandmarkByKey("sample_mine")!.GroundRow > 0);
        Assert.True(Content.LandmarkByKey("sample_store")!.Protected);
        foreach (string locale in new[] { "en", "de" })
        {
            var table = TestLocales.Load(locale);
            Assert.All(Content.Landmarks, t => Assert.True(table.ContainsKey(t.NameKey), $"{locale}: {t.NameKey} is missing"));
        }
    }
}
