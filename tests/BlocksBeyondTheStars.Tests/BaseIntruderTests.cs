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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Wild animals and player-built ground (#2521): no wild ground animal spawns on a block a player placed, and a wild
/// animal standing on one — or inside a walled yard or sealed room of a base — despawns while no player is looking at
/// it. Pets stay. The floor test uses a stone pad written without an owner as the "natural ground" control: only a
/// player edit (<c>owner_id &lt;&gt; 0</c>) makes a floor the player's.
/// </summary>
public sealed class BaseIntruderTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BaseIntruderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_intruders_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private SvGameServer Started(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna → a full species roster
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private const int Cx = 40, Cz = 40;

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        max = System.Math.Max(max, y);
                        break;
                    }
                }

        return max;
    }

    /// <summary>A stone pad floating above every natural feature; <paramref name="owner"/> = the player who built it,
    /// empty = a server-internal write (stands in for natural ground).</summary>
    private void Pad(SvGameServer server, int cx, int cz, int r, int padY, string owner)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone, owner: owner);
            }
    }

    /// <summary>Roster slot 0 as a plain, solitary, always-awake, passive land walker.</summary>
    private static CreatureSpecies Walker(SvGameServer server)
    {
        var sp = server.SpeciesRoster[0];
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = CreatureTemperament.Passive;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.Size = 1f;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        return sp;
    }

    private static PlayerSession OnFoot(SvGameServer server, Vector3f at, float yaw)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Yaw = yaw;
        return p;
    }

    [Fact]
    public void NoWildGroundAnimalSpawns_OnAFloorAPlayerBuilt()
    {
        var server = Started("nospawn", out var repo);
        using (repo)
        {
            Walker(server);
            int padY = MaxTopY(server, Cx, Cz, 6) + 8;
            Pad(server, Cx, Cz, 4, padY, owner: string.Empty);
            var p = OnFoot(server, new Vector3f(Cx + 0.5f, padY + 1, Cz - 20.5f), 0f);
            var spot = new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f);

            Assert.False(server.OnPlayerBuiltFloorForTest(spot));
            Assert.True(server.SpawnSpotClearForTest(spot), "the unowned pad is ordinary ground for this test to mean anything");

            // The player lays that one floor block: the same spot is no longer wild ground.
            server.World.SetBlock(new Vector3i(Cx, padY, Cz), _content.GetBlock("stone")!.NumericId, owner: p.State.PlayerId);
            Assert.True(server.OnPlayerBuiltFloorForTest(spot));
            Assert.False(server.SpawnSpotClearForTest(spot), "no wild ground animal spawns on a block a player placed");

            // …and a slab-high step counts too: the block in the feet cell itself is the floor then.
            var slabSpot = new Vector3f(Cx + 2.5f, padY + 1.5f, Cz + 0.5f);
            Assert.False(server.OnPlayerBuiltFloorForTest(slabSpot));
            server.World.SetBlock(new Vector3i(Cx + 2, padY + 1, Cz), _content.GetBlock("stone")!.NumericId, owner: p.State.PlayerId);
            Assert.True(server.OnPlayerBuiltFloorForTest(slabSpot));
        }
    }

    [Fact]
    public void AWildAnimalOnAPlayerFloor_DespawnsWhileNobodyLooks_APetDoesNot()
    {
        var server = Started("floor", out var repo);
        using (repo)
        {
            var sp = Walker(server);
            int padY = MaxTopY(server, Cx, Cz, 12) + 8;
            var p = OnFoot(server, new Vector3f(Cx + 0.5f, padY + 1, Cz - 7.5f), 0f); // facing +Z, across the floor
            Pad(server, Cx, Cz, 10, padY, owner: p.State.PlayerId);                   // the player's floor
            Pad(server, Cx + 24, Cz, 3, padY, owner: string.Empty);                     // unowned ground beside it

            string wildId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 5.5f), sp.Id);
            string outsideId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 24.5f, padY + 1, Cz + 0.5f), sp.Id);
            p.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "tc_pet",
                HomeBodyId = server.World.LocationId,
                Name = "Krümel",
                SpeciesId = sp.Id,
                Species = sp,
                SizeScale = 1f,
            });
            server.ReconcileCompanionsForTest();
            var pet = server.Creatures.First(c => c.IsCompanion && c.CompanionId == "tc_pet");
            pet.Position = new Vector3f(Cx - 2.5f, padY + 1, Cz + 5.5f);

            // In plain view: nothing vanishes in front of the player.
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, c => c.Id == wildId);

            // The player turns round: the wild animal on the floor leaves, the pet stays, and so does the animal
            // on the unowned ground (also behind the player now — it is simply not inside anything).
            p.State.Yaw = 180f;
            Assert.True(server.SweepBaseIntrudersForTest());
            Assert.DoesNotContain(server.Creatures, c => c.Id == wildId);
            Assert.Contains(server.Creatures, c => c.Id == pet.Id);
            Assert.Contains(server.Creatures, c => c.Id == outsideId);

            // The same through the real tick (throttled sweep): a second intruder, held still, is gone after a few seconds.
            string secondId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 3.5f, padY + 1, Cz + 6.5f), sp.Id);
            server.PauseCreatureForTest(secondId, 30f);
            for (int i = 0; i < 8; i++)
            {
                p.State.Position = new Vector3f(Cx + 0.5f, padY + 1, Cz - 7.5f);
                server.TickForTest(0.5);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == secondId);
            Assert.Contains(server.Creatures, c => c.Id == pet.Id);
        }
    }

    [Fact]
    public void AFishInWaterAPlayerPlaced_StillSpawnsThere_AndIsNeverSwept()
    {
        // A cell's owner sticks (water flowing back into a dug channel keeps the digger's name), so the water a fish
        // swims in says nothing about a player's floor — water and lava life is outside the floor rule altogether.
        var server = Started("aquarium", out var repo);
        using (repo)
        {
            var fish = server.SpeciesRoster[0];
            fish.Habitat = CreatureHabitat.Water;
            fish.Temperament = CreatureTemperament.Passive;
            fish.Activity = CreatureActivity.Cathemeral;
            fish.BodyPlan = CreatureBodyPlan.Standard;
            fish.Size = 1f;
            fish.SocialGroupSize = 1;
            fish.BiomeExclusive = false;

            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;
            int padY = MaxTopY(server, Cx, Cz, 8) + 8;
            var p = OnFoot(server, new Vector3f(Cx + 0.5f, padY + 1, Cz - 12.5f), 180f); // facing away from the basin

            // An unowned stone basin, filled three deep with water the player poured in.
            const int r = 3;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    server.World.SetBlock(new Vector3i(Cx + dx, padY, Cz + dz), stone);
                    bool wall = System.Math.Abs(dx) == r || System.Math.Abs(dz) == r;
                    for (int y = padY + 1; y <= padY + 3; y++)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, y, Cz + dz), wall ? stone : water, owner: wall ? string.Empty : p.State.PlayerId);
                    }
                }

            var swim = new Vector3f(Cx + 0.5f, padY + 2, Cz + 0.5f); // mid-water, as the spawner seats a swimmer
            Assert.False(server.OnPlayerBuiltFloorForTest(swim));
            Assert.True(server.SpawnSpotClearForTest(swim), "a fish may spawn in water a player placed");

            string fishId = server.SpawnCreatureAtForTest(swim, fish.Id);
            Assert.False(server.SweepBaseIntrudersForTest());
            Assert.Contains(server.Creatures, c => c.Id == fishId);
        }
    }

    [Fact]
    public void AWildAnimalInRegrownGrass_OnNaturalGround_IsNotSwept()
    {
        // A harvested plant regrows under its harvester's name — the grass is "player-owned" in the store, the ground
        // under it is not. Plants are never floor, so the animal grazing there stays.
        var server = Started("regrown", out var repo);
        using (repo)
        {
            var sp = Walker(server);
            int padY = MaxTopY(server, Cx, Cz, 8) + 8;
            Pad(server, Cx, Cz, 4, padY, owner: string.Empty);                                 // natural ground
            var p = OnFoot(server, new Vector3f(Cx + 0.5f, padY + 1, Cz - 7.5f), 180f);       // facing away
            server.World.SetBlock(new Vector3i(Cx, padY + 1, Cz), _content.GetBlock("flora_grasstuft")!.NumericId, owner: p.State.PlayerId);

            var spot = new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f);
            Assert.False(server.OnPlayerBuiltFloorForTest(spot));
            Assert.True(server.SpawnSpotClearForTest(spot), "regrown grass on natural ground is still wild ground");

            string wildId = server.SpawnCreatureAtForTest(spot, sp.Id);
            Assert.False(server.SweepBaseIntrudersForTest());
            Assert.Contains(server.Creatures, c => c.Id == wildId);
        }
    }

    [Fact]
    public void AWildAnimalBeingTamed_StaysOnTheFloor_WhoeverRunsTheSession()
    {
        var server = Started("taming", out var repo);
        using (repo)
        {
            var sp = Walker(server);
            int padY = MaxTopY(server, Cx, Cz, 12) + 8;
            var builder = OnFoot(server, new Vector3f(Cx + 0.5f, padY + 1, Cz - 7.5f), 180f); // facing away from the animal
            Pad(server, Cx, Cz, 10, padY, owner: builder.State.PlayerId);

            // A second player lured it onto the builder's floor and is taming it with the translator.
            var tamer = server.AddLocalPlayer("Tamer");
            tamer.State.AboardShip = false;
            tamer.State.Position = new Vector3f(Cx + 0.5f, padY + 1, Cz + 1.5f);
            string wildId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 6.5f), sp.Id);
            server.TameDecodeForTest("Tamer");
            Assert.NotEqual(string.Empty, server.TameCurrentNeedForTest("Tamer"));
            var wild = server.Creatures.First(c => c.Id == wildId);
            wild.FrozenTimer = 0; // the decode's attention hold is a separate exemption — let it lapse

            // Nobody looks at it (the tamer has turned round too), yet the session keeps it.
            tamer.State.Yaw = 180f;
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, c => c.Id == wildId);

            // The session ends (the tamer walks off and the translator finds nothing near): now it leaves like any intruder.
            tamer.State.Position = new Vector3f(Cx + 0.5f, padY + 1, Cz - 9.5f);
            server.TameDecodeForTest("Tamer");
            Assert.Equal(string.Empty, server.TameCurrentNeedForTest("Tamer"));
            Assert.True(server.SweepBaseIntrudersForTest());
            Assert.DoesNotContain(server.Creatures, c => c.Id == wildId);
        }
    }

    [Fact]
    public void AWildAnimalInsideAWalledYard_DespawnsWhileNobodyLooks()
    {
        var server = Started("yard", out var repo);
        using (repo)
        {
            var sp = Walker(server);
            var stone = _content.GetBlock("stone")!.NumericId;
            int padY = MaxTopY(server, Cx, Cz, 12) + 8;
            Pad(server, Cx, Cz, 10, padY, owner: string.Empty); // the yard's ground is not the player's floor here
            const int ring = 6;
            for (int dx = -ring; dx <= ring; dx++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (System.Math.Abs(dx) == ring || System.Math.Abs(dz) == ring)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 1, Cz + dz), stone);
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 2, Cz + dz), stone);
                    }
                }

            var p = OnFoot(server, new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f), 0f);
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock("Builder", Cx, padY + 1, Cz, "base_core");
            Assert.Single(server.BaseSnapshots);

            var inside = new Vector3f(Cx + 3.5f, padY + 1, Cz - 3.5f);
            Assert.True(server.InWalledBaseAreaForTest((int)inside.X, (int)inside.Y, (int)inside.Z), "the ring must read as walled");
            string wildId = server.SpawnCreatureAtForTest(inside, sp.Id);

            // The player stands outside the ring looking away from it — the wall would hide the animal anyway.
            p.State.Position = new Vector3f(Cx + 0.5f, padY + 1, Cz + 9.5f);
            p.State.Yaw = 0f;
            Assert.True(server.SweepBaseIntrudersForTest());
            Assert.DoesNotContain(server.Creatures, c => c.Id == wildId);
        }
    }
}
