// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3075 through the real wire: a hyperjump in flight brings the new system's star map but no new environment — the
/// environment keeps describing the world last landed on. The flight view's star (<see cref="FlightStar"/>) comes from the
/// star map, so it is the new system's star.
/// </summary>
public sealed class FlightStarClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void AfterAJumpInFlight_TheFlightStarIsTheNewSystems_NotTheLastWorldsSun()
    {
        using var h = new ClientServerHarness(Content, c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.PlaceStarterShip = true;
        });
        StarMapData? map = null;
        WorldEnvironment? env = null;
        SpaceState? space = null;
        h.Client.StarMapReceived += m => map = m;
        h.Client.WorldEnvironmentReceived += e => env = e;
        h.Client.SpaceStateReceived += s => space = s;
        h.Join();
        h.Server.Ship.Modules.Add("jump_generator");
        h.Client.SendRequestStarMap();
        Assert.True(h.PumpUntil(() => map != null && env != null, 30), "star map and environment must arrive");

        int landedSun = env!.SunColor;
        Assert.Equal(landedSun, FlightStar.ColorOf(map, landedSun)); // on the ground both name the same star (#1604)
        var home = FlightStar.SystemOf(map, map!.ActiveLocationId);
        Assert.NotNull(home);
        var target = map.Systems.First(s => s.Id != home!.Id && s.StarColor != home.StarColor && s.Bodies.Length > 0);

        h.Client.SendEnterSpace();
        Assert.True(h.PumpUntil(() => space != null, 30), "the flight must open");
        map = null;
        h.Client.SendHyperjumpSystem(target.Id);
        Assert.True(h.PumpUntil(() => map != null && FlightStar.SystemOf(map, map.ActiveLocationId)?.Id == target.Id, 30),
            "the jump must bring the new system's star map");

        Assert.Equal(landedSun, env!.SunColor); // the environment still describes the world last landed on …
        Assert.Equal(target.StarColor, FlightStar.ColorOf(map, env.SunColor)); // … the flight star is the new system's
    }

    [Fact]
    public void WithoutAKnownBodyOrStarColour_TheEnvironmentsSunStays()
    {
        var map = new StarMapData
        {
            ActiveLocationId = "shipint:Pilot",
            Systems = new[] { new NetStarSystem { Id = "sys0", Name = "Kel", Bodies = new[] { new NetBody { Id = "b0" } }, StarColor = 0xAFC3FF } },
        };
        Assert.Equal(0xFFF1CE, FlightStar.ColorOf(map, 0xFFF1CE)); // a location the map does not carry
        Assert.Equal(0xFFF1CE, FlightStar.ColorOf(null, 0xFFF1CE));

        map.ActiveLocationId = "b0";
        Assert.Equal(0xAFC3FF, FlightStar.ColorOf(map, 0xFFF1CE));
        map.Systems[0].StarColor = 0; // an older server sends no star colour
        Assert.Equal(0xFFF1CE, FlightStar.ColorOf(map, 0xFFF1CE));
    }
}
