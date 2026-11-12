// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3512: every built structure as one <see cref="IStructureSource"/> — the registry (unique keys in a pinned order, labels
/// in English and German), the parameter line (<c>key=value;…</c>) and every source building with its defaults. The other
/// half (<c>StructureSourceTests.Identity.cs</c>) pins that each wrapper hands back exactly what its generator builds when
/// called the way the server calls it.
/// </summary>
public sealed partial class StructureSourceTests
{
    private const long SeedA = 0x5EED_1234_ABCDL;
    private const long SeedB = -987_654_321_987L;

    private static readonly Lazy<GameContent> LazyContent = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static GameContent Content => LazyContent.Value;

    /// <summary>The editor lists the sources in this order; a new source is appended to its group on purpose.</summary>
    private static readonly string[] PinnedKeys =
    {
        "settlement", "fallen_ruin", "city", "monument", "bandit_camp", "lodge", "pioneer_outpost", "sps_lab", "factory",
        "rail_station", "rail_ruin", "wreck", "text_tablet", "landmark",
        "ancient_ruin",
        "station", "station_kit", "derelict",
        "vault", "singing_shrine", "sealed_observatory", "mirun", "ornasa", "choir_tower", "guardian_core", "hunter_camp",
    };

    public static TheoryData<string> SourceKeys()
    {
        var data = new TheoryData<string>();
        foreach (var s in StructureSources.All)
        {
            data.Add(s.Key);
        }

        return data;
    }

    [Fact]
    public void Keys_AreUniqueAndInTheirPinnedOrder()
    {
        var keys = StructureSources.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PinnedKeys, keys);
        foreach (var s in StructureSources.All)
        {
            Assert.Same(s, StructureSources.Get(s.Key));
            Assert.Equal("ui.source." + s.Key, s.LabelKey);
        }

        Assert.Null(StructureSources.Get("no_such_source"));
        Assert.Null(StructureSources.Get(null));
    }

    [Fact]
    public void EverySourceAndParameter_HasItsLabelInEnglishAndGerman()
    {
        var missing = new List<string>();
        foreach (string language in new[] { "en", "de" })
        {
            var table = TestLocales.Load(language);
            foreach (var s in StructureSources.All)
            {
                var keys = new[] { s.LabelKey }.Concat(s.GetParams(Content).Select(p => p.LabelKey));
                missing.AddRange(keys.Where(k => !table.TryGetValue(k, out string? text) || string.IsNullOrWhiteSpace(text))
                    .Select(k => language + ": " + k));
            }
        }

        Assert.True(missing.Count == 0, "Missing locale keys: " + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void EveryParameter_DefaultsToAValueItAccepts()
    {
        foreach (var s in StructureSources.All)
        {
            var parameters = s.GetParams(Content);
            Assert.Equal(parameters.Count, parameters.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count());
            foreach (var p in parameters)
            {
                Assert.True(p.TryNormalize(p.Default, out string n), $"{s.Key}.{p.Key}: default '{p.Default}' is not accepted");
                Assert.Equal(p.Default, n);
                if (p.Type == StructureParamType.Choice)
                {
                    Assert.True(p.Options.Count > 0, $"{s.Key}.{p.Key} offers no option with the shipped content");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SourceKeys))]
    public void EverySource_BuildsWithItsDefaults_ForTwoSeeds(string key)
    {
        var source = StructureSources.Get(key)!;
        var a = source.Build(new StructureBuildRequest(Content, SeedA));
        var b = source.Build(new StructureBuildRequest(Content, SeedB));
        foreach (var built in new[] { a, b })
        {
            Assert.Equal(key, built.SourceKey);
            Assert.True(built.CountBlocks() > 0, $"{key} built an empty structure");
            Assert.Equal(built.Width * built.Height * built.Length, built.Blocks.Length);
            Assert.InRange(built.GroundRow, 0, built.Height - 1);
            Assert.All(built.Markers, m => Assert.True(built.InBounds(m.X, m.Y, m.Z), $"{key}: marker {m} lies outside the box"));
        }

        Assert.Equal(a.ContentHash(), source.Build(new StructureBuildRequest(Content, SeedA)).ContentHash());
    }

    [Fact]
    public void SeededSources_DifferBetweenSeeds()
    {
        foreach (string key in new[] { "settlement", "monument", "bandit_camp", "factory", "wreck", "station", "derelict" })
        {
            var source = StructureSources.Get(key)!;
            Assert.NotEqual(source.Build(new StructureBuildRequest(Content, SeedA)).ContentHash(),
                source.Build(new StructureBuildRequest(Content, SeedB)).ContentHash());
        }
    }

    [Fact]
    public void ParamLine_RoundTrips_AndKeepsOnlyWhatDiffersFromTheDefault()
    {
        var source = StructureSources.Get("settlement")!;
        Assert.True(StructureSources.TryParseParams(source, Content, "tier=city;ruined=yes", out var values, out string error), error);
        Assert.Equal("city", values["tier"]);
        Assert.Equal("true", values["ruined"]);
        Assert.Equal(string.Empty, values["kit"]); // not named: the default

        Assert.Equal("tier=city;ruined=true", StructureSources.FormatParams(source, Content, values));
        Assert.Equal(string.Empty, StructureSources.FormatParams(source, Content,
            new Dictionary<string, string> { ["tier"] = "village", ["ruined"] = "false" }));
    }

    [Fact]
    public void ParamLine_RejectsUnknownKeysAndValuesThatDoNotFit()
    {
        var factory = StructureSources.Get("factory")!;
        Assert.False(StructureSources.TryParseParams(factory, Content, "machines=99;colour=red", out var values, out string error));
        Assert.Contains("machines", error, StringComparison.Ordinal);
        Assert.Contains("colour", error, StringComparison.Ordinal);
        Assert.Equal("3", values["machines"]); // the default stands in

        var monument = StructureSources.Get("monument")!;
        Assert.False(StructureSources.TryParseParams(monument, Content, "archetype=pyramid", out _, out _));
        Assert.True(StructureSources.TryParseParams(monument, Content, "archetype=fossil_skull", out _, out _));
    }

    [Fact]
    public void InvalidRequestValues_BuildWithTheDefault()
    {
        var factory = StructureSources.Get("factory")!;
        var fallback = factory.Build(new StructureBuildRequest(Content, SeedA)
        {
            Params = new Dictionary<string, string> { ["machines"] = "lots", ["unknown"] = "x" },
        });
        Assert.Equal(factory.Build(new StructureBuildRequest(Content, SeedA)).ContentHash(), fallback.ContentHash());
    }

    [Fact]
    public void Intact_IsOfferedOnlyWhereAGeneratorHasIt_AndChangesTheBuild()
    {
        var intact = StructureSources.All.Where(s => s.SupportsIntact).Select(s => s.Key).ToList();
        Assert.Equal(new[] { "wreck", "landmark" }, intact);

        var wreck = StructureSources.Get("wreck")!;
        var asInWorld = wreck.Build(new StructureBuildRequest(Content, SeedA));
        var whole = wreck.Build(new StructureBuildRequest(Content, SeedA) { AsInWorld = false });
        Assert.True(whole.CountBlocks() > asInWorld.CountBlocks(), "the intact hull has no breaches");

        var station = StructureSources.Get("station")!;
        Assert.Equal(station.Build(new StructureBuildRequest(Content, SeedA)).ContentHash(),
            station.Build(new StructureBuildRequest(Content, SeedA) { AsInWorld = false }).ContentHash());
    }

    [Fact]
    public void ContentHash_SeesEveryPart()
    {
        var b = new BuiltStructure("t", 2, 2, 2);
        ulong empty = b.ContentHash();
        b.Set(1, 1, 1, 5);
        ulong block = b.ContentHash();
        b.Set(1, 1, 1, 5, tint: 0x112233);
        ulong tint = b.ContentHash();
        b.Set(1, 1, 1, 5, tint: 0x112233, shape: 7);
        ulong shape = b.ContentHash();
        b.Markers.Add(new BuiltMarker("npc", 0, 0, 0));
        ulong marker = b.ContentHash();
        b.Cavity.Add(0);
        ulong cavity = b.ContentHash();
        Assert.Equal(6, new[] { empty, block, tint, shape, marker, cavity }.Distinct().Count());
        Assert.NotEqual(new BuiltStructure("t", 2, 2, 2).ContentHash(), new BuiltStructure("t", 2, 2, 2, groundRow: 1).ContentHash());
    }
}
