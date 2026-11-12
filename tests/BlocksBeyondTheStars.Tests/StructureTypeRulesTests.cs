// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3517: the placement rules of the stamped structure kinds as data (<c>data/structure_types.json</c> over
/// <see cref="StructureTypeRules.Defaults"/>). The shipped file must equal the C# defaults — the defaults are the constants the
/// stampers held, which the placement goldens pin — and spell out every field, so it documents every number. A broken entry
/// is reported and dropped, never a failed load.
/// </summary>
public sealed class StructureTypeRulesTests
{
    private static string ShippedFile => Path.Combine(TestPaths.DataDir(), "structure_types.json");

    /// <summary>The fields an entry may carry (the definition's properties in camelCase, the kind aside).</summary>
    private static readonly string[] Fields =
    {
        "enabled", "minGeneration", "chance", "density", "thresholds", "thresholdCaps", "minCount", "cap", "seatPolicy", "keepClear",
        "keepClearFrom", "conditions", "whitelist",
    };

    [Fact]
    public void TheShippedFile_EqualsTheCSharpDefaults()
    {
        var warnings = new List<string>();
        var shipped = StructureTypeRules.Load(ShippedFile, warnings.Add);

        Assert.Empty(warnings);
        foreach (string kind in StructureTypeRules.Kinds)
        {
            var file = shipped.Get(kind);
            var defaults = StructureTypeRules.Defaults.Get(kind);
            Assert.True(file.SameAs(defaults), $"'{kind}' in data/structure_types.json differs from StructureTypeRules' defaults.");
        }
    }

    [Fact]
    public void TheShippedFile_SpellsOutEveryKindAndEveryField()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ShippedFile));
        var kinds = doc.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')).ToList();

        Assert.Equal(StructureTypeRules.Kinds, kinds.Select(p => p.Name));
        foreach (var kind in kinds)
        {
            var names = kind.Value.EnumerateObject().Select(p => p.Name).ToList();
            Assert.True(Fields.All(names.Contains), $"'{kind.Name}' leaves out {string.Join(", ", Fields.Except(names))}.");
            Assert.True(names.All(Fields.Contains), $"'{kind.Name}' has unknown fields {string.Join(", ", names.Except(Fields))}.");
        }
    }

    [Fact]
    public void TheLoadedContent_CarriesTheShippedRules()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

        Assert.All(StructureTypeRules.Kinds, kind => Assert.True(content.StructureTypes.Get(kind).SameAs(StructureTypeRules.Defaults.Get(kind))));
    }

    [Fact]
    public void ABrokenEntry_IsReportedAndDropped_TheOthersStillLoad()
    {
        const string json = """
            {
              "_comment": "ignored",
              "ruin": { "cap": 1 },
              "monument": { "chance": 2.0 },
              "factory": { "thresholds": [0.9, 0.5] },
              "chest": { "capp": 3 },
              "vault": { "thresholds": [0.75] },
              "datacube": "many",
              "spslab": { "minCount": 7 },
              "lodge": { "seatPolicy": "tree", "conditions": ["underwater"] },
              "castle": { "cap": 2 },
              "settlement": { "keepClearFrom": { "dragon": 4 } }
            }
            """;
        var warnings = new List<string>();
        var rules = StructureTypeRules.Parse(json, warnings.Add);

        // The good override lands, the rest of its kind keeps the defaults.
        Assert.Equal(1, rules.Get(StructureTypeRules.Ruin).Cap);
        Assert.Equal(StructureTypeRules.Defaults.Get(StructureTypeRules.Ruin).Thresholds, rules.Get(StructureTypeRules.Ruin).Thresholds);

        // Every broken entry is reported once and its kind keeps its defaults.
        string[] broken =
        {
            StructureTypeRules.Monument, StructureTypeRules.Factory, StructureTypeRules.Chest, StructureTypeRules.Vault,
            StructureTypeRules.DataCube, StructureTypeRules.SpsLab, StructureTypeRules.Lodge, StructureTypeRules.Settlement,
        };
        foreach (string kind in broken)
        {
            Assert.True(rules.Get(kind).SameAs(StructureTypeRules.Defaults.Get(kind)), $"'{kind}' should keep its defaults.");
            Assert.Single(warnings, w => w.Contains($"'{kind}'", StringComparison.Ordinal));
        }

        Assert.Single(warnings, w => w.Contains("'castle'", StringComparison.Ordinal));
        Assert.Equal(broken.Length + 1, warnings.Count);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    public void AnUnreadableFile_KeepsEveryDefault(string json)
    {
        var warnings = new List<string>();
        var rules = StructureTypeRules.Parse(json, warnings.Add);

        Assert.Single(warnings);
        Assert.Same(StructureTypeRules.Defaults, rules);
    }

    [Fact]
    public void AMinGeneration_BelowTheCodesOwn_IsRaisedToIt_SoNoOlderWorldChanges()
    {
        var warnings = new List<string>();
        var rules = StructureTypeRules.Parse("""{ "landmark": { "minGeneration": 0 }, "lodge": { "minGeneration": 40 } }""", warnings.Add);

        Assert.Equal(StructureTypeRules.Defaults.Get(StructureTypeRules.Landmark).MinGeneration, rules.Get(StructureTypeRules.Landmark).MinGeneration);
        Assert.Equal(40, rules.Get(StructureTypeRules.Lodge).MinGeneration); // holding a kind back stays possible
        Assert.Contains(warnings, w => w.Contains("'landmark'", StringComparison.Ordinal) && w.Contains("generation", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileThatCannotBeRead_KeepsEveryDefault_WithAWarning()
    {
        string path = Path.Combine(Path.GetTempPath(), "bbts_rules_locked_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "lodge": { "cap": 1 } }""");
        try
        {
            // Held open without sharing: Windows refuses the read (an IOException) — the load warns and keeps the defaults.
            using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var warnings = new List<string>();
            var rules = StructureTypeRules.Load(path, warnings.Add);
            if (OperatingSystem.IsWindows())
            {
                Assert.Same(StructureTypeRules.Defaults, rules);
                Assert.Single(warnings);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NoFile_IsTheDefaults_WithoutAWord()
    {
        var warnings = new List<string>();
        var rules = StructureTypeRules.Load(Path.Combine(Path.GetTempPath(), "bbts_no_such_" + Guid.NewGuid().ToString("N") + ".json"), warnings.Add);

        Assert.Empty(warnings);
        Assert.Same(StructureTypeRules.Defaults, rules);
    }

    /// <summary>The cumulative count is the old <c>r &lt; a ? 0 : r &lt; b ? 1 : …</c> ladder for every roll — including a roll
    /// exactly on a threshold and the monuments' rolls above 1 (divided by a small world's size factor).</summary>
    [Fact]
    public void CountBelow_IsTheOldLadder()
    {
        var d = StructureTypeRules.Defaults;
        var rolls = Enumerable.Range(0, 2001).Select(i => i / 1000.0).Concat(new[] { 0.55, 0.85, 0.30, 0.75, 0.95, 0.70, 0.92, 0.45, 0.80, 0.6, 0.88 });
        foreach (double r in rolls)
        {
            Assert.Equal(r < 0.55 ? 0 : r < 0.85 ? 1 : 2, d.Get(StructureTypeRules.Ruin).CountBelow(r));
            Assert.Equal(r < 0.30 ? 0 : r < 0.75 ? 1 : r < 0.95 ? 2 : 3, d.Get(StructureTypeRules.Monument).CountBelow(r));
            Assert.Equal(r < 0.70 ? 0 : r < 0.92 ? 1 : 2, d.Get(StructureTypeRules.Factory).CountBelow(r));
            Assert.Equal(r < 0.45 ? 0 : r < 0.80 ? 1 : r < 0.95 ? 2 : 3, d.Get(StructureTypeRules.DataCube).CountBelow(r));
            Assert.Equal(r < 0.6 ? 0 : r < 0.88 ? 1 : 2, d.Get(StructureTypeRules.Chest).CountBelow(r));
        }
    }

    [Fact]
    public void MarginFrom_IsTheLargestOfOwnKindsAndTheOthers()
    {
        var lodge = StructureTypeRules.Defaults.Get(StructureTypeRules.Lodge);

        Assert.Equal(40, lodge.MarginFrom(StructureTypeRules.Settlement, 6));
        Assert.Equal(16, lodge.MarginFrom(StructureTypeRules.Pad, 6));
        Assert.Equal(6, lodge.MarginFrom(StructureTypeRules.Intercity, 6));
        Assert.Equal(10, lodge.MarginFrom(StructureTypeRules.Underground, 10)); // the other's own margin wins when it is larger
    }

    [Fact]
    public void Conditions_ReadThePlanetType()
    {
        var landmark = StructureTypeRules.Defaults.Get(StructureTypeRules.Landmark);
        var spsLab = StructureTypeRules.Defaults.Get(StructureTypeRules.SpsLab);
        var meadow = new PlanetType { Key = "meadow", Atmosphere = "breathable" };
        var titas = new PlanetType { Key = "titas", Atmosphere = "toxic", RestrictStructures = true, AllowedStructures = { "sps_labs" } };

        Assert.True(StructureTypeRules.ConditionsHold(landmark, meadow, asteroid: false));
        Assert.False(StructureTypeRules.ConditionsHold(landmark, meadow, asteroid: true));
        Assert.False(StructureTypeRules.ConditionsHold(landmark, new PlanetType { Atmosphere = "none" }, asteroid: false));
        Assert.True(StructureTypeRules.ConditionsHold(spsLab, titas, asteroid: false));
        Assert.False(StructureTypeRules.ConditionsHold(spsLab, meadow, asteroid: false)); // only where the type names it
        Assert.True(StructureTypeRules.AllowedOn(spsLab, titas, restricted: true));
        Assert.False(StructureTypeRules.AllowedOn(StructureTypeRules.Defaults.Get(StructureTypeRules.Settlement), titas, restricted: true));
    }
}
