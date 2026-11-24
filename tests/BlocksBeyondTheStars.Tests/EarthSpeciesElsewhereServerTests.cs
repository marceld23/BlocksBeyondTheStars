// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3976 on a real server: on a generation-49 world with air to breathe a villager's pet may be a dog or a cat although the
/// roster does not hold it — the species table knows it, so the creature message carries its name, coats and calls — and a
/// pen may keep a cow, a sheep or a chicken that lays its own product; a generation-48 world keeps none of them; a wild dog
/// or cat of another world never spawns in a town. The rules themselves are <see cref="EarthSpeciesElsewhereTests"/>.
/// </summary>
public sealed class EarthSpeciesElsewhereServerTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly string[] EarthPets = { "au_dog", "au_cat" };
    private static readonly Dictionary<string, string> EarthPens = new() { ["au_cow"] = "milk", ["au_sheep"] = "wool", ["au_chicken"] = "egg" };

    private const string Village = "Petsham";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_earthpets_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;

    public EarthSpeciesElsewhereServerTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private static NpcLifeWorld World(string name, int generation)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.StartPlanet = "meadowlands";
            c.World.TerrainGeneration = generation;
        });
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        return w;
    }

    /// <summary>Sixteen pet keepers in the village round the player, and their pets after one pass.</summary>
    private static List<(int NpcId, string SpeciesId)> PetsOfSixteenKeepers(NpcLifeWorld w)
    {
        for (int i = 0; i < 16; i++)
        {
            int keeper = w.Server.SpawnVillagerForTest(Village, new Vector3f(NpcLifeWorld.Cx + (i % 8) - 4 + 0.5f, w.Feet, NpcLifeWorld.Cz + (i / 8 * 3) + 0.5f));
            w.Server.MakePetKeeperForTest(keeper);
        }

        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 0.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        w.Server.TickProfessionsForTest();
        return w.Server.NpcPetsForTest.ToList();
    }

    [Fact]
    public void OnAGeneration49World_APersonsPet_MayBeADogOrACat_AndTheClientIsToldWhatItIs()
    {
        using var w = World("pets49", WorldDescription.EarthSpeciesGeneration);
        Assert.Equal(EarthPets, w.Server.KeptAnimalsForTest.Pets);
        var pets = PetsOfSixteenKeepers(w);
        Assert.Equal(16, pets.Count);
        var earth = pets.Where(p => EarthPets.Contains(p.SpeciesId)).ToList();
        _out.WriteLine($"{earth.Count} of 16 keepers keep a dog or a cat");
        Assert.InRange(earth.Count, 3, 13); // half of them, by the person's own key

        // A dog or cat that is no wild animal of this world: not in the roster, but in the species table and on the wire.
        var notWild = earth.Where(p => w.Server.SpeciesRoster.All(s => s.Id != p.SpeciesId)).ToList();
        Assert.NotEmpty(notWild); // this world (seed 4242) hosts neither wild
        string id = notWild[0].SpeciesId;
        Assert.True(w.Server.KnowsSpeciesForTest(id));
        var pet = w.Server.NpcPetScalesForTest.First(p => p.SpeciesId == id);
        var net = w.Server.NetCreatureForTest(pet.Id);
        Assert.Equal(id == "au_dog" ? "creature.dog.name" : "creature.cat.name", net.NameKey);
        Assert.False(string.IsNullOrEmpty(net.Voices));

        // A grown dog or cat, no puppy: its own size, only capped by what a house allows.
        var record = Content.Value.AuthoredCreatures[id.Substring(3)];
        var body = BlocksBeyondTheStars.WorldGeneration.CreatureGenerator.MakeAuthored(record, Content.Value.GetPlanet("meadowlands")!, 1);
        Assert.Equal(SvGameServer.PetScaleFor(body, 1f), pet.Scale, 3);
    }

    [Fact]
    public void OnAGeneration48World_NoPersonKeepsAnEarthAnimal()
    {
        using var w = World("pets48", WorldDescription.EarthSpeciesGeneration - 1);
        Assert.Empty(w.Server.KeptAnimalsForTest.Pets);
        Assert.Empty(w.Server.KeptAnimalsForTest.PenAnimals);
        var pets = PetsOfSixteenKeepers(w);
        Assert.DoesNotContain(pets, p => EarthPets.Contains(p.SpeciesId));
        Assert.All(pets, p => Assert.Contains(w.Server.SpeciesRoster, s => s.Id == p.SpeciesId)); // the world's own, as before
    }

    private SvGameServer StartVillages(string name, long seed, int generation, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            LandmarkChance = 0.0,
        };
        config.World.TerrainGeneration = generation;
        config.World.Settlements = Frequency.Frequent;
        var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void OnAGeneration49World_APen_MayKeepACowASheepOrAChicken_AndLaysItsOwnProduct()
    {
        int earthPens = 0, pens = 0;
        for (long seed = 2602; seed < 2612 && earthPens == 0; seed++)
        {
            var server = StartVillages("pens49_" + seed, seed, WorldDescription.EarthSpeciesGeneration, out var repo);
            try
            {
                foreach (var (key, species, product) in server.PenAnimalsForTest)
                {
                    pens++;
                    if (EarthPens.TryGetValue(species, out var want))
                    {
                        earthPens++;
                        Assert.Equal(want, product);
                        Assert.True(server.KnowsSpeciesForTest(species), $"{species} of pen {key}");
                    }
                }
            }
            finally
            {
                server.Stop();
                repo.Dispose();
            }
        }

        _out.WriteLine($"{earthPens} of {pens} pens keep an Earth animal");
        Assert.True(earthPens > 0, $"none of {pens} pens kept a cow, a sheep or a chicken");
    }

    [Fact]
    public void AWildDogOrCat_OfAnotherWorld_NeverSpawnsInATown()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stray" + seed));
            var config = new ServerConfig
            {
                WorldName = "stray" + seed,
                Seed = seed,
                StartPlanet = "meadowlands",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
            };
            var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            try
            {
                if (server.SpeciesRoster.FirstOrDefault(s => EarthPets.Contains(s.Id)) is not { } stray)
                {
                    continue;
                }

                _out.WriteLine($"seed {seed}: {stray.Id} lives wild on {server.World.LocationId}");
                int padY = Pad(server);
                var lane = new Vector3f(NpcLifeWorld.Cx + 2.5f, padY + 1, NpcLifeWorld.Cz + 0.5f);
                var outside = new Vector3f(NpcLifeWorld.Cx + 20.5f, padY + 1, NpcLifeWorld.Cz + 0.5f);
                if (!server.SpawnSpotClearForSpeciesTest(stray.Id, lane) || !server.SpawnSpotClearForSpeciesTest(stray.Id, outside))
                {
                    continue; // control: the pad lies outside the stray's climate zones here — the town rule would prove nothing
                }

                server.RegisterTestSettlementForTest("Strayton", new Vector3i(NpcLifeWorld.Cx - 5, padY, NpcLifeWorld.Cz - 5),
                    new Vector3i(NpcLifeWorld.Cx + 5, padY + 8, NpcLifeWorld.Cz + 5));
                Assert.False(server.SpawnSpotClearForSpeciesTest(stray.Id, lane), "a stray spawned in the town's lane");
                Assert.True(server.SpawnSpotClearForSpeciesTest(stray.Id, outside), "a stray may live outside the town");
                return;
            }
            finally
            {
                server.Stop();
                repo.Dispose();
            }
        }

        Assert.Fail("no world of forty had a dog or a cat living wild");
    }

    /// <summary>A stone pad (half-size 24) eight above the highest natural block round the centre; returns its Y.</summary>
    private int Pad(SvGameServer server)
    {
        const int r = 24;
        int top = int.MinValue;
        for (int dx = -r; dx <= r; dx += 4)
            for (int dz = -r; dz <= r; dz += 4)
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(NpcLifeWorld.Cx + dx, y, NpcLifeWorld.Cz + dz)).IsAir)
                    {
                        top = Math.Max(top, y);
                        break;
                    }
                }

        int padY = top + 8;
        var stone = Content.Value.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(NpcLifeWorld.Cx + dx, padY, NpcLifeWorld.Cz + dz), stone);
            }

        return padY;
    }
}
