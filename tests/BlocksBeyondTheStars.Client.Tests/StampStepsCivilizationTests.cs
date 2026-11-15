// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3646: a CIVILIZATION template in the structure editor — its own target (<see cref="StampTarget.Civilization"/>), the honest
/// tools it gets (the air brush, the prop marker, only the markers a lost civilization may carry) and the "Stamping…" panel's
/// steps: its civilization and pool first, the seat on the sea floor (under water) or on land as its civilization lives, its
/// civilization's materials, never staffed, never protected — checked against the server's rules and the shipped content.
/// </summary>
public sealed class StampStepsCivilizationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private readonly ITestOutputHelper _out;

    public StampStepsCivilizationTests(ITestOutputHelper output) => _out = output;

    private static StampDesign Civ(string civilization, string pool, string propPool = "", bool ruin = true, int groundRow = 2, int air = 0)
    {
        var d = new StampDesign
        {
            Target = StampTarget.Civilization,
            Civilization = civilization,
            CivPool = pool,
            PropPool = propPool,
            Ruin = ruin,
            RuinProfile = LandmarkRules.ProfileMasonry,
            RuinStrength = 0.3,
            GroundRow = groundRow,
            AirCells = air,
            Surface = "sand",
            Protection = LandmarkRules.ProtectionOpen,
        };
        d.TokenCells[MaterialTokens.Wall] = 4;
        d.Markers.Add(new StampMarker(LandmarkRules.ChestMarker, "table=civ_grosstechniker_common", 1, 2, 3));
        d.Markers.Add(new StampMarker(CivilizationRules.PropMarker, "pool=remains"));
        return d;
    }

    private static StampStep Step(List<StampStep> steps, string title) => Assert.Single(steps, s => s.Title == title);

    private static string[] Args(StampStep step) => step.Args.Select(a => a.ToString()).ToArray();

    // ---------------------------------------------------------------- the target and its tools

    [Fact]
    public void ALandmarkThatNamesACivilization_IsACivilizationTemplate()
    {
        Assert.Equal(StampTarget.Civilization, StructureStampRules.TargetFor(false, false, true, false, false, false, "", civilization: true));
        Assert.Equal(StampTarget.Landmark, StructureStampRules.TargetFor(false, false, true, false, false, false, ""));
        Assert.Equal(StampTarget.SettlementWhole, StructureStampRules.TargetFor(false, false, false, false, false, false, "", civilization: true));
        Assert.Equal(StampTarget.StationWhole, StructureStampRules.TargetFor(true, false, true, false, false, false, "", civilization: true));
    }

    [Fact]
    public void OnlyACivilizationTemplate_GetsTheAirBrushAndThePropMarker()
    {
        foreach (var t in Enum.GetValues<StampTarget>())
        {
            bool civ = t == StampTarget.Civilization;
            Assert.Equal(civ, StructureStampRules.ShowsInPalette(t, StructureStampRules.KindAir, CivilizationEditorRules.AirPocketId));
            Assert.Equal(civ, StructureStampRules.ShowsInPalette(t, "marker", CivilizationRules.PropMarker));
            Assert.Equal(civ, StructureStampRules.ReadsMarkerSettings(t, CivilizationRules.PropMarker));
            Assert.Equal(civ, StructureStampRules.ReadsAir(t));
        }
    }

    [Fact]
    public void ACivilizationTemplate_ShowsOnlyWhatALostCivilizationMayCarry()
    {
        const StampTarget t = StampTarget.Civilization;
        foreach (string marker in new[] { "vendor", "npc", "mission_board", "doctor", LandmarkRules.DenMarker })
        {
            Assert.False(StructureStampRules.ShowsInPalette(t, "marker", marker), marker);
        }

        foreach (string marker in new[] { LandmarkRules.ChestMarker, LandmarkRules.SoundMarker, "loot", "door_slide", CivilizationRules.PropMarker })
        {
            Assert.True(StructureStampRules.ShowsInPalette(t, "marker", marker), marker);
        }

        Assert.True(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindCavity, LandmarkRules.CavityKind));
        Assert.True(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindKeep, "keep"));
        Assert.True(StructureStampRules.ShowsInPalette(t, "block", MaterialTokens.Wall)); // its civilization's materials
        Assert.False(StructureStampRules.ReadsMarkerSettings(t, "vendor"));
        Assert.False(StructureStampRules.ReadsMarkerSettings(t, LandmarkRules.DenMarker));
        Assert.True(StructureStampRules.ReadsMarkerSettings(t, LandmarkRules.ChestMarker));
        Assert.False(StructureStampRules.TakesProtection(t)); // the server never protects a civilization's site
        Assert.True(StructureStampRules.ReadsZones(t));
    }

    [Fact]
    public void AirPocketsOutsideACivilizationTemplate_AreNamedOnSave()
    {
        var landmark = new StampDesign { Target = StampTarget.Landmark, AirCells = 5 };
        Assert.Contains(StructureStampRules.Warnings(landmark), w => w.Key == "ui.ed.warn.air" && w.Count == 5);
        var civ = new StampDesign { Target = StampTarget.Civilization, AirCells = 5 };
        Assert.DoesNotContain(StructureStampRules.Warnings(civ), w => w.Key == "ui.ed.warn.air");
    }

    // ---------------------------------------------------------------- the stamping panel

    [Fact]
    public void ASunkenSite_NamesItsCivilizationPoolAndTheSeaFloor()
    {
        var steps = StampStepsDescriber.Describe(Civ("grosstechniker", CivilizationRules.PoolSmall, air: 12), Content);

        Assert.Equal(StampStepsDescriber.TitleCivilization, steps[0].Title);
        Assert.Equal("ui.stamp.civ.site", steps[0].Text);
        Assert.Equal(new[] { "@civ.grosstechniker.name", "@ui.stamp.civ.pool.small" }, Args(steps[0]));
        var seat = Step(steps, StampStepsDescriber.TitleSeat);
        Assert.Equal("ui.stamp.seat.seabed", seat.Text);
        Assert.Equal(new[] { "2", CivilizationRules.SunkenHeadroom.ToString(CultureInfo.InvariantCulture),
            CivilizationRules.SunkenMaxDig.ToString(CultureInfo.InvariantCulture), "12" }, Args(seat));
        Assert.Contains("@block.dark_sandstone.name", Args(Step(steps, StampStepsDescriber.TitleTokens))); // the civilization's @wall
        Assert.Equal("ui.stamp.furnish.never", Step(steps, StampStepsDescriber.TitleFurnish).Text);
        Assert.Equal("ui.stamp.turn.pinned", Step(steps, StampStepsDescriber.TitleTurn).Text);
        Assert.Equal("ui.stamp.ruin.always", Step(steps, StampStepsDescriber.TitleRuin).Text);
        Assert.Equal("ui.stamp.residents.civilization", Step(steps, StampStepsDescriber.TitleResidents).Text);
        Assert.Equal("ui.stamp.protection.civilization", Step(steps, StampStepsDescriber.TitleProtection).Text);
        Assert.Equal(new[] { Content.StructureTypes.Get(StructureTypeRules.Civilization).KeepClear.ToString(CultureInfo.InvariantCulture) },
            Args(Step(steps, StampStepsDescriber.TitleKeepClear)));
        Assert.IsType<StampListStep>(Step(steps, StampStepsDescriber.TitleChests)); // a chest without its own items still holds loot
    }

    [Fact]
    public void TheSeaFloorConstants_AreTheServersOwn()
    {
        Assert.Equal(3, CivilizationRules.SunkenHeadroom);
        Assert.Equal(48, CivilizationRules.SunkenMaxDig);
    }

    [Fact]
    public void ALandSite_SitsOnLand_AndAPropAtItsMarker()
    {
        var land = StampStepsDescriber.Describe(Civ("labyrinth_builders", CivilizationRules.PoolGiant), Content);
        Assert.Equal("ui.stamp.seat.civ_land", Step(land, StampStepsDescriber.TitleSeat).Text);
        Assert.Equal("@ui.stamp.civ.pool.giant", Args(land[0])[1]);

        var prop = StampStepsDescriber.Describe(Civ("labyrinth_builders", CivilizationRules.PoolProp, "statue", ruin: true, groundRow: 0), Content);
        Assert.Equal("ui.stamp.civ.prop", prop[0].Text);
        Assert.Equal(new[] { "@civ.labyrinth_builders.name", "statue" }, Args(prop[0]));
        Assert.Equal("ui.stamp.seat.prop", Step(prop, StampStepsDescriber.TitleSeat).Text);
        Assert.Equal("ui.stamp.turn.prop", Step(prop, StampStepsDescriber.TitleTurn).Text);
        Assert.Equal("ui.stamp.ruin.never", Step(prop, StampStepsDescriber.TitleRuin).Text); // set down whole after the site's ruin pass
        Assert.DoesNotContain(prop, s => s.Title == StampStepsDescriber.TitleKeepClear);
    }

    [Fact]
    public void AnUnknownCivilization_SaysTheLoaderDropsIt()
    {
        var steps = StampStepsDescriber.Describe(Civ("nobody_here", CivilizationRules.PoolCity), Content);
        Assert.Equal("ui.stamp.civ.unknown", steps[0].Text);
        Assert.Equal(new[] { "nobody_here" }, Args(steps[0]));
        Assert.Equal("ui.stamp.seat.civ_land", Step(steps, StampStepsDescriber.TitleSeat).Text);
    }

    [Fact]
    public void EveryCivilizationStepKey_ExistsInEnglishAndGerman_AndReads()
    {
        var en = Locale("en");
        var de = Locale("de");
        foreach (var civ in Content.Civilizations)
        {
            foreach (string pool in CivilizationRules.TemplatePools)
            {
                foreach (var step in StampStepsDescriber.Describe(Civ(civ.Key, pool, "statue", air: 3), Content))
                {
                    foreach (string key in new[] { step.Title, step.Text }.Concat(step.Args.Where(a => a.IsKey).Select(a => a.Text)))
                    {
                        Assert.True(en.ContainsKey(key) && de.ContainsKey(key), $"{civ.Key}/{pool}: {key}");
                    }

                    Assert.DoesNotContain("{", Render(step, en));
                    Assert.DoesNotContain("{", Render(step, de));
                }
            }
        }
    }

    [Fact]
    public void TheShippedGiantDwelling_ReadsInEnglish()
    {
        var t = Content.CivilizationTemplateByKey("gt_giant_dwelling");
        Assert.NotNull(t);
        var d = Civ(t!.Civilization, t.CivPool, t.PropPool, LandmarkRules.IsRuin(t), t.GroundRow);
        d.Markers.Clear();
        d.TokenCells.Clear();
        d.RuinProfile = t.RuinProfile;
        d.RuinStrength = t.RuinStrength;
        d.RuinZones.AddRange(t.RuinZones);
        TemplateBody.ForEachPlain(t, p =>
        {
            d.AirCells += p.Air ? 1 : 0;
            d.KeepCells += p.Keep && !p.Cavity ? 1 : 0;
        });
        foreach (var c in t.Cells.Where(c => c.Kind == "marker"))
        {
            d.Markers.Add(new StampMarker(c.Id, c.Config, c.X, c.Y, c.Z));
        }

        var en = Locale("en");
        foreach (var step in StampStepsDescriber.Describe(d, Content))
        {
            _out.WriteLine($"{en[step.Title]}: {Render(step, en)}");
            Assert.DoesNotContain("{", Render(step, en));
        }
    }

    // ---------------------------------------------------------------- the ruin preview

    [Fact]
    public void TheRuinPreview_IsTheServersCivilizationBuild()
    {
        var t = Content.CivilizationTemplateByKey("gt_glyph_pillar");
        Assert.NotNull(t);
        var civ = Content.GetCivilization(t!.Civilization)!;
        var preview = CivilizationEditorRules.RuinPreview(t, Content, 7, "sand");
        var server = CivilizationLayouts.Build(t, civ, 7, 0, Content, "sand", LandmarkGenerator.RuinRevision, airless: false, civ.Scope.Underwater);

        Assert.Equal(Blocks(server.Structure), Blocks(preview.Structure));
        Assert.True(preview.Ruined);
    }

    [Fact]
    public void TheRuinPreview_KeepsTheAirPockets()
    {
        var t = new StructureTemplate
        {
            Key = "bell",
            Civilization = "grosstechniker",
            CivPool = CivilizationRules.PoolSmall,
            Kind = CivilizationRules.TemplateKind,
            Ruin = LandmarkRules.RuinAlways,
            Width = 3,
            Height = 2,
            Length = 1,
        };
        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Kind = "block", Id = "dark_sandstone", Keep = true });
        t.Cells.Add(new TemplateCell { X = 1, Y = 0, Kind = CivilizationRules.AirKind });
        t.Cells.Add(new TemplateCell { X = 2, Y = 0, Kind = LandmarkRules.CavityKind });
        var layout = CivilizationEditorRules.RuinPreview(t, Content, 1, "sand");

        Assert.True(layout.HasAirPockets);
        Assert.Equal(CivilizationRules.AirKind, CivilizationEditorRules.EmptyKind(layout.IsCavity(1, 0, 0), layout.IsAirPocket(1, 0, 0)));
        Assert.Equal(LandmarkRules.CavityKind, CivilizationEditorRules.EmptyKind(cavity: true, air: false));
        Assert.Null(CivilizationEditorRules.EmptyKind(cavity: false, air: false));
    }

    private static string Blocks(SettlementStructure s)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    sb.Append(s.Get(x, y, z)).Append(',');
                }
            }
        }

        return sb.ToString();
    }

    private static string Render(StampStep step, Dictionary<string, string> table)
    {
        object[] args = step.Args.Select(a => (object)(a.IsKey ? table.GetValueOrDefault(a.Text, a.Text) : a.Text)).ToArray();
        return string.Format(CultureInfo.InvariantCulture, table[step.Text], args);
    }

    private static Dictionary<string, string> Locale(string code)
        => ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json"), Encoding.UTF8));
}
