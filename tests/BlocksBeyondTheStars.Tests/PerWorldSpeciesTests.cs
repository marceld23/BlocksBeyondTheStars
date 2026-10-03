// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
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
/// The species tables are a world's, not the server's (#2226). Every test here keeps TWO worlds resident — two players
/// on two bodies, or one on a planet and one aboard a station or inside a ship in space — and reads what each world's
/// animals, spawner, clone tank and plants see while the other world is loaded and ticked. The rolled species ids
/// ("sp0", "sp1", …) repeat from world to world, so every case is also the id collision case. The last section holds
/// the same rule for what was gated on the server's uptime with one field for all worlds — the burn pass, the sentry
/// posts — and for the Crystal Net's sent levels, keyed by net ids that repeat from world to world as well; the last
/// case for the one terrain generator all worlds share, which has to be configured for the world the server looks at.
/// </summary>
public sealed class PerWorldSpeciesTests : IDisposable
{
    private static readonly Vector3i Tank = new(1, 200, 0);

    private readonly string _root;
    private readonly GameContent _content;

    public PerWorldSpeciesTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_worldspecies_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

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

    /// <summary>Records every per-connection server send so a test can assert what a player was told.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public List<(int Conn, object Msg)> Sent { get; } = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll()
        {
            _ = ClientConnected;
            _ = ClientDisconnected;
            _ = PayloadReceived;
        }

        public void Stop() { }

        public void Dispose() { }
    }

    /// <summary>A jungle world with free flight, so a second player can land on another body of the home system.</summary>
    private SvGameServer NewServer(string world, IServerTransport? transport = null, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = "jungle", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot where the join put them — on the ground, where the wildlife is. Nothing bites them.</summary>
    private static SvSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.GodMode = true;
        return p;
    }

    /// <summary>A player high up in the air (empty cells, everything in reach) with the given items and no starter kit.</summary>
    private static SvSession InTheAir(SvGameServer server, string name, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.SuitEnergy = 100f;
        p.State.ModeOverride = PlayerModeOverride.Creative; // a free game mode: no bait, no matter dust
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }

        foreach (string key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 1));
        }

        return p;
    }

    /// <summary>Another body of the home system that has animals of its own — the second world of a test.</summary>
    private CelestialBody OtherLivingBody(SvGameServer server, string home)
    {
        string system = server.Galaxy.FindBody(home)!.SystemId;
        long seed = server.Metadata.Seed;
        int generation = server.Metadata.Description.TerrainGeneration;
        return server.Galaxy.AllBodies().First(b => b.Id != home && b.SystemId == system
            && b.Kind is CelestialKind.Planet or CelestialKind.Moon
            && !string.IsNullOrEmpty(b.PlanetType) && _content.GetPlanet(b.PlanetType!) is { } type
            && CreatureGenerator.GenerateRoster(type, WorldGenerator.RosterSeedFor(seed, b.Id), generation, _content.AuthoredCreaturesFor(type)).Count > 0);
    }

    /// <summary>Lands a second player on another living body: from here on two worlds are resident, and the other
    /// one's fauna and flora were set up LAST.</summary>
    private SvSession LandOnAnotherWorld(SvGameServer server, string home, string name, out string other)
    {
        var p = server.AddLocalPlayer(name);
        other = OtherLivingBody(server, home).Id;
        server.Travel(name, other);
        Assert.Equal(other, p.CurrentLocationId);
        Assert.True(server.ResidentWorldCount >= 2);
        Assert.Equal(other, server.ActiveLocationId);
        p.State.AboardShip = false;
        p.State.GodMode = true;
        return p;
    }

    private static void Ticks(SvGameServer server, double seconds, double step = 0.5)
    {
        for (double t = 0; t < seconds; t += step)
        {
            server.TickForTest(step);
        }
    }

    /// <summary>Points the server at a world, as the tick and every message handler do before they act on one.</summary>
    private static void At(SvGameServer server, string location)
        => Assert.True(server.ActivateWorldForTest(location), "world '" + location + "' is not resident");

    private static List<CombatEntity> Wild(SvGameServer server)
        => server.Creatures.Where(c => !c.IsGiant && !c.IsCompanion && c.CloneOf.Length == 0).ToList();

    private static List<CombatEntity> Clones(SvGameServer server) => server.Creatures.Where(c => c.CloneOf.Length > 0).ToList();

    private static NetCreature? LastCreatureSentTo(RecordingTransport t, SvSession who, string creatureId)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is CreatureList).Select(x => (CreatureList)x.Msg)
            .LastOrDefault()?.Creatures.FirstOrDefault(c => c.Id == creatureId);

    private static NetCrystalDevice? LastTankSentTo(RecordingTransport t, SvSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is CrystalDeviceList).Select(x => (CrystalDeviceList)x.Msg)
            .LastOrDefault()?.Devices.FirstOrDefault(d => d.X == Tank.X && d.Y == Tank.Y && d.Z == Tank.Z);

    /// <summary>What an animal on the wire shows of its species: enough to tell two species with one id apart.</summary>
    private static void AssertIsSpecies(CreatureSpecies expected, NetCreature? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual!.SpeciesId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.ColorRgb, actual.ColorRgb);
        Assert.Equal(expected.Legs, actual.Legs);
        Assert.Equal(expected.Habitat.ToString(), actual.Habitat);
        Assert.Equal(expected.BodyPlan.ToString(), actual.BodyPlan);
    }

    /// <summary>Whether a spawned animal carries what a species gives it at birth: its id, health, bite and yield.</summary>
    private static bool BornAs(CreatureSpecies sp, CombatEntity c)
        => c.SpeciesId == sp.Id && c.HullMax.Equals(sp.MaxHealth) && c.Hostile == sp.Hostile
           && c.DamagePerSecond.Equals(sp.AttackDamage) && c.Loot.Count == 1 && c.Loot[0].Item == sp.DropItem && c.Loot[0].Count == sp.DropCount;

    // ---------------- (a) Every world's animals keep their own species ----------------

    [Fact]
    public void TwoWorldsWithTheSameSpeciesIds_EachAnimalKeepsItsOwnSpecies()
    {
        var t = new RecordingTransport();
        var server = NewServer("ids", t);
        var keeper = OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        var homeRoster = server.SpeciesRoster.ToList();
        var homeSp0 = homeRoster.Single(s => s.Id == "sp0");
        string homeAnimal = server.SpawnCreatureAtForTest(keeper.State.Position, "sp0");

        // A second world becomes resident; its fauna is set up after the home world's.
        var visitor = LandOnAnotherWorld(server, home, "Visitor", out string other);
        var otherRoster = server.SpeciesRoster.ToList();
        var otherSp0 = otherRoster.Single(s => s.Id == "sp0");
        Assert.NotEqual(homeSp0.Name, otherSp0.Name); // one id, two animals
        string otherAnimal = server.SpawnCreatureAtForTest(visitor.State.Position, "sp0");

        Ticks(server, 3.0, 0.1); // both worlds tick, turn and turn about

        // The home world's animal is still the home world's sp0 — to the server and to the player who sees it — and
        // the roster is the very objects the world was loaded with: nothing was refilled when it became active again.
        At(server, home);
        AssertIsSpecies(homeSp0, LastCreatureSentTo(t, keeper, homeAnimal));
        AssertIsSpecies(homeSp0, server.NetCreatureForTest(homeAnimal));
        Assert.Null(LastCreatureSentTo(t, keeper, otherAnimal));
        Assert.Equal(homeRoster, server.SpeciesRoster);

        // The other world: the same id, and its own animal behind it.
        At(server, other);
        AssertIsSpecies(otherSp0, LastCreatureSentTo(t, visitor, otherAnimal));
        AssertIsSpecies(otherSp0, server.NetCreatureForTest(otherAnimal));
        Assert.Null(LastCreatureSentTo(t, visitor, homeAnimal));
        Assert.Equal(otherRoster, server.SpeciesRoster);
    }

    [Fact]
    public void AWorldThatIsLeft_TakesItsSpeciesWithIt_AndRollsTheSameOnesWhenItIsLoadedAgain()
    {
        var server = NewServer("unload");
        OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        var homeRoster = server.SpeciesRoster.ToList();

        LandOnAnotherWorld(server, home, "Visitor", out string other);
        var otherNames = server.SpeciesRoster.Select(s => s.Name).ToList();

        server.DisconnectLocalPlayerForTest("Visitor"); // the last player leaves: the world is dropped
        Assert.False(server.ActivateWorldForTest(other));
        At(server, home);
        Assert.Equal(homeRoster, server.SpeciesRoster); // the world that stays keeps its tables untouched

        LandOnAnotherWorld(server, home, "Visitor", out string again);
        Assert.Equal(other, again);
        Assert.Equal(otherNames, server.SpeciesRoster.Select(s => s.Name).ToList()); // built once per load, from the seed
    }

    // ---------------- (b) The spawner ----------------

    [Fact]
    public void TheSpawnerOfAWorld_PlacesOnlyThatWorldsSpecies_WhileAnotherWorldTicksInBetween()
    {
        var server = NewServer("spawner");
        OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        var homeRoster = server.SpeciesRoster.ToList();

        LandOnAnotherWorld(server, home, "Visitor", out string other);
        var otherRoster = server.SpeciesRoster.ToList();

        // What the two worlds hand a newborn differs for every id they share — or this test could not tell them apart.
        Assert.All(homeRoster, h => Assert.DoesNotContain(otherRoster, o => o.Id == h.Id
            && o.MaxHealth.Equals(h.MaxHealth) && o.DropItem == h.DropItem && o.DropCount == h.DropCount));

        // Start both worlds empty, so every animal counted below was placed with both worlds resident.
        foreach (string world in new[] { home, other })
        {
            At(server, world);
            ((List<CombatEntity>)server.Creatures).RemoveAll(c => !c.IsGiant);
        }

        Ticks(server, 40.0);

        At(server, home);
        var atHome = Wild(server);
        Assert.NotEmpty(atHome);
        Assert.All(atHome, c => Assert.Contains(homeRoster, sp => BornAs(sp, c)));

        At(server, other);
        var atTheOther = Wild(server);
        Assert.NotEmpty(atTheOther);
        Assert.All(atTheOther, c => Assert.Contains(otherRoster, sp => BornAs(sp, c)));
    }

    // ---------------- (c) A guest clone ----------------

    [Fact]
    public void AGuestClone_KeepsItsSpeciesAndKeepsMoving_AfterAnotherWorldWasLoaded()
    {
        var t = new RecordingTransport();
        var server = NewServer("guest", t);
        var keeper = InTheAir(server, "Keeper", "clone_tank");
        string home = server.ActiveLocationId;
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");

        // A land animal of another jungle world that is awake by day (the test world's clock stands in the morning).
        var foreign = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 4711)
            .First(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Land && s.Activity is CreatureActivity.Cathemeral or CreatureActivity.Diurnal);
        uint seed = server.RegisterForeignCreatureForTest(foreign, "elsewhere");
        Assert.True(server.GiveSampleForTest(keeper, seed));
        server.SetCrystalDeviceForTest(keeper, Tank, action: 2, mode: 0, config: "sp=" + SvGameServer.TankChoiceForTest(seed));
        server.SetCrystalDeviceForTest(keeper, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        var clone = Assert.Single(Clones(server));
        Assert.StartsWith("gx", clone.SpeciesId);

        // Another world is loaded while the clone lives: its fauna is set up, its species table is built.
        LandOnAnotherWorld(server, home, "Visitor", out _);

        var before = clone.Position;
        t.Sent.Clear();
        Ticks(server, 40.0, 0.1);

        At(server, home);
        Assert.Same(clone, Assert.Single(Clones(server)));
        Assert.NotEqual(before, clone.Position); // it moves — which it only does while the creature tick knows its species
        var sent = LastCreatureSentTo(t, keeper, clone.Id);
        Assert.NotNull(sent);
        Assert.Equal(foreign.Name, sent!.Name); // and the client is still told what it is
        Assert.Equal(foreign.ColorRgb, sent.ColorRgb);
        Assert.DoesNotContain(server.SpeciesRoster, s => s.Id == clone.SpeciesId); // a guest beside its tank, not a roster species
    }

    // ---------------- (d) The clone tank ----------------

    [Fact]
    public void TheCloneTankOfAWorld_OffersAndGrowsThatWorldsSpecies()
    {
        var t = new RecordingTransport();
        var server = NewServer("tank", t);
        var keeper = InTheAir(server, "Keeper", "clone_tank");
        string home = server.ActiveLocationId;
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        var native = server.SpeciesRoster.First(s => !s.Hostile && !s.IsGiant && s.Habitat is CreatureHabitat.Land or CreatureHabitat.Air);

        LandOnAnotherWorld(server, home, "Visitor", out _);
        var twin = server.SpeciesRoster.Single(s => s.Id == native.Id); // the other world has an animal with this id, too
        Assert.NotEqual(native.Name, twin.Name);

        // The Keeper scans the animal on the home world. The tank's list follows on its next beat: this world's animal.
        keeper.State.ScannedCreatureSites.Add(home + ":" + native.Id);
        t.Sent.Clear();
        Ticks(server, 1.5, 0.1);
        var listed = LastTankSentTo(t, keeper);
        Assert.NotNull(listed);
        Assert.Equal(new[] { native.Id + "|" + native.Name }, listed!.Choices);

        // And it grows that animal: the native path reads the tank's own world.
        At(server, home);
        server.SetCrystalDeviceForTest(keeper, Tank, action: 2, mode: 0, config: "sp=" + native.Id);
        server.SetCrystalDeviceForTest(keeper, Tank, action: 1);
        Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);

        At(server, home);
        var clone = Assert.Single(Clones(server));
        Assert.True(BornAs(native, clone));
        AssertIsSpecies(native, server.NetCreatureForTest(clone.Id));
        AssertIsSpecies(native, LastCreatureSentTo(t, keeper, clone.Id));
    }

    // ---------------- (e) A void world beside a planet ----------------

    [Fact]
    public void AStationBesideAPlanet_HasNoRosterOfItsOwn_AndLeavesThePlanetsAlone()
    {
        var server = NewServer("station");
        OnFoot(server, "Settler");
        string home = server.ActiveLocationId;
        var roster = server.SpeciesRoster.ToList();
        Assert.NotEmpty(roster);

        var pilot = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation("Pilot", station.Id);
        Assert.StartsWith("station:", pilot.CurrentLocationId);

        AssertTheVoidWorldLeftThePlanetAlone(server, home, roster, pilot.CurrentLocationId);
    }

    [Fact]
    public void AShipInteriorInSpace_HasNoRosterOfItsOwn_AndLeavesThePlanetsAlone()
    {
        var server = NewServer("shipint", configure: c => c.PlaceStarterShip = true); // the interior needs the hull
        var settler = OnFoot(server, "Settler");
        string home = server.ActiveLocationId;

        // Out of the parked ship and onto the ground beside it: the wildlife is for players on foot.
        int x = (int)settler.State.Position.X + 40, z = (int)settler.State.Position.Z, y = 250;
        while (y > 1 && server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
        {
            y--;
        }

        settler.State.Position = new Vector3f(x + 0.5f, y + 1, z + 0.5f);
        var roster = server.SpeciesRoster.ToList();
        Assert.NotEmpty(roster);

        var pilot = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        server.EnterShipInterior("Pilot");
        Assert.True(server.InShipInterior("Pilot"));

        AssertTheVoidWorldLeftThePlanetAlone(server, home, roster, pilot.CurrentLocationId);
    }

    private static void AssertTheVoidWorldLeftThePlanetAlone(SvGameServer server, string home, List<CreatureSpecies> roster, string voidWorld)
    {
        // The void world is the one that loaded last, and it has no animals of its own.
        Assert.Equal(voidWorld, server.ActiveLocationId);
        Assert.Empty(server.SpeciesRoster);

        // The planet's roster is what it was — and its wildlife goes on: the spawner still has species to place.
        At(server, home);
        Assert.Equal(roster, server.SpeciesRoster);
        ((List<CombatEntity>)server.Creatures).RemoveAll(c => !c.IsGiant);
        Ticks(server, 30.0);

        At(server, home);
        var wild = Wild(server);
        Assert.NotEmpty(wild);
        Assert.All(wild, c => Assert.Contains(roster, sp => BornAs(sp, c)));
        At(server, voidWorld);
        Assert.Empty(server.SpeciesRoster);
        Assert.Empty(server.Creatures);
    }

    // ---------------- The plants of a world ----------------

    [Fact]
    public void EveryWorld_KeepsItsOwnPlantsAndTrees_WhileAnotherWorldIsResident()
    {
        var server = NewServer("flora");
        OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        string plantBlock = FloraCatalog.All.Select(f => f.Key).First(key => server.FloraSpeciesForBlock(key) is not null);
        var plant = server.FloraSpeciesForBlock(plantBlock)!;
        var tree = server.TreeSpeciesForBlock("wood_log");
        Assert.NotNull(tree);
        var fruit = Enum.GetValues<TreeKind>().Select(kind => (server.FruitShapeForTest(kind), server.FruitTintForTest(kind))).ToList();

        LandOnAnotherWorld(server, home, "Visitor", out string other);
        var otherPlant = server.FloraSpeciesForBlock(plantBlock);
        var otherTree = server.TreeSpeciesForBlock("wood_log");
        Assert.NotEqual(plant.Name, otherPlant?.Name); // another world, another plant behind the same block
        Assert.NotEqual(tree!.Name, otherTree?.Name);

        Ticks(server, 2.0);

        At(server, home);
        Assert.Same(plant, server.FloraSpeciesForBlock(plantBlock));
        Assert.Same(tree, server.TreeSpeciesForBlock("wood_log"));
        Assert.Equal(fruit, Enum.GetValues<TreeKind>().Select(kind => (server.FruitShapeForTest(kind), server.FruitTintForTest(kind))).ToList());

        At(server, other);
        Assert.Same(otherPlant, server.FloraSpeciesForBlock(plantBlock));
        Assert.Same(otherTree, server.TreeSpeciesForBlock("wood_log"));
    }

    [Fact]
    public void AHarvestedPlant_GrowsBack_WhileAWorldWithNothingToRegrowIsResident()
    {
        var server = NewServer("regrow");
        InTheAir(server, "Keeper");
        string home = server.ActiveLocationId;
        LandOnAnotherWorld(server, home, "Visitor", out _); // nothing was harvested there: its regrow list is empty

        At(server, home);
        var world = server.WorldAt(home)!;
        var host = new Vector3i(2, 199, 0);
        var cell = new Vector3i(2, 200, 0);
        var plant = _content.GetBlock("flora_plant")!.NumericId;
        world.SetBlock(host, _content.GetBlock("mud")!.NumericId);
        world.SetBlock(cell, plant);
        server.MineBlock("Keeper", cell.X, cell.Y, cell.Z);
        Assert.True(world.GetBlock(cell).IsAir, "the plant should be gone right after the harvest");

        Ticks(server, 90.0); // three times the regrow time; both worlds tick, turn and turn about

        Assert.Equal(plant.Value, world.GetBlock(cell).Value);
    }

    // ---------------- The feed-tame queue of a world ----------------

    private const string MmpId = "au_mini_michi_paul"; // the jungle's biped: two favourite meals win one over

    [Fact]
    public void AFeedTame_IsCompletedOnItsOwnWorld_WhileAnotherWorldsAnimalsTickInBetween()
    {
        var server = NewServer("feed");
        var paul = server.AddLocalPlayer("Paul");
        paul.State.AboardShip = false;
        string home = server.ActiveLocationId;
        var sp = server.SpeciesRoster.Single(s => s.Id == MmpId);
        Assert.Equal("fruit_banana", sp.FavouriteFood);

        // A second world with a player on foot and animals around them: its creature tick runs after the home world's.
        var visitor = LandOnAnotherWorld(server, home, "Visitor", out string other);
        server.SpawnCreatureAtForTest(visitor.State.Position);
        At(server, home);

        // A stone pad high above the home world's terrain, the player on it with bananas, a small herd beside them.
        var world = server.WorldAt(home)!;
        const int cx = 300, cz = 300, padY = 230;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -16; dx <= 16; dx++)
        {
            for (int dz = -16; dz <= 16; dz++)
            {
                world.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        paul.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
        paul.State.Yaw = 0f;
        paul.State.Inventory.SetSlot(0, new ItemStack("fruit_banana", 6));
        paul.State.SelectedHotbarSlot = 0;
        var herd = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            herd.Add(server.SpawnCreatureAtForTest(new Vector3f(cx + 5.5f + i, padY + 1, cz + 0.5f + (i % 2)), MmpId));
        }

        // Every other wild animal of the home world stays out of it: none begs or rushes a piece.
        void Step()
        {
            At(server, home);
            foreach (var c in server.Creatures)
            {
                if (!herd.Contains(c.Id) && !c.IsCompanion)
                {
                    c.BegCooldownUntil = 1e9;
                    c.FrozenTimer = 1e9;
                }
            }

            server.TickForTest(0.1);
            At(server, home);
        }

        void ThrowAndWaitUntilEaten()
        {
            for (int i = 0; i < 300 && server.ThrownFoodCellForTest() is null; i++)
            {
                server.ThrowFoodForTest("Paul", 0);
                At(server, home);
                if (server.ThrownFoodCellForTest() is null)
                {
                    Step(); // no one begging yet — wait for the herd
                }
            }

            Assert.NotNull(server.ThrownFoodCellForTest());
            for (int i = 0; i < 150 && server.ThrownFoodCellForTest() is not null; i++)
            {
                Step();
            }

            Assert.Null(server.ThrownFoodCellForTest()); // eaten
        }

        ThrowAndWaitUntilEaten();
        Assert.Equal(1, server.FavouriteMealsForTest("Paul", MmpId));
        ThrowAndWaitUntilEaten();
        Step();
        Step(); // the tame runs a tick after the meal — on the world the meal was eaten on

        var companion = Assert.Single(server.CompanionEntitiesForTest("Paul"));
        Assert.Equal(MmpId, companion.SpeciesId);
        Assert.Equal(0, server.FavouriteMealsForTest("Paul", MmpId));
        Assert.True(server.ActivateWorldForTest(other));
        Assert.Empty(server.CompanionEntitiesForTest("Paul")); // and nothing of it on the other world
    }

    // ---------------- The uptime gates of a world: burning, sentry posts, the Crystal Net's sent levels ----------------
    //
    // Every case below drives the real server tick. The …ForTest hooks of these systems reset the gates, which is
    // exactly what hid the fault: the uptime stands still inside one server tick, so one gate for the whole server is
    // taken by the world that is ticked first — here always the home world, whose player joined first.

    /// <summary>A pool of lava high in the air of a world, an animal of that world standing in it that cannot walk
    /// out, and the player beside it — so the animal is inside the range the server simulates.</summary>
    private CombatEntity AnimalInLava(SvGameServer server, string location, SvSession beside, out Vector3f at)
    {
        At(server, location);
        var world = server.WorldAt(location)!;
        var lava = _content.GetBlock("lava")!.NumericId;
        var stone = _content.GetBlock("stone")!.NumericId;
        const int y = 300;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                world.SetBlock(new Vector3i(dx, y - 1, dz), stone);
                world.SetBlock(new Vector3i(dx, y, dz), lava);
            }
        }

        at = new Vector3f(0.5f, y + 0.5f, 0.5f);
        beside.State.Position = new Vector3f(3.5f, y + 1, 0.5f);
        var species = server.SpeciesRoster.First(s => !s.IsGiant && s.Habitat != CreatureHabitat.Lava);
        string id = server.SpawnCreatureAtForTest(at, species.Id);
        var beast = server.Creatures.First(c => c.Id == id);
        beast.HullMax = 100_000f; // it outlives the test: what is measured is the health it loses
        beast.Hull = beast.HullMax;
        return beast;
    }

    /// <summary>Real server ticks with the animal held in its pool (the creature tick would walk it out).</summary>
    private static void TicksInLava(SvGameServer server, CombatEntity beast, Vector3f at, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            beast.Position = at;
            beast.FrozenTimer = 1e9;
            server.TickForTest(0.1);
        }
    }

    [Fact]
    public void LavaBurnsTheAnimalsOfAWorld_WhileAnotherWorldIsTickedBeforeIt()
    {
        var server = NewServer("burn");
        OnFoot(server, "Keeper"); // the home world: ticked first, on every server tick
        string home = server.ActiveLocationId;
        var visitor = LandOnAnotherWorld(server, home, "Visitor", out string other);
        var beast = AnimalInLava(server, other, visitor, out var at);
        float full = beast.Hull;

        TicksInLava(server, beast, at, 2.0);

        At(server, other);
        Assert.Contains(beast, server.Creatures);
        Assert.True(beast.Hull < full, "the animal stands in lava on the second world and loses nothing");
    }

    [Fact]
    public void AWorldThatWasNotTickedForAWhile_DoesNotBurnTheWholeGapInOnePass()
    {
        var server = NewServer("burngap");
        var keeper = OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        var beast = AnimalInLava(server, home, keeper, out var at);
        float full = beast.Hull;
        var beside = keeper.State.Position;
        LandOnAnotherWorld(server, home, "Visitor", out _);

        TicksInLava(server, beast, at, 2.0);
        float warm = beast.Hull;
        Assert.True(warm < full, "the animal does not burn at all");

        // The Keeper logs off. The home body stays resident — it is the server's default body — and is not ticked
        // while its only player is away; the other world goes on for a minute.
        server.DisconnectLocalPlayerForTest("Keeper");
        Ticks(server, 60.0);
        At(server, home);
        Assert.Contains(beast, server.Creatures);
        Assert.Equal(warm, beast.Hull); // nothing happened on a world nobody ticked

        // Back again: the first burn pass of the home world. The animal did not stand in the lava for that minute.
        OnFoot(server, "Keeper").State.Position = beside;
        TicksInLava(server, beast, at, 0.1);
        At(server, home);
        Assert.Contains(beast, server.Creatures);
        float lost = warm - beast.Hull;
        Assert.InRange(lost, 1f, 20f); // one pass's worth at most (a second in lava costs 15) — not the minute's 900
    }

    [Fact]
    public void TheSentryPostsOfAWorld_Fire_WhileAnotherWorldIsTickedBeforeIt()
    {
        var server = NewServer("sentries");
        OnFoot(server, "Keeper"); // the home world: ticked first, and it has no base at all
        string home = server.ActiveLocationId;
        var visitor = LandOnAnotherWorld(server, home, "Visitor", out string other);

        // The visitor's base on the other world: a core in the air beside them, a sentry post next to it, and a hostile
        // machine four blocks from the post.
        At(server, other);
        var feet = visitor.State.Position;
        var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
        server.PlaceBaseForTest(visitor, core);
        int baseId = server.BaseSnapshots.Single(b => b.OwnerId == visitor.State.PlayerId).Id;
        var post = new Vector3i(core.X + 1, core.Y, core.Z);
        server.WorldAt(other)!.SetBlock(post, _content.GetBlock("sentry_post")!.NumericId, 0, 0, 0, visitor.State.Name);
        At(server, other);
        Assert.Equal(1, server.SentryCountForTest(baseId));
        var target = new Vector3f(post.X + 4.5f, post.Y + 0.5f, post.Z + 0.5f);
        server.SpawnPlanetEnemyAtForTest(target);
        var machine = server.PlanetEnemies[^1];
        machine.HullMax = 100_000f; // it outlives the test: what is measured is the hits it takes
        machine.Hull = machine.HullMax;
        int scans = server.SentryRescansForTest;

        for (int i = 0; i < 30; i++)
        {
            machine.Position = target; // it does not get to walk out of the post's reach
            server.TickForTest(0.1);
        }

        At(server, other);
        Assert.Contains(machine, server.PlanetEnemies);
        Assert.True(machine.Hull < machine.HullMax, "the sentry post on the second world never fired");

        // And the home world's pass left this base's cached cells alone: they were derived once in these three
        // seconds, on the first pass — not again on every pass because the other world had thrown them away.
        Assert.Equal(scans + 1, server.SentryRescansForTest);
    }

    /// <summary>A player in the air of their own world with a switch and a conduit beside them: one network, OFF.</summary>
    private static Vector3i SwitchAndConduit(SvGameServer server, SvSession p, string location)
    {
        At(server, location);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.SuitEnergy = 100f;
        p.State.ModeOverride = PlayerModeOverride.Creative;
        foreach (string key in new[] { "crystal_switch", "crystal_conduit" })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 4, 64));
        }

        var lever = new Vector3i(1, 200, 0);
        server.PlaceBlock(p.State.PlayerId, lever.X, lever.Y, lever.Z, "crystal_switch");
        server.PlaceBlock(p.State.PlayerId, 2, 200, 0, "crystal_conduit");
        At(server, location);
        Assert.Single(server.CrystalNetSnapshots);
        return lever;
    }

    private static List<CrystalNetList> NetListsSentTo(RecordingTransport t, SvSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is CrystalNetList).Select(x => (CrystalNetList)x.Msg).ToList();

    [Fact]
    public void TheCrystalNetOfAWorld_TellsItsPlayersOfALevelChange_WhileAnotherWorldsNetHasTheSameId()
    {
        var t = new RecordingTransport();
        var server = NewServer("levels", t);
        var keeper = server.AddLocalPlayer("Keeper");
        string home = server.ActiveLocationId;
        var homeLever = SwitchAndConduit(server, keeper, home);
        int netId = server.CrystalNetSnapshots[0].Id;

        var visitor = LandOnAnotherWorld(server, home, "Visitor", out string other);
        var otherLever = SwitchAndConduit(server, visitor, other);
        Assert.Equal(netId, server.CrystalNetSnapshots[0].Id); // net ids start over on every world: one id, two networks

        // The other world's network goes ON, the home world's stays OFF.
        At(server, other);
        server.SetCrystalDeviceForTest(visitor, otherLever, action: 0);
        Ticks(server, 1.0, 0.1);
        At(server, other);
        Assert.True(server.CrystalLevelAt(otherLever));
        At(server, home);
        Assert.False(server.CrystalLevelAt(homeLever));

        // Nothing changes: nothing is sent. With one table of sent levels for the whole server each world found the
        // other's level behind its own net id and sent its whole list again, beat after beat.
        t.Sent.Clear();
        Ticks(server, 2.0, 0.1);
        Assert.Empty(NetListsSentTo(t, keeper));
        Assert.Empty(NetListsSentTo(t, visitor));

        // The Keeper flips the home lever: the home world's player is told that its network is ON now — although the
        // other world's network with the same id has been ON all along.
        At(server, home);
        server.SetCrystalDeviceForTest(keeper, homeLever, action: 0);
        Ticks(server, 1.0, 0.1);
        At(server, home);
        Assert.True(server.CrystalLevelAt(homeLever));
        var told = NetListsSentTo(t, keeper).LastOrDefault();
        Assert.NotNull(told);
        Assert.True(Assert.Single(told!.Nets).On);
        Assert.Empty(NetListsSentTo(t, visitor)); // and nobody on the other world hears of it
    }

    // ---------------- The terrain a world's systems read ----------------

    /// <summary>A generator of its own for one resident world: the server's seed and galaxy settings, that world's mode.</summary>
    private static WorldGenerator GeneratorFor(SvGameServer server, ServerWorld world)
    {
        var generator = server.FreshGeneratorForTest();
        generator.SetWorldMode(world.Circumference, world.Cratered, world.LandingPadFlats, world.LocationId, world.FrontierOreBoost);
        return generator;
    }

    /// <summary>The server has ONE generator for every resident world, and it keeps the mode (size, cratering, landing
    /// pads, the body's own salt) of whichever world configured it last. The creature spawner, the giants and the
    /// ground-height fallback ask it directly — so it has to follow the cursor, or a world reads another body's terrain.</summary>
    [Fact]
    public void TheTerrainQueriesOfAWorld_ReadThatWorldsTerrain_AfterAnotherWorldWasLoaded()
    {
        var server = NewServer("terrain");
        OnFoot(server, "Keeper");
        string home = server.ActiveLocationId;
        LandOnAnotherWorld(server, home, "Visitor", out string other); // the generator was configured for this world last
        var homeWorld = server.WorldAt(home)!;
        var otherWorld = server.WorldAt(other)!;
        var forHome = GeneratorFor(server, homeWorld);
        var forOther = GeneratorFor(server, otherWorld);
        var columns = new[] { (X: 40, Z: 40), (X: -300, Z: 120), (X: 1000, Z: -200), (X: 2500, Z: 77), (X: -1700, Z: -450), (X: 3333, Z: 600) };

        // The two bodies are different ground: the home world's columns read in the other world's mode come out wrong.
        Assert.Contains(columns, c => forHome.SurfaceHeight(homeWorld.Planet, c.X, c.Z) != forOther.SurfaceHeight(homeWorld.Planet, c.X, c.Z));

        At(server, home);
        Assert.All(columns, c => Assert.Equal(forHome.SurfaceHeight(homeWorld.Planet, c.X, c.Z), server.SurfaceHeightForTest(c.X, c.Z)));
        At(server, other);
        Assert.All(columns, c => Assert.Equal(forOther.SurfaceHeight(otherWorld.Planet, c.X, c.Z), server.SurfaceHeightForTest(c.X, c.Z)));

        // And through the tick, which sets the cursor the same way: both worlds tick, turn and turn about, the other
        // world's chunk streaming configures the generator again and again — the home world still reads its own ground.
        Ticks(server, 2.0, 0.1);
        At(server, home);
        Assert.All(columns, c => Assert.Equal(forHome.SurfaceHeight(homeWorld.Planet, c.X, c.Z), server.SurfaceHeightForTest(c.X, c.Z)));
    }
}
