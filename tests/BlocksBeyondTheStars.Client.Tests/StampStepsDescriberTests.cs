// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3523: the structure editor's "what happens when it is stamped" panel and its honest tools — which stamp-time steps apply
/// to each target with which values (<see cref="StampStepsDescriber"/>), and what each target reads of a template
/// (<see cref="StructureStampRules"/>), checked against the generators the server runs where the rule can be observed there.
/// </summary>
public sealed class StampStepsDescriberTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static readonly StampTarget[] AllTargets = Enum.GetValues<StampTarget>();

    private static StampDesign Design(StampTarget target, string tier = "village", string surface = "stone")
        => new() { Target = target, Tier = tier, Surface = surface };

    private static StampStep Step(List<StampStep> steps, string title) => Assert.Single(steps, s => s.Title == title);

    private static string[] Args(StampStep step) => step.Args.Select(a => a.Text).ToArray();

    // ---------------------------------------------------------------- targets and pools

    [Theory]
    [InlineData(true, false, false, false, false, false, "", StampTarget.StationWhole)]
    [InlineData(true, true, false, false, false, false, "", StampTarget.StationModule)]
    [InlineData(true, false, false, true, false, false, "", StampTarget.StationEntry)]
    [InlineData(false, false, false, false, false, false, "", StampTarget.SettlementWhole)]
    [InlineData(false, false, false, false, true, false, "", StampTarget.UndergroundWhole)]
    [InlineData(false, true, false, false, true, false, "", StampTarget.SettlementModule)]
    [InlineData(false, true, false, false, false, true, "", StampTarget.CityModule)]
    [InlineData(false, false, true, false, true, false, "", StampTarget.Landmark)]
    [InlineData(false, false, false, true, false, false, "underground", StampTarget.UndergroundEntry)]
    [InlineData(false, false, false, true, false, false, "landmark", StampTarget.LandmarkEntry)]
    [InlineData(false, false, false, true, false, false, "settlement", StampTarget.SettlementEntry)]
    public void TheFormDecidesTheTarget(bool station, bool module, bool landmark, bool entry, bool underground, bool city, string pool, StampTarget want)
        => Assert.Equal(want, StructureStampRules.TargetFor(station, module, landmark, entry, underground, city, pool));

    [Fact]
    public void APoolTakesExactlyTheSourcesTheRegistryAccepts()
    {
        foreach (var target in new[] { StampTarget.SettlementEntry, StampTarget.UndergroundEntry, StampTarget.LandmarkEntry, StampTarget.StationEntry })
        {
            var (pool, underground) = StructureStampRules.RegistryPool(target);
            foreach (var source in StructureSources.All)
            {
                var entry = new StructureTemplate { Key = "e", Source = source.Key, Underground = underground };
                bool accepted = SourceTemplates.Problem(entry, pool, Content) is null;
                Assert.True(accepted == StructureStampRules.PoolTakes(target, source), $"{target} / {source.Key}");
            }
        }
    }

    [Fact]
    public void EachEditorBakesItsOwnKindsAndNeverAShip()
    {
        Assert.True(StructureStampRules.EditorBakes(station: true, StructureSourceKind.Space));
        Assert.False(StructureStampRules.EditorBakes(station: true, StructureSourceKind.Surface));
        Assert.True(StructureStampRules.EditorBakes(station: false, StructureSourceKind.Underground));
        Assert.False(StructureStampRules.EditorBakes(station: false, StructureSourceKind.Space));
        Assert.False(StructureStampRules.EditorBakes(station: false, StructureSourceKind.Ship));
        Assert.False(StructureStampRules.EditorBakes(station: true, StructureSourceKind.Ship));
    }

    // ---------------------------------------------------------------- what each target reads, against the generators

    [Fact]
    public void AWholeSettlementTemplateCarriesItsMarkerSettings_AnUnstampedOneDoesNot()
    {
        var t = Template(("marker", "chest", "table=ruin_rare"), ("block", "stone", ""));
        var stamped = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp());
        var legacy = SettlementGenerator.FromTemplate(t, Content, null, null);

        Assert.True(StructureStampRules.ReadsMarkerSettings(StampTarget.SettlementWhole, "chest"));
        Assert.Contains(stamped.Markers, m => m.Type == "chest" && m.Data == "table=ruin_rare");
        Assert.False(StructureStampRules.ReadsMarkerSettings(StampTarget.UndergroundWhole, "chest")); // seated without a stamp
        Assert.Contains(legacy.Markers, m => m.Type == "chest" && m.Data.Length == 0);
    }

    [Fact]
    public void AWholeStationTemplateResolvesTokensAndCarriesSettings()
    {
        var t = Template(("marker", "sound", "sound=amb_sounder"), ("block", MaterialTokens.Wall, ""));
        var s = StationGenerator.FromTemplate(t, Content, ModuleMaterials.ForStation(Content), markerSettings: true);

        Assert.True(StructureStampRules.ResolvesTokens(StampTarget.StationWhole));
        Assert.Equal(Content.GetBlock("iron_wall")!.NumericId.Value, s.Get(1, 0, 0));
        Assert.True(StructureStampRules.ReadsMarkerSettings(StampTarget.StationWhole, "sound"));
        Assert.Contains(s.Markers, m => m.Type == "sound" && m.Data == "sound=amb_sounder");
        Assert.False(StructureStampRules.ResolvesTokens(StampTarget.StationModule)); // the kit composer writes block keys only
    }

    [Fact]
    public void AFallenWholeTemplateLeavesItsKeptCellsStanding()
    {
        var t = new StructureTemplate { Key = "tower", Tier = "village", Width = 5, Height = 12, Length = 5 };
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 12; y++)
            {
                for (int z = 0; z < 5; z++)
                {
                    t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Id = "stone", Keep = x == 2 && z == 2 });
                }
            }
        }

        ushort stone = Content.GetBlock("stone")!.NumericId.Value;
        for (long seed = 1; seed <= 3; seed++)
        {
            var fallen = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp { Ruined = true, Seed = seed, Surface = "stone" });
            Assert.True(fallen.Ruined);
            for (int y = 0; y < 12; y++)
            {
                Assert.Equal(stone, fallen.Get(2, y, 2));
            }
        }

        Assert.True(StructureStampRules.ReadsKeep(StampTarget.SettlementWhole));
    }

    [Fact]
    public void ThePaletteShowsWhatTheTargetReads()
    {
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.StationModule, "block", MaterialTokens.Wall));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.StationModule, StructureStampRules.KindSettings, "settings"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.StationModule, "marker", "sound"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.StationModule, "marker", "chest"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.StationModule, StructureStampRules.KindPort, "door"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.StationWhole, "block", MaterialTokens.Wall));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.StationWhole, "marker", "chest"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.StationWhole, StructureStampRules.KindKeep, "keep"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.SettlementWhole, StructureStampRules.KindCavity, "cavity"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.SettlementWhole, StructureStampRules.KindPort, "door"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.SettlementWhole, StructureStampRules.KindKeep, "keep"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.CityModule, StructureStampRules.KindKeep, "keep"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.Landmark, StructureStampRules.KindCavity, "cavity"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.Landmark, StructureStampRules.KindSettings, "settings"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.UndergroundWhole, "marker", "den"));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.UndergroundWhole, "block", "stone"));
        Assert.False(StructureStampRules.ShowsInPalette(StampTarget.SettlementEntry, StructureStampRules.KindDevice, "device"));
    }

    [Fact]
    public void OnlyALandmarkReadsAVendorsSettings()
    {
        foreach (var t in AllTargets)
        {
            Assert.Equal(t == StampTarget.Landmark, StructureStampRules.ReadsMarkerSettings(t, "vendor"));
        }
    }

    // ---------------------------------------------------------------- warnings and the refusal

    [Fact]
    public void SaveWarnsAboutWhatTheTargetIgnores()
    {
        var d = Design(StampTarget.StationWhole);
        d.KeepCells = 3;
        d.PortCells = 2;
        d.CavityCells = 1;
        d.Markers.Add(new StampMarker("vendor", "theme=settlers"));
        d.Markers.Add(new StampMarker("chest", "table=ruin_rare")); // read by a whole station
        var keys = StructureStampRules.Warnings(d).Select(w => (w.Key, w.Count)).ToList();

        Assert.Equal(new[] { ("ui.ed.warn.keep", 3), ("ui.ed.warn.cavity", 1), ("ui.ed.warn.ports", 2), ("ui.ed.warn.settings", 1) }, keys);
    }

    [Fact]
    public void ALandmarkReadsItsKeepCavityAndSettings()
    {
        var d = Design(StampTarget.Landmark);
        d.KeepCells = 3;
        d.CavityCells = 9;
        d.Markers.Add(new StampMarker("vendor", "theme=settlers"));
        d.Markers.Add(new StampMarker("den", "species=native"));
        Assert.Empty(StructureStampRules.Warnings(d));
    }

    [Fact]
    public void AStationModuleRefusesTokens_AWholeStationTakesThem()
    {
        var module = Design(StampTarget.StationModule);
        module.TokenCells[MaterialTokens.Wall] = 4;
        module.TokenCells[MaterialTokens.Floor] = 2;
        var refusal = StructureStampRules.Refusal(module);
        Assert.NotNull(refusal);
        Assert.Equal(("ui.ed.refuse.tokens", 6), (refusal!.Value.Key, refusal.Value.Count));

        var whole = Design(StampTarget.StationWhole);
        whole.TokenCells[MaterialTokens.Wall] = 4;
        Assert.Null(StructureStampRules.Refusal(whole));
    }

    // ---------------------------------------------------------------- the steps

    [Fact]
    public void AStationModule_DocksTurnsAndNeverFalls()
    {
        var d = Design(StampTarget.StationModule);
        d.Function = StructureRoles.Canteen;
        d.TokenCells[MaterialTokens.Wall] = 5;
        var steps = StampStepsDescriber.Describe(d, Content);

        Assert.Equal("ui.stamp.seat.station_module", Step(steps, StampStepsDescriber.TitleSeat).Text);
        Assert.Equal(new[] { "5" }, Args(Step(steps, StampStepsDescriber.TitleTokens)));
        Assert.Equal("ui.stamp.tokens.air", Step(steps, StampStepsDescriber.TitleTokens).Text);
        Assert.Equal("ui.function.canteen", Step(steps, StampStepsDescriber.TitleFurnish).Args[0].Text);
        Assert.Equal("ui.stamp.turn.dock", Step(steps, StampStepsDescriber.TitleTurn).Text);
        Assert.Equal("ui.stamp.ruin.never", Step(steps, StampStepsDescriber.TitleRuin).Text);
        Assert.DoesNotContain(steps, s => s.Title == StampStepsDescriber.TitleKeepClear);
    }

    [Fact]
    public void AVillageTemplate_ResolvesItsTokensForTheDesertGround()
    {
        var d = Design(StampTarget.SettlementWhole, "village", "sand");
        d.TokenCells[MaterialTokens.Wall] = 10;
        var tokens = Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleTokens);

        Assert.Equal("ui.stamp.tokens.resolved", tokens.Text);
        Assert.Equal(new[] { "10", "block.sand.name", "block.sand.name", "block.carbon.name", "block.sand.name", "block.sand.name", "block.sand.name" },
            Args(tokens));
    }

    [Fact]
    public void AWholeSettlement_TurnsFallsAndKeepsItsDistance()
    {
        var d = Design(StampTarget.SettlementWhole, "town");
        d.KeepCells = 7;
        d.Markers.Add(new StampMarker("room", string.Empty));
        d.Markers.Add(new StampMarker("vendor", string.Empty));
        d.Markers.Add(new StampMarker("npc", string.Empty));
        d.Markers.Add(new StampMarker("doctor", string.Empty));
        var steps = StampStepsDescriber.Describe(d, Content);

        Assert.Equal("ui.stamp.turn.pinned", Step(steps, StampStepsDescriber.TitleTurn).Text);
        var ruin = Step(steps, StampStepsDescriber.TitleRuin);
        Assert.Equal("ui.stamp.ruin.rolled", ruin.Text);
        Assert.Equal(new[] { "ui.ed.landmark.profile.masonry", "50", "7" }, Args(ruin));
        Assert.Equal(new[] { "1", "ui.stamp.style.town" }, Args(Step(steps, StampStepsDescriber.TitleFurnish)));
        Assert.Equal(new[] { "1", "1", "1" }, Args(Step(steps, StampStepsDescriber.TitleResidents)));
        string keepClear = Content.StructureTypes.Get(StructureTypeRules.Settlement).KeepClear.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(new[] { keepClear }, Args(Step(steps, StampStepsDescriber.TitleKeepClear)));
    }

    [Fact]
    public void Chests_ListTheOnesWithSettings_AndCountTheFurniture()
    {
        var d = Design(StampTarget.SettlementWhole);
        d.Markers.Add(new StampMarker("chest", "table=ruin_rare;chance=0.5", 1, 2, 3));
        d.Markers.Add(new StampMarker("chest", "items=iron_plate:2-4", 4, 5, 6));
        d.Markers.Add(new StampMarker("chest", string.Empty));
        var chests = Assert.IsType<StampListStep>(Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleChests));

        Assert.Equal(new[] { "2", "1" }, Args(chests));
        Assert.Equal(new[] { "1, 2, 3", "50", "ruin_rare" }, Args(chests.Lines[0]));
        Assert.Equal("ui.stamp.chest.items", chests.Lines[1].Text);
        Assert.Equal(new[] { "4, 5, 6", "100", "iron_plate:2-4" }, Args(chests.Lines[1]));
    }

    [Fact]
    public void ALandmarkChestWithoutSettings_GetsTheDefaultTable()
    {
        var d = Design(StampTarget.Landmark);
        d.Markers.Add(new StampMarker("chest", string.Empty, 7, 0, 7));
        var chests = Assert.IsType<StampListStep>(Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleChests));

        Assert.Equal(new[] { "7, 0, 7", "100", StampStepsDescriber.DefaultLootTable }, Args(Assert.Single(chests.Lines)));
    }

    [Fact]
    public void ALandmark_FallsWithItsProfile_OrStandsInhabited()
    {
        var ruin = Design(StampTarget.Landmark);
        ruin.Ruin = true;
        ruin.RuinProfile = LandmarkRules.ProfileLattice;
        ruin.GroundRow = 4;
        var steps = StampStepsDescriber.Describe(ruin, Content);
        Assert.Equal(new[] { "ui.ed.landmark.profile.lattice", "40", "0" }, Args(Step(steps, StampStepsDescriber.TitleRuin)));
        Assert.Equal("ui.stamp.seat.landmark", Step(steps, StampStepsDescriber.TitleSeat).Text);
        Assert.Equal("ui.stamp.residents.landmark", Step(steps, StampStepsDescriber.TitleResidents).Text);

        var inhabited = Design(StampTarget.Landmark);
        inhabited.Protected = true;
        steps = StampStepsDescriber.Describe(inhabited, Content);
        Assert.Equal("ui.stamp.ruin.never", Step(steps, StampStepsDescriber.TitleRuin).Text);
        Assert.Equal("ui.stamp.residents.landmark_inhabited", Step(steps, StampStepsDescriber.TitleResidents).Text);
        Assert.Equal("ui.stamp.seat.landmark_flat", Step(steps, StampStepsDescriber.TitleSeat).Text);
    }

    [Fact]
    public void AnUndergroundTemplate_IsAFindNobodyLivesIn()
    {
        var d = Design(StampTarget.UndergroundWhole);
        d.Markers.Add(new StampMarker("chest", string.Empty));
        d.Markers.Add(new StampMarker("data_terminal", string.Empty));
        d.Markers.Add(new StampMarker("vendor", string.Empty));
        var steps = StampStepsDescriber.Describe(d, Content);

        Assert.Equal(new[] { "2" }, Args(Step(steps, StampStepsDescriber.TitleChests)));
        Assert.Equal("ui.stamp.residents.underground", Step(steps, StampStepsDescriber.TitleResidents).Text);
        Assert.Equal("ui.stamp.turn.never", Step(steps, StampStepsDescriber.TitleTurn).Text);
        Assert.Equal("ui.stamp.ruin.never", Step(steps, StampStepsDescriber.TitleRuin).Text);
    }

    [Fact]
    public void AGeneratorEntry_SaysWhatBuildsItAndSkipsTheCellSteps()
    {
        var d = Design(StampTarget.SettlementEntry);
        d.Source = "monument";
        d.Weight = 3;
        d.TokenCells[MaterialTokens.Wall] = 2; // a design behind the entry is not what is stamped
        var steps = StampStepsDescriber.Describe(d, Content);

        Assert.Equal(StampStepsDescriber.TitleBuild, steps[0].Title);
        Assert.Equal(new[] { "ui.source.monument", "3" }, Args(steps[0]));
        Assert.DoesNotContain(steps, s => s.Title is StampStepsDescriber.TitleTokens or StampStepsDescriber.TitleFurnish or StampStepsDescriber.TitleChests);
        Assert.Equal("ui.stamp.ruin.entry", Step(steps, StampStepsDescriber.TitleRuin).Text);
        Assert.Equal("ui.stamp.turn.never", Step(steps, StampStepsDescriber.TitleTurn).Text);

        var station = Design(StampTarget.StationEntry);
        station.Source = "derelict";
        steps = StampStepsDescriber.Describe(station, Content);
        Assert.Equal("ui.stamp.ruin.never", Step(steps, StampStepsDescriber.TitleRuin).Text);
        Assert.Equal("ui.stamp.seat.space", Step(steps, StampStepsDescriber.TitleSeat).Text);
    }

    [Fact]
    public void EveryStepKeyAndKeyArgument_ExistsInEnglishAndGerman()
    {
        var en = Locale("en");
        var de = Locale("de");
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in EveryKindOfDesign())
        {
            foreach (var step in StampStepsDescriber.Describe(d, Content))
            {
                var keys = new List<string> { step.Title, step.Text };
                keys.AddRange(step.Args.Where(a => a.IsKey).Select(a => a.Text));
                if (step is StampListStep list)
                {
                    keys.AddRange(list.Lines.Select(l => l.Text));
                    keys.Add("ui.stamp.chest.more");
                }

                missing.UnionWith(keys.Where(k => !en.ContainsKey(k) || !de.ContainsKey(k)).Select(k => $"{d.Target}: {k}"));
            }

            foreach (var w in StructureStampRules.Warnings(d))
            {
                if (!en.ContainsKey(w.Key) || !de.ContainsKey(w.Key))
                {
                    missing.Add($"{d.Target}: {w.Key}");
                }
            }
        }

        Assert.True(missing.Count == 0, "keys missing in en/de:\n" + string.Join("\n", missing));
    }

    /// <summary>Every target with every kind of cell and marker the describer looks at.</summary>
    private static IEnumerable<StampDesign> EveryKindOfDesign()
    {
        foreach (var target in AllTargets)
        {
            foreach (bool ruin in new[] { false, true })
            {
                var d = Design(target, ruin ? "town" : "village", "sand");
                d.Ruin = ruin;
                d.Protected = !ruin;
                d.GroundRow = ruin ? 3 : 0;
                d.Source = "settlement";
                d.Function = StructureRoles.Hub;
                d.KeepCells = d.CavityCells = d.PortCells = 1;
                d.TokenCells[MaterialTokens.Roof] = 1;
                foreach (string marker in new[] { "room", "vendor", "npc", "chest", "sound", "den", "loot" })
                {
                    d.Markers.Add(new StampMarker(marker, ruin ? "chance=0.5;table=ruin_rare" : string.Empty));
                }

                yield return d;
            }
        }
    }

    private static Dictionary<string, string> Locale(string code)
        => ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json"), Encoding.UTF8));

    /// <summary>A one-row template of (kind, id, settings) cells at x = 0, 1, 2 ….</summary>
    private static StructureTemplate Template(params (string Kind, string Id, string Config)[] cells)
    {
        var t = new StructureTemplate { Key = "t", Tier = "village", Width = cells.Length, Height = 1, Length = 1 };
        for (int i = 0; i < cells.Length; i++)
        {
            t.Cells.Add(new TemplateCell { X = i, Kind = cells[i].Kind, Id = cells[i].Id, Config = cells[i].Config });
        }

        return t;
    }
}
