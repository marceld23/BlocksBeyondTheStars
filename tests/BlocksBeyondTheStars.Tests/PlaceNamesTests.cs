// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2952 (a player's wish): every player can give the star systems, planets and moons they have visited a name of their
/// own. The name is private — it lives in that player's state alone — and the generated name stays the place's name for
/// everyone else and for every key derived from it.
/// </summary>
public sealed class PlaceNamesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlaceNamesTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_places_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static CelestialBody Here(SvGameServer server) => server.Galaxy.FindBody(server.ActiveLocationId)!;

    [Fact]
    public void NamingTheWorldYouStandOn_KeepsTheNameForYouAlone()
    {
        var server = Started("mine", out var repo);
        using (repo)
        {
            var justus = server.AddLocalPlayer("Justus");
            var friend = server.AddLocalPlayer("Friend");
            var here = Here(server);
            string generated = here.Name;

            server.SetPlaceNameForTest(justus, here.Id, "Mon");

            Assert.Equal("Mon", justus.State.PlaceNames[here.Id]);
            Assert.Empty(friend.State.PlaceNames);
            Assert.Equal(generated, here.Name); // the place keeps its name for everyone else — and as every key's source

            server.SetPlaceNameForTest(friend, here.Id, "Darius II");
            Assert.Equal("Mon", justus.State.PlaceNames[here.Id]);
            Assert.Equal("Darius II", friend.State.PlaceNames[here.Id]);
        }
    }

    [Fact]
    public void AWorldNeverVisited_CannotBeNamed_NorAnAsteroidField()
    {
        var server = Started("unvisited", out var repo);
        using (repo)
        {
            var justus = server.AddLocalPlayer("Justus");
            var here = Here(server);
            var far = server.Galaxy.Systems.SelectMany(s => s.Bodies)
                .First(b => b.Id != here.Id && b.Kind is CelestialKind.Planet or CelestialKind.Moon && !justus.State.LandedBodies.Contains(b.Id));

            server.SetPlaceNameForTest(justus, far.Id, "Fernweh");
            Assert.False(justus.State.PlaceNames.ContainsKey(far.Id), "a world never visited was named");

            var field = server.Galaxy.Systems.SelectMany(s => s.Bodies).FirstOrDefault(b => b.Kind == CelestialKind.AsteroidField);
            if (field is not null)
            {
                justus.State.LandedBodies.Add(field.Id);
                server.SetPlaceNameForTest(justus, field.Id, "Steinhaufen");
                Assert.False(justus.State.PlaceNames.ContainsKey(field.Id), "only planets and moons carry a private name");
            }

            server.SetPlaceNameForTest(justus, "no-such-place", "Nirgendwo");
            Assert.Empty(justus.State.PlaceNames);
        }
    }

    [Fact]
    public void TheSystemYouAreIn_CanBeNamed_AndAnEmptyOrTheRealName_GivesTheRealNameBack()
    {
        var server = Started("system", out var repo);
        using (repo)
        {
            var justus = server.AddLocalPlayer("Justus");
            var system = server.Galaxy.Systems.Single(s => s.Id == Here(server).SystemId);

            server.SetPlaceNameForTest(justus, system.Id, "Sonnenreich");
            Assert.Equal("Sonnenreich", justus.State.PlaceNames[system.Id]);

            server.SetPlaceNameForTest(justus, system.Id, "   ");
            Assert.False(justus.State.PlaceNames.ContainsKey(system.Id));

            server.SetPlaceNameForTest(justus, system.Id, "Sonnenreich");
            server.SetPlaceNameForTest(justus, system.Id, system.Name);
            Assert.False(justus.State.PlaceNames.ContainsKey(system.Id), "the generated name is no private name");
        }
    }

    [Fact]
    public void TheName_LosesControlCharacters_AndIsCutToTwentyFour()
    {
        var server = Started("clean", out var repo);
        using (repo)
        {
            var justus = server.AddLocalPlayer("Justus");
            var here = Here(server);

            server.SetPlaceNameForTest(justus, here.Id, "  Mon\u0007\nde\tLuxe und noch viel mehr Text  ");

            string name = justus.State.PlaceNames[here.Id];
            Assert.True(name.Length <= SvGameServer.PlaceNameMaxLength, name);
            Assert.DoesNotContain(name, char.IsControl);
            Assert.StartsWith("Mon", name);
            Assert.Equal(name.Trim(), name);
        }
    }

    [Fact]
    public void ThePrivateNames_SurviveASaveAndALoad()
    {
        var server = Started("saved", out var repo);
        using (repo)
        {
            var justus = server.AddLocalPlayer("Justus");
            var here = Here(server);
            server.SetPlaceNameForTest(justus, here.Id, "Mon");

            var loaded = repo.LoadPlayer(justus.State.PlayerId);
            Assert.NotNull(loaded);
            Assert.Equal("Mon", loaded!.PlaceNames[here.Id]);
        }
    }
}
