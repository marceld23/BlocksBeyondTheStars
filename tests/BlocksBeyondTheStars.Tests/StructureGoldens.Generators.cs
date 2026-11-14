// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generator goldens (#3511, before the structure-pipeline refactor of #3510): a stable fingerprint — every cell's block
/// (by key), tint, glow and shape, every marker (type, cell, door side, data) and whatever else the generator decides
/// (modules, caches, the repair mask, the composition it recorded) — of each structure generator in
/// <c>WorldGeneration</c>, over a few seeds and every parameter the server passes, built from the shipped content.
/// <para>The refactor wraps these generators (a common source interface, editor previews); a wrapper must build byte for
/// byte what the generator built, because saved worlds replay many structures from their records on every load. If a case
/// here moves, a structure that stands in somebody's world would move with it. A deliberate change of world output belongs
/// behind a terrain-generation gate (≥ 38) — the cases here are what generation ≤ 37 builds, and must never be re-pinned
/// for it. The pins live in <c>StructureGoldens.GeneratorsPins.cs</c>; a failure prints the whole computed table.</para>
/// <para>Two generators take a <c>Math.Cos/Sin</c> round (the city's plaza lamps, the monuments' stone circles): their
/// integer cells could in theory differ on another libm; <see cref="StructureGoldens.AssertPinned"/> takes Linux-only
/// pins for such a case should CI ever disagree (none needed so far, as with <c>WorldGenerationGoldenTests</c>).</para>
/// </summary>
public sealed partial class StructureGeneratorGoldenTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A small, a dated and a wide seed — the wide one runs through every generator's 64-bit fold.</summary>
    private static readonly long[] Seeds = { 1, 20261112, 0x5DEECE66DL };

    private static readonly string[] Surfaces = { "grass", "sand", "ice" };

    private static readonly string[] Tiers = { "hamlet", "village", "town", "city" };

    private static string N(long seed) => seed.ToString(CultureInfo.InvariantCulture);

    private static List<StructureTemplate> ModulePool() => Content.SettlementTemplates.Where(t => t.IsModule).ToList();

    /// <summary>Every shipped kit of a kind and tier, pin-only ones included (an old record replays through them).</summary>
    private static IEnumerable<StructureKit> KitsOf(string kind, string tier)
        => Content.StructureKits.Where(k => k.KindOrDefault == kind && k.Tier == tier && k.MinTerrainGeneration == 0) // #3583: no planned kit
            .OrderBy(k => k.Key, StringComparer.Ordinal);

    private static ulong Hash(SettlementStructure s) => StructureGoldens.Settlement(Content, s);

    /// <summary>A structure plus the composition its composer recorded (#1872).</summary>
    private static ulong Hash(SettlementStructure s, IReadOnlyList<string> composition)
    {
        var h = StructureGoldens.Settlement(new StructureGoldenHash(Content), s);
        h.Add(composition.Count);
        foreach (string key in composition)
        {
            h.Add(key);
        }

        return h.Value;
    }

    // ---------------------------------------------------------------- settlements + cities

    [Fact]
    public void ProceduralSettlements_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        foreach (string tier in Tiers)
        {
            foreach (bool ruined in new[] { false, true })
            {
                for (int i = 0; i < Seeds.Length; i++)
                {
                    string surface = Surfaces[i];
                    var s = SettlementGenerator.Generate(tier, ruined, Seeds[i], surface, Content);
                    cases.Add(($"settlement:{tier}:{(ruined ? "ruined" : "intact")}:{N(Seeds[i])}:{surface}", Hash(s)));
                }
            }
        }

        // The legacy crop pick (#2689) of a pre-record settlement.
        var legacy = SettlementGenerator.Generate("town", false, Seeds[1], "grass", Content, legacyCrops: true);
        cases.Add(("settlement:town:legacy-crops", Hash(legacy)));
        StructureGoldens.AssertPinned("procedural settlements", SettlementPins, cases, LinuxPins);
    }

    [Fact]
    public void ModuleKitAndTemplateSettlements_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        var pool = ModulePool();

        // Plot modules composed into a procedural settlement (#1827), the picks recorded (#1872) — and the legacy roll (#2690).
        foreach (string tier in Tiers)
        {
            var composition = new List<string>();
            var s = SettlementGenerator.Generate(tier, false, Seeds[1], "grass", Content, pool, 0.5, composition);
            cases.Add(($"modules:{tier}", Hash(s, composition)));
        }

        var legacyRoll = new List<string>();
        var rolled = SettlementGenerator.Generate("village", false, Seeds[2], "sand", Content, pool, 0.5, legacyRoll, legacyModuleRoll: true);
        cases.Add(("modules:village:legacy-roll", Hash(rolled, legacyRoll)));

        // Every shipped settlement kit (#1876, the modular kits #1886; the pin-only defaults replay old records), its grid
        // drawn like the server does.
        foreach (string tier in Tiers)
        {
            foreach (var kit in KitsOf(StructureKit.KindSettlement, tier))
            {
                for (int i = 0; i < 2; i++)
                {
                    var layout = SettlementLayoutSpec.FromKit(kit, tier, new Random((int)Seeds[i]));
                    var composition = new List<string>();
                    var s = SettlementGenerator.Generate(tier, false, Seeds[i], Surfaces[i], Content, null, 0, composition, null, layout, kit, pool);
                    cases.Add(($"kit:{kit.Key}:{N(Seeds[i])}:{layout.Serialize()}", Hash(s, composition)));
                }
            }
        }

        // Every complete (whole) settlement template, as the stamper builds it.
        foreach (var t in Content.SettlementTemplates.Where(t => !t.IsModule).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            cases.Add(($"template:{t.Key}", Hash(SettlementGenerator.FromTemplate(t, Content))));
        }

        StructureGoldens.AssertPinned("module, kit and template settlements", SettlementKitPins, cases, LinuxPins);
    }

    [Fact]
    public void Cities_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        var zones = CityZones();
        var pool = ModulePool();
        foreach (long seed in Seeds.Take(2))
        {
            cases.Add(($"city:procedural:{N(seed)}", Hash(CityGenerator.Generate(seed, Content, zones))));
            var composition = new List<string>();
            var withModules = CityGenerator.Generate(seed, Content, zones, pool, 0.5, composition);
            cases.Add(($"city:modules:{N(seed)}", Hash(withModules, composition)));
        }

        foreach (string key in new[] { "city_gds_default_1", "city_gds_modular_1" })
        {
            var kit = Content.KitByKey(key)!;
            var spec = CityLayoutSpec.FromKit(kit);
            foreach (long seed in Seeds.Take(2))
            {
                var composition = new List<string>();
                var city = CityGenerator.Generate(seed, Content, zones, pool, 0.5, composition, null, spec, kit, pool);
                cases.Add(($"city:kit:{key}:{N(seed)}", Hash(city, composition)));
            }
        }

        StructureGoldens.AssertPinned("cities", CityPins, cases, LinuxPins);
    }

    /// <summary>The open zones the server hands the city composer: the pad ring and the wreck crash site.</summary>
    private static IReadOnlyList<CityGenerator.OpenZone> CityZones()
    {
        int c = CityGenerator.Footprint / 2;
        return new[]
        {
            new CityGenerator.OpenZone(c - 11, c - 11, c + 11, c + 11),
            new CityGenerator.OpenZone(c - 56 - 14, c + 56 - 14, c - 56 + 14, c + 56 + 14),
        };
    }

    // ---------------------------------------------------------------- stations

    [Fact]
    public void Stations_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        foreach (string tier in new[] { "small", "medium", "large", "huge", "colossal" })
        {
            foreach (long seed in Seeds.Take(2))
            {
                cases.Add(($"station:{tier}:{N(seed)}", StructureGoldens.Station(Content, StationGenerator.Generate(tier, seed, Content))));
            }

            foreach (var kit in KitsOf(StructureKit.KindStation, tier))
            {
                foreach (long seed in Seeds.Take(2))
                {
                    var s = StationKitComposer.Compose(kit, key => Content.TemplateByKey(StructureKit.KindStation, key), seed, Content,
                        out var composition, out var failure);
                    Assert.True(s != null, $"{kit.Key}: {failure}");
                    ulong hash = StructureGoldens.Station(Content, s!);
                    cases.Add(($"stationkit:{kit.Key}:{N(seed)}", hash));

                    // A pinned composition replays byte for byte (#1874) — the server's every later load.
                    var replay = StationKitComposer.Replay(composition, key => Content.TemplateByKey(StructureKit.KindStation, key), Content,
                        tier, out var replayFailure);
                    Assert.True(replay != null, $"{kit.Key} replay: {replayFailure}");
                    Assert.Equal(hash, StructureGoldens.Station(Content, replay!));
                }
            }
        }

        foreach (var t in Content.StationTemplates.Where(t => !t.IsModule).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            cases.Add(($"stationtemplate:{t.Key}", StructureGoldens.Station(Content, StationGenerator.FromTemplate(t, Content))));
        }

        StructureGoldens.AssertPinned("stations", StationPins, cases, LinuxPins);
    }

    // ---------------------------------------------------------------- monuments, tablets, landmarks

    [Fact]
    public void Monuments_EveryArchetypeOfEveryPool_MatchThePins()
    {
        // The generation-21 pool holds the generation-0 and generation-1 pools as its prefix.
        Assert.Equal(MonumentGenerator.Archetypes, MonumentGenerator.ArchetypesGen21.Take(MonumentGenerator.Archetypes.Length));
        Assert.Equal(MonumentGenerator.ArchetypesGen1, MonumentGenerator.ArchetypesGen21.Take(MonumentGenerator.ArchetypesGen1.Length));
        var cases = new List<(string, ulong)>();
        foreach (string archetype in MonumentGenerator.ArchetypesGen21)
        {
            for (int i = 0; i < 2; i++)
            {
                string surface = i == 0 ? "grass" : "stone";
                var s = MonumentGenerator.Generate(archetype, Seeds[i + 1], surface, Content, withCache: i == 0);
                cases.Add(($"monument:{archetype}:{N(Seeds[i + 1])}:{surface}:{(i == 0 ? "cache" : "plain")}", Hash(s)));
            }
        }

        StructureGoldens.AssertPinned("monuments", MonumentPins, cases, LinuxPins);
    }

    [Fact]
    public void TextMonumentsAndLandmarks_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        ushort B(string key) => Content.GetBlock(key)!.NumericId.Value;
        var kuba = Content.GetPlanet("asteroid_kuba_w")!;
        Assert.NotEmpty(kuba.TextMonuments);
        foreach (var spec in kuba.TextMonuments)
        {
            // As GameServerTextMonuments builds it: basalt slab, concrete letters, the accent run in gold.
            int accentLine = spec.AccentFrom.Count >= 2 ? spec.AccentFrom[0] : -1;
            int accentFrom = spec.AccentFrom.Count >= 2 ? spec.AccentFrom[1] : 0;
            var t = TextMonumentGenerator.Build(spec.Lines, B("basalt"), B("concrete"), B("gold_block"), accentLine, accentFrom);
            cases.Add(($"tablet:{spec.Key}", StructureGoldens.Tablet(Content, t)));
        }

        cases.Add(("tablet:plain", StructureGoldens.Tablet(Content, TextMonumentGenerator.Build(new[] { "HELLO", "WORLD 42" }, B("stone"), B("concrete"), B("gold_block")))));

        foreach (string key in new[] { "sample_tower", "sample_mine", "sample_store" })
        {
            var t = Content.LandmarkByKey(key)!;
            foreach (bool ruin in new[] { false, true })
            {
                for (int turns = 0; turns < 4; turns++)
                {
                    var l = LandmarkGenerator.Build(t, Seeds[1], turns, Content, "grass", ruin);
                    cases.Add(($"landmark:{key}:{(ruin ? "ruin" : "intact")}:t{turns}", StructureGoldens.Landmark(Content, l)));
                }
            }

            // The template's own ruin rule on another seed and surface.
            var own = LandmarkGenerator.Build(t, Seeds[2], 1, Content, "sand");
            cases.Add(($"landmark:{key}:own:{N(Seeds[2])}:sand", StructureGoldens.Landmark(Content, own)));
        }

        StructureGoldens.AssertPinned("text monuments and landmarks", TabletLandmarkPins, cases, LinuxPins);
    }
}
