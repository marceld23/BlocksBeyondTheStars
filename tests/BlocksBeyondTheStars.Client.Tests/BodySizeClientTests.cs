// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2691/#2692 through the real wire: from terrain generation 26 a body's size is seeded by the world and the sky/cloud
/// families come from the mixed hash. The client never recomputes a size — it reads the server's value off the star map
/// (<see cref="NetBody.Circumference"/>, via <see cref="BodySize"/>) — and it computes the orbit tints with the generation
/// the join told it, so its views agree with the world it lands on.
/// </summary>
public sealed class BodySizeClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // the encoded wire, as on a socket: the new field must survive MessagePack
    public void TheStarMap_CarriesEveryBodysSize_AndTheWorldYouLandOnHasIt(bool passObjects)
    {
        using var h = new ClientServerHarness(Content, passObjects: passObjects);
        StarMapData? map = null;
        WorldEnvironment? env = null;
        h.Client.StarMapReceived += m => map = m;
        h.Client.WorldEnvironmentReceived += m => env = m;
        h.Join();
        Assert.NotNull(h.JoinAccepted);
        var join = h.JoinAccepted!;
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, join.TerrainGeneration);

        h.Client.SendRequestStarMap();
        Assert.True(h.PumpUntil(() => map != null && env != null, 30), "the star map and the environment must arrive");

        // Every body carries the server's size — exactly the world-aware formula, never 0.
        var bodies = map!.Systems.SelectMany(s => s.Bodies).ToList();
        foreach (var b in bodies.Where(b => b.Kind is "Planet" or "Moon" or "AsteroidField"))
        {
            var server = h.Server.Galaxy.FindBody(b.Id)!;
            int expected = WorldConstants.CircumferenceFor(server.Id,
                WorldConstants.SizeClassFor(server.Kind, server.PlanetType ?? string.Empty), server.SizeBias, join.WorldSeed, join.TerrainGeneration);
            Assert.Equal(expected, b.Circumference);
            Assert.Equal(expected, BodySize.CircumferenceOf(b));
        }

        // The world you stand on is that size, and so is the one you travel to.
        var here = bodies.Single(b => b.Id == map.ActiveLocationId);
        Assert.Equal(here.Circumference, env!.Circumference);
        Assert.Equal(here.Circumference, h.Server.World.Circumference);

        var home = map.Systems.Single(s => s.Bodies.Any(b => b.Id == here.Id));
        var planets = home.Bodies.Where(b => b.Kind == "Planet").ToList();
        if (planets.Count >= 3)
        {
            // Seeded sizes: the planets of a system no longer share one size (the classic ids gave them all the same).
            Assert.True(planets.Select(p => p.Circumference).Distinct().Count() > 1,
                string.Join(", ", planets.Select(p => $"{p.Id}={p.Circumference}")));
        }

        var other = home.Bodies.First(b => b.Id != here.Id && b.Kind is "Planet" or "Moon" && Content.GetPlanet(b.PlanetType ?? string.Empty) is { Void: false });
        h.Server.Travel(join.PlayerId, other.Id);
        Assert.Equal(other.Id, h.Server.ActiveLocationId);
        env = null;
        for (int i = 0; i < 10; i++)
        {
            h.Tick(); // the last environment received is the new world's (anything queued before the travel came first)
        }

        Assert.NotNull(env);
        Assert.Equal(other.Circumference, env!.Circumference);
        Assert.Equal(other.Circumference, h.Server.World.Circumference);

        // The orbit look the client paints for that body is the sky the server ships once you are there (#2692).
        var planet = Content.GetPlanet(other.PlanetType!)!;
        var orbit = OrbitLook.For(planet, join.WorldSeed, other.Id, join.TerrainGeneration);
        Assert.Equal(env.SkyColor, orbit.SkyRgb);
        Assert.Equal(env.CloudColor, orbit.CloudRgb);
    }

    [Fact]
    public void AnOlderServer_SendsNoSize_AndTheClientFallsBackToTheClassicOne()
    {
        var body = new NetBody { Id = "sys2-p1", Kind = "Planet", PlanetType = "rocky", SizeBias = 0.4f };
        Assert.Equal(WorldConstants.CircumferenceFor("sys2-p1", WorldConstants.WorldSizeClass.Planet, 0.4f), BodySize.CircumferenceOf(body));

        var rock = new NetBody { Id = "sys2-a0", Kind = "AsteroidField", PlanetType = "asteroid_icy" };
        Assert.Equal(WorldConstants.CircumferenceFor("sys2-a0", WorldConstants.WorldSizeClass.Asteroid), BodySize.CircumferenceOf(rock));

        var moon = new NetBody { Id = "sys2-p1-m0", Kind = "Moon", PlanetType = "rocky" };
        Assert.Equal(WorldConstants.CircumferenceFor("sys2-p1-m0", WorldConstants.WorldSizeClass.Moon), BodySize.CircumferenceOf(moon));

        // Missing from the star map: the key the view falls back to, sized classic.
        Assert.Equal(WorldConstants.CircumferenceFor("home", WorldConstants.WorldSizeClass.Planet), BodySize.CircumferenceOf(null, "home", null, null));

        // A server value always wins.
        body.Circumference = 7424;
        Assert.Equal(7424, BodySize.CircumferenceOf(body));
    }
}
