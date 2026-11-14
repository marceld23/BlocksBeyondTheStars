// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3607: the giant ship pool — <c>data/giant_ships/*.json</c> and <c>usercontent/giant_ship_templates/</c> load into
/// <see cref="GameContent.GiantShips"/>, a pool of its own. A giant ship is never a ship type (no <see cref="GameContent.Ships"/>
/// entry, no <see cref="GameContent.GetShip"/>, no blueprint) and no other structure pool ever offers one; a landmark never
/// becomes one.
/// </summary>
public sealed class GiantShipTemplateTests
{
    /// <summary>A small valid giant ship: a 6 × 4 × 5 iron floor with an entrance on it.</summary>
    internal static StructureTemplate Ship(string key = "gs_test")
    {
        var t = new StructureTemplate { Key = key, Width = 6, Height = 4, Length = 5 };
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                t.Cells.Add(LandmarkShapes.Block(x, 0, z, "iron_wall"));
            }
        }

        t.Cells.Add(LandmarkShapes.Marker(2, 1, 2, GiantShipRules.EntranceMarker));
        return t;
    }

    [Fact]
    public void TheSampleHulk_LoadsIntoTheGiantShipPool_AsAPinOnlyCompactShip()
    {
        var warnings = new List<string>();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), warn: warnings.Add);

        Assert.DoesNotContain(warnings, w => w.Contains("giant", StringComparison.OrdinalIgnoreCase));
        var hulk = content.GetGiantShip("sample_hulk");
        Assert.NotNull(hulk);
        Assert.True(hulk!.GiantShip);
        Assert.False(hulk.Landmark);
        Assert.Equal(GiantShipRules.TemplateKind, hulk.Kind);
        Assert.True(hulk.PinOnly);
        Assert.True(TemplateBody.IsCompact(hulk));
        Assert.Equal(GiantShipRules.NameKey("sample_hulk"), hulk.NameKey);
        Assert.Equal((40, 14, 16, 1), (hulk.Width, hulk.Height, hulk.Length, hulk.GroundRow));
        Assert.Empty(content.GiantShipProblems(hulk));

        // What the B1/B2 tests lean on: water, a tree in a planter, a doorway between two rooms, two chests (one hidden).
        var ids = new HashSet<string>(StringComparer.Ordinal);
        TemplateBody.ForEachPlain(hulk, p => ids.Add(p.Id));
        Assert.Superset(new HashSet<string> { "water", "wood_log", "tree_leaves", "glass", "iron_wall", "light_white" }, ids);
        Assert.Single(hulk.Cells, c => c.Id == GiantShipRules.EntranceMarker);
        Assert.Single(hulk.Cells, c => c.Id == GiantShipRules.DoorMarker && c.Config == "axis=x");
        Assert.Equal(2, hulk.Cells.Count(c => c.Id == LandmarkRules.ChestMarker && c.Config.Contains("table=ruin_common", StringComparison.Ordinal)));
        Assert.Single(hulk.Cells, c => c.Id == LandmarkRules.ChestMarker && MarkerSettings.Parse(c.Config).GetBool("hidden"));

        // Pin-only: never rolled, still found by key.
        Assert.DoesNotContain(content.GiantShipsFor(null, "meadowlands"), t => t.Key == "sample_hulk");
    }

    [Fact]
    public void Pool_FiltersByPackPlanetTypePinAndGeneration()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var everywhere = Ship("gs_everywhere");
        var desert = Ship("gs_desert");
        desert.PlanetTypes.Add("desert");
        var extras = Ship("gs_extras");
        extras.Pack = "extras";
        var pinned = Ship("gs_pinned");
        pinned.PinOnly = true;
        var young = Ship("gs_young");
        young.MinTerrainGeneration = 42;

        var warnings = new List<string>();
        content.SetGiantShips(new[] { everywhere, desert, extras, pinned, young }, warnings.Add);
        Assert.Empty(warnings);
        Assert.Contains("extras", content.StructurePacks);

        string[] Keys(IReadOnlyList<StructureTemplate> l) => l.Select(t => t.Key).ToArray();
        Assert.Equal(new[] { "gs_everywhere", "gs_extras", "gs_young" }, Keys(content.GiantShipsFor(null, "meadowlands")));
        Assert.Equal(new[] { "gs_everywhere", "gs_desert", "gs_extras", "gs_young" }, Keys(content.GiantShipsFor(null, "desert")));
        Assert.Equal(new[] { "gs_everywhere", "gs_desert", "gs_young" }, Keys(content.GiantShipsFor(new[] { "default" }, "desert")));
        Assert.Equal(new[] { "gs_desert" }, Keys(content.GiantShipsFor(null, "desert", namedTypeOnly: true)));
        Assert.Empty(content.GiantShipsFor(null, null, namedTypeOnly: true));
        Assert.Equal(new[] { "gs_everywhere", "gs_extras" }, Keys(content.GiantShipsFor(null, "meadowlands", generation: 41)));
        Assert.Equal(new[] { "gs_everywhere", "gs_desert", "gs_extras", "gs_young" }, Keys(content.GiantShipsFor(null, null)));
        Assert.Same(pinned, content.GetGiantShip("gs_pinned")); // a pinned record still finds it
        Assert.Null(content.GetGiantShip("gs_nothing"));
        Assert.Null(content.GetGiantShip(null));
    }

    [Fact]
    public void Pool_DropsABrokenGiantShip_WithAWarning_AndKeepsTheRest()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var good = Ship("good");
        var unknownBlock = Ship("unknown_block");
        unknownBlock.Cells.Add(LandmarkShapes.Block(1, 1, 1, "no_such_block"));
        var tooBig = Ship("too_big");
        tooBig.Length = GiantShipRules.MaxSize + 1;
        var noEntrance = Ship("no_entrance");
        noEntrance.Cells.RemoveAll(c => c.Kind == "marker");
        var badTable = Ship("bad_table");
        badTable.Cells.Add(LandmarkShapes.Marker(4, 1, 3, LandmarkRules.ChestMarker, "table=no_such_table"));
        var twin = Ship("good");
        var nullCells = Ship("null_cells");
        nullCells.Cells = null!; // a hand-edited file saying "cells": null
        var nullId = Ship("null_id");
        nullId.Palette.Add(new TemplatePaletteEntry { Id = null! });
        nullId.Rows.Add("1,1:1.1.0");

        var warnings = new List<string>();
        content.SetGiantShips(new[] { good, unknownBlock, tooBig, noEntrance, badTable, twin, new StructureTemplate(), nullCells, nullId },
            warnings.Add);

        Assert.Equal(new[] { "good" }, content.GiantShips.Select(t => t.Key));
        Assert.Equal(8, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("null_cells", StringComparison.Ordinal) && w.Contains("no body", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("null_id", StringComparison.Ordinal) && w.Contains("(empty)", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("no_such_block", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("larger than", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("no_entrance", StringComparison.Ordinal) && w.Contains("'entrance'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("no_such_table", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("already loaded", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("without a key", StringComparison.Ordinal));
    }

    /// <summary>The proof (#3607): no ship-type list and no other structure pool ever sees a giant ship.</summary>
    [Fact]
    public void NoShipTypeList_AndNoOtherPool_EverSeesAGiantShip()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var giant = content.GiantShips.Select(t => t.Key).ToList();
        Assert.Contains("sample_hulk", giant);
        foreach (string key in giant)
        {
            Assert.False(content.Ships.ContainsKey(key), key + " is a ship type");
            Assert.Null(content.GetShip(key));
            Assert.Null(content.UserShipByKey(key));
            Assert.DoesNotContain(content.Blueprints.Values, b => b.Key == key || b.Features.Contains(key));
            Assert.Null(content.LandmarkByKey(key));
            Assert.Null(content.SettlementTemplateByKey(key));
            Assert.Null(content.StationTemplateByKey(key));
            Assert.Null(content.SettlementModuleByKey(key));
        }

        Assert.DoesNotContain(content.Landmarks, t => t.GiantShip);
        Assert.DoesNotContain(content.LandmarksFor(null, "meadowlands", atmosphere: true, inhabited: true), t => t.GiantShip);
        Assert.DoesNotContain(content.StationTemplates, t => t.GiantShip);
        Assert.DoesNotContain(content.SettlementTemplates, t => t.GiantShip);
        Assert.DoesNotContain(content.SettlementModulesFor(null, null), t => t.GiantShip);
        Assert.DoesNotContain(content.UndergroundTemplatesFor(null, null), t => t.GiantShip);
        foreach (string tier in new[] { "hamlet", "village", "town", "city", "small", "medium", "large" })
        {
            Assert.DoesNotContain(content.CompleteTemplatesFor(StructureKit.KindSettlement, tier, null, null), t => t.GiantShip);
            Assert.DoesNotContain(content.CompleteTemplatesFor(StructureKit.KindStation, tier, null, null), t => t.GiantShip);
        }
    }

    [Fact]
    public void AGiantShipThatStraysIntoAnotherPool_IsLeftOut_WithAWarning()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var station = Ship("stray_station");
        station.GiantShip = true;
        var settlement = Ship("stray_settlement");
        settlement.GiantShip = true;
        settlement.Tier = "village";
        var landmark = Ship("stray_landmark");
        landmark.GiantShip = true;

        var warnings = new List<string>();
        content.SetStructureTemplates(content.StationTemplates.Append(station).ToList(),
            content.SettlementTemplates.Append(settlement).ToList(), warnings.Add);
        content.SetLandmarks(content.Landmarks.Append(landmark).ToList(), warnings.Add);

        Assert.Contains(warnings, w => w.Contains("stray_station", StringComparison.Ordinal) && w.Contains("giant ship", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("stray_settlement", StringComparison.Ordinal) && w.Contains("giant ship", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("stray_landmark", StringComparison.Ordinal) && w.Contains("giant ship", StringComparison.Ordinal));
        Assert.Null(content.StationTemplateByKey("stray_station"));
        Assert.Null(content.SettlementTemplateByKey("stray_settlement"));
        Assert.Null(content.LandmarkByKey("stray_landmark"));
        Assert.False(landmark.Landmark); // left as it was: never turned into a landmark
    }

    /// <summary>The user-content folders: a giant ship from the editor's folder joins the pool; a landmark file never
    /// becomes a giant ship (in either folder), and a giant ship in the landmark folder never becomes a landmark.</summary>
    [Fact]
    public void UserContent_GiantShipFolder_LoadsIntoThePool_AndNoLandmarkCrossesOver()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_giant_ships_" + Guid.NewGuid().ToString("N"));
        try
        {
            string ships = Path.Combine(root, GiantShipRules.UserFolder);
            string landmarks = Path.Combine(root, "landmark_templates");
            Directory.CreateDirectory(ships);
            Directory.CreateDirectory(landmarks);
            var mine = Ship(string.Empty); // the key comes from the file name
            File.WriteAllText(Path.Combine(ships, "my_cruiser.json"), System.Text.Json.JsonSerializer.Serialize(mine, ContentLoader.JsonOptions));
            var asLandmark = Ship("tower_as_ship");
            asLandmark.Landmark = true;
            File.WriteAllText(Path.Combine(ships, "tower_as_ship.json"), System.Text.Json.JsonSerializer.Serialize(asLandmark, ContentLoader.JsonOptions));
            var shipAsLandmark = Ship("ship_in_landmarks");
            shipAsLandmark.GiantShip = true;
            File.WriteAllText(Path.Combine(landmarks, "ship_in_landmarks.json"), System.Text.Json.JsonSerializer.Serialize(shipAsLandmark, ContentLoader.JsonOptions));
            File.WriteAllText(Path.Combine(ships, "broken.json"), "{ not json");

            var warnings = new List<string>();
            var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), root, warnings.Add);

            var loaded = content.GetGiantShip("my_cruiser");
            Assert.NotNull(loaded);
            Assert.True(loaded!.GiantShip);
            Assert.Contains("sample_hulk", content.GiantShips.Select(t => t.Key)); // the shipped pool is still there
            Assert.Null(content.GetGiantShip("tower_as_ship"));
            Assert.Contains(warnings, w => w.Contains("tower_as_ship", StringComparison.Ordinal) && w.Contains("landmark", StringComparison.Ordinal));
            Assert.Null(content.LandmarkByKey("ship_in_landmarks"));
            Assert.Null(content.GetGiantShip("ship_in_landmarks"));
            Assert.Contains(warnings, w => w.Contains("unreadable giant ship", StringComparison.Ordinal) && w.Contains("broken.json", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void APlanetTypeMayWhitelistTheGiantWrecks_ButNotAnUnknownKind()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var planet = content.GetPlanet("meadowlands")!;
        planet.AllowedStructures.Add(GiantShipRules.StructureKind);
        content.Validate(); // the giant wrecks are a known whitelist kind

        planet.AllowedStructures.Add("giant_wreckz");
        var e = Assert.Throws<ContentValidationException>(content.Validate);
        Assert.Contains(e.Problems, p => p.Contains("giant_wreckz", StringComparison.Ordinal));
    }
}
