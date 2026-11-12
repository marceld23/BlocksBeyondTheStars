// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The school club's landmarks (terrain generation 39) in the structure editor's "Stamping…" panel: the protection line every
/// target gets (<see cref="StructureStampRules.ProtectionKey"/> — also the line under the form's Protection picker), the span
/// seat and a landmark's ruin zones (<see cref="StampStepsDescriber"/>), and the zone tool's place in the palette.
/// </summary>
public sealed class StampStepsProtectionTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static StampDesign Design(StampTarget target, string protection = "", bool ruin = false)
        => new() { Target = target, Tier = "village", Surface = "stone", Protection = protection, Ruin = ruin };

    private static StampStep Step(List<StampStep> steps, string title) => Assert.Single(steps, s => s.Title == title);

    private static string[] Args(StampStep step) => step.Args.Select(a => a.Text).ToArray();

    // ---------------------------------------------------------------- protection

    [Theory]
    [InlineData(StampTarget.SettlementWhole, true)]
    [InlineData(StampTarget.UndergroundWhole, true)]
    [InlineData(StampTarget.Landmark, true)]
    [InlineData(StampTarget.StationWhole, true)]
    [InlineData(StampTarget.SettlementEntry, true)]
    [InlineData(StampTarget.UndergroundEntry, true)]
    [InlineData(StampTarget.LandmarkEntry, true)]
    [InlineData(StampTarget.StationEntry, true)]
    [InlineData(StampTarget.SettlementModule, false)]
    [InlineData(StampTarget.CityModule, false)]
    [InlineData(StampTarget.StationModule, false)]
    public void EveryWholeTemplateAndEntryTakesAProtection_AModuleFollowsItsStructure(StampTarget target, bool takes)
        => Assert.Equal(takes, StructureStampRules.TakesProtection(target));

    [Theory]
    [InlineData(StampTarget.SettlementWhole, false, "ui.stamp.protection.auto_settlement")]
    [InlineData(StampTarget.SettlementEntry, false, "ui.stamp.protection.auto_settlement")]
    [InlineData(StampTarget.StationWhole, false, "ui.stamp.protection.auto_station")]
    [InlineData(StampTarget.StationEntry, false, "ui.stamp.protection.auto_station")]
    [InlineData(StampTarget.UndergroundWhole, false, "ui.stamp.protection.auto_underground")]
    [InlineData(StampTarget.UndergroundEntry, false, "ui.stamp.protection.auto_underground")]
    [InlineData(StampTarget.Landmark, false, "ui.stamp.protection.auto_landmark_intact")]
    [InlineData(StampTarget.Landmark, true, "ui.stamp.protection.auto_landmark_ruin")]
    [InlineData(StampTarget.LandmarkEntry, false, "ui.stamp.protection.auto_landmark")]
    public void Auto_SaysTheRuleOfTheKind(StampTarget target, bool ruin, string want)
    {
        foreach (string auto in new[] { "", "auto", "AUTO", "nonsense" }) // empty and unknown words read as auto, as on the server
        {
            var steps = StampStepsDescriber.Describe(Design(target, auto, ruin), Content);
            Assert.Equal(want, Step(steps, StampStepsDescriber.TitleProtection).Text);
        }
    }

    [Fact]
    public void ProtectedAndOpen_SayWhatTheTemplateNames_ExceptForAModule()
    {
        foreach (var target in Enum.GetValues<StampTarget>())
        {
            foreach (string mode in LandmarkRules.ProtectionModes)
            {
                string key = StructureStampRules.ProtectionKey(Design(target, mode));
                if (!StructureStampRules.TakesProtection(target))
                {
                    Assert.Equal("ui.stamp.protection.module", key);
                    continue;
                }

                // Anchored on the shared rule the server asks (LandmarkRules.IsProtected): "protected" whatever the kind says,
                // "open" whatever it says, auto = the kind's own rule.
                var template = new StructureTemplate { Protection = mode };
                bool always = LandmarkRules.IsProtected(template, byKind: false), never = !LandmarkRules.IsProtected(template, byKind: true);
                Assert.Equal(always, key == "ui.stamp.protection.protected");
                Assert.Equal(never, key == "ui.stamp.protection.open");
                Assert.Equal(!always && !never, key.StartsWith("ui.stamp.protection.auto_", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void TheProtectionStep_StandsBetweenTheResidentsAndTheKeepClear()
    {
        var steps = StampStepsDescriber.Describe(Design(StampTarget.Landmark, LandmarkRules.ProtectionProtected), Content);
        var titles = steps.Select(s => s.Title).ToList();
        int protection = titles.IndexOf(StampStepsDescriber.TitleProtection);
        Assert.Equal(titles.IndexOf(StampStepsDescriber.TitleResidents) + 1, protection);
        Assert.Equal(protection + 1, titles.IndexOf(StampStepsDescriber.TitleKeepClear));
    }

    // ---------------------------------------------------------------- the span seat

    [Fact]
    public void ASpan_NamesItsDeckRowTheGapAndTheBanks()
    {
        foreach (var target in new[] { StampTarget.Landmark, StampTarget.LandmarkEntry })
        {
            var d = Design(target);
            d.Seat = "SPAN";
            d.GroundRow = 40;
            var seat = Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleSeat);
            Assert.Equal("ui.stamp.seat.span", seat.Text);
            Assert.Equal(new[] { "40", LandmarkRules.SpanGapDepth.ToString(CultureInfo.InvariantCulture),
                LandmarkRules.SpanBankTolerance.ToString(CultureInfo.InvariantCulture) }, Args(seat));
        }

        var settlement = Design(StampTarget.SettlementWhole);
        settlement.Seat = LandmarkRules.SeatSpan; // only a landmark spans
        Assert.Equal("ui.stamp.seat.settlement", Step(StampStepsDescriber.Describe(settlement, Content), StampStepsDescriber.TitleSeat).Text);
    }

    // ---------------------------------------------------------------- ruin zones

    [Fact]
    public void ALandmarkWithRuinZones_ListsEveryZoneInItsOrder()
    {
        var d = Design(StampTarget.Landmark, ruin: true);
        d.RuinProfile = LandmarkRules.ProfileMasonry;
        d.RuinStrength = 0.3;
        d.KeepCells = 5;
        d.RuinZones.Add(new RuinZone { X0 = 9, Y0 = 20, Z0 = 4, X1 = 2, Y1 = 60, Z1 = 0, Profile = LandmarkRules.ProfileLattice }); // corners swapped
        d.RuinZones.Add(new RuinZone { X1 = 30, Y1 = 19, Z1 = 30, Strength = 0.8 }); // the landmark's own profile
        var ruin = Assert.IsType<StampListStep>(Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleRuin));

        Assert.Equal("ui.stamp.ruin.zoned", ruin.Text);
        Assert.Equal(new[] { "2", "ui.ed.landmark.profile.masonry", "30", "5" }, Args(ruin));
        Assert.Equal(0, ruin.More);
        Assert.Equal(2, ruin.Lines.Count);
        Assert.All(ruin.Lines, l => Assert.Equal("ui.stamp.ruin.zone_line", l.Text));
        Assert.Equal(new[] { "1", "2, 20, 0", "9, 60, 4", "ui.ed.landmark.profile.lattice", "40" }, Args(ruin.Lines[0])); // lattice's default
        Assert.Equal(new[] { "2", "0, 0, 0", "30, 19, 30", "ui.ed.landmark.profile.masonry", "80" }, Args(ruin.Lines[1]));
    }

    [Fact]
    public void ZonesOfAnIntactLandmark_OrWithoutZones_KeepTheClassicRuinLine()
    {
        var intact = Design(StampTarget.Landmark);
        intact.RuinZones.Add(new RuinZone { X1 = 3, Y1 = 3, Z1 = 3 });
        Assert.Equal("ui.stamp.ruin.never", Step(StampStepsDescriber.Describe(intact, Content), StampStepsDescriber.TitleRuin).Text);

        var plain = Design(StampTarget.Landmark, ruin: true);
        var step = Step(StampStepsDescriber.Describe(plain, Content), StampStepsDescriber.TitleRuin);
        Assert.IsNotType<StampListStep>(step);
        Assert.Equal("ui.stamp.ruin.always", step.Text);
    }

    [Fact]
    public void TheZoneTool_ShowsForALandmarkOnly()
    {
        foreach (var target in Enum.GetValues<StampTarget>())
        {
            Assert.Equal(target == StampTarget.Landmark,
                StructureStampRules.ShowsInPalette(target, StructureStampRules.KindZone, StructureStampRules.KindZone));
        }
    }
}
