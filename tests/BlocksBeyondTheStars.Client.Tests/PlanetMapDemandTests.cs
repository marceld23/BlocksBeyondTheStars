// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3979: the browser fetches the planet maps on demand. <see cref="PlanetMapDemand"/> decides which map a world needs,
/// whether it is here and when a fetch starts; <see cref="PlanetMapWait"/> and <see cref="PlanetMapTickGate"/> hold the
/// far terrain, the biome looks and the in-process server until it arrived. Built on a tiny content of its own (two map
/// types sharing one map, one procedural type) and a small synthetic map — never the real files.
/// </summary>
public sealed class PlanetMapDemandTests
{
    private const string MapKey = "test_map";

    private static GameContent NewContent() => new(
        Array.Empty<BlockDefinition>(), Array.Empty<ItemDefinition>(), Array.Empty<RecipeDefinition>(),
        Array.Empty<BlueprintDefinition>(), Array.Empty<ShipModuleDefinition>(), new Dictionary<GameLocale, Dictionary<string, string>>(),
        planets: new[]
        {
            new PlanetType { Key = "mapworld", TerrainMap = MapKey },
            new PlanetType { Key = "mapworld_twin", TerrainMap = MapKey },
            new PlanetType { Key = "rolled" },
        });

    private static byte[] MapBytes()
    {
        const int w = 8, h = 4;
        return PlanetMapCodec.Encode(new PlanetMap(w, h, new short[w * h], new byte[w * h]));
    }

    private static (PlanetMapDemand Demand, List<string> Fetches) BrowserDemand(GameContent? content = null)
    {
        var fetches = new List<string>();
        return (new PlanetMapDemand(content ?? NewContent(), fetches.Add), fetches);
    }

    private static Galaxy GalaxyOf(params string[] planetTypes)
    {
        var galaxy = new Galaxy();
        AddSystem(galaxy, planetTypes);
        return galaxy;
    }

    private static void AddSystem(Galaxy galaxy, params string[] planetTypes)
    {
        var system = new StarSystem();
        foreach (string type in planetTypes)
        {
            system.Bodies.Add(new CelestialBody { Id = "b" + system.Bodies.Count, PlanetType = type });
        }

        galaxy.Systems.Add(system);
    }

    [Theory]
    [InlineData("maps/earth.bbmap", true)]
    [InlineData("maps\\venus.bbmap", true)]
    [InlineData("/maps/mars.bbmap", true)]
    [InlineData("planets.json", false)]
    [InlineData("mapsx/earth.bbmap", false)]
    [InlineData("wiki/maps/x.png", false)]
    [InlineData(null, false)]
    public void OnlyTheMapsFolder_StaysOutOfTheEagerDownload(string? path, bool onDemand)
    {
        Assert.Equal(onDemand, PlanetMapDemand.IsOnDemandDataFile(path));
        Assert.Equal("maps/earth.bbmap", PlanetMapDemand.DataPathOf("earth"));
    }

    [Fact]
    public void WithoutAFetch_EveryMapIsReady_AndNothingIsRead()
    {
        var content = NewContent();
        int reads = 0;
        content.SetPlanetMapSource(_ => { reads++; return null; });
        var demand = new PlanetMapDemand(content, fetch: null);

        Assert.False(demand.FetchesOnDemand);
        Assert.True(demand.IsReady("mapworld")); // desktop: the content reads the file itself when the generator asks
        Assert.Equal(0, demand.Need(new[] { "mapworld", "rolled" }));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void AMissingMap_IsFetchedOnce_AndReadyOnceItArrived()
    {
        var (demand, fetches) = BrowserDemand();

        Assert.True(demand.IsReady("rolled"));       // procedural terrain needs no map
        Assert.True(demand.IsReady("no_such_type")); // unknown types neither
        Assert.True(demand.IsReady(null));
        Assert.False(demand.IsReady("mapworld"));
        Assert.False(demand.IsReady("mapworld_twin")); // the same map: no second fetch while one runs
        Assert.Equal(new[] { MapKey }, fetches);

        Assert.True(demand.Arrived(MapKey, MapBytes(), out string? error));
        Assert.Null(error);
        Assert.Equal(1, demand.Version);
        Assert.True(demand.IsReady("mapworld"));
        Assert.True(demand.IsReady("mapworld_twin"));
        Assert.NotNull(demand.Content.GetPlanetMap(MapKey));
        Assert.Single(fetches);
    }

    [Fact]
    public void AMapTheContentCanAlreadyRead_IsNotFetched()
    {
        var content = NewContent();
        byte[] bytes = MapBytes();
        content.SetPlanetMapSource(key => key == MapKey ? bytes : null); // the copy the browser kept from an earlier session
        var (demand, fetches) = BrowserDemand(content);

        Assert.True(demand.IsReady("mapworld"));
        Assert.Empty(fetches);
    }

    [Fact]
    public void ADamagedOrFailedFetch_IsTriedAgain_UpToTheLimit()
    {
        var (demand, fetches) = BrowserDemand();
        Assert.False(demand.IsReady("mapworld"));

        byte[] damaged = MapBytes();
        damaged[16] ^= 0xFF; // the content hash
        Assert.False(demand.Arrived(MapKey, damaged, out string? error));
        Assert.NotNull(error);
        Assert.Null(demand.Content.GetPlanetMap(MapKey));

        Assert.False(demand.IsReady("mapworld")); // attempt 2
        demand.Failed(MapKey);
        Assert.Equal(1, demand.Need(new[] { "mapworld", "mapworld_twin", "rolled" })); // attempt 3: one map on its way
        demand.Failed(MapKey);
        Assert.Equal(PlanetMapDemand.MaxAttempts, fetches.Count);
        Assert.Equal(3, demand.Version);

        Assert.False(demand.IsReady("mapworld")); // given up: no fourth fetch, the world keeps waiting
        Assert.Equal(PlanetMapDemand.MaxAttempts, fetches.Count);
        Assert.Equal(0, demand.Need(new[] { "mapworld" })); // ...and it no longer counts as coming
    }

    [Fact]
    public void TheStarMapsBodies_AskForTheirMaps()
    {
        var (demand, fetches) = BrowserDemand();
        var starMap = new StarMapData
        {
            Systems = new[]
            {
                new NetStarSystem { Bodies = new[] { new NetBody { PlanetType = "rolled" }, new NetBody { PlanetType = "mapworld" } } },
                new NetStarSystem { Bodies = new[] { new NetBody { PlanetType = "mapworld_twin" }, new NetBody { PlanetType = null } } },
            },
        };

        Assert.Equal(new[] { "rolled", "mapworld", "mapworld_twin", null }, PlanetMapDemand.PlanetTypesOf(starMap));
        Assert.Equal(1, demand.Need(PlanetMapDemand.PlanetTypesOf(starMap)));
        Assert.Equal(new[] { MapKey }, fetches);
        Assert.Empty(PlanetMapDemand.PlanetTypesOf((StarMapData?)null));
    }

    [Fact]
    public void AView_WaitsForItsMap_AndRetriesOnceAFetchEnded()
    {
        var (demand, _) = BrowserDemand();
        var wait = new PlanetMapWait();

        Assert.True(wait.Ready(null, "mapworld")); // no demand: desktop
        Assert.True(wait.Ready(demand, "rolled"));
        Assert.False(wait.Ready(demand, "mapworld"));
        Assert.Equal("mapworld", wait.Waiting);
        Assert.False(wait.ShouldRetry(demand)); // nothing ended yet

        demand.Arrived(MapKey, MapBytes(), out _);
        Assert.True(wait.ShouldRetry(demand));
        Assert.True(wait.Ready(demand, "mapworld"));
        Assert.Null(wait.Waiting);
        Assert.False(wait.ShouldRetry(demand));

        Assert.False(new PlanetMapWait().Ready(BrowserDemand().Demand, "mapworld"));
        var cleared = new PlanetMapWait();
        cleared.Ready(BrowserDemand().Demand, "mapworld");
        cleared.Clear();
        Assert.Null(cleared.Waiting);
    }

    [Fact]
    public void TheBrowserServer_HoldsWhileAMapOfItsGalaxyIsOnItsWay()
    {
        var (demand, fetches) = BrowserDemand();
        var gate = new PlanetMapTickGate();
        var galaxy = GalaxyOf("rolled", "rolled");

        Assert.True(new PlanetMapTickGate().MayTick(null, galaxy));
        Assert.True(new PlanetMapTickGate().MayTick(new PlanetMapDemand(NewContent(), null), GalaxyOf("mapworld")));
        Assert.True(gate.MayTick(demand, galaxy)); // no map bodies yet
        Assert.Empty(fetches);

        AddSystem(galaxy, "mapworld", "mapworld_twin"); // the galaxy grows the Sol system
        Assert.False(gate.MayTick(demand, galaxy));
        Assert.False(gate.MayTick(demand, galaxy));
        Assert.Equal(new[] { MapKey }, fetches);

        demand.Arrived(MapKey, MapBytes(), out _);
        Assert.True(gate.MayTick(demand, galaxy));
        Assert.True(gate.MayTick(demand, galaxy));
    }

    [Fact]
    public void TheBrowserServer_RunsOn_WhenAMapCannotBeFetched()
    {
        var (demand, fetches) = BrowserDemand();
        var gate = new PlanetMapTickGate();
        var galaxy = GalaxyOf("mapworld");

        for (int attempt = 1; attempt <= PlanetMapDemand.MaxAttempts; attempt++)
        {
            Assert.False(gate.MayTick(demand, galaxy));
            Assert.Equal(attempt, fetches.Count);
            demand.Failed(MapKey);
        }

        Assert.True(gate.MayTick(demand, galaxy)); // given up: a world that needs the map fails loudly instead of a frozen game
    }
}
