// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generator entries as the registry keeps them safe (#3518): an entry builds from its seed and parameters alone (a settlement
/// or city entry is purely procedural, a station kit is no entry), a broken entry never throws out of the build, a metropolis
/// entry is the city source only, and no template takes a key the placement records reserve.
/// </summary>
public sealed class TemplatePipelineEntryTests
{
    private const int Gen38 = WorldDescription.StructurePipelineGeneration;

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static StructureTemplate Entry(string source, string tier = "village", string sourceParams = "")
        => new() { Key = "test_entry", Tier = tier, Source = source, SourceParams = sourceParams };

    [Theory]
    [InlineData("settlement", "village", "tier=town")]
    [InlineData("city", "metropolis", "")]
    public void ASettlementOrCityEntry_BuildsFromItsSeedAndParametersAlone_NotFromTheLiveModulePool(string source, string tier, string sourceParams)
    {
        var entry = Entry(source, tier, sourceParams);
        var withModules = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var withoutModules = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        withoutModules.SetStructureTemplates(withoutModules.StationTemplates, withoutModules.SettlementTemplates.Where(t => !t.IsModule).ToList());
        Assert.NotEmpty(withModules.SettlementTemplates.Where(t => t.IsModule));

        for (long seed = 1; seed <= 6; seed++)
        {
            var a = SourceTemplates.Build(entry, withModules, seed, "grass", "meadowlands", Gen38, out string failA);
            var b = SourceTemplates.Build(entry, withoutModules, seed, "grass", "meadowlands", Gen38, out string failB);
            Assert.True(a is not null, failA);
            Assert.True(b is not null, failB);
            Assert.Equal(a!.ContentHash(), b!.ContentHash()); // a new or removed module never changes what a world replays
        }
    }

    [Fact]
    public void AnEntryNamingAKit_OrTheStationKitSource_IsRefused()
    {
        string settlementKit = Content.KitsFor(StructureKit.KindSettlement, null, null, null).First().Key;
        string? problem = SourceTemplates.Problem(Entry("settlement", "village", "kit=" + settlementKit), StructureKit.KindSettlement, Content);
        Assert.Contains("procedurally", problem, StringComparison.Ordinal);
        Assert.Null(SourceTemplates.Problem(Entry("settlement", "village", "tier=town"), StructureKit.KindSettlement, Content));

        var stationKit = Entry("station_kit", "medium");
        Assert.Contains("live kit pool", SourceTemplates.Problem(stationKit, StructureKit.KindStation, Content), StringComparison.Ordinal);
        Assert.False(SourceTemplates.CanBeEntry("station_kit"));
        Assert.Null(SourceTemplates.Build(stationKit, Content, 1, "stone", null, Gen38, out string failure));
        Assert.NotEmpty(failure);
    }

    [Fact]
    public void ABrokenEntry_NeverThrowsOutOfTheBuild_ItIsReportedAsUnbuildable()
    {
        // Content without a single block: every source that needs one fails inside its generator, in whatever way it fails.
        var empty = new GameContent(Array.Empty<BlockDefinition>(), Array.Empty<ItemDefinition>(), Array.Empty<RecipeDefinition>(),
            Array.Empty<BlueprintDefinition>(), Array.Empty<ShipModuleDefinition>(), new Dictionary<GameLocale, Dictionary<string, string>>());
        int failed = 0;
        foreach (var source in StructureSources.All.Where(s => SourceTemplates.CanBeEntry(s.Key)))
        {
            BuiltStructure? built = null;
            string failure = string.Empty;
            var ex = Record.Exception(() => built = SourceTemplates.Build(Entry(source.Key), empty, 7, "stone", null, Gen38, out failure));
            Assert.True(ex is null, $"{source.Key}: {ex}");
            if (built is null)
            {
                Assert.NotEmpty(failure);
                failed++;
            }
        }

        Assert.True(failed > 0, "an empty content should break at least one source");
    }

    [Theory]
    [InlineData("bandit_camp", false)]
    [InlineData("settlement", false)]
    [InlineData("city", true)]
    public void AMetropolisEntry_IsTheCitySourceOnly(string source, bool accepted)
    {
        string? problem = SourceTemplates.Problem(Entry(source, CityGenerator.Tier), StructureKit.KindSettlement, Content);
        Assert.Equal(accepted, problem is null);
        if (!accepted)
        {
            Assert.Contains("landing pads", problem, StringComparison.Ordinal);
        }

        // Other tiers keep taking every surface source.
        Assert.Null(SourceTemplates.Problem(Entry("bandit_camp", "village"), StructureKit.KindSettlement, Content));
    }

    [Theory]
    [InlineData("procedural")]
    [InlineData("Procedural")]
    [InlineData("kit:my_kit")]
    [InlineData("source:my_entry")]
    public void NoTemplate_TakesAKeyThePlacementRecordsReserve(string key)
    {
        Assert.True(StructureCatalog.IsReservedTemplateKey(key));
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var village = content.SettlementTemplates.First(t => !t.IsModule && !t.IsSource);
        var copy = TemplatePipelineTests.Clone(village);
        copy.Key = key;
        var station = TemplatePipelineTests.Clone(content.StationTemplates.First());
        station.Key = key;
        var warnings = new List<string>();

        content.SetStructureTemplates(content.StationTemplates.Append(station).ToList(), content.SettlementTemplates.Append(copy).ToList(), warnings.Add);

        Assert.DoesNotContain(content.SettlementTemplates, t => t.Key == key);
        Assert.DoesNotContain(content.StationTemplates, t => t.Key == key);
        Assert.Equal(2, warnings.Count(w => w.Contains(key, StringComparison.Ordinal) && w.Contains("reserved", StringComparison.Ordinal)));
        Assert.False(StructureCatalog.IsReservedTemplateKey("procedural_village"));
    }
}
