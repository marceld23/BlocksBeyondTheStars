// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2868, the server's part of the planet hide themes: every creature on the wire names the world its look belongs to
/// (<see cref="NetCreature.LookPlanet"/>, <see cref="NetCreature.LookSeed"/>). A roster animal wears the world it is on;
/// a companion, a clone and a cross keep the look of their home world — through a save, onto another world, and for a
/// register entry from before the stamp, by its origin body.
/// </summary>
public sealed class FaunaLookTests : IDisposable
{
    private static readonly Vector3i Tank = new(1, 200, 0);

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_faunalook_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    // ---------------- Pure: the seed, the data field, the wire ----------------

    [Fact]
    public void WorldLookSeed_IsDeterministic_NeverZero_AndDiffersFromWorldToWorld()
    {
        var seen = new HashSet<int>();
        int worlds = 0;
        foreach (long seed in new[] { 0L, 1L, 77L, 4242L, -9L, long.MaxValue, long.MinValue })
        {
            for (int i = 0; i < 60; i++)
            {
                string key = i == 0 ? "Lone Body" : "Sol · Body " + i.ToString(CultureInfo.InvariantCulture); // a body without a system, and the bodies of one system
                int look = FaunaLooks.WorldLookSeed(seed, key);
                Assert.NotEqual(0, look);
                Assert.Equal(look, FaunaLooks.WorldLookSeed(seed, key));
                seen.Add(look);
                worlds++;
            }
        }

        Assert.Equal(worlds, seen.Count); // 420 worlds, 420 different motifs
        Assert.NotEqual(0, FaunaLooks.WorldLookSeed(77, string.Empty));
    }

    [Fact]
    public void WorldLookSeed_DoesNotDependOnTheServersCulture()
    {
        int invariant = FaunaLooks.WorldLookSeed(-4242, "Sol · Terra");
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.NumberFormat.NegativeSign = "~"; // a locale whose minus is not "-" must not re-dress a world's animals
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = odd;
            Assert.Equal(invariant, FaunaLooks.WorldLookSeed(-4242, "Sol · Terra"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void PlanetType_FaunaTheme_BindsFromItsJsonName_AndDefaultsToDerived()
    {
        var types = JsonSerializer.Deserialize<List<PlanetType>>(
            "[{\"key\":\"frosty\",\"faunaTheme\":\"frost\"},{\"key\":\"plain\"}]", ContentLoader.JsonOptions)!;
        Assert.Equal("frost", types[0].FaunaTheme);
        Assert.Equal(string.Empty, types[1].FaunaTheme); // empty = the client derives the theme
    }

    [Fact]
    public void NetCreature_LookFields_RoundTrip_AndDefaultToNoLook()
    {
        var legacy = new NetCreature();
        Assert.Equal(string.Empty, legacy.LookPlanet);
        Assert.Equal(0, legacy.LookSeed);

        var list = new CreatureList { Creatures = new[] { new NetCreature { Id = "c1", LookPlanet = "ice", LookSeed = -123456 } } };
        var decoded = Assert.IsType<CreatureList>(NetCodec.Decode(NetCodec.Encode(list)));
        Assert.Equal("ice", decoded.Creatures[0].LookPlanet);
        Assert.Equal(-123456, decoded.Creatures[0].LookSeed);
    }

    [Fact]
    public void ACross_WearsTheLookOfOneParentsWorld_TheSamePairEveryTime()
    {
        var parents = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 77).Where(s => !s.Hostile && !s.IsGiant).Take(2).ToList();
        Assert.Equal(2, parents.Count);
        var (a, b) = (parents[0], parents[1]);
        (a.LookPlanet, a.LookSeed) = ("ice", 111);
        (b.LookPlanet, b.LookSeed) = ("desert", 222);

        int fromA = 0, fromB = 0;
        for (uint seed = 1; seed <= 64; seed++)
        {
            var child = SvGameServer.CrossCreaturesForTest(a, b, seed);
            Assert.True((child.LookPlanet, child.LookSeed) is ("ice", 111) or ("desert", 222),
                $"seed {seed}: the cross wears {child.LookPlanet}/{child.LookSeed}, not one parent's world");
            var again = SvGameServer.CrossCreaturesForTest(a, b, seed);
            Assert.Equal((child.LookPlanet, child.LookSeed), (again.LookPlanet, again.LookSeed));
            if (child.LookSeed == 111)
            {
                fromA++;
            }
            else
            {
                fromB++;
            }
        }

        Assert.True(fromA > 0 && fromB > 0, $"over 64 crosses both parents' worlds should come up ({fromA}/{fromB})");
    }

    // ---------------- The server: roster animals, samples, companions ----------------

    [Fact]
    public void AWildCreature_AndASampleOfIt_WearTheLookOfTheWorldItIsOn()
    {
        var server = NewServer("jungle", "wild", seed: 4242);
        var p = Ranger(server);
        server.Tick(6.0);

        // The generator is untouched: a rolled roster carries no look of its own.
        Assert.All(server.SpeciesRoster, s =>
        {
            Assert.Equal(string.Empty, s.LookPlanet);
            Assert.Equal(0, s.LookSeed);
        });

        var expected = LookOf(server, server.World.LocationId);
        Assert.Equal("jungle", expected.Planet);
        var wild = server.Creatures.Where(c => c.OwnerId.Length == 0).ToList();
        Assert.NotEmpty(wild);
        foreach (var c in wild)
        {
            var wire = server.NetCreatureForTest(c.Id);
            Assert.Equal(expected, (wire.LookPlanet, wire.LookSeed));
        }

        // A sample registers the species with this world's look — every clone of it will wear it.
        var sp = server.SpeciesRoster.First(s => s.Id == wild[0].SpeciesId);
        uint seed = server.GiveCreatureSampleForTest(p, sp);
        var snapshot = server.BioEntryForTest(seed)!.Creature!;
        Assert.Equal(expected, (snapshot.LookPlanet, snapshot.LookSeed));
        Assert.Equal(string.Empty, sp.LookPlanet); // the roster species itself stays as rolled
    }

    [Fact]
    public void ATamedCompanion_KeepsItsHomeLook_ThroughASaveAndLoad()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tamed"));
        using (repo)
        {
            var server = NewServer("jungle", "tamed", seed: 4242, repo: repo);
            var p = Ranger(server);
            server.Tick(6.0);
            var home = LookOf(server, server.World.LocationId);

            var creature = server.Creatures.First(c => c.OwnerId.Length == 0);
            server.SpeciesRoster.First(s => s.Id == creature.SpeciesId).Temperament = CreatureTemperament.Passive;
            Assert.True(TameNearest(server, p, creature.Id, creature.Position), "a forgiving passive creature should tame");

            var tc = Assert.Single(server.TamedCreaturesForTest("Ranger"));
            Assert.Equal(home, (tc.Species.LookPlanet, tc.Species.LookSeed));
            var pet = Assert.Single(server.CompanionEntitiesForTest("Ranger"));
            var wire = server.NetCreatureForTest(pet.Id);
            Assert.Equal(home, (wire.LookPlanet, wire.LookSeed));

            var reloaded = Assert.Single(repo.LoadPlayer("Ranger")!.TamedCreatures);
            Assert.Equal(home, (reloaded.Species.LookPlanet, reloaded.Species.LookSeed));
        }
    }

    [Fact]
    public void APetFromAnotherWorld_WearsItsHomeLook_NotTheWorldItIsShownOn()
    {
        var server = NewServer("jungle", "visitor", seed: 4242);
        var p = Ranger(server);
        server.Tick(2.0);

        var frosty = CreatureGenerator.GenerateRoster(_content.GetPlanet("ice")!, 5).First(s => !s.IsGiant);
        frosty.Id = "zz_frosty"; // no species of this world
        (frosty.LookPlanet, frosty.LookSeed) = ("ice", 424242);
        p.State.TamedCreatures.Add(new TamedCreature
        {
            Id = "tc_frosty",
            HomeBodyId = server.World.LocationId,
            Name = "Frosty",
            SpeciesId = frosty.Id,
            Species = frosty,
            Bond = 50,
        });
        server.ReconcileCompanionsForTest();

        var pet = Assert.Single(server.CompanionEntitiesForTest("Ranger"));
        var wire = server.NetCreatureForTest(pet.Id);
        Assert.Equal(("ice", 424242), (wire.LookPlanet, wire.LookSeed));
        Assert.NotEqual(LookOf(server, server.World.LocationId), (wire.LookPlanet, wire.LookSeed));
    }

    // ---------------- The bio lab: clones and crosses ----------------

    [Fact]
    public void AClone_KeepsTheLookOfTheWorldItWasSampledOn()
    {
        var server = NewServer("jungle", "clone");
        var p = TankOwner(server);
        var visitor = Visitor();
        (visitor.LookPlanet, visitor.LookSeed) = ("ice", 31337);
        uint seed = server.RegisterForeignCreatureForTest(visitor, "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed));

        var clone = GrowClone(server, p, seed);
        var wire = server.NetCreatureForTest(clone.Id);
        Assert.Equal(("ice", 31337), (wire.LookPlanet, wire.LookSeed));
    }

    [Fact]
    public void AnOlderSample_WithoutALook_GrowsAClone_InTheLookOfItsOriginBody()
    {
        var server = NewServer("jungle", "older");
        var p = TankOwner(server);
        var origin = OtherBodies(server).First();
        uint seed = server.RegisterForeignCreatureForTest(Visitor(), origin.Id); // a register entry from before the stamp
        Assert.Equal(string.Empty, server.BioEntryForTest(seed)!.Creature!.LookPlanet);
        Assert.True(server.GiveSampleForTest(p, seed));

        var clone = GrowClone(server, p, seed);
        var wire = server.NetCreatureForTest(clone.Id);
        Assert.Equal(LookOf(server, origin.Id), (wire.LookPlanet, wire.LookSeed));
        Assert.NotEqual("jungle", wire.LookPlanet);
    }

    [Fact]
    public void ACrossOfTwoOlderSamples_TakesTheLookOfOneParentsOriginBody()
    {
        var server = NewServer("jungle", "cross");
        var bodies = OtherBodies(server).Take(2).ToList();
        Assert.Equal(2, bodies.Count);
        var species = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 77).Where(s => !s.Hostile && !s.IsGiant).Take(2).ToList();
        uint a = server.RegisterForeignCreatureForTest(species[0], bodies[0].Id);
        uint b = server.RegisterForeignCreatureForTest(species[1], bodies[1].Id);

        var child = server.CrossForTest(a, b)!.Creature!;
        (string Planet, int Seed) look = (child.LookPlanet, child.LookSeed);
        Assert.Contains(look, new[] { LookOf(server, bodies[0].Id), LookOf(server, bodies[1].Id) });
        var swapped = server.CrossForTest(b, a)!.Creature!;
        Assert.Equal(look, (swapped.LookPlanet, swapped.LookSeed)); // A × B and B × A are the same child
    }

    // ---------------- Helpers ----------------

    private SvGameServer NewServer(string planet, string world, long seed = 77, SqliteWorldRepository? repo = null)
    {
        repo ??= new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // the classic relief: the ranger stands at a fixed (0, 64, 0)
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The look a location gives its animals: its planet type and the world look seed of "system · body".</summary>
    private static (string Planet, int Seed) LookOf(SvGameServer server, string bodyId)
    {
        var body = server.Galaxy.FindBody(bodyId)!;
        string system = server.Galaxy.Systems.FirstOrDefault(s => s.Id == body.SystemId)?.Name ?? string.Empty;
        string key = system.Length == 0 ? body.Name : system + " · " + body.Name;
        return (body.PlanetType!, FaunaLooks.WorldLookSeed(server.Metadata.Seed, key));
    }

    /// <summary>Planets and moons of the galaxy other than the active world, of other planet types.</summary>
    private static IEnumerable<CelestialBody> OtherBodies(SvGameServer server)
        => server.Galaxy.Systems.SelectMany(s => s.Bodies)
            .Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon && b.Id != server.World.LocationId
                        && !string.IsNullOrEmpty(b.PlanetType) && b.PlanetType != server.World.PlanetKey)
            .GroupBy(b => b.PlanetType)
            .Select(g => g.First());

    /// <summary>A peaceful land animal of a jungle world, not this server's own roster object.</summary>
    private CreatureSpecies Visitor()
        => CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 77)
            .First(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Land);

    private static SvSession Ranger(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Ranger");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Inventory.Add("creature_translator", 1, 1);
        p.State.Inventory.Add("forage_bait", 50, 50);
        p.State.Inventory.Add("meat_bait", 50, 50);
        p.State.Inventory.Add("nectar_lure", 50, 50);
        p.State.SuitEnergy = 100f;
        return p;
    }

    /// <summary>Drives the taming ritual on a creature to its end, always answering its current need.</summary>
    private static bool TameNearest(SvGameServer server, SvSession p, string creatureId, Vector3f at)
    {
        p.State.Position = at;
        int before = server.TamedCreaturesForTest("Ranger").Count;
        server.TameDecodeForTest("Ranger");
        for (int i = 0; i < 30; i++)
        {
            string need = server.TameCurrentNeedForTest("Ranger");
            if (string.IsNullOrEmpty(need))
            {
                break;
            }

            server.TameRespondForTest("Ranger", need);
        }

        return server.TamedCreaturesForTest("Ranger").Count > before && server.Creatures.All(c => c.Id != creatureId);
    }

    /// <summary>The player "Keeper" high in the air with matter dust and a clone tank standing at <see cref="Tank"/>.</summary>
    private static SvSession TankOwner(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Keeper");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.SuitEnergy = 100f;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }

        Assert.Equal(0, p.State.Inventory.Add("clone_tank", 1, 1));
        Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        return p;
    }

    /// <summary>Grows one clone of a sample species in the tank and returns it.</summary>
    private static CombatEntity GrowClone(SvGameServer server, SvSession p, uint seed)
    {
        var before = server.Creatures.Where(c => c.CloneOf.Length > 0).Select(c => c.Id).ToHashSet();
        server.SetCrystalDeviceForTest(p, Tank, action: 2, mode: 0, config: "sp=" + SvGameServer.TankChoiceForTest(seed));
        server.SetCrystalDeviceForTest(p, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
        for (double t = 0; t < CrystalNetRules.CloneGrowSeconds + 1.0; t += 0.5)
        {
            server.TickForTest(0.5);
        }

        return Assert.Single(server.Creatures.Where(c => c.CloneOf.Length > 0), c => !before.Contains(c.Id));
    }
}
