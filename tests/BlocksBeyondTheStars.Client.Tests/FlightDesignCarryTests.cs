// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3409 (Justus: "after a jump the stations have the OLD model", ghost asteroids, the wreck as a brown box): the
/// in-flight rebuild of a jump keeps the designs that arrived for the new flight and drops the old flight's.</summary>
public sealed class FlightDesignCarryTests
{
    private static readonly Dictionary<string, string> Designs = new()
    {
        ["sys6-st"] = "space:sys6-p0",          // the departure system's station
        ["e220"] = "space:sys6-p0",             // …and one of its rocks
        ["sys5-st"] = "space:sys5-p0",          // the arrived system's station, wreck and rocks
        ["sys5-w"] = "space:sys5-p0",
        ["e65"] = "space:sys5-p0",
        ["pstation:Screelit:0"] = string.Empty, // a design from before any instance was known
    };

    [Fact]
    public void TheArrivedFlightsDesignsAreCarried_TheOldOnesAreNot()
    {
        var carried = FlightDesignCarry.ArrivedFor(Designs, i => i, "space:sys5-p0");

        Assert.Equal(new[] { "e65", "sys5-st", "sys5-w" }, Sorted(carried));
    }

    [Fact]
    public void TheWayBackCarriesTheHomeSystemsDesigns()
    {
        var carried = FlightDesignCarry.ArrivedFor(Designs, i => i, "space:sys6-p0");

        Assert.Equal(new[] { "e220", "sys6-st" }, Sorted(carried));
    }

    [Fact]
    public void NoInstance_CarriesNothing()
    {
        Assert.Empty(FlightDesignCarry.ArrivedFor(Designs, i => i, null));
        Assert.Empty(FlightDesignCarry.ArrivedFor(Designs, i => i, string.Empty));
    }

    private static string[] Sorted(IReadOnlyList<string> ids)
    {
        var list = new List<string>(ids);
        list.Sort(System.StringComparer.Ordinal);
        return list.ToArray();
    }
}
