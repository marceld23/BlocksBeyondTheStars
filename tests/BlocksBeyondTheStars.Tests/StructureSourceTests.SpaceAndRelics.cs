// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3512: the wrapper == generator pins of the wreck, tablet, landmark, the space sources and the buried ruin.</summary>
public sealed partial class StructureSourceTests
{
    [Fact]
    public void Wreck_IsTheGeneratorsWreck_AndItsRepairMaskWhenIntact()
    {
        var design = Content.Ships.Values.OrderBy(s => s.Key, StringComparer.Ordinal).Last();
        var hull = WreckGenerator.Generate(design, SeedA, Content);
        var markers = hull.Markers.Select(m => new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)).ToList();

        // #3521: the classic box is the wreck of terrain generation 37 and older (and of a ship without a layout).
        var inWorld = StructureSources.Get("wreck")!.Build(new StructureBuildRequest(Content, SeedA)
        {
            Params = new Dictionary<string, string> { ["ship"] = design.Key },
            Generation = WorldDescription.LandmarkGeneration,
        });
        AssertGrid(inWorld, hull.Width, hull.Height, hull.Length, 0, hull.Get);
        AssertMarkers(markers, inWorld.Markers);

        var intact = StructureSources.Get("wreck")!.Build(new StructureBuildRequest(Content, SeedA)
        {
            Params = new Dictionary<string, string> { ["ship"] = design.Key },
            AsInWorld = false,
            Generation = WorldDescription.LandmarkGeneration,
        });
        AssertGrid(intact, hull.Width, hull.Height, hull.Length, 0, hull.IntactAt);
        AssertMarkers(markers, intact.Markers);
    }

    [Fact]
    public void Wreck_OfGeneration38_IsTheLayoutWreck_AndItsRepairMaskWhenIntact()
    {
        var design = Content.Ships.Values.OrderBy(s => s.Key, StringComparer.Ordinal).Last();
        var hull = WreckGenerator.GenerateFromLayout(design, Content.GetShipLayout(design.Layout)!, SeedA, Content);
        var markers = hull.Markers.Select(m => new BuiltMarker(m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)).ToList();

        var inWorld = Build("wreck", SeedA, ("ship", design.Key));
        AssertGrid(inWorld, hull.Width, hull.Height, hull.Length, 0, hull.Get, hull.ModifierAt, hull.ShapeAt);
        AssertMarkers(markers, inWorld.Markers);
        Assert.Equal(WreckGenerator.PlanetGroundRow, inWorld.GroundRow);

        var intact = StructureSources.Get("wreck")!.Build(new StructureBuildRequest(Content, SeedA)
        {
            Params = new Dictionary<string, string> { ["ship"] = design.Key },
            AsInWorld = false,
        });
        AssertGrid(intact, hull.Width, hull.Height, hull.Length, 0, hull.IntactAt, hull.IntactModifierAt, hull.IntactShapeAt);

        // A ship without a layout crashes as the box on every generation.
        var box = WreckGenerator.Generate(Content.GetShip("starter")!, SeedA, Content);
        AssertGrid(Build("wreck", SeedA, ("ship", "starter")), box.Width, box.Height, box.Length, 0, box.Get);
    }

    [Fact]
    public void TextTablet_IsTheServersTablet_WithItsLampsAsMarkers()
    {
        ushort B(string key, string fallback) => (Content.GetBlock(key) ?? Content.GetBlock(fallback))?.NumericId.Value ?? 1;
        var lines = new List<string> { "KuBa", "West" };
        var tablet = TextMonumentGenerator.Build(lines, B("basalt", "stone"), B("concrete", "stone"), B("gold_block", "concrete"));
        var b = Build("text_tablet", SeedA, ("lines", "KuBa/West"));
        AssertGrid(b, tablet.Width, tablet.Height, tablet.Depth, 0, tablet.Get);
        Assert.NotEmpty(b.Markers);
        Assert.All(b.Markers, m => Assert.True(m.Type == "lamp" && tablet.IsLamp(m.X, m.Y, m.Z)));
    }

    [Fact]
    public void Landmark_IsTheGeneratorsLandmark_RuinedAsTheTemplateSays_IntactWhenAsked()
    {
        var template = Content.Landmarks[0];
        var layout = LandmarkGenerator.Build(template, SeedB, 1, Content, "stone");
        var b = Build("landmark", SeedB, ("template", template.Key), ("turns", "1"));
        AssertSame(b, layout.Structure);
        Assert.Equal(layout.Structure.Markers.Count, b.Markers.Count);
        Assert.Equal(layout.GroundRow, b.GroundRow);
        Assert.Equal(layout.CavityCount, b.Cavity.Count);

        var whole = LandmarkGenerator.Build(template, SeedB, 1, Content, "stone", ruin: false);
        AssertSame(StructureSources.Get("landmark")!.Build(new StructureBuildRequest(Content, SeedB)
        {
            Params = new Dictionary<string, string> { ["template"] = template.Key, ["turns"] = "1", ["ruin"] = "yes" },
            AsInWorld = false,
        }), whole.Structure);
    }

    [Fact]
    public void Station_And_StationKit_AreTheirGenerators()
    {
        AssertSame(Build("station", SeedA, ("tier", "large")), StationGenerator.Generate("large", SeedA, Content));

        var kit = Content.KitsFor(StructureKit.KindStation, null, null, null).First();
        var composed = StationKitComposer.Compose(kit, key => Content.TemplateByKey(StructureKit.KindStation, key), SeedB, Content, out _, out _);
        Assert.NotNull(composed);
        AssertSame(Build("station_kit", SeedB, ("kit", kit.Key)), composed!);
    }

    [Fact]
    public void Derelict_IsTheGeneratorsDerelict_WithItsSpotsAsMarkers()
    {
        foreach (var (variant, layout) in new[]
                 {
                     ("beta", DerelictGenerator.Generate(SeedA, Content)),
                     ("legacy", DerelictGenerator.GenerateLegacy(SeedA, Content)),
                 })
        {
            var b = Build("derelict", SeedA, ("variant", variant));
            AssertGrid(b, layout.Width, layout.Height, layout.Length, 0, layout.Get, layout.GetModifier, layout.GetShape);
            var expected = layout.Doors.Select(d => new BuiltMarker("door", d.Cell.X, d.Cell.Y, d.Cell.Z, d.AxisX ? "x" : "z"))
                .Append(new BuiltMarker("arrival", layout.Arrival.X, layout.Arrival.Y, layout.Arrival.Z))
                .Concat(layout.CrateSpots.Select(c => new BuiltMarker("crate", c.X, c.Y, c.Z)))
                .Concat(layout.SpawnerSpot is { } s ? new[] { new BuiltMarker("guardian_spawner", s.X, s.Y, s.Z) } : Array.Empty<BuiltMarker>())
                .Concat(layout.TraderPosts.Select(t => new BuiltMarker("trader_post", t.X, t.Y, t.Z)));
            AssertMarkers(expected, b.Markers);
        }

        var guaranteed = DerelictGenerator.GenerateLegacy(SeedB, Content, proceduralOnly: true);
        var g = Build("derelict", SeedB, ("variant", "legacy"), ("guaranteed", "true"));
        AssertGrid(g, guaranteed.Width, guaranteed.Height, guaranteed.Length, 0, guaranteed.Get, guaranteed.GetModifier, guaranteed.GetShape);
    }

    [Fact]
    public void AncientRuin_IsTheComposersGdsRuin_WithBasinsAndOreBelowTheFloor()
    {
        var kit = Content.KitByKey("ruin_gds_1")!;
        Assert.True(CityLayoutSpec.TryParse(CityLayoutSpec.FromKit(kit).Serialize(), out var spec));
        var cross = AncientRuinGenerator.EntranceCrossings(spec)
            .OrderBy(c => StableHashing.Unit($"gds_ruin:cross:{c.I}:{c.J}", SeedA)).First();
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var layout = AncientRuinGenerator.Compose(SeedA, Content, spec, kit, pool, new List<string>(), cross.I, cross.J,
            terrainGeneration: WorldDescription.CurrentTerrainGeneration); // the request's default generation (#3516)

        var b = Build("ancient_ruin", SeedA);
        int shift = b.GroundRow;
        Assert.Equal(layout.BasinColumns.Count > 0 ? AncientRuinGenerator.BasinDepth + 1 : 0, shift);
        AssertGrid(b, layout.Width, layout.Height, layout.Length, shift, layout.Get, layout.GetModifier, layout.GetShape);
        ushort ressaw = Content.GetBlock("ressaw")!.NumericId.Value;
        Assert.All(layout.BasinColumns, c => Assert.Equal(ressaw, b.Get(c.X, shift - 1, c.Z)));
        ushort ore = Content.GetBlock("ressaw_crystal_ore")!.NumericId.Value;
        Assert.All(layout.OreCells, c => Assert.Equal(ore, b.Get(c.X, c.Y + shift, c.Z)));

        var caches = b.Markers.Where(m => m.Type == AncientRuinGenerator.CacheMarker).ToList();
        Assert.Equal(layout.Caches.Select((c, n) => new BuiltMarker(AncientRuinGenerator.CacheMarker, c.X, c.Y + shift, c.Z, n.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            caches);
        Assert.Contains(b.Markers, m => m.Type == "entrance" && m.X == layout.Shaft.X && m.Z == layout.Shaft.Z);
    }
}
