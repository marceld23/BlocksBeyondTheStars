// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4017: the animals of the REAL Earth (<c>sol_earth</c>, booted as a player finds it) where they belong. A player stands a game
/// minute at places of every climate zone and of the open ocean; every animal the spawner puts round them stands, when first
/// seen, in one of its own zones (an exclusive land animal) or in the sea (the dolphin and the whale), each place shows the
/// animals it must (rabbits in temperate Europe and in the tundra, elephants in the savanna, monkeys and parrots in the rain
/// forest, eagles in the high mountains, whales and dolphins at sea) and none it must not (no cat in the Sahara, the rain
/// forest or the tundra). An inland lake seats no dolphin, the open sea does; and every metropolis keeps a handful of pets — dogs
/// and cats — that walk beside their people inside the city. The zone rules themselves are <see cref="EarthFaunaZonesTests"/>.
/// </summary>
public sealed class EarthFaunaSpawnTests : IDisposable
{
    private const int Circumference = 16000;
    private const int TicksPerPlace = 240; // a game minute at 0.25 s
    private const float SeenRadius = 120f;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_earthfauna_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public EarthFaunaSpawnTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>A place, the animals it must show and the ones it must not.</summary>
    private sealed record Place(string Name, double Lon, double Lat, string[] MustSee, string[] MustNotSee);

    private static readonly string[] Land = { "dog", "cat", "rabbit", "cow", "sheep", "monkey", "elephant", "chicken" };

    private static readonly Place[] Places =
    {
        new("France, Loire countryside", 0.5, 47.0, new[] { "rabbit", "cow", "sheep", "chicken" },
            new[] { "elephant", "monkey", "parrot", "dolphin", "whale" }),
        new("Kenya savanna", 37.0, -1.0, new[] { "elephant" }, new[] { "monkey", "parrot", "dolphin", "whale" }), // its highlands hold temperate meadows
        new("Amazon rain forest", -56.0, -7.0, new[] { "monkey", "parrot" },
            new[] { "cat", "dog", "rabbit", "cow", "sheep", "chicken", "elephant", "eagle" }),
        new("Himalaya", 86.0, 28.5, new[] { "eagle" }, new[] { "monkey", "parrot", "rabbit" }),
        new("Siberian tundra", 100.0, 66.0, new[] { "rabbit" }, new[] { "cat", "dog", "cow", "sheep", "chicken", "monkey", "elephant" }),
        new("Sahara", 10.0, 24.0, Array.Empty<string>(), Land.Concat(new[] { "parrot", "eagle", "dolphin", "whale" }).ToArray()),
        new("Mid-Atlantic", -35.0, 40.0, new[] { "whale" }, Land),
        new("Caribbean", -75.0, 15.0, new[] { "whale" }, Land),
    };

    [Fact]
    [Trait("Category", "Slow")] // an Earth boot and eight places of a game minute each
    public void OnTheRealEarth_EveryAnimal_LivesWhereItBelongs_AndTheCitiesKeepPets()
    {
        var clock = Stopwatch.StartNew();
        var (server, admin) = BootOnEarth();
        _output.WriteLine($"boot {clock.ElapsedMilliseconds} ms");
        var roster = server.SpeciesRoster.ToDictionary(s => s.Id);
        var everywhere = new HashSet<string>();
        foreach (var place in Places)
        {
            var seen = StandAt(server, admin, place.Lon, place.Lat, roster);
            everywhere.UnionWith(seen.Keys);
            _output.WriteLine($"{place.Name}: {(seen.Count == 0 ? "nothing" : string.Join(", ", seen.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}")))} ({clock.ElapsedMilliseconds} ms)");
            Assert.All(place.MustSee, key => Assert.True(seen.ContainsKey(key), $"no {key} at {place.Name}"));
            Assert.All(place.MustNotSee, key => Assert.False(seen.ContainsKey(key), $"a {key} at {place.Name}"));
        }

        // Every one of the twelve was met somewhere (the dolphin at one sea place or the other: the whale shares its water).
        Assert.Equal(roster.Keys.Select(id => id.Substring("au_".Length)).OrderBy(k => k, StringComparer.Ordinal),
            everywhere.OrderBy(k => k, StringComparer.Ordinal));
        AssertAnInlandLakeSeatsNoDolphin_TheCoastDoes(server);
        AssertTheCitiesKeepPets(server, admin);
        _output.WriteLine($"total {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>Stands the player on foot a game minute at a place and returns how many animals of each record key were seen
    /// round it — checking each one, when first seen, against its zones and its sea.</summary>
    private Dictionary<string, int> StandAt(SvGameServer server, PlayerSession admin, double lon, double lat,
        IReadOnlyDictionary<string, CreatureSpecies> roster)
    {
        var world = server.World;
        var (x, z) = PlaceRules.BlockOf(lon, lat, Circumference);
        bool water = world.Generator.TryGetWaterSurface(world.Planet, x, z, out int waterTop, out _);
        float y = (water ? waterTop : world.Generator.SurfaceHeight(world.Planet, x, z)) + 1.1f;
        var seen = new Dictionary<string, int>();
        var ids = new HashSet<string>();
        for (int i = 0; i < TicksPerPlace; i++)
        {
            admin.State.Position = new Vector3f(x + 0.5f, y, z + 0.5f);
            admin.State.AboardShip = false;
            server.TickForTest(0.25);
            foreach (var c in server.Creatures)
            {
                if (c.IsCompanion || !ids.Add(c.Id) || !roster.TryGetValue(c.SpeciesId, out var sp))
                {
                    continue;
                }

                float dx = (float)WorldConstants.WrapDeltaX((double)c.Position.X - x, Circumference), dz = c.Position.Z - z;
                if (dx * dx + dz * dz > SeenRadius * SeenRadius)
                {
                    continue;
                }

                string key = sp.Id.Substring("au_".Length);
                seen[key] = seen.GetValueOrDefault(key) + 1;
                AssertAtHome(server, sp, c.Position);
            }
        }

        return seen;
    }

    /// <summary>An exclusive animal stands in one of its zones, a sea animal in the sea — within two blocks of where it is first
    /// seen (it may have taken a step since it was placed).</summary>
    private static void AssertAtHome(SvGameServer server, CreatureSpecies sp, Vector3f at)
    {
        var world = server.World;
        int cx = (int)Math.Floor(at.X), cz = (int)Math.Floor(at.Z);
        bool Near(Func<int, int, bool> test)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (test(cx + dx, cz + dz))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        if (sp.BiomeExclusive && sp.FloraThemes.Length > 0)
        {
            Assert.True(Near((px, pz) => CreatureBiomes.NamesTheme(sp.FloraThemes,
                CreatureBiomes.ThemeAt(world.Planet, world.Generator.BiomePoolIndexAt(world.Planet, px, pz)))),
                $"{sp.Id} at {cx},{cz} outside its zones");
        }

        if (sp.SeaOnly)
        {
            Assert.True(Near((px, pz) => world.Generator.IsSeaColumn(world.Planet, px, pz)), $"{sp.Id} at {cx},{cz} outside the sea");
        }
    }

    /// <summary>A lake in central Germany — water deep enough for a dolphin, above the sea level — seats none; the shallows of the open Atlantic do.</summary>
    private void AssertAnInlandLakeSeatsNoDolphin_TheCoastDoes(SvGameServer server)
    {
        var world = server.World;
        var lake = FindWater(server, 9.0, 50.6, sea: false);
        Assert.True(lake.HasValue, "no inland water deep enough for a dolphin round Hesse");
        var (lx, lz) = lake!.Value;
        _output.WriteLine($"inland water at {lx},{lz}: top {Top(server, lx, lz)}, sea level {world.Generator.SeaLevel(world.Planet)}");
        Assert.NotNull(server.WaterColumnNearForTest(lx, lz)); // there is water to swim in …
        Assert.Null(server.SpawnWaterNearForTest("au_dolphin", lx, lz)); // … but it is no sea

        var coast = FindWater(server, -35.0, 40.0, sea: true); // the shallows over the mid-Atlantic ridge
        Assert.True(coast.HasValue, "no sea water shallow enough for a dolphin in the mid-Atlantic");
        var seat = server.SpawnWaterNearForTest("au_dolphin", coast!.Value.X, coast.Value.Z);
        Assert.NotNull(seat);
        Assert.True(world.Generator.IsSeaColumn(world.Planet, seat!.Value.X, seat.Value.Z));
    }

    /// <summary>The first water column round a place (rings out to 160 blocks) a dolphin's depth band takes, in the sea or not.</summary>
    private static (int X, int Z)? FindWater(SvGameServer server, double lon, double lat, bool sea)
    {
        var world = server.World;
        var (x0, z0) = PlaceRules.BlockOf(lon, lat, Circumference);
        for (int r = 0; r <= 160; r += 2)
        {
            for (int dx = -r; dx <= r; dx += 2)
            {
                for (int dz = -r; dz <= r; dz += 2)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                    {
                        continue; // only the ring r: nearer columns came first
                    }

                    int x = x0 + dx, z = z0 + dz;
                    if (world.Generator.TryGetWaterSurface(world.Planet, x, z, out int top, out int bed) && top - bed is >= 3 and <= 16
                        && world.Generator.IsSeaColumn(world.Planet, x, z) == sea)
                    {
                        return (x, z);
                    }
                }
            }
        }

        return null;
    }

    private static int Top(SvGameServer server, int x, int z)
        => server.World.Generator.TryGetWaterSurface(server.World.Planet, x, z, out int top, out _) ? top : int.MinValue;

    /// <summary>Every metropolis keeps <see cref="SvGameServer.MetropolisPetsPerPlace"/> pets; standing by London's keepers
    /// brings their dogs and cats out, each beside its person and inside the city on a claimed column.</summary>
    private void AssertTheCitiesKeepPets(SvGameServer server, PlayerSession admin)
    {
        var keepers = server.PetKeepersForTest.ToHashSet();
        var snaps = server.NpcSnapshots;
        var posts = server.NpcPostsForTest;
        var metros = server.MetropolisesForTest;
        foreach (var m in metros)
        {
            int count = Enumerable.Range(0, snaps.Count).Count(i => posts[i].Settlement == m.Name && keepers.Contains(snaps[i].Id));
            _output.WriteLine($"{m.Name}: {count} households keep a pet");
            Assert.Equal(SvGameServer.MetropolisPetsPerPlace, count);
        }

        var london = metros.Single(m => m.Name == "London");
        var londoners = Enumerable.Range(0, snaps.Count).Where(i => posts[i].Settlement == london.Name && keepers.Contains(snaps[i].Id))
            .Select(i => snaps[i]).ToList();
        int met = 0;
        foreach (var keeper in londoners)
        {
            for (int i = 0; i < 8; i++)
            {
                var at = server.NpcSnapshots.Single(n => n.Id == keeper.Id).Pos;
                admin.State.Position = new Vector3f(at.X, at.Y + 0.1f, at.Z);
                admin.State.AboardShip = false;
                server.TickForTest(0.25);
            }

            var pos = server.NpcSnapshots.Single(n => n.Id == keeper.Id).Pos;
            foreach (var pet in server.Creatures.Where(c => c.OwnerId == "npc:" + keeper.Id))
            {
                met++;
                Assert.Contains(pet.SpeciesId, new[] { "au_dog", "au_cat" });
                Assert.InRange(WorldConstants.WrapDeltaX((double)pet.Position.X - pos.X, Circumference), -6.0, 6.0);
                Assert.InRange(pet.Position.Z - pos.Z, -6f, 6f);
                Assert.InRange(pet.Position.Y - pos.Y, -3f, 3f); // beside its person — never up on a roof
                int lx = WorldConstants.WrapDeltaX((int)Math.Floor(pet.Position.X) - london.Min.X, Circumference);
                int lz = (int)Math.Floor(pet.Position.Z) - london.Min.Z;
                Assert.True(london.Layout.InBounds(lx, 0, lz) && london.Layout.FloorAt(lx, lz) >= 0, $"London's pet at {lx},{lz} off the city's ground");
            }
        }

        _output.WriteLine($"London: met {met} pets beside {londoners.Count} keepers");
        Assert.True(met >= londoners.Count / 2, $"only {met} of London's {londoners.Count} pets came out");
    }

    private (SvGameServer Server, PlayerSession Admin) BootOnEarth()
    {
        var config = new ServerConfig
        {
            WorldName = "earth_fauna",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, config.WorldName));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
        server.Start();
        var admin = server.AddLocalPlayer("Ada");
        admin.State.Role = PlayerRole.WorldAdmin;
        if (!server.Galaxy.Systems.Any(s => s.Id == "sol"))
        {
            server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" });
        }

        Assert.True(server.VisitBodyForTest("sol-earth"));
        admin.CurrentLocationId = "sol-earth";
        admin.State.AboardShip = false;
        return (server, admin);
    }
}
