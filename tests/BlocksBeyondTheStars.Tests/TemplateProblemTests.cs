// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3692: the structure-template checks as codes the editor translates. The coded check and the string check are the same
/// check — every coded problem carries exactly the sentence the string check returns, in the same order — so the server, the
/// content loader's warnings, the tools and the older tests read what they always read; and each problem names its code and
/// the values its localized sentence shows.
/// </summary>
public sealed class TemplateProblemTests
{
    private static readonly HashSet<string> KnownBlocks = new(StringComparer.Ordinal) { "iron_wall", "glass", "ladder", "water" };

    private static List<string> Codes(IEnumerable<TemplateProblem> problems) => problems.Select(p => p.Code).ToList();

    [Fact]
    public void Ports_CodedAndSentence_AreTheSameCheck()
    {
        var corner = StructurePortsTests.Box(5, 4, 5, (0, 1, 0, "door"));
        var lShape = StructurePortsTests.Box(5, 4, 5, (4, 1, 1, "door"), (4, 1, 2, "door"), (4, 2, 1, "door"));
        var blocked = StructurePortsTests.Box(5, 4, 5, (4, 1, 2, "door"));
        blocked.Cells.Add(new TemplateCell { X = 3, Y = 1, Z = 2, Kind = "block", Id = "iron_wall" });
        var markerPort = StructurePortsTests.Box(5, 4, 5);
        markerPort.Cells.Add(new TemplateCell { X = 2, Y = 1, Z = 1, Kind = "marker", Id = "vendor", Port = "door" });
        var unknown = StructurePortsTests.Box(5, 4, 5, (4, 1, 2, "door:swing"));

        foreach (var t in new[] { corner, lShape, blocked, markerPort, unknown })
        {
            Assert.Equal(StructurePorts.Validate(t), TemplateProblem.Sentences(StructurePorts.ValidateProblems(t)));
        }

        Assert.Contains("port.face", Codes(StructurePorts.ValidateProblems(corner)));
        Assert.Contains("port.not_rectangle", Codes(StructurePorts.ValidateProblems(lShape)));
        Assert.Contains("port.blocked", Codes(StructurePorts.ValidateProblems(blocked)));
        var onMarker = Assert.Single(StructurePorts.ValidateProblems(markerPort));
        Assert.Equal("port.on_marker", onMarker.Code);
        Assert.Equal(new[] { "door", "2, 1, 1" }, onMarker.Args);
        Assert.Equal("ui.problem.port.on_marker", onMarker.Key);
        Assert.Contains("port.unknown", Codes(StructurePorts.ValidateProblems(unknown)));
    }

    [Fact]
    public void Landmark_CodedAndSentence_AreTheSameCheck()
    {
        var t = new StructureTemplate
        {
            Key = "bad",
            Kind = "landmark",
            Landmark = true,
            Ruin = "sometimes",
            RuinProfile = "hull",
            RuinStrength = 2,
            GroundRow = 9,
            Width = 4,
            Height = 4,
            Length = 4,
            Kit = "k",
            Function = "room",
            Protection = "maybe",
            MinTerrainGeneration = -1,
            Requires = new List<string> { "sunshine" },
            RuinZones = new List<RuinZone> { new RuinZone { X0 = 0, X1 = 9, Profile = "gold", Strength = 3 } },
        };
        var coded = LandmarkRules.ProblemList(t);
        Assert.Equal(LandmarkRules.Problems(t), TemplateProblem.Sentences(coded));
        Assert.Equal(new[]
        {
            "ruin.mode", "ruin.profile_wreck", "ruin.strength", "condition.unknown", "ground_row.outside", "landmark.module",
            "protection.unknown", "generation.negative", "zone.outside", "zone.profile", "zone.strength",
        }, Codes(coded));
        Assert.Equal(new[] { "1", "gold" }, coded.Single(p => p.Code == "zone.profile").Args);
        Assert.Equal(new[] { "size.none" }, Codes(LandmarkRules.ProblemList(new StructureTemplate { Kind = "landmark", Landmark = true })));
    }

    [Fact]
    public void GiantShip_CodedAndSentence_AreTheSameCheck()
    {
        var ship = GiantShipTemplateTests.Ship();
        ship.Style = "wood";
        ship.Landmark = true;
        ship.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 1, Kind = "marker", Id = "vendor" });
        var coded = GiantShipRules.ProblemList(ship, KnownBlocks.Contains);
        Assert.Equal(GiantShipRules.Problems(ship, KnownBlocks.Contains), TemplateProblem.Sentences(coded));
        Assert.Contains("giant_ship.landmark", Codes(coded));
        Assert.Contains("giant_ship.style", Codes(coded));
        var marker = coded.Single(p => p.Code == "marker.not_ship");
        Assert.Equal(new[] { "vendor", "1, 1, 1" }, marker.Args);
        Assert.Empty(GiantShipRules.ProblemList(GiantShipTemplateTests.Ship(), KnownBlocks.Contains));
    }

    [Fact]
    public void SourceEntry_And_KitComposer_NameTheirCodes_AndKeepTheirSentences()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var entry = new StructureTemplate { Key = "e", Source = "no_such_source" };
        var problem = SourceTemplates.ProblemOf(entry, StructureKit.KindSettlement, content);
        Assert.NotNull(problem);
        Assert.Equal("source.unknown", problem!.Code);
        Assert.Equal(SourceTemplates.Problem(entry, StructureKit.KindSettlement, content), problem.English);

        var empty = new StructureKit { Key = "empty", Kind = StructureKit.KindStation };
        Assert.Null(StationKitComposer.ComposeExplained(empty, _ => null, 1, content, out _, out var failure));
        Assert.Equal("kit.no_entries", failure!.Code);
        Assert.Null(StationKitComposer.Compose(empty, _ => null, 1, content, out _, out string? sentence));
        Assert.Equal(failure.English, sentence);
    }

    [Fact]
    public void AProblem_ReadsAsItsEnglishSentence()
    {
        var p = new TemplateProblem("size.too_large", "it is larger than 256 blocks on an axis", TemplateProblem.Num(256));
        Assert.Equal("it is larger than 256 blocks on an axis", p.ToString());
        Assert.Equal("ui.problem.size.too_large", p.Key);
        Assert.Equal("3, 4, 5", TemplateProblem.At(3, 4, 5));
    }
}
