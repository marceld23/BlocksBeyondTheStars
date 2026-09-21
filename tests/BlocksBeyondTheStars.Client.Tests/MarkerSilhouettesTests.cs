// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #1975: every marker the build editors offer stands in the build as a recognisable silhouette, inside its own
/// column — never a silent fall-back to the old small cube for a marker somebody forgot.
/// </summary>
public sealed class MarkerSilhouettesTests
{
    /// <summary>The station and settlement marker palettes of the structure editor (doors aside — those are real doors).</summary>
    private static readonly string[] StructureMarkers =
    {
        "hangar", "vendor", "mission_board", "heal_tank", "quarters", "console", "npc", "greenhouse", "spawn", "cabin", "lounge", "room",
        "doctor", "grocer", "arms_dealer", "sage", "tamer", "blockfarmer", "streamer", "reporter",
        "loot", "chest", "data_terminal", "tavern", "workshop", "guard_post",
    };

    [Theory]
    [MemberData(nameof(EveryStructureMarker))]
    public void EveryStructureMarkerHasASilhouetteInItsColumn(string id)
    {
        var s = MarkerSilhouettes.For(id, MarkerSilhouettes.KindMarker);
        Assert.NotNull(s);
        Assert.False(s!.Value.KeepsCube);
        Assert.NotEmpty(s.Value.Boxes);
        foreach (var b in s.Value.Boxes)
        {
            Assert.InRange(b.Centre.X - b.Size.X * 0.5f, -1e-4f, 1f);
            Assert.InRange(b.Centre.X + b.Size.X * 0.5f, 0f, 1f + 1e-4f);
            Assert.InRange(b.Centre.Z - b.Size.Z * 0.5f, -1e-4f, 1f);
            Assert.InRange(b.Centre.Z + b.Size.Z * 0.5f, 0f, 1f + 1e-4f);
            Assert.InRange(b.Centre.Y - b.Size.Y * 0.5f, -1e-4f, 2f);
            Assert.InRange(b.Centre.Y + b.Size.Y * 0.5f, 0f, 2f + 1e-4f);
        }
    }

    public static TheoryData<string> EveryStructureMarker()
    {
        var data = new TheoryData<string>();
        foreach (var id in StructureMarkers)
        {
            data.Add(id);
        }

        return data;
    }

    [Fact]
    public void APersonStandsTwoBlocksTallAndAPlateLiesFlat()
    {
        var npc = MarkerSilhouettes.For("npc", MarkerSilhouettes.KindMarker)!.Value;
        Assert.True(npc.Boxes.Max(b => b.Centre.Y + b.Size.Y * 0.5f) > 1.5f);
        var room = MarkerSilhouettes.For("room", MarkerSilhouettes.KindMarker)!.Value;
        Assert.True(room.Boxes.Max(b => b.Centre.Y + b.Size.Y * 0.5f) < 0.2f);
    }

    [Fact]
    public void TheSameIdMeansDifferentThingsPerKind()
    {
        // "workshop" is the craftsman's post in a settlement (a person) and a bench on the ship.
        Assert.False(MarkerSilhouettes.For("workshop", MarkerSilhouettes.KindMarker)!.Value.KeepsCube);
        Assert.True(MarkerSilhouettes.For("workshop", MarkerSilhouettes.KindStation)!.Value.KeepsCube);
        // "console" is a terminal marker in a station template and a decorated block on the ship.
        Assert.False(MarkerSilhouettes.For("console", MarkerSilhouettes.KindMarker)!.Value.KeepsCube);
        Assert.True(MarkerSilhouettes.For("console", MarkerSilhouettes.KindStation)!.Value.KeepsCube);
    }

    [Theory]
    [InlineData("cockpit")]
    [InlineData("console")]
    [InlineData("lab")]
    [InlineData("medbay")]
    [InlineData("workshop")]
    public void ShipStationDecorSitsOnTheCellAboveTheBlock(string id)
    {
        var s = MarkerSilhouettes.For(id, MarkerSilhouettes.KindStation)!.Value;
        Assert.True(s.KeepsCube);
        foreach (var b in s.Boxes)
        {
            Assert.True(b.Centre.Y - b.Size.Y * 0.5f >= 1f - 1e-4f, "decor must not sink into the station block");
            Assert.True(b.Centre.Y + b.Size.Y * 0.5f <= 2.5f, "decor must stay near the cell above");
        }
    }

    [Theory]
    [InlineData("reactor")]
    [InlineData("life_support")]
    [InlineData("quarters")]
    [InlineData("cargo")]
    [InlineData("hangar")]
    [InlineData("ship_laser_basic")]
    [InlineData("ship_cannon_1")]
    public void StationsWithoutDecorKeepTheirCube(string id)
    {
        Assert.Null(MarkerSilhouettes.For(id, MarkerSilhouettes.KindStation));
    }

    [Fact]
    public void TheHatchIsAFrameAndTheOtherElementsStayCubes()
    {
        Assert.NotNull(MarkerSilhouettes.For("hatch", MarkerSilhouettes.KindElement));
        Assert.Null(MarkerSilhouettes.For("engine", MarkerSilhouettes.KindElement));
        Assert.Null(MarkerSilhouettes.For("light", MarkerSilhouettes.KindElement));
    }

    [Fact]
    public void DoorsAndBlocksAreNotSilhouettes()
    {
        Assert.Null(MarkerSilhouettes.For("door_slide", MarkerSilhouettes.KindMarker));
        Assert.Null(MarkerSilhouettes.For("door_slide", MarkerSilhouettes.KindElement));
        Assert.Null(MarkerSilhouettes.For("stone", "block"));
        Assert.Null(MarkerSilhouettes.For(null!, MarkerSilhouettes.KindMarker));
    }
}
