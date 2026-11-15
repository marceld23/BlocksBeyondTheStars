// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
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

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Wild animals and the monorail (#3656–#3658, a playtest: a hunter walked into the halted public train and bit the
/// seated rider; a train drove straight through a herd on the line). The intercity stations are town ground; a wild
/// animal cannot step into a wagon and one found inside is set down beside it; a rider is nobody's prey and anchors no
/// spawn; a train brakes to stand short of an animal on its track, honks, asks it to step aside and runs on once the track
/// is clear — and an animal that stays is set down beside the track, never hurt; a tall roamer keeps off a running line.
/// The arena is a stone pad floating above every natural feature on a jungle world (a full species roster, no spawning:
/// the abundance rule is off), a straight line of four pylons along it and the player's cab in the middle.
/// </summary>
public sealed class RailWildlifeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_railwild_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = 40, Cz = 40;

    /// <summary>The pylons stand every 18 blocks from <see cref="West"/> to <see cref="East"/> along z = <see cref="Cz"/>.</summary>
    private const int West = Cx - 30, East = Cx + 24;

    /// <summary>The line's middle across (the pylon tops' cell centres).</summary>
    private const float LineZ = Cz + 0.5f;

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private sealed class Arena
    {
        public SvGameServer Server = null!;
        public PlayerSession Driver = null!;
        public int PadY;
        public string TrainId = string.Empty;

        public int Feet => PadY + 1;
    }

    private Arena Build(string name, out SqliteWorldRepository repo, bool seatWagon = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceIntercityRail = false,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off; // no spawning: only the test's own animals
        config.Rules.Bandits = AlienActivity.Off;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();

        var a = new Arena { Server = server, PadY = Pad(server) };
        a.Driver = server.AddLocalPlayer("Driver");
        var p = a.Driver.State;
        p.AboardShip = false;
        p.Inventory.SetSlot(5, new ItemStack(RailRules.PylonBlockKey, 8));
        p.Inventory.SetSlot(6, new ItemStack(RailRules.CabItemKey, 1));
        p.Inventory.SetSlot(7, new ItemStack("wagon_seats", 1));
        for (int x = West; x <= East; x += 18)
        {
            p.Position = new Vector3f(x + 0.5f, a.Feet, Cz + 3.5f);
            server.PlaceBlock(p.PlayerId, x, a.Feet, Cz, RailRules.PylonBlockKey);
        }

        Assert.Single(server.RailLinesForTest());
        var target = new Vector3f(Cx - 3 + 0.5f, a.PadY + 3f, LineZ);
        p.Position = new Vector3f(target.X, a.Feet, target.Z + 3f);
        server.UseGadgetForTest(p.PlayerId, RailRules.CabItemKey, target);
        a.TrainId = Assert.Single(server.TrainsForTest()).Id;
        if (seatWagon)
        {
            var tail = server.WagonPoseForTest(a.TrainId, 1)!.Value.Pos;
            p.Position = new Vector3f(tail.X, a.Feet, tail.Z + 3f);
            server.UseGadgetForTest(p.PlayerId, "wagon_seats", tail);
            Assert.Equal(2, server.TrainsForTest().Single().Wagons.Count);
        }

        p.Position = new Vector3f(Cx + 0.5f, a.Feet, Cz + 6.5f); // on the pad beside the line
        return a;
    }

    /// <summary>A stone pad along the line, 8 above the highest natural block under it; returns its Y.</summary>
    private int Pad(SvGameServer server)
    {
        int top = int.MinValue;
        for (int x = West - 4; x <= East + 4; x++)
            for (int z = Cz - 8; z <= Cz + 8; z++)
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
                    {
                        top = System.Math.Max(top, y);
                        break;
                    }
                }

        int padY = top + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = West - 4; x <= East + 4; x++)
            for (int z = Cz - 8; z <= Cz + 8; z++)
            {
                server.World.SetBlock(new Vector3i(x, padY, z), stone);
            }

        return padY;
    }

    /// <summary>A roster slot as a plain, solitary, always-awake land animal of <paramref name="temperament"/>.</summary>
    private static CreatureSpecies Land(SvGameServer server, int slot, CreatureTemperament temperament = CreatureTemperament.Passive, float size = 1f)
    {
        var sp = server.SpeciesRoster[slot];
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = temperament;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.Size = size;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        sp.AngeredByMining = false;
        return sp;
    }

    private static void Tick(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds - 1e-6; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    /// <summary>The cab's pose and the way the train runs (it is placed running along its facing: +1, +1).</summary>
    private static (Vector3f Pos, float Fx, float Fz) Cab(Arena a)
    {
        var (pos, yaw) = a.Server.WagonPoseForTest(a.TrainId, 0)!.Value;
        return (pos, MathF.Cos(yaw), MathF.Sin(yaw));
    }

    private static bool Held(Arena a) => a.Server.TrainCreatureHoldForTest(a.TrainId).HeldByCreature;

    [Fact]
    public void ObstacleSpeed_IsNoLimitWithoutAnAnimal_ZeroAtTheHoldSpot_TheBrakingCurveBefore()
    {
        Assert.True(float.IsPositiveInfinity(RailMotion.ObstacleSpeed(float.PositiveInfinity)));
        Assert.True(float.IsPositiveInfinity(RailMotion.ObstacleSpeed(float.NaN)));
        Assert.Equal(0f, RailMotion.ObstacleSpeed(0f));
        Assert.Equal(0f, RailMotion.ObstacleSpeed(-2f));
        Assert.Equal(RailMotion.BrakingSpeed(9f), RailMotion.ObstacleSpeed(9f));
    }

    [Fact]
    public void ABody_ReachesTheWagon_OnlyWhenItRisesIntoTheBox_ASmallAnimalWalksUnderAnElevatedLine()
    {
        // The open intercity line: the floor floats 3.7 over the highest ground block, so an animal's feet stand 2.7 under it.
        Assert.False(SvGameServer.ReachesWagonBand(-2.7f, 2f), "a two-block animal walks under the wagon");
        Assert.True(SvGameServer.ReachesWagonBand(-2.7f, 3f), "a three-block one would bump into it");
        // A station: the floor 0.7 over the platform — every animal reaches it there.
        Assert.True(SvGameServer.ReachesWagonBand(-0.7f, 2f));
        Assert.False(SvGameServer.ReachesWagonBand(4.2f, 2f), "over the roof is free");
    }

    /// <summary>A meadow world with an intercity line (IntercityRailTests' known seed first, a short search behind it).</summary>
    private SvGameServer StartWithLine(out SqliteWorldRepository repo)
    {
        foreach (long seed in new long[] { 5 }.Concat(Enumerable.Range(1, 10).Select(i => (long)i).Where(i => i != 5)))
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, "station" + seed));
            var config = new ServerConfig
            {
                WorldName = "station" + seed,
                Seed = seed,
                StartPlanet = "meadowlands",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                IntercityRailChance = 1.0,
            };
            config.World.TerrainGeneration = WorldDescription.GeographyGeneration;
            var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            if (server.IntercityRailForTest() is not null)
            {
                return server;
            }

            server.Stop();
            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No intercity line on 'meadowlands' across eleven seeds.");
    }

    [Fact]
    public void AStation_IsTownGround_NoGroundAnimalSpawnsThere_TheOpenLineStaysWild()
    {
        var server = StartWithLine(out var repo);
        using (repo)
        {
            var ic = server.IntercityRailForTest();
            var walker = Land(server, 0);
            foreach (var st in ic!.Value.Stations)
            {
                // The platform at the hall's far end — sixteen blocks and more from the town's box, past its own margin.
                var (sx, sz) = RailStationGenerator.ToStructure(st.Heading, RailStationGenerator.Length - 3, RailStationGenerator.StopV);
                var platform = new Vector3f(st.Origin.X + sx + 0.5f, st.Origin.Y + 1, st.Origin.Z + sz + 0.5f);
                Assert.True(server.TownZoneDepthForTest(platform) > 0, $"the far platform of {st.Settlement}'s station is town ground");
                Assert.False(server.SpawnSpotClearForSpeciesTest(walker.Id, platform), "no land animal spawns on a platform");
            }

            // Halfway between the towns the line is wild ground as before.
            var mid = ic.Value.Pylons[ic.Value.Pylons.Count / 2];
            Assert.Equal(0, server.TownZoneDepthForTest(new Vector3f(mid.X + 0.5f, mid.Y, mid.Z + 0.5f)));
        }
    }

    [Fact]
    public void AWildAnimal_CannotStepIntoAHaltedWagon_AndOneFoundInsideIsSetDownBesideIt()
    {
        var a = Build("barrier", out var repo);
        using (repo)
        {
            var server = a.Server;
            server.SetTrainForTest(a.Driver.State.PlayerId, a.TrainId, halt: 1);
            Tick(server, 0.2);
            var walker = Land(server, 0);
            var (cab, fx, fz) = Cab(a);
            float rx = fz, rz = -fx; // right of the cab (local +X)

            // Beside the wagon's side, outside its box: a step into the box is a wall, a step along it is free.
            var beside = new Vector3f(cab.X + rx * 3.5f, a.Feet, cab.Z + rz * 3.5f);
            string id = server.SpawnCreatureAtForTest(beside, walker.Id);
            Assert.False(server.InAnyWagonForTest(beside, 0.4f, 2f));
            Assert.True(server.CreatureStepBlockedForTest(id, new Vector3f(cab.X + rx * 1f, a.Feet, cab.Z + rz * 1f)), "the step into the wagon is a wall");
            Assert.False(server.CreatureStepBlockedForTest(id, new Vector3f(beside.X + fx * 0.8f, a.Feet, beside.Z + fz * 0.8f)), "along its side is free");

            // An animal found inside the wagon (an old save, a train that pulled in over it) is set down beside it, unhurt.
            var inside = new Vector3f(cab.X + rx * 0.2f, a.Feet, cab.Z + rz * 0.2f);
            string inId = server.SpawnCreatureAtForTest(inside, walker.Id);
            var animal = server.Creatures.First(c => c.Id == inId);
            float hull = animal.Hull;
            Assert.True(server.InAnyWagonForTest(animal.Position, 0.4f, 2f));
            server.TickForTest(0.1);
            Assert.Contains(server.Creatures, c => c.Id == inId);
            Assert.False(server.InAnyWagonForTest(animal.Position, 0.4f, 2f), $"still in the wagon at {animal.Position}");
            Assert.True(MathF.Abs(animal.Position.Z - LineZ) > 1.5f, "set down beside the track");
            Assert.Equal(hull, animal.Hull);
        }
    }

    [Fact]
    public void ARider_IsNobodysPrey_NoHunterBitesTheSeatedRider_AndARiderAnchorsNoSpawn()
    {
        var a = Build("rider", out var repo, seatWagon: true);
        using (repo)
        {
            var server = a.Server;
            var p = a.Driver.State;
            server.SetTrainForTest(p.PlayerId, a.TrainId, halt: 1);
            Tick(server, 0.2);
            var hunter = Land(server, 1, CreatureTemperament.Aggressive);
            hunter.AttackDamage = 6f;
            var (wagon, yaw) = server.WagonPoseForTest(a.TrainId, 1)!.Value;
            var seat = RailRules.SeatOffsets("wagon_seats")[0];
            var seatWorld = RailRules.LocalToWorld(wagon, yaw, seat);
            // The hunter waits at the open door on the seat's side, within a bite of the seat.
            float side = seat.X < 0f ? -1f : 1f;
            var door = RailRules.LocalToWorld(wagon, yaw, new Vector3f(side * 2.6f, 0f, 0f));
            string id = server.SpawnCreatureAtForTest(new Vector3f(door.X, a.Feet, door.Z), hunter.Id);

            // The control: on foot at the very same spot, the player is bitten.
            p.Position = seatWorld;
            float before = p.Health;
            Tick(server, 1.5);
            Assert.True(p.Health < before, "control: a player on foot by the door is bitten");

            p.Health = before;
            p.Position = new Vector3f(seatWorld.X, a.Feet, seatWorld.Z);
            server.EnterTrainForTest(p.PlayerId, a.TrainId, wagon: 1, seat: 0);
            Assert.Equal(RailRules.FrameId(a.TrainId, 1), p.InTrain); // seated in the passenger wagon
            var players = server.CreaturePlayersForTest();
            Assert.DoesNotContain(p.PlayerId, players.Reactors);
            Assert.Contains(p.PlayerId, players.Targets); // the land along the line keeps living for the rider
            Assert.Null(server.CreatureSpawnAnchorForTest(0));
            Tick(server, 3);
            Assert.Equal(before, p.Health);
            Assert.False(server.InAnyWagonForTest(server.Creatures.First(c => c.Id == id).Position, 0.3f, 2f), "the hunter never got into the wagon");

            // A second player on foot anchors every spawn; the rider none.
            var walker = server.AddLocalPlayer("Walker");
            walker.State.AboardShip = false;
            walker.State.Position = new Vector3f(Cx + 0.5f, a.Feet, Cz + 6.5f);
            for (int wild = 0; wild < 4; wild++)
            {
                Assert.Equal(walker.State.PlayerId, server.CreatureSpawnAnchorForTest(wild));
            }
        }
    }

    [Fact]
    public void ATrain_HaltsShortOfAnAnimalOnTheTrack_Honks_AndRunsOnOnceTheTrackIsClear()
    {
        var a = Build("halt", out var repo);
        using (repo)
        {
            var server = a.Server;
            var (cab, fx, fz) = Cab(a);
            var spot = new Vector3f(cab.X + fx * 16f, a.Feet, LineZ);
            string id = server.SpawnCreatureAtForTest(spot, Land(server, 0).Id);
            var animal = server.Creatures.First(c => c.Id == id);
            animal.FrozenTimer = 60; // held in stasis on the track: it cannot step aside on its own
            float hull = animal.Hull;

            for (int i = 0; i < 200 && !Held(a); i++)
            {
                server.TickForTest(0.1);
            }

            var hold = server.TrainCreatureHoldForTest(a.TrainId);
            Assert.True(hold.HeldByCreature, "the train holds short of the animal");
            Assert.Equal(id, hold.HeldBy);
            Assert.True(hold.Horn >= 1, "and honked");
            Assert.InRange(server.TrainCreatureGapForTest(a.TrainId), SvGameServer.TrainCreatureClearance - 0.2f, SvGameServer.TrainCreatureClearance + 0.6f);
            var net = server.NetTrainsForTest().Single();
            Assert.True(net.Halted, "the clients see a standing train");
            Assert.Equal(0f, net.Velocity);
            Assert.Equal(hold.Horn, net.Horn);
            Assert.True(animal.TrackAsideUntil > 0, "the horn asked the animal to step aside");

            // It keeps waiting, and honks again a few seconds later.
            float heldArc = server.TrainsForTest().Single().Arc;
            Tick(server, SvGameServer.TrainHornSeconds + 0.5);
            Assert.True(Held(a));
            Assert.Equal(heldArc, server.TrainsForTest().Single().Arc);
            Assert.True(server.TrainCreatureHoldForTest(a.TrainId).Horn > hold.Horn, "it honks again");

            // The animal walks off the track: the train runs on.
            animal.FrozenTimer = 0;
            animal.Position = new Vector3f(spot.X, a.Feet, Cz + 6.5f);
            Tick(server, 2);
            Assert.False(Held(a));
            Assert.NotEqual(heldArc, server.TrainsForTest().Single().Arc);
            Assert.Contains(server.Creatures, c => c.Id == id);
            Assert.Equal(hull, animal.Hull);
        }
    }

    [Fact]
    public void AnAnimalThatStaysOnTheTrack_IsSetDownBesideIt_AfterAWhile_NeverHurt()
    {
        var a = Build("patience", out var repo);
        using (repo)
        {
            var server = a.Server;
            var (cab, fx, fz) = Cab(a);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cab.X + fx * 14f, a.Feet, LineZ), Land(server, 0).Id);
            var animal = server.Creatures.First(c => c.Id == id);
            animal.FrozenTimer = 120; // it will not move by itself
            float hull = animal.Hull;
            for (int i = 0; i < 200 && !Held(a); i++)
            {
                server.TickForTest(0.1);
            }

            Assert.True(Held(a));
            Tick(server, SvGameServer.TrainMoveAsideSeconds - 2);
            Assert.True(Held(a), "it waits a while first");
            Tick(server, 3);
            Assert.Contains(server.Creatures, c => c.Id == id);
            Assert.True(MathF.Abs(animal.Position.Z - LineZ) > 2f, $"set down beside the track (at {animal.Position})");
            Assert.Equal(hull, animal.Hull);
            Tick(server, 1);
            Assert.False(Held(a), "the track is clear: the train runs on");
        }
    }

    [Fact]
    public void ARoamer_ThatReachesTheWagons_KeepsOffARunningLine_ButMayLeaveIt_AndAHunterStillCrosses()
    {
        var a = Build("corridor", out var repo);
        using (repo)
        {
            var server = a.Server;
            server.SetTrainForTest(a.Driver.State.PlayerId, a.TrainId, halt: 1);
            Tick(server, 0.2);
            var walker = Land(server, 0);
            float x = Cx + 15.5f; // between two pylons, far from the cab
            Assert.True(server.InTrainCorridorForTest(new Vector3f(x, a.Feet, LineZ + 1.5f), 2f));
            Assert.False(server.InTrainCorridorForTest(new Vector3f(x, a.Feet, LineZ + 4f), 2f));

            string id = server.SpawnCreatureAtForTest(new Vector3f(x, a.Feet, LineZ + 4f), walker.Id);
            var animal = server.Creatures.First(c => c.Id == id);
            animal.Loco.Mode = MoveMode.Roam;
            Assert.True(server.CreatureStepBlockedForTest(id, new Vector3f(x, a.Feet, LineZ + 1.5f)), "a roamer keeps off the line");
            Assert.False(server.CreatureStepBlockedForTest(id, new Vector3f(x + 0.8f, a.Feet, LineZ + 4f)), "along it is free");

            animal.Loco.Mode = MoveMode.Seek;
            Assert.False(server.CreatureStepBlockedForTest(id, new Vector3f(x, a.Feet, LineZ + 1.5f)), "a hunter still crosses (the train brakes for it)");

            animal.Loco.Mode = MoveMode.Roam;
            animal.Position = new Vector3f(x, a.Feet, LineZ + 1f);
            Assert.False(server.CreatureStepBlockedForTest(id, new Vector3f(x, a.Feet, LineZ + 2.5f)), "one on the line may step off it");
        }
    }
}
