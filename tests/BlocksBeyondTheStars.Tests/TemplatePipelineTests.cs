// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The template side of the structure pipeline without a server (#3518, #3519): generator entries load, are checked by the
/// source registry and join the pools after the existing entries; a whole settlement template turns, carries its marker
/// settings and falls with its keep cells standing; a station template resolves its material tokens; the old paths stay as
/// they were.
/// </summary>
public sealed class TemplatePipelineTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_templatepipe_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    // ---------------------------------------------------------------- generator entries: load and check

    [Fact]
    public void AGeneratorEntry_LoadsWithoutCells_AndTheRegistryDropsWhatItCannotBuild()
    {
        string user = Path.Combine(_root, "usercontent");
        WriteTemplate(user, "settlement_templates", "camp_entry", """{ "key": "camp_entry", "tier": "village", "source": "bandit_camp", "weight": 3 }""");
        WriteTemplate(user, "settlement_templates", "monument_entry", """{ "key": "monument_entry", "tier": "town", "source": "monument", "sourceParams": "archetype=nope" }""");
        WriteTemplate(user, "settlement_templates", "ghost_entry", """{ "key": "ghost_entry", "tier": "village", "source": "no_such_source" }""");
        WriteTemplate(user, "settlement_templates", "space_entry", """{ "key": "space_entry", "tier": "village", "source": "station" }""");
        WriteTemplate(user, "settlement_templates", "celled_entry",
            """{ "key": "celled_entry", "tier": "village", "source": "bandit_camp", "width": 1, "height": 1, "length": 1, "cells": [ { "x": 0, "y": 0, "z": 0, "kind": "block", "id": "stone" } ] }""");
        WriteTemplate(user, "station_templates", "station_entry", """{ "key": "station_entry", "tier": "large", "source": "station", "sourceParams": "tier=large" }""");
        WriteTemplate(user, "landmark_templates", "tower_entry", """{ "key": "tower_entry", "source": "monument", "ruin": "always" }""");

        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), user, warnings.Add);
        Assert.Contains(warnings, w => w.Contains("celled_entry", StringComparison.Ordinal)); // the shape is checked at load

        int dropped = SourceTemplates.Validate(content, warnings.Add);
        Assert.Equal(3, dropped);
        Assert.Contains(warnings, w => w.Contains("ghost_entry", StringComparison.Ordinal) && w.Contains("unknown source", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("monument_entry", StringComparison.Ordinal) && w.Contains("archetype", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("space_entry", StringComparison.Ordinal));
        Assert.Equal(0, SourceTemplates.Validate(content, warnings.Add)); // idempotent

        var camp = Assert.Single(content.SourceTemplatesFor(StructureKit.KindSettlement, "village", null, null));
        Assert.Equal("camp_entry", camp.Key);
        Assert.Same(camp, content.SourceTemplateByKey(StructureKit.KindSettlement, "camp_entry"));
        Assert.Equal("station_entry", Assert.Single(content.SourceTemplatesFor(StructureKit.KindStation, "large", null, null)).Key);
        Assert.Equal("tower_entry", Assert.Single(content.LandmarksFor(null, null, true, true, sources: true)).Key);

        // The existing tables never see an entry: the complete templates, the legacy pick, the key lookups, the authored landmarks.
        Assert.DoesNotContain(content.CompleteTemplatesFor(StructureKit.KindSettlement, "village", null, null), t => t.IsSource);
        Assert.Null(content.SettlementTemplateByKey("camp_entry"));
        Assert.DoesNotContain(content.LandmarksFor(null, null, true, true), t => t.IsSource);
        for (int seed = 0; seed < 50; seed++)
        {
            Assert.False(content.PickSettlementTemplate("village", null, new Random(seed))?.IsSource ?? false);
        }
    }

    [Fact]
    public void AKitEntry_NamingAGeneratorEntry_IsDropped()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var templates = content.SettlementTemplates.ToList();
        templates.Add(new StructureTemplate { Key = "camp_entry", Tier = "village", Source = "bandit_camp" });
        content.SetStructureTemplates(content.StationTemplates, templates);
        var kit = new StructureKit { Key = "kit_with_entry", Kind = StructureKit.KindSettlement, Tier = "village" };
        kit.Entries.Add(new KitEntry { Module = "camp_entry" });
        var warnings = new List<string>();
        content.SetStructureKits(content.StructureKits.Concat(new[] { kit }).ToList(), warnings.Add);

        Assert.Empty(content.KitByKey("kit_with_entry")!.Entries);
        Assert.Contains(warnings, w => w.Contains("generator entry", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the draw

    [Fact]
    public void TheJointPick_WithoutEntries_DrawsExactlyAsBefore_AndEntriesFollowTheKits()
    {
        var templates = Content.CompleteTemplatesFor(StructureKit.KindSettlement, "village", null, null);
        var kits = Content.KitsFor(StructureKit.KindSettlement, "village", null, null);
        Assert.NotEmpty(kits); // the premise: the shipped village table has kits
        for (int seed = 0; seed < 200; seed++)
        {
            bool coin = seed % 3 == 0;
            var before = SvGameServer.PickTemplateOrKitForTest(templates, kits, coin, new Random(seed));
            var now = SvGameServer.PickTemplateKitOrSourceForTest(templates, kits, Array.Empty<StructureTemplate>(), coin, new Random(seed));
            Assert.Equal((before.Template, before.Kit, string.Empty), now);
        }

        // An entry heavier than every kit together is drawn most of the time when the coin does not ask for a template.
        var entry = new StructureTemplate { Key = "heavy_entry", Tier = "village", Source = "bandit_camp", Weight = 1000 };
        int entries = 0, withTemplateCoin = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            entries += SvGameServer.PickTemplateKitOrSourceForTest(templates, kits, new[] { entry }, false, new Random(seed)).Source.Length > 0 ? 1 : 0;
            withTemplateCoin += SvGameServer.PickTemplateKitOrSourceForTest(templates, kits, new[] { entry }, true, new Random(seed)).Source.Length > 0 ? 1 : 0;
        }

        Assert.InRange(entries, 150, 200);
        Assert.Equal(templates.Count > 0 ? 0 : entries, withTemplateCoin); // the template branch is unchanged
    }

    // ---------------------------------------------------------------- whole settlement templates (generation 38)

    [Fact]
    public void AWholeTemplate_WithoutAStamp_BuildsAsBefore()
    {
        foreach (var t in Content.SettlementTemplates.Where(t => !t.IsModule && !t.Underground))
        {
            var before = SettlementGenerator.FromTemplate(t, Content);
            var stamped = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp { MarkerSettings = false });
            Assert.Equal(StructureGoldens.Settlement(Content, before), StructureGoldens.Settlement(Content, stamped));
        }
    }

    [Fact]
    public void ATurnedTemplate_CarriesItsMarkersAndTheirSettings_WithTheCells()
    {
        var t = VillageWithChest(out var chest, "table=ruin_common;chance=1");
        t.Cells.RemoveAll(c => c.Kind == "marker" && c.Id == SettlementGenerator.RoomMarker); // no furniture: the bodies compare 1:1
        var plain = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp());
        Assert.Equal("table=ruin_common;chance=1", plain.Markers.Single(m => m.Type == LandmarkRules.ChestMarker).Data);

        for (int turns = 1; turns < 4; turns++)
        {
            var turned = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp { Turns = turns });
            var marker = turned.Markers.Single(m => m.Type == LandmarkRules.ChestMarker);
            var (x, z) = Turn(chest.X, chest.Z, t.Width, t.Length, turns);
            Assert.Equal(new Vector3i(x, chest.Y, z), marker.LocalPos);
            Assert.Equal((turns & 1) == 1 ? (t.Length, t.Width) : (t.Width, t.Length), (turned.Width, turned.Length));
            Assert.Equal(Count(plain), Count(turned)); // the same body, turned
        }
    }

    [Fact]
    public void AFallenTemplate_KeepsItsKeepCellsAndOnlyTheMarkersARuinKeeps()
    {
        var t = VillageWithChest(out _, "table=ruin_common");
        var walls = t.Cells.Where(c => c.Kind == "block" && c.Y >= 2).ToList();
        Assert.NotEmpty(walls);
        foreach (var c in walls)
        {
            c.Keep = true; // every upper cell stands
        }

        var intact = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp());
        var fallen = SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp { Ruined = true, Seed = 77, Surface = "grass" });
        Assert.True(fallen.Ruined);
        Assert.Equal(string.Empty, fallen.Inhabitant);
        foreach (var c in walls)
        {
            Assert.Equal(intact.Get(c.X, c.Y, c.Z), fallen.Get(c.X, c.Y, c.Z));
        }

        Assert.All(fallen.Markers, m => Assert.True(RuinMarkerRules.IsKept(m.Type), m.Type));
        Assert.Contains(fallen.Markers, m => m.Type == LandmarkRules.ChestMarker);

        // Without keep cells the pass takes from the upper rows — the template really falls.
        var bare = VillageWithChest(out _, string.Empty);
        var bareFallen = SettlementGenerator.FromTemplate(bare, Content, null, new TemplateStamp { Ruined = true, Seed = 77, Surface = "grass" });
        Assert.True(Count(bareFallen) < Count(intact) || !SameBlocks(intact, bareFallen));
        Assert.Equal(StructureGoldens.Settlement(Content, fallen),
            StructureGoldens.Settlement(Content, SettlementGenerator.FromTemplate(t, Content, null, new TemplateStamp { Ruined = true, Seed = 77, Surface = "grass" })));
    }

    // ---------------------------------------------------------------- station templates

    [Fact]
    public void AStationTemplate_ResolvesItsTokens_FromGeneration38_AndAnUnknownTokenIsReported()
    {
        var t = StationWithTokens(Clone(Content.StationTemplates.First(s => s.Key == "pocket_waystation")), out var wall, out var floor);
        t.Key = "test_station";
        var old = StationGenerator.FromTemplate(t, Content);
        Assert.Equal(0, old.Get(wall.X, wall.Y, wall.Z)); // before generation 38 a token stayed air

        var now = StationGenerator.FromTemplate(t, Content, ModuleMaterials.ForStation(Content), markerSettings: true);
        Assert.Equal(Content.GetBlock("iron_wall")!.NumericId.Value, now.Get(wall.X, wall.Y, wall.Z));
        Assert.Equal(Content.GetBlock("steel_floor")!.NumericId.Value, now.Get(floor.X, floor.Y, floor.Z));
        Assert.Equal("sound=amb_sounder;radius=20", now.Markers.Single(m => m.Type == LandmarkRules.SoundMarker).Data);

        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "block", Id = "@wal" });
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStructureTemplates(content.StationTemplates.Concat(new[] { t }).ToList(), content.SettlementTemplates, warnings.Add);
        Assert.Contains(warnings, w => w.Contains(t.Key, StringComparison.Ordinal) && w.Contains("@wal", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- helpers

    private void WriteTemplate(string user, string folder, string name, string json)
    {
        string dir = Path.Combine(user, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".json"), json);
    }

    /// <summary>A deep copy of a shipped template (cells included), so a test may change it.</summary>
    internal static StructureTemplate Clone(StructureTemplate t)
    {
        var copy = TemplateTransform.RotateY(TemplateTransform.RotateY(t, 2), 2); // four quarter turns: a fresh copy
        copy.Tier = t.Tier;
        return copy;
    }

    /// <summary>The shipped river_hamlet_home with a chest marker (settings <paramref name="config"/>) on a free floor cell.</summary>
    internal static StructureTemplate VillageWithChest(out Vector3i chest, string config, GameContent? content = null)
    {
        var t = Clone((content ?? Content).SettlementTemplates.First(s => s.Key == "river_hamlet_home"));
        t.Key = "test_village";
        chest = FreeFloorCell(t);
        t.Cells.Add(new TemplateCell { X = chest.X, Y = chest.Y, Z = chest.Z, Kind = "marker", Id = LandmarkRules.ChestMarker, Config = config });
        return t;
    }

    /// <summary>A free cell inside the template that stands on a block, nearest the centre.</summary>
    internal static Vector3i FreeFloorCell(StructureTemplate t)
    {
        var taken = new HashSet<(int, int, int)>(t.Cells.Select(c => (c.X, c.Y, c.Z)));
        var blocks = new HashSet<(int, int, int)>(t.Cells.Where(c => c.Kind == "block").Select(c => (c.X, c.Y, c.Z)));
        return Enumerable.Range(1, t.Width - 2)
            .SelectMany(x => Enumerable.Range(1, t.Length - 2).Select(z => (x, z)))
            .SelectMany(p => Enumerable.Range(1, t.Height - 2).Select(y => new Vector3i(p.x, y, p.z)))
            .Where(c => !taken.Contains((c.X, c.Y, c.Z)) && !taken.Contains((c.X, c.Y + 1, c.Z)) && blocks.Contains((c.X, c.Y - 1, c.Z)))
            .OrderBy(c => Math.Abs(c.X - (t.Width / 2)) + Math.Abs(c.Z - (t.Length / 2)))
            .ThenBy(c => c.Y)
            .First();
    }

    /// <summary>The station template with its first iron wall a <c>@wall</c> token, its first steel floor a <c>@floor</c>
    /// token and a sound marker on a free floor cell.</summary>
    internal static StructureTemplate StationWithTokens(StructureTemplate t, out Vector3i wall, out Vector3i floor)
    {
        var w = t.Cells.First(c => c.Kind == "block" && c.Id == "iron_wall");
        var f = t.Cells.First(c => c.Kind == "block" && c.Id == "steel_floor");
        w.Id = MaterialTokens.Wall;
        f.Id = MaterialTokens.Floor;
        wall = new Vector3i(w.X, w.Y, w.Z);
        floor = new Vector3i(f.X, f.Y, f.Z);
        var spot = FreeFloorCell(t);
        t.Cells.Add(new TemplateCell { X = spot.X, Y = spot.Y, Z = spot.Z, Kind = "marker", Id = LandmarkRules.SoundMarker, Config = "sound=amb_sounder;radius=20" });
        return t;
    }

    private static (int X, int Z) Turn(int x, int z, int width, int length, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            int nx = length - 1 - z;
            z = x;
            x = nx;
            (width, length) = (length, width);
        }

        return (x, z);
    }

    private static int Count(SettlementStructure s)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    n += s.Get(x, y, z) != 0 ? 1 : 0;
                }
            }
        }

        return n;
    }

    private static bool SameBlocks(SettlementStructure a, SettlementStructure b)
    {
        for (int x = 0; x < a.Width; x++)
        {
            for (int y = 0; y < a.Height; y++)
            {
                for (int z = 0; z < a.Length; z++)
                {
                    if (a.Get(x, y, z) != b.Get(x, y, z))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
