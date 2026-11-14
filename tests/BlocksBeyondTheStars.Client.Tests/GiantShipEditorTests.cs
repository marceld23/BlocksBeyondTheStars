// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3616: the town editor's "Use as: Giant ship" — the target's honest tools (<see cref="StructureStampRules"/>), its
/// "Stamping…" steps (<see cref="StampStepsDescriber"/>) and the slide doors' axis a save derives from the walls
/// (<see cref="GiantShipEditorRules"/>), checked against the shipped sample hull's authored doorway.
/// </summary>
public sealed class GiantShipEditorTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static StampDesign Design() => new() { Target = StampTarget.GiantShip, Tier = "village", Surface = "stone" };

    private static StampStep Step(List<StampStep> steps, string title) => Assert.Single(steps, s => s.Title == title);

    private static string[] Args(StampStep step) => step.Args.Select(a => a.Text).ToArray();

    // ---------------------------------------------------------------- the target

    [Fact]
    public void TheTownEditorsGiantShipUse_IsItsOwnTarget_AndNoEntry()
    {
        Assert.Equal(StampTarget.GiantShip, StructureStampRules.TargetFor(false, false, false, false, false, false, "", giantShip: true));
        Assert.Equal(StampTarget.StationWhole, StructureStampRules.TargetFor(true, false, false, false, false, false, "", giantShip: true));
        Assert.Equal(StampTarget.LandmarkEntry, StructureStampRules.TargetFor(false, false, false, true, false, false, "landmark", giantShip: true));
        Assert.False(StructureStampRules.IsEntry(StampTarget.GiantShip));
        Assert.False(StructureStampRules.IsStation(StampTarget.GiantShip));
        foreach (var t in new[] { StampTarget.SettlementEntry, StampTarget.UndergroundEntry, StampTarget.LandmarkEntry, StampTarget.StationEntry })
        {
            Assert.True(StructureStampRules.IsEntry(t)); // the explicit list still names every entry
        }
    }

    [Fact]
    public void ThePalette_ShowsTheGiantShipMarkersAndTools_Only()
    {
        var t = StampTarget.GiantShip;
        foreach (string marker in GiantShipRules.Markers)
        {
            Assert.True(StructureStampRules.ShowsInPalette(t, "marker", marker), marker);
        }

        foreach (string marker in new[] { "vendor", "npc", "den", "room", "door_hinge", "mission_board", "guard_post" })
        {
            Assert.False(StructureStampRules.ShowsInPalette(t, "marker", marker), marker);
        }

        Assert.True(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindKeep, "keep"));
        Assert.True(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindCavity, "cavity"));
        Assert.True(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindSettings, "settings"));
        Assert.False(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindZone, StructureStampRules.KindZone));
        Assert.False(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindPort, "door"));
        Assert.False(StructureStampRules.ShowsInPalette(t, StructureStampRules.KindDevice, "device"));
        Assert.False(StructureStampRules.ShowsInPalette(t, "block", MaterialTokens.Wall)); // concrete blocks only
        Assert.True(StructureStampRules.ShowsInPalette(t, "block", "iron_wall"));
    }

    [Fact]
    public void TheRelicCache_IsOfferedToAGiantShipOnly()
    {
        foreach (var t in Enum.GetValues<StampTarget>())
        {
            Assert.Equal(t == StampTarget.GiantShip, StructureStampRules.ShowsInPalette(t, "marker", StructureStampRules.RelicCacheMarker));
        }
    }

    [Fact]
    public void AGiantShipReadsChestsSoundsAndDoorAxes_NeverADenOrAVendor()
    {
        var t = StampTarget.GiantShip;
        Assert.True(StructureStampRules.ReadsMarkerSettings(t, LandmarkRules.ChestMarker));
        Assert.True(StructureStampRules.ReadsMarkerSettings(t, LandmarkRules.SoundMarker));
        Assert.True(StructureStampRules.ReadsMarkerSettings(t, GiantShipRules.DoorMarker));
        Assert.False(StructureStampRules.ReadsMarkerSettings(t, LandmarkRules.DenMarker));
        Assert.False(StructureStampRules.ReadsMarkerSettings(t, "vendor"));
        Assert.False(StructureStampRules.ReadsMarkerSettings(StampTarget.Landmark, GiantShipRules.DoorMarker)); // only a giant ship's doors

        var d = Design();
        d.KeepCells = 4;
        d.CavityCells = 2;
        d.Markers.Add(new StampMarker(GiantShipRules.DoorMarker, "axis=x"));
        d.Markers.Add(new StampMarker(LandmarkRules.ChestMarker, "table=ruin_common"));
        Assert.Empty(StructureStampRules.Warnings(d)); // keep, cavity, a door's axis and a chest's table are all read
    }

    [Fact]
    public void MaterialTokens_RefuseTheSave()
    {
        var d = Design();
        d.TokenCells[MaterialTokens.Wall] = 3;
        var refusal = StructureStampRules.Refusal(d);
        Assert.NotNull(refusal);
        Assert.Equal(("ui.ed.refuse.tokens", 3), (refusal!.Value.Key, refusal.Value.Count));
    }

    // ---------------------------------------------------------------- the steps

    [Fact]
    public void TheSteps_SayItIsAlwaysAnAbandonedOpenWreck()
    {
        var d = Design();
        d.GroundRow = 2;
        d.KeepCells = 6;
        d.Markers.Add(new StampMarker(LandmarkRules.ChestMarker, string.Empty, 3, 1, 4));
        d.Markers.Add(new StampMarker(LandmarkRules.SoundMarker, "sound=amb_hum"));
        var steps = StampStepsDescriber.Describe(d, Content);

        var seat = Step(steps, StampStepsDescriber.TitleSeat);
        Assert.Equal("ui.stamp.seat.giant_ship", seat.Text);
        Assert.Equal(new[] { "2" }, Args(seat));
        Assert.Equal("ui.stamp.turn.giant_ship", Step(steps, StampStepsDescriber.TitleTurn).Text);
        Assert.Equal("ui.stamp.furnish.never", Step(steps, StampStepsDescriber.TitleFurnish).Text);
        var ruin = Step(steps, StampStepsDescriber.TitleRuin);
        Assert.Equal("ui.stamp.ruin.giant_ship", ruin.Text);
        Assert.Equal(new[] { "50", "6" }, Args(ruin)); // the crash and hull profiles' own strength
        Assert.Equal("ui.stamp.residents.giant_ship", Step(steps, StampStepsDescriber.TitleResidents).Text);
        Assert.Equal("ui.stamp.protection.giant_ship", Step(steps, StampStepsDescriber.TitleProtection).Text);
        Assert.Equal(new[] { "1", "0" }, Args(Step(steps, StampStepsDescriber.TitleLife)));
        string keepClear = Content.StructureTypes.Get(StructureTypeRules.GiantWreck).KeepClear.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(new[] { keepClear }, Args(Step(steps, StampStepsDescriber.TitleKeepClear)));

        // A chest without settings loots the default table, as in a landmark.
        var chests = Assert.IsType<StampListStep>(Step(steps, StampStepsDescriber.TitleChests));
        Assert.Equal(new[] { "3, 1, 4", "100", StampStepsDescriber.DefaultLootTable }, Args(Assert.Single(chests.Lines)));
    }

    [Fact]
    public void TheRuinStep_NamesTheTemplatesOwnStrength()
    {
        var d = Design();
        d.RuinStrength = 0.3;
        Assert.Equal(new[] { "30", "0" }, Args(Step(StampStepsDescriber.Describe(d, Content), StampStepsDescriber.TitleRuin)));
    }

    // ---------------------------------------------------------------- the doors' axis

    /// <summary>A wall of solid cells, 6 high, along the plane <paramref name="axis"/> = 5 from 0 to 9 on the other axis, with a
    /// 2-wide, 3-high doorway whose low end sits at 3.</summary>
    private static Func<int, int, int, bool> WallWithDoorway(char axis)
    {
        var solid = new HashSet<Vector3i>();
        for (int along = 0; along < 10; along++)
        {
            for (int y = 0; y < 6; y++)
            {
                bool opening = (along == 3 || along == 4) && y < 3;
                if (!opening)
                {
                    solid.Add(axis == 'x' ? new Vector3i(5, y, along) : new Vector3i(along, y, 5));
                }
            }
        }

        return (x, y, z) => solid.Contains(new Vector3i(x, y, z));
    }

    [Fact]
    public void TheWalls_TellTheDoorsAxis()
    {
        Assert.Equal("x", GiantShipEditorRules.DoorAxis(WallWithDoorway('x'), 5, 0, 3)); // the wall is the plane x = 5
        Assert.Equal("z", GiantShipEditorRules.DoorAxis(WallWithDoorway('z'), 3, 0, 5)); // the wall is the plane z = 5
        Assert.Null(GiantShipEditorRules.DoorAxis((_, _, _) => false, 0, 0, 0)); // a door standing free tells nothing
        Assert.Null(GiantShipEditorRules.DoorAxis((_, _, _) => true, 0, 0, 0)); // nor one walled in on every side
    }

    [Fact]
    public void ASave_KeepsAnAuthoredAxis_AndFillsAMissingOne()
    {
        var wall = WallWithDoorway('z');
        Assert.Equal("axis=x", GiantShipEditorRules.WithDoorAxis("axis=x", wall, 3, 0, 5, out bool unclear)); // the author's word wins
        Assert.False(unclear);
        Assert.Equal("axis=z", GiantShipEditorRules.WithDoorAxis(string.Empty, wall, 3, 0, 5, out unclear));
        Assert.False(unclear);
        Assert.Equal("axis=z", GiantShipEditorRules.WithDoorAxis("axis=y", wall, 3, 0, 5, out _)); // an invalid word is replaced
        Assert.Equal(string.Empty, GiantShipEditorRules.WithDoorAxis(null, (_, _, _) => false, 0, 0, 0, out unclear));
        Assert.True(unclear);
    }

    /// <summary>The derivation agrees with the shipped sample hull's authored doorway (A2's <c>axis=x</c> in the x = 20 wall)
    /// — the rule the derelict decay and <see cref="GiantShipRules.Problems"/> read.</summary>
    [Fact]
    public void TheDerivedAxis_MatchesTheSampleHullsAuthoredDoorway()
    {
        var hulk = Assert.IsType<StructureTemplate>(Content.GetGiantShip("sample_hulk"));
        var solid = new HashSet<Vector3i>();
        TemplateBody.ForEachPlain(new StructureTemplate { Palette = hulk.Palette, Rows = hulk.Rows }, p =>
        {
            if (!p.Cavity)
            {
                solid.Add(new Vector3i(p.X, p.Y, p.Z));
            }
        });

        var doors = hulk.Cells.Where(c => c.Kind == "marker" && c.Id == GiantShipRules.DoorMarker).ToList();
        Assert.NotEmpty(doors);
        foreach (var door in doors)
        {
            string authored = MarkerSettings.Parse(door.Config).Get(GiantShipRules.AxisSetting);
            Assert.Equal(authored, GiantShipEditorRules.DoorAxis((x, y, z) => solid.Contains(new Vector3i(x, y, z)), door.X, door.Y, door.Z));
        }
    }
}
