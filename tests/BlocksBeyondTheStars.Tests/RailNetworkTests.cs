// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The rail network's parts without a world (#3985): a place list's rail lines checked at load (an unknown city, fewer than
/// two stops, a city with fewer rail-hall squares than lines through it), Earth's two lines in the data, a city hall's place
/// on a synthetic district map (the street beside its square, open as far as the line needs it, the cheapest way toward the
/// neighbours, one hall per square), the side-platform and through halls, and a station record's template.
/// </summary>
public sealed class RailNetworkTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlaceDefinition City(string key, int stations = 1) => new() { Key = key, Kind = PlaceListKinds.City, Stations = stations };

    private static PlaceDefinition Line(string key, params string[] stops)
        => new() { Key = key, Kind = PlaceListKinds.Rail, NameKey = "rail." + key + ".name", Stops = stops.Select(s => new PlaceRailStop { City = s }).ToList() };

    [Fact]
    public void TheLoadChecks_RefuseAnUnknownCity_ALoneStop_AndAMissingHallSquare()
    {
        var good = new PlaceListDefinition { Places = { City("a"), City("b", stations: 2), City("c"), Line("one", "a", "b"), Line("two", "b", "c") } };
        Assert.Empty(PlaceRules.RailProblems(good));

        var unknown = new PlaceListDefinition { Places = { City("a"), Line("one", "a", "nowhere") } };
        Assert.Contains(PlaceRules.RailProblems(unknown), p => p.Contains("'nowhere', which is no city"));

        var lone = new PlaceListDefinition { Places = { City("a"), Line("one", "a") } };
        Assert.Contains(PlaceRules.RailProblems(lone), p => p.Contains("fewer than two stops"));

        var crowded = new PlaceListDefinition { Places = { City("a"), City("b"), City("c"), Line("one", "a", "b"), Line("two", "b", "c") } };
        Assert.Contains(PlaceRules.RailProblems(crowded), p => p.Contains("'b' keeps 1 rail-hall square(s) for 2 rail lines"));

        var waypoint = Line("one", "a", "b");
        waypoint.Stops[1].Via.Add(new List<double> { 10.0 });
        Assert.Contains(PlaceRules.RailProblems(new PlaceListDefinition { Places = { City("a"), City("b"), waypoint } }), p => p.Contains("no [longitude, latitude]"));
    }

    [Fact]
    public void EarthsData_ListsTheAtlanticAndTheOrientLine_AndPassesTheChecks()
    {
        var list = Content.PlacesFor("sol_earth")!;
        var lines = list.OfKind(PlaceListKinds.Rail).ToList();
        Assert.Equal(new[] { "berlin", "paris", "london", "toronto", "new_york", "rio" }, lines[0].Stops.Select(s => s.City));
        Assert.Equal(new[] { "paris", "cairo", "tokyo", "sydney" }, lines[1].Stops.Select(s => s.City));
        Assert.Empty(PlaceRules.RailProblems(list));
        Assert.Equal(2, list.Places.Single(p => p.Key == "paris").Stations); // a hall per line, change on foot
    }

    /// <summary>A 4 × 4 city: a landmark square over the middle four districts, its one rail-hall square on the north edge.
    /// Row = Z index (row 0 the south), column = X index.</summary>
    private static CityLayoutSpec Spec(params string[] rows) => new(rows.Length, MetropolisPlanner.DistrictSize, MetropolisPlanner.Street, MetropolisPlanner.Height, rows, metro: true);

    private static readonly CityLayoutSpec Square = Spec("RRRR", "RLLR", "RLLR", "RSRR");

    [Fact]
    public void AHall_StandsBesideItsSquare_ItsTrackInTheStreet_LeavingTowardTheNeighbour()
    {
        var spec = Square;
        int box = spec.Footprint;

        // North: the line leaves along a street running north from the square, out of the box.
        var north = MetroRailHalls.Choose(spec, prev: null, next: (box / 2.0, box + 2000.0), new List<MetroRailHalls.Rect>())!;
        Assert.Equal(2, north.Heading);
        Assert.True(north.Exit.Z > box);
        Assert.NotNull(MetroRailHalls.SquareUnder(spec, north.Footprint));
        Assert.Equal(RailStationGenerator.SidePlatformWidth, north.Footprint.X1 - north.Footprint.X0 + 1);

        // South: the street east of the square runs between the landmark's districts — closed; the west one is open.
        var south = MetroRailHalls.Choose(spec, prev: null, next: (box / 2.0, -2000.0), new List<MetroRailHalls.Rect>())!;
        Assert.Equal(3, south.Heading);
        Assert.Equal(RailHallPlatforms.High, south.Platforms); // the square lies east of its street
        var (ox, _) = spec.DistrictOrigin(1, 3);
        Assert.True(south.Footprint.X0 < ox && south.Footprint.X1 >= ox, "the track in the street, the platform on the square");

        // A through station needs its street open both ways: the west one.
        var through = MetroRailHalls.Choose(spec, prev: (box / 2.0, box + 2000.0), next: (box / 2.0, -2000.0), new List<MetroRailHalls.Rect>())!;
        Assert.Equal(south.Footprint.X0, through.Footprint.X0);
        Assert.True(through.Entry.Z > box && through.Exit.Z < 0);

        // One hall per square: what the first line takes leaves nothing for a second.
        var taken = MetroRailHalls.Takes(spec, north, through: false).ToList();
        Assert.Null(MetroRailHalls.Choose(spec, prev: null, next: (box / 2.0, box + 2000.0), taken));
    }

    [Fact]
    public void ASideHall_KeepsOnePlatform_AndAThroughHall_ItsStopInTheMiddle()
    {
        for (int heading = 0; heading < 4; heading++)
        {
            var low = RailStationGenerator.Generate(heading, Content, RailHallPlatforms.Low, through: true);
            var (w, l) = RailStationGenerator.Footprint(heading, RailHallPlatforms.Low);
            Assert.Equal((w, l), (low.Width, low.Length));
            Assert.Equal(RailStationGenerator.SidePlatformWidth, heading < 2 ? l : w);
            var stop = low.Markers.Single(m => m.Type == RailStationGenerator.StopMarker).LocalPos;
            var end = low.Markers.Single(m => m.Type == RailStationGenerator.EndPylonMarker).LocalPos;
            int along = heading < 2 ? System.Math.Abs(stop.X - end.X) : System.Math.Abs(stop.Z - end.Z);
            Assert.Equal(RailStationGenerator.ThroughStopU - RailStationGenerator.EndPylonU, along); // the stop in the hall's middle
            int across = heading < 2 ? stop.Z - end.Z : stop.X - end.X;
            Assert.Equal(-2, across); // a low-side hall's stop on its low platform
        }

        // The full hall is unchanged.
        var full = RailStationGenerator.Generate(0, Content);
        Assert.Equal((RailStationGenerator.Length, RailStationGenerator.Width), (full.Width, full.Length));
    }

    [Theory]
    [InlineData("heading=2")]
    [InlineData("heading=0;platforms=low;through")]
    [InlineData("heading=3;platforms=high")]
    public void AStationTemplate_RoundTrips(string template)
    {
        Assert.True(SvGameServer.IntercityHallShape.TryParse(template, out int heading, out var shape));
        Assert.Equal(template, shape.Template(heading));
    }
}
