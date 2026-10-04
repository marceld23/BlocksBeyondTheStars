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
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The fixes after the bio lab's first code read (#2214, #2215, #2217), through the real server: the clone tank's
/// species list follows the sample case, a finished cross waits for room, the habitat rule reads every block, a tank
/// over the cap refuses a start, a growing tank starts its wait over after a reload, the free game mode is free on
/// both paths, a tank remembers the species of every living clone, a guest clone moves on a world without a roster,
/// the sampler takes a giant without stasis and names a full register, and an empty seedling says so. After the review
/// of those fixes: a tank's clones come back as their own world's animals while another world is resident (#2226: the
/// species tables are per world), the clones that are not back yet count against the cap, a plant cross waits until
/// both its samples fit, a growing tank releases what it was started on and keeps its place inside the cap. After the
/// review of the per-world tables: a tamed guest clone and its wild sibling keep their movement profile when their world
/// is loaded again.
/// </summary>
public sealed class BioTankFixTests : IDisposable
{
    private static readonly Vector3i Tank = new(1, 200, 0);

    private readonly string _root;
    private readonly GameContent _content;

    public BioTankFixTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_biotank_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string planet = "jungle", string world = "tank", IServerTransport? transport = null,
        Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player high up in the air (empty cells, everything in reach) with the given items and no starter kit.</summary>
    private static SvSession Player(SvGameServer server, string name, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.SuitEnergy = 100f;
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

    /// <summary>The player "Keeper" with matter dust and a clone tank standing at <see cref="Tank"/>.</summary>
    private static SvSession TankOwner(SvGameServer server)
    {
        var p = Player(server, "Keeper", "clone_tank");
        Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        return p;
    }

    private BlockId Block(string key) => _content.GetBlock(key)!.NumericId;

    private static void Ticks(SvGameServer server, double seconds, double step = 0.5)
    {
        for (double t = 0; t < seconds; t += step)
        {
            server.TickForTest(step);
        }
    }

    private static int Samples(SvSession p, uint seed)
        => p.State.SampleCase.CountOf(ItemKey.WithSeed(BioItems.Sample, seed));

    private static string Choice(uint seed) => SvGameServer.TankChoiceForTest(seed);

    private static List<CombatEntity> Clones(SvGameServer server) => server.Creatures.Where(c => c.CloneOf.Length > 0).ToList();

    private static void Configure(SvGameServer server, SvSession p, Vector3i tank, string config)
        => server.SetCrystalDeviceForTest(p, tank, action: 2, mode: 0, config: config);

    private static void Start(SvGameServer server, SvSession p, Vector3i tank)
        => server.SetCrystalDeviceForTest(p, tank, action: 1);

    private static string? ConfigValue(SvGameServer server, Vector3i tank, string key)
        => (server.CrystalDeviceConfig(tank) ?? string.Empty).Split(';')
            .Where(part => part.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(part => part.Substring(key.Length + 1))
            .FirstOrDefault();

    private static NetCrystalDevice? LastTankSent(RecordingTransport t)
        => t.Sent.Select(x => x.Msg).OfType<CrystalDeviceList>().LastOrDefault()?.Devices
            .FirstOrDefault(d => d.X == Tank.X && d.Y == Tank.Y && d.Z == Tank.Z);

    private static List<string> RejectionsTo(RecordingTransport t, SvSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is ActionRejected).Select(x => ((ActionRejected)x.Msg).Reason).ToList();

    /// <summary>Two peaceful land or air species of the test world — animals that need no water beside the tank.</summary>
    private static List<CreatureSpecies> LandSpecies(SvGameServer server)
    {
        var list = server.SpeciesRoster
            .Where(s => !s.Hostile && !s.IsGiant && s.Habitat is CreatureHabitat.Land or CreatureHabitat.Air)
            .Take(2).ToList();
        Assert.Equal(2, list.Count);
        return list;
    }

    /// <summary>Fills every free slot of the sample case with a sample of its own kind: no room for a new species.</summary>
    private static void FillSampleCase(SvSession p)
    {
        for (int i = 0; i < p.State.SampleCase.SlotCount; i++)
        {
            if (p.State.SampleCase.Slots[i] is null)
            {
                p.State.SampleCase.SetSlot(i, new ItemStack(ItemKey.WithSeed(BioItems.MineralSample, 0x7000u + (uint)i), 1));
            }
        }
    }

    private static void FreeOneSlot(SvSession p)
    {
        for (int i = 0; i < p.State.SampleCase.SlotCount; i++)
        {
            if (p.State.SampleCase.Slots[i] is { } s && ItemKey.Base(s.Item) == BioItems.MineralSample)
            {
                p.State.SampleCase.SetSlot(i, null);
                return;
            }
        }

        Assert.Fail("no filler slot to free");
    }

    /// <summary>Grows one clone of a sample species in the tank and returns it.</summary>
    private static CombatEntity GrowClone(SvGameServer server, SvSession p, uint seed)
    {
        var before = Clones(server).Select(c => c.Id).ToHashSet();
        Configure(server, p, Tank, "sp=" + Choice(seed));
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        return Assert.Single(Clones(server), c => !before.Contains(c.Id));
    }

    // ---------------- 1. The species list follows the sample case ----------------

    [Fact]
    public void TheSpeciesList_FollowsTheSampleCase_WithoutAClick()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = TankOwner(server);
        server.World.SetBlock(new Vector3i(-2, 200, 0), Block(BioItems.Lab));
        Ticks(server, 1.5, 0.1); // the placement's own list has gone out

        t.Sent.Clear();
        Ticks(server, 2.0, 0.1);
        Assert.DoesNotContain(t.Sent, x => x.Msg is CrystalDeviceList); // nothing changed: nothing is sent

        // A sample gained while the tank already stands: the list goes out again, with the new choice.
        uint seed = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed));
        Ticks(server, 1.0, 0.1);
        var gained = LastTankSent(t);
        Assert.NotNull(gained);
        Assert.Contains(gained!.Choices, c => c.StartsWith(Choice(seed) + "|", StringComparison.Ordinal));

        // More of the same species changes nothing a tank could offer: no new list.
        t.Sent.Clear();
        Assert.True(server.GiveSampleForTest(p, seed));
        Ticks(server, 1.0, 0.1);
        Assert.DoesNotContain(t.Sent, x => x.Msg is CrystalDeviceList);

        // Spent in the lab (another file's code path): the choice leaves the list with the last sample.
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed });
        Assert.Equal(0, Samples(p, seed));
        Ticks(server, 1.0, 0.1);
        var spent = LastTankSent(t);
        Assert.NotNull(spent);
        Assert.DoesNotContain(spent!.Choices, c => c.StartsWith(Choice(seed) + "|", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSpeciesList_FollowsAScan_Too()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        TankOwner(server);
        Ticks(server, 1.5, 0.1);
        var sp = LandSpecies(server)[0];
        Assert.DoesNotContain(LastTankSent(t)!.Choices, c => c.StartsWith(sp.Id + "|", StringComparison.Ordinal));

        server.ScanSubject("Keeper", "creature", sp.Id);
        Ticks(server, 1.0, 0.1);
        Assert.Contains(LastTankSent(t)!.Choices, c => c.StartsWith(sp.Id + "|", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSpeciesList_IsNotSentAgain_ForAScanThatIsNoAnimal()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = TankOwner(server);
        Ticks(server, 1.5, 0.1);

        // A block and a plant: discoveries for the Codex, and nothing a tank could offer.
        t.Sent.Clear();
        int known = p.State.ScannedWhere.Count;
        server.ScanSubject("Keeper", "block", "dirt");
        server.ScanSubject("Keeper", "block", "flora_bush");
        Assert.Equal(known + 2, p.State.ScannedWhere.Count); // both went into the first-scan ledger
        Ticks(server, 2.0, 0.1);
        Assert.DoesNotContain(t.Sent, x => x.Msg is CrystalDeviceList);
    }

    // ---------------- 2. A finished cross waits for room ----------------

    [Fact]
    public void AFinishedAnimalCross_WaitsForRoomInTheSampleCase_AndSaysSoOnce()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = TankOwner(server);
        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        var parents = LandSpecies(server);
        uint a = server.GiveCreatureSampleForTest(p, parents[0], 2);
        uint b = server.GiveCreatureSampleForTest(p, parents[1], 2);
        uint child = BioHash.CrossSeed(a, b);
        FillSampleCase(p); // two parent stacks that stay, and 22 other kinds: no slot for a new species

        Configure(server, p, Tank, "sp=" + parents[0].Id + ";x=" + parents[1].Id);
        Ticks(server, 1.0, 0.1);
        t.Sent.Clear();
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));

        // 8. The start of the job goes out with the device list. (The server sent one here and at the finish before
        // the fix, too — through the tank's light; these lines hold that in place for the menu that follows it.)
        Ticks(server, 0.5, 0.1);
        Assert.Contains("growing=1", LastTankSent(t)?.Config ?? string.Empty);
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 20.0);

        // Done growing — and waiting: no animal without its sample, the light stays on, the owner heard it once.
        Assert.Empty(Clones(server));
        Assert.Equal(0, Samples(p, child));
        Assert.True(server.CrystalDeviceOutput(Tank));
        Assert.Equal("1", ConfigValue(server, Tank, "growing"));
        Assert.Single(t.Sent, x => x.Conn == p.ConnectionId && x.Msg is ServerMessage { Text: "@srv.crystal.clone_case_full" });

        // Room: the animal and the sample come together.
        FreeOneSlot(p);
        Ticks(server, 3.0);
        var clone = Assert.Single(Clones(server));
        Assert.StartsWith("gx", clone.SpeciesId);
        Assert.Equal(1, Samples(p, child));
        Assert.False(server.CrystalDeviceOutput(Tank));
        Assert.Equal("0", ConfigValue(server, Tank, "growing"));
        Assert.Single(t.Sent, x => x.Conn == p.ConnectionId && x.Msg is ServerMessage { Text: "@srv.crystal.clone_case_full" });

        // 8. And so does the finished job: the tank's new state, its new species and the new choice.
        var sent = LastTankSent(t);
        Assert.NotNull(sent);
        Assert.Contains("growing=0", sent!.Config);
        Assert.Contains("sp=" + Choice(child), sent.Config);
        Assert.Contains(sent.Choices, c => c.StartsWith(Choice(child) + "|", StringComparison.Ordinal));
    }

    [Fact]
    public void AFinishedPlantCross_WaitsForRoomInTheSampleCase()
    {
        var server = NewServer();
        var p = TankOwner(server);
        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        uint bush = server.GiveFloraSampleForTest(p, "flora_bush", 2);
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 2);
        uint child = BioHash.CrossSeed(bush, fern);
        FillSampleCase(p);

        Configure(server, p, Tank, "sp=" + Choice(bush) + ";x=" + Choice(fern));
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 5.0);
        Assert.Equal(0, Samples(p, child));
        Assert.Equal("1", ConfigValue(server, Tank, "growing")); // the two samples wait in the tank

        FreeOneSlot(p);
        Ticks(server, 3.0);
        Assert.Equal(2, Samples(p, child));
        Assert.Equal("0", ConfigValue(server, Tank, "growing"));
    }

    [Theory]
    [InlineData(1)] // one of the two samples would fit: the tank must not hand out half of what it made
    [InlineData(0)] // a full stack
    public void AFinishedPlantCross_WaitsForRoomInTheStackOfItsSpecies(int room)
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = TankOwner(server);
        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        uint bush = server.GiveFloraSampleForTest(p, "flora_bush", 2);
        uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 2);
        uint child = server.CrossForTest(bush, fern)!.Seed;
        int held = BioRules.SampleStack - room;
        Assert.True(server.GiveSampleForTest(p, child, held)); // the owner crossed this pair before, many times

        Configure(server, p, Tank, "sp=" + Choice(bush) + ";x=" + Choice(fern));
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 5.0);

        // The two samples are all a plant cross makes: they wait in the tank until both fit.
        Assert.Equal(held, Samples(p, child));
        Assert.Equal("1", ConfigValue(server, Tank, "growing"));
        Assert.Single(t.Sent, x => x.Conn == p.ConnectionId && x.Msg is ServerMessage { Text: "@srv.crystal.clone_case_full" });

        Assert.True(p.State.SampleCase.Remove(ItemKey.WithSeed(BioItems.Sample, child), 2 - room));
        Ticks(server, 3.0);
        Assert.Equal(BioRules.SampleStack, Samples(p, child));
        Assert.Equal("0", ConfigValue(server, Tank, "growing"));
    }

    [Fact]
    public void AFinishedAnimalCross_ComesOut_WhenTheOwnerHoldsAFullStackOfTheNewSpecies()
    {
        var server = NewServer();
        var p = TankOwner(server);
        p.State.UnlockedBlueprints.Add(BioItems.CrossingBlueprint);
        var parents = LandSpecies(server);
        uint a = server.GiveCreatureSampleForTest(p, parents[0], 2);
        uint b = server.GiveCreatureSampleForTest(p, parents[1], 2);
        uint child = server.CrossForTest(a, b)!.Seed;
        Assert.True(server.GiveSampleForTest(p, child, BioRules.SampleStack));

        Configure(server, p, Tank, "sp=" + parents[0].Id + ";x=" + parents[1].Id);
        Start(server, p, Tank);
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 2.0);

        // The animal is what the tank made; the sample that comes with it is missing from nobody who holds twenty.
        Assert.StartsWith("gx", Assert.Single(Clones(server)).SpeciesId);
        Assert.Equal(BioRules.SampleStack, Samples(p, child));
        Assert.Equal("0", ConfigValue(server, Tank, "growing"));
    }

    // ---------------- 3. The habitat rule reads every block ----------------

    [Fact]
    public void ASwimmer_IsGrownBesideASingleBlockOfWater()
    {
        var server = NewServer();
        var p = TankOwner(server);
        var swimmer = new CreatureSpecies
        {
            Id = "swimmer",
            Name = "Test Swimmer",
            Habitat = CreatureHabitat.Water,
            Temperament = CreatureTemperament.Passive,
            Legs = 0,
            VoiceSeed = 4711,
            DropItem = "creature_meat",
        };
        uint seed = server.RegisterForeignCreatureForTest(swimmer, "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed, 2));
        Configure(server, p, Tank, "sp=" + Choice(seed));

        Start(server, p, Tank); // up in the air: no water anywhere near
        Assert.False(server.CrystalDeviceOutput(Tank));
        Assert.Equal(2, Samples(p, seed));

        // One block of water right beside the tank — an odd offset, which a step of two never read.
        server.World.SetBlock(new Vector3i(Tank.X + 1, Tank.Y, Tank.Z), Block("water"));
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Assert.Equal(1, Samples(p, seed));
    }

    // ---------------- 4. A tank over the cap ----------------

    [Fact]
    public void ATankOverTheCap_RefusesAStart_AndTakesNothing()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = Player(server, "Keeper");
        Assert.Equal(0, p.State.Inventory.Add("clone_tank", 3, 64));
        Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
        var third = new Vector3i(1, 200, -2);
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        server.PlaceBlock("Keeper", 1, 200, 2, "clone_tank");
        server.PlaceBlock("Keeper", third.X, third.Y, third.Z, "clone_tank"); // over the cap of two: registered inert
        uint seed = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed, 2));
        Configure(server, p, third, "sp=" + Choice(seed));

        t.Sent.Clear();
        Start(server, p, third);

        Assert.False(server.CrystalDeviceOutput(third));
        Assert.NotEqual("1", ConfigValue(server, third, "growing"));
        Assert.Equal(2, Samples(p, seed));
        Assert.Equal(64, p.State.Inventory.CountOf("matter_dust"));
        Assert.Contains(t.Sent, x => x.Conn == p.ConnectionId && x.Msg is ShipAiLine { LineKey: "vega.sys.crystal_cap" });

        // A tank inside the cap still works with the very same settings.
        Configure(server, p, Tank, "sp=" + Choice(seed));
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Assert.Equal(1, Samples(p, seed));
    }

    // ---------------- 5. A growing tank and a reload ----------------

    [Fact]
    public void AGrowingTank_StartsItsWaitOver_AfterTheWorldWasLeft()
    {
        {
            var server = NewServer(world: "wait");
            var p = TankOwner(server);
            uint seed = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
            Assert.True(server.GiveSampleForTest(p, seed));
            Configure(server, p, Tank, "sp=" + Choice(seed));
            Start(server, p, Tank);
            Ticks(server, 10.0);
            Assert.True(server.CrystalDeviceOutput(Tank));
            server.SaveAllForTest();
            server.Stop();
        }

        {
            var server = NewServer(world: "wait");
            server.AddLocalPlayer("Keeper");
            Ticks(server, 20.0);
            Assert.Empty(Clones(server));                    // not at once: the wait started over
            Assert.True(server.CrystalDeviceOutput(Tank));   // and the tank shows that it is growing

            Ticks(server, CrystalNetRules.CloneGrowSeconds - 15.0);
            Assert.Single(Clones(server));
            Assert.Equal("0", ConfigValue(server, Tank, "growing"));
        }
    }

    [Fact]
    public void AGrowingTank_KeepsItsPlaceInsideTheCap_AfterAReload()
    {
        // Which tank of an owner is over the cap follows the order the tanks register in. Placed, that is the order
        // of placing; loaded, it is the store's order — by coordinate. The spare tank is placed LAST and sorts FIRST.
        var growing = new Vector3i(1, 200, 2);
        var spare = new Vector3i(1, 200, -2);
        {
            var server = NewServer(world: "cap");
            var p = Player(server, "Keeper");
            Assert.Equal(0, p.State.Inventory.Add("clone_tank", 3, 64));
            Assert.Equal(0, p.State.Inventory.Add("matter_dust", 64, 1024));
            server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
            server.PlaceBlock("Keeper", growing.X, growing.Y, growing.Z, "clone_tank");
            server.PlaceBlock("Keeper", spare.X, spare.Y, spare.Z, "clone_tank"); // over the cap of two: inert
            uint seed = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
            Assert.True(server.GiveSampleForTest(p, seed));
            Configure(server, p, growing, "sp=" + Choice(seed));
            Start(server, p, growing);
            Assert.True(server.CrystalDeviceOutput(growing));
            Ticks(server, 10.0);
            server.SaveAllForTest();
            server.Stop();
        }

        {
            var server = NewServer(world: "cap");
            server.AddLocalPlayer("Keeper");
            Ticks(server, CrystalNetRules.CloneGrowSeconds + 2.0);

            // The job that was paid for finishes: the tank that grows is not the one left out.
            Assert.Single(Clones(server));
            Assert.Equal("0", ConfigValue(server, growing, "growing"));
        }
    }

    // ---------------- 6. A free game mode ----------------

    [Fact]
    public void InAFreeGameMode_TheNativePathIsFreeToo()
    {
        var server = NewServer();
        var p = Player(server, "Keeper", "clone_tank");
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        var sp = LandSpecies(server)[0];
        p.State.ScannedCreatureSites.Add(server.ActiveLocationId + ":" + sp.Id); // scanned here: a native choice
        Configure(server, p, Tank, "sp=" + sp.Id);

        Start(server, p, Tank); // explorer rules: a bait and two matter dust, and the pocket is empty
        Assert.False(server.CrystalDeviceOutput(Tank));

        p.State.ModeOverride = PlayerModeOverride.Creative; // the owner's mode counts
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        Assert.Equal(sp.Id, Assert.Single(Clones(server)).SpeciesId);
    }

    [Fact]
    public void TheSpeciesSetting_ChangedWhileTheTankGrows_NeitherSwapsTheAnimalNorLosesTheJob()
    {
        var server = NewServer();
        var p = Player(server, "Keeper", "clone_tank");
        server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
        var species = LandSpecies(server);
        p.State.ScannedCreatureSites.Add(server.ActiveLocationId + ":" + species[0].Id); // only the first is scanned
        p.State.ModeOverride = PlayerModeOverride.Creative;
        uint sample = server.RegisterForeignCreatureForTest(species[1], "elsewhere");
        Assert.True(server.GiveSampleForTest(p, sample));

        // Another native species is picked while the tank grows — one the owner never scanned.
        Configure(server, p, Tank, "sp=" + species[0].Id);
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, 10.0);
        Configure(server, p, Tank, "sp=" + species[1].Id);
        Ticks(server, CrystalNetRules.CloneGrowSeconds);
        Assert.Equal(species[0].Id, Assert.Single(Clones(server)).SpeciesId); // what was started, not what is set
        Assert.Equal(species[1].Id, ConfigValue(server, Tank, "sp"));        // the setting itself is the player's
        Assert.True(string.IsNullOrEmpty(ConfigValue(server, Tank, "grow")));

        // A sample is picked while the tank grows: the job still ends with its animal.
        Configure(server, p, Tank, "sp=" + species[0].Id);
        Start(server, p, Tank);
        Assert.True(server.CrystalDeviceOutput(Tank));
        Ticks(server, 10.0);
        Configure(server, p, Tank, "sp=" + Choice(sample));
        Ticks(server, CrystalNetRules.CloneGrowSeconds);
        Assert.Equal(2, Clones(server).Count);
        Assert.All(Clones(server), c => Assert.Equal(species[0].Id, c.SpeciesId));
        Assert.Equal("0", ConfigValue(server, Tank, "growing"));
    }

    // ---------------- 7. The tank remembers every living clone ----------------

    [Fact]
    public void EveryClone_ComesBackAsItsOwnSpecies_WhateverTheTankIsSetToNow()
    {
        uint a, b;
        {
            var server = NewServer(world: "list");
            var p = TankOwner(server);
            var species = LandSpecies(server);
            a = server.RegisterForeignCreatureForTest(species[0], "elsewhere");
            b = server.RegisterForeignCreatureForTest(species[1], "far away");
            Assert.True(server.GiveSampleForTest(p, a));
            Assert.True(server.GiveSampleForTest(p, b));
            GrowClone(server, p, a);
            GrowClone(server, p, b);
            Assert.Equal(Choice(a) + "," + Choice(b), ConfigValue(server, Tank, "cl"));
            Assert.Equal("2", ConfigValue(server, Tank, "clones"));

            // The owner now sets the tank to a plant: the animals it grew are still its clones.
            uint plant = server.GiveFloraSampleForTest(p, "flora_bush");
            Configure(server, p, Tank, "sp=" + Choice(plant));
            Assert.Equal(Choice(a) + "," + Choice(b), ConfigValue(server, Tank, "cl")); // a client cannot drop the list
            server.SaveAllForTest();
            server.Stop();
        }

        {
            var server = NewServer(world: "list");
            server.AddLocalPlayer("Keeper");
            Ticks(server, 1.0);
            var back = Clones(server).Select(c => c.SpeciesId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var expected = new[] { "gx" + Choice(a).Substring(2), "gx" + Choice(b).Substring(2) }.OrderBy(id => id, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, back);
        }
    }

    [Fact]
    public void ADefeatedClone_IsForgotten_AndDoesNotComeBack()
    {
        uint a, b;
        {
            var server = NewServer(world: "defeat");
            var p = TankOwner(server);
            var species = LandSpecies(server);
            a = server.RegisterForeignCreatureForTest(species[0], "elsewhere");
            b = server.RegisterForeignCreatureForTest(species[1], "far away");
            Assert.True(server.GiveSampleForTest(p, a));
            Assert.True(server.GiveSampleForTest(p, b));
            var first = GrowClone(server, p, a);
            GrowClone(server, p, b);

            p.State.Position = first.Position;
            first.Hull = 1f; // #2280: one punch defeats it — the bare hand no longer lands 15-damage hits on every click
            for (int i = 0; i < 40 && server.Creatures.Any(c => c.Id == first.Id); i++)
            {
                server.AttackEntity("Keeper", first.Id);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == first.Id);
            Assert.Equal(Choice(b), ConfigValue(server, Tank, "cl")); // at once, not on the next beat
            Assert.Equal("1", ConfigValue(server, Tank, "clones"));
            server.SaveAllForTest();
            server.Stop();
        }

        {
            var server = NewServer(world: "defeat");
            server.AddLocalPlayer("Keeper");
            Ticks(server, 1.0);
            Assert.Equal("gx" + Choice(b).Substring(2), Assert.Single(Clones(server)).SpeciesId);
        }
    }

    [Fact]
    public void ATamedClone_IsACompanion_AndItsTankForgetsIt()
    {
        var server = NewServer();
        var p = TankOwner(server);
        Assert.Equal(0, p.State.Inventory.Add("creature_translator", 1, 1));
        foreach (string bait in new[] { "forage_bait", "meat_bait", "nectar_lure" })
        {
            Assert.Equal(0, p.State.Inventory.Add(bait, 50, 64));
        }

        var sp = LandSpecies(server)[0];
        sp.Temperament = CreatureTemperament.Passive; // a forgiving one, so the ritual is deterministic
        uint seed = server.GiveCreatureSampleForTest(p, sp); // sampled on this world: the clone is the native species
        var clone = GrowClone(server, p, seed);
        Assert.Equal(sp.Id, clone.SpeciesId);
        Assert.Equal(sp.Id, ConfigValue(server, Tank, "cl"));

        p.State.Position = clone.Position;
        server.TameDecodeForTest("Keeper");
        for (int i = 0; i < 30 && server.TameCurrentNeedForTest("Keeper") is { Length: > 0 } need; i++)
        {
            server.TameRespondForTest("Keeper", need);
        }

        Assert.Single(server.TamedCreaturesForTest("Keeper"));
        Assert.Empty(Clones(server));
        Assert.Equal(string.Empty, ConfigValue(server, Tank, "cl"));
        Assert.Equal("0", ConfigValue(server, Tank, "clones"));
    }

    [Fact]
    public void ATamedGuestClone_AndItsWildSibling_StillMove_AfterTheirWorldWasLoadedAgain()
    {
        // A floor high above the terrain, so the animals and their owner share one level: 33 × 33 around the tank.
        const int floorY = 199;
        string guestId;
        {
            var server = NewServer(world: "petwalk");
            var p = TankOwner(server);
            var stone = Block("stone");
            for (int x = -16; x <= 16; x++)
            {
                for (int z = -16; z <= 16; z++)
                {
                    server.World.SetBlock(new Vector3i(x, floorY, z), stone);
                }
            }

            Assert.Equal(0, p.State.Inventory.Add("creature_translator", 1, 1));
            foreach (string bait in new[] { "forage_bait", "meat_bait", "nectar_lure" })
            {
                Assert.Equal(0, p.State.Inventory.Add(bait, 50, 64));
            }

            // A land animal of another jungle world that is awake by day (the test world's clock stands in the morning),
            // and a forgiving one, so the taming ritual is deterministic.
            var foreign = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 4711)
                .First(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Land && s.Activity is CreatureActivity.Cathemeral or CreatureActivity.Diurnal);
            foreign.Temperament = CreatureTemperament.Passive;
            uint seed = server.RegisterForeignCreatureForTest(foreign, "elsewhere");
            Assert.True(server.GiveSampleForTest(p, seed, 2));
            guestId = GrowClone(server, p, seed).SpeciesId;
            GrowClone(server, p, seed);
            Assert.StartsWith("gx", guestId);
            Assert.True(server.LocomotionProfileForTest(guestId).CruiseSpeed > 0f); // grown here: the tank registered all of it

            // One of the two becomes a companion; its sibling stays the tank's clone.
            p.State.Position = Clones(server)[0].Position;
            server.TameDecodeForTest("Keeper");
            for (int i = 0; i < 30 && server.TameCurrentNeedForTest("Keeper") is { Length: > 0 } need; i++)
            {
                server.TameRespondForTest("Keeper", need);
            }

            Assert.Equal(guestId, Assert.Single(server.TamedCreaturesForTest("Keeper")).SpeciesId);
            Assert.Equal(guestId, Assert.Single(Clones(server)).SpeciesId);
            p.State.Position = new Vector3f(0.5f, floorY + 1, 0.5f);
            server.SaveAllForTest();
            server.Stop();
        }

        {
            // The world is loaded again: its species table holds the roster and nothing of the guest. The owner's pet
            // is put beside them first (on a join, or by the creature tick) — before the world's first Crystal Net beat
            // brings the tank's clone back.
            var server = NewServer(world: "petwalk");
            var p = server.AddLocalPlayer("Keeper");
            p.State.AboardShip = false;
            p.State.GodMode = true;
            server.ReconcileCompanionsForTest();
            var pet = Assert.Single(server.CompanionEntitiesForTest("Keeper"));
            Assert.Equal(guestId, pet.SpeciesId);
            Assert.Empty(Clones(server));
            Assert.True(server.LocomotionProfileForTest(guestId).CruiseSpeed > 0f, "the pet's species came without a movement profile");

            Ticks(server, 1.0, 0.1);
            Assert.Same(pet, Assert.Single(server.CompanionEntitiesForTest("Keeper")));
            var wild = Assert.Single(Clones(server));
            Assert.Equal(guestId, wild.SpeciesId);

            // Both are stepped with a real movement profile, not the all-zero default of an id the world has none for.
            var profile = server.LocomotionProfileForTest(guestId);
            Assert.True(profile.CruiseSpeed > 0f, "the guest species has no movement profile on the reloaded world");
            Assert.True(profile.BurstSpeed > 0f);

            // And they really move. The owner walks ten blocks along the floor (inside the 24-block leash, which would
            // only snap the pet along): the pet comes after them, and the wild clone wanders off its spot.
            p.State.Position = new Vector3f(pet.Position.X < 0 ? pet.Position.X + 10f : pet.Position.X - 10f, pet.Position.Y, pet.Position.Z);
            static double FlatDistance(Vector3f a, Vector3f b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));
            double gap = FlatDistance(pet.Position, p.State.Position);
            var wildAt = wild.Position;
            Ticks(server, 30.0, 0.1);

            Assert.Same(pet, Assert.Single(server.CompanionEntitiesForTest("Keeper")));
            Assert.True(FlatDistance(pet.Position, p.State.Position) < gap - 3.0,
                $"the pet did not follow its owner: {gap:0.0} blocks apart before, {FlatDistance(pet.Position, p.State.Position):0.0} after");
            Assert.Same(wild, Assert.Single(Clones(server)));
            Assert.True(FlatDistance(wild.Position, wildAt) > 0.5, "the wild clone did not leave its spot");
        }
    }

    [Fact]
    public void ACloneLostWithoutAWord_LeavesTheListOnTheNextBeat()
    {
        var server = NewServer();
        var p = TankOwner(server);
        uint seed = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
        Assert.True(server.GiveSampleForTest(p, seed));
        var clone = GrowClone(server, p, seed);
        Assert.Equal(Choice(seed), ConfigValue(server, Tank, "cl"));

        // Fire, a sentry post, a pen built around a sleeper: ways out of the world that tell the tank nothing. The live
        // list is the server's own list object, so a test can take an animal out of it the same silent way.
        Assert.True(((List<CombatEntity>)server.Creatures).Remove(clone));
        Ticks(server, 1.0, 0.1);

        Assert.Equal(string.Empty, ConfigValue(server, Tank, "cl"));
        Assert.Equal("0", ConfigValue(server, Tank, "clones"));
    }

    [Fact]
    public void AnOlderTankRow_WithACounterAndOneSpecies_StillBringsItsClonesBack()
    {
        string location, speciesId;
        {
            var server = NewServer(world: "legacy");
            TankOwner(server);
            location = server.ActiveLocationId;
            speciesId = LandSpecies(server)[0].Id;
            server.SaveAllForTest();
            server.Stop();
        }

        // The row as the first version of the tank wrote it: a counter and the species it is set to.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "legacy")))
        {
            repo.Initialize();
            repo.SaveCrystalCell(new StoredCrystalCell
            {
                Planet = location,
                X = Tank.X,
                Y = Tank.Y,
                Z = Tank.Z,
                Kind = nameof(CrystalDeviceKind.CloneTank),
                OwnerId = "Keeper",
                Config = "sp=" + speciesId + ";growing=0;clones=2",
            });
            repo.Flush();
        }

        {
            var server = NewServer(world: "legacy");
            server.AddLocalPlayer("Keeper");
            Ticks(server, 1.0);
            Assert.Equal(2, Clones(server).Count);
            Assert.All(Clones(server), c => Assert.Equal(speciesId, c.SpeciesId));
            Assert.Equal(speciesId + "," + speciesId, ConfigValue(server, Tank, "cl")); // and from now on it has the list
        }
    }

    [Fact]
    public void TheClonesOfAWorld_ComeBackAsThatWorldsAnimals_WhileAnotherWorldIsResident()
    {
        string home;
        string[] natives, nativeNames;
        List<string> homeRoster;
        uint guest;
        {
            var server = NewServer(world: "twin", configure: c => c.Rules.FreeSpaceFlight = true);
            var p = Player(server, "Keeper", "clone_tank");
            server.PlaceBlock("Keeper", Tank.X, Tank.Y, Tank.Z, "clone_tank");
            home = server.ActiveLocationId;
            homeRoster = server.SpeciesRoster.Select(s => s.Name).ToList();
            natives = LandSpecies(server).Select(s => s.Id).ToArray();
            nativeNames = LandSpecies(server).Select(s => s.Name).ToArray();
            p.State.ModeOverride = PlayerModeOverride.Creative; // a free game mode: no bait, no matter dust
            foreach (string id in natives)
            {
                p.State.ScannedCreatureSites.Add(home + ":" + id);
                Configure(server, p, Tank, "sp=" + id);
                Start(server, p, Tank);
                Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
            }

            Assert.Equal(natives[0] + "," + natives[1], ConfigValue(server, Tank, "cl"));
            server.SaveAllForTest();
            server.Stop();
        }

        {
            // Two worlds resident at once. Each has its own species table (#2226), and the rolled ids ("sp0", …) repeat
            // from world to world: the visitor's world — the one whose fauna was set up last — has animals behind the
            // very ids the home tank lists. The home world's clones are due on its first beat, as the home world's animals.
            var server = NewServer(world: "twin", configure: c => c.Rules.FreeSpaceFlight = true);
            var keeper = server.AddLocalPlayer("Keeper");
            server.AddLocalPlayer("Visitor");
            var homeBody = server.Galaxy.FindBody(home)!;
            var other = server.Galaxy.AllBodies().First(b => b.Id != home && b.SystemId == homeBody.SystemId
                && b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType));
            server.Travel("Visitor", other.Id);
            Assert.NotEqual(homeRoster, server.SpeciesRoster.Select(s => s.Name).ToList());
            Assert.All(natives, id => Assert.Contains(server.SpeciesRoster, s => s.Id == id)); // the same ids, other animals

            // The world the server looks at is the one it ticked last; a test that means the Keeper's world says so.
            void AtHome() => Assert.True(server.ActivateWorldForTest(home));

            Ticks(server, 1.0);
            AtHome();
            Assert.Equal(homeRoster, server.SpeciesRoster.Select(s => s.Name).ToList()); // the home world reads its own table
            var back = Clones(server);
            Assert.Equal(natives, back.Select(c => c.SpeciesId).ToArray()); // every clone is back beside its tank
            Assert.Equal(nativeNames, back.Select(c => server.NetCreatureForTest(c.Id).Name).ToArray()); // no animal of the other world stands in for them
            Assert.Equal(natives[0] + "," + natives[1], ConfigValue(server, Tank, "cl")); // and none is forgotten
            Assert.Equal("2", ConfigValue(server, Tank, "clones"));

            // A clone grown in this very residency joins the list; it does not push the others out.
            var visitor = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 4711)
                .First(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Land);
            guest = server.RegisterForeignCreatureForTest(visitor, "elsewhere");
            Assert.True(server.GiveSampleForTest(keeper, guest));
            keeper.State.AboardShip = false;
            keeper.State.Position = new Vector3f(0, 200, 0);
            keeper.State.ModeOverride = PlayerModeOverride.Creative;
            Configure(server, keeper, Tank, "sp=" + Choice(guest));
            Start(server, keeper, Tank);
            Assert.True(server.CrystalDeviceOutput(Tank), "the tank did not start");
            Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
            AtHome();
            Assert.Equal(new[] { natives[0], natives[1], "gx" + Choice(guest).Substring(2) }, Clones(server).Select(c => c.SpeciesId).ToArray());
            Assert.Equal(natives[0] + "," + natives[1] + "," + Choice(guest), ConfigValue(server, Tank, "cl"));
            Assert.Equal("3", ConfigValue(server, Tank, "clones"));
            server.SaveAllForTest();
            server.Stop();
        }

        {
            // The next load is an ordinary one: every clone stands beside the tank, each as its own species.
            var server = NewServer(world: "twin", configure: c => c.Rules.FreeSpaceFlight = true);
            server.AddLocalPlayer("Keeper");
            Ticks(server, 1.0);
            var back = Clones(server).Select(c => c.SpeciesId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var expected = new[] { natives[0], natives[1], "gx" + Choice(guest).Substring(2) }.OrderBy(id => id, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, back);
        }
    }

    [Fact]
    public void ClonesThatAreNotBackYet_CountAgainstTheCap()
    {
        string location, native;
        {
            var server = NewServer(world: "count");
            TankOwner(server);
            location = server.ActiveLocationId;
            native = LandSpecies(server)[0].Id;
            server.SaveAllForTest();
            server.Stop();
        }

        int cap = CrystalNetRules.MaxLivingClonesPerOwner;
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "count")))
        {
            repo.Initialize();
            repo.SaveCrystalCell(new StoredCrystalCell
            {
                Planet = location,
                X = Tank.X,
                Y = Tank.Y,
                Z = Tank.Z,
                Kind = nameof(CrystalDeviceKind.CloneTank),
                OwnerId = "Keeper",
                Config = "sp=" + native + ";growing=0;clones=" + cap + ";cl=" + string.Join(",", Enumerable.Repeat(native, cap)),
            });
            repo.Flush();
        }

        {
            var t = new RecordingTransport();
            var server = NewServer(world: "count", transport: t);
            var p = server.AddLocalPlayer("Keeper");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 200, 0);
            p.State.ModeOverride = PlayerModeOverride.Creative;
            p.State.ScannedCreatureSites.Add(location + ":" + native);

            // Start is pressed before the world's first beat: the tank's clones wait in its list, none stands beside
            // it yet — and they are the owner's clones all the same.
            Assert.Empty(Clones(server));
            Start(server, p, Tank);
            Assert.False(server.CrystalDeviceOutput(Tank));
            Assert.Contains("@srv.crystal.clone_cap", RejectionsTo(t, p));

            Ticks(server, 1.0);
            Assert.Equal(cap, Clones(server).Count);
        }
    }

    [Fact]
    public void AnEntryThatCanNeverBeAnAnimalAgain_LeavesTheList_AndTheOthersComeBack()
    {
        string location, native;
        uint plant;
        {
            var server = NewServer(world: "gone");
            var p = TankOwner(server);
            location = server.ActiveLocationId;
            native = LandSpecies(server)[0].Id;
            plant = server.GiveFloraSampleForTest(p, "flora_bush"); // in the save's register — and no animal
            server.SaveAllForTest();
            server.Stop();
        }

        // A list with one animal of this world, a plant, a species the register does not know and an id this world's
        // roster never had.
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "gone")))
        {
            repo.Initialize();
            repo.SaveCrystalCell(new StoredCrystalCell
            {
                Planet = location,
                X = Tank.X,
                Y = Tank.Y,
                Z = Tank.Z,
                Kind = nameof(CrystalDeviceKind.CloneTank),
                OwnerId = "Keeper",
                Config = "sp=" + native + ";growing=0;clones=4;cl=" + Choice(plant) + ",g:deadbeef," + native + ",au_nowhere",
            });
            repo.Flush();
        }

        {
            var server = NewServer(world: "gone");
            server.AddLocalPlayer("Keeper");
            Ticks(server, 1.0);
            Assert.Equal(native, Assert.Single(Clones(server)).SpeciesId);
            Assert.Equal(native, ConfigValue(server, Tank, "cl"));
            Assert.Equal("1", ConfigValue(server, Tank, "clones"));
        }
    }

    [Fact]
    public void AClientConfigure_KeepsThePlayersChoice_AndNeverWritesTheTanksOwnKeys()
    {
        var server = NewServer();
        var p = TankOwner(server);
        var species = LandSpecies(server);
        uint a = server.RegisterForeignCreatureForTest(species[0], "elsewhere");
        uint b = server.RegisterForeignCreatureForTest(species[1], "far away");

        // What a client sends is the whole config it was told, with its own change at the end. Even with a full
        // clone list echoed back, the player's choice survives the length cut — and the echoed list is ignored.
        string echoed = "cl=" + string.Join(",", Enumerable.Repeat(Choice(a), 6)) + ";clones=6;growing=1;grow=" + Choice(a);
        Configure(server, p, Tank, echoed + ";sp=" + Choice(a) + ";x=" + Choice(b));

        Assert.Equal(Choice(a), ConfigValue(server, Tank, "sp"));
        Assert.Equal(Choice(b), ConfigValue(server, Tank, "x"));
        Assert.True(string.IsNullOrEmpty(ConfigValue(server, Tank, "cl")));
        Assert.True(string.IsNullOrEmpty(ConfigValue(server, Tank, "grow")));
        Assert.NotEqual("1", ConfigValue(server, Tank, "growing"));
        Assert.NotEqual("6", ConfigValue(server, Tank, "clones"));
        Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0);
        Assert.Empty(Clones(server)); // nothing was paid for, nothing comes out
    }

    // ---------------- 9. #2215: a guest on a world without a roster ----------------

    [Fact]
    public void AGuestClone_Moves_OnAWorldWithoutAnyRoster()
    {
        var server = NewServer("crystal", "airless"); // an airless world: its roster is empty
        Assert.Empty(server.SpeciesRoster);
        var p = TankOwner(server);
        Ticks(server, 2.0);
        Assert.DoesNotContain(server.Creatures, c => !c.IsGiant); // and no animal appears by itself

        // A land animal of a jungle world that is awake by day (the test world's clock stands in the morning).
        var visitor = CreatureGenerator.GenerateRoster(_content.GetPlanet("jungle")!, 77)
            .First(s => !s.Hostile && !s.IsGiant && s.Habitat == CreatureHabitat.Land && s.Activity is CreatureActivity.Cathemeral or CreatureActivity.Diurnal);
        uint seed = server.RegisterForeignCreatureForTest(visitor, "jungle");
        Assert.True(server.GiveSampleForTest(p, seed));
        var clone = GrowClone(server, p, seed);
        Assert.StartsWith("gx", clone.SpeciesId);

        // It wanders like any animal — which it only does when the creature tick runs on this world at all.
        var at = clone.Position;
        Ticks(server, 40.0, 0.1);
        Assert.NotEqual(at, clone.Position);
        Assert.Empty(server.SpeciesRoster); // a guest lives beside its tank, it does not join the world's roster
    }

    // ---------------- 10. #2217: the sampler and the giants ----------------

    [Fact]
    public void TheSampler_TakesAHostileGiant_WithoutStasis()
    {
        // The giants' own test setup: a salt-flat world of the generation that brought them, the player on the ground.
        var server = NewServer("salt_flats", "giant", configure: c =>
        {
            c.Seed = 4242;
            c.World.TerrainGeneration = WorldDescription.GiantsGeneration;
        });
        var p = Player(server, "Keeper", BioItems.Sampler);
        int ground = 200;
        while (ground > 1 && server.World.GetBlock(new Vector3i(0, ground, 0)).IsAir)
        {
            ground--;
        }

        p.State.Position = new Vector3f(0.5f, ground + 1, 0.5f);
        server.SummonGiantForTest("Keeper", "colossus");
        var sp = server.GiantSpeciesForTest().Colossus!;
        sp.Temperament = CreatureTemperament.Aggressive;
        Assert.True(sp.Hostile);
        var giant = server.GiantsForTest().Single(g => g.Kind == CreatureBodyPlan.Colossus);
        var near = new Vector3f(p.State.Position.X + 30f, p.State.Position.Y, p.State.Position.Z); // out of the hand sampler's 6 blocks
        server.SetGiantForTest(giant.Id, near, 0f);

        server.UseGadgetForTest("Keeper", BioItems.Sampler, near);

        Assert.Equal(1, Samples(p, server.BioSeedForTest(sp)));
        Assert.Contains(server.Creatures, c => c.Id == giant.Id); // and it lives
    }

    [Fact]
    public void TheSampler_StillNeedsStasis_ForAHostileAnimalThatIsNoGiant()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = Player(server, "Keeper", BioItems.Sampler, "stasis_projector");
        p.State.GodMode = true; // it would bite
        var sp = LandSpecies(server)[0];
        sp.Temperament = CreatureTemperament.Aggressive;
        var at = new Vector3f(1, 200, 0);
        server.SpawnCreatureAtForTest(at, sp.Id);

        server.UseGadgetForTest("Keeper", BioItems.Sampler, at);
        Assert.Equal(0, Samples(p, server.BioSeedForTest(sp)));
        Assert.Contains("@srv.bio.sampler_hostile", RejectionsTo(t, p));

        server.UseGadgetForTest("Keeper", "stasis_projector", at);
        server.UseGadgetForTest("Keeper", BioItems.Sampler, at);
        Assert.Equal(1, Samples(p, server.BioSeedForTest(sp)));
    }

    // ---------------- 11. The sampler's two refusals ----------------

    [Fact]
    public void TheSampler_NamesAFullRegister_AndAFullCase_Apart()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = Player(server, "Keeper", BioItems.Sampler);
        var species = LandSpecies(server);
        var here = new Vector3f(1, 200, 0);
        var there = new Vector3f(1, 200, 20); // out of the sampler's reach of the first animal

        // A really full case: every slot holds another kind.
        FillSampleCase(p);
        server.SpawnCreatureAtForTest(here, species[0].Id);
        server.UseGadgetForTest("Keeper", BioItems.Sampler, here);
        Assert.Equal(new[] { "@srv.bio.sample_case_full" }, RejectionsTo(t, p));

        // A full species register: the case has room, but the save cannot take a new species.
        FreeOneSlot(p);
        for (int i = 0; i < BioRules.MaxRegisterEntries * 2; i++)
        {
            server.RegisterForeignCreatureForTest(species[0], "world " + i);
        }

        t.Sent.Clear();
        server.SpawnCreatureAtForTest(there, species[1].Id); // a species this save has not registered yet
        server.UseGadgetForTest("Keeper", BioItems.Sampler, there);
        Assert.Equal(new[] { "@srv.bio.register_full" }, RejectionsTo(t, p));
        Assert.Equal(0, Samples(p, server.BioSeedForTest(species[1])));
    }

    // ---------------- 12. An empty seedling ----------------

    [Fact]
    public void ASeedlingWithoutASpecies_IsRefusedAsEmpty_NotAsATree()
    {
        var t = new RecordingTransport();
        var server = NewServer(transport: t);
        var p = Player(server, "Gardener");
        server.World.SetBlock(new Vector3i(1, 199, 2), Block("dirt"));
        string unknown = ItemKey.WithSeed(BioItems.Seedling, 0xDEADBEEF);
        uint animal = server.RegisterForeignCreatureForTest(LandSpecies(server)[0], "elsewhere");
        string noPlant = ItemKey.WithSeed(BioItems.Seedling, animal);
        foreach (string key in new[] { BioItems.Seedling, unknown, noPlant })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1, 64));
        }

        server.PlaceBlock("Gardener", 1, 200, 2, BioItems.Seedling); // from an item list: it carries no species at all
        server.PlaceBlock("Gardener", 1, 200, 2, unknown);           // a seed this save does not know
        Assert.Equal(new[] { "@srv.bio.seedling_empty", "@srv.bio.seedling_empty" }, RejectionsTo(t, p));

        t.Sent.Clear();
        server.PlaceBlock("Gardener", 1, 200, 2, noPlant); // a real species that is no single plant keeps its own line
        Assert.Equal(new[] { "@srv.bio.not_plantable" }, RejectionsTo(t, p));

        Assert.True(server.World.GetBlock(new Vector3i(1, 200, 2)).IsAir);
        Assert.Equal(1, p.State.Inventory.CountOf(BioItems.Seedling));
    }
}
