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
/// Wild animals and towns (#3453, a playtest: animals on the roofs and in the streets of a village). The town zone is an
/// intact settlement's box (or a lodge's footprint) grown by <see cref="SvGameServer.TownWildMargin"/>: no ground animal
/// spawns there — not on a roof, not in a lane — fliers may spawn over it but not under its roofs, ruins stay wild; a wild
/// animal walking or hunting toward a town turns away at the zone's edge, one already inside may only step out or along,
/// and it leaves unseen once it has stayed too long; a pet follows its owner in. The arena is a stone pad floating above
/// every natural feature (written without an owner, so it is natural ground), the town a box registered over it.
/// </summary>
public sealed class CreatureTownRuleTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    private const int Cx = 40, Cz = 40;

    /// <summary>The test town's box: 11 × 9 × 11 round the pad's centre (its zone reaches 4 further: Cx ± 9, Cz ± 9).</summary>
    private const int Half = 5;

    public CreatureTownRuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_towns_" + Guid.NewGuid().ToString("N"));
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
            PlaceSettlements = false, // no generated towns or lodges: the test registers its own
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Lays the pad (half-size <paramref name="r"/>) 8 above the highest natural block under it; returns its Y.</summary>
    private int Pad(SvGameServer server, int r)
    {
        int top = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(Cx + dx, y, Cz + dz)).IsAir)
                    {
                        top = System.Math.Max(top, y);
                        break;
                    }
                }

        int padY = top + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(Cx + dx, padY, Cz + dz), stone);
            }

        return padY;
    }

    /// <summary>The test town over the pad: an intact settlement box, foundation row = the pad.</summary>
    private static void RegisterTown(SvGameServer server, int padY)
        => server.RegisterTestSettlementForTest("Testvale", new Vector3i(Cx - Half, padY, Cz - Half), new Vector3i(Cx + Half, padY + 8, Cz + Half));

    /// <summary>A roof slab (5 × 5, three blocks up) on four corner posts in the middle of the town — written like a stamp,
    /// without an owner.</summary>
    private void House(SvGameServer server, int padY)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                server.World.SetBlock(new Vector3i(Cx + dx, padY + 4, Cz + dz), stone);
                if (System.Math.Abs(dx) == 2 && System.Math.Abs(dz) == 2)
                {
                    for (int y = padY + 1; y <= padY + 3; y++)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, y, Cz + dz), stone);
                    }
                }
            }
    }

    /// <summary>A roster slot as a plain, solitary, always-awake land animal of <paramref name="temperament"/>.</summary>
    private static CreatureSpecies Land(SvGameServer server, int slot, CreatureTemperament temperament = CreatureTemperament.Passive)
    {
        var sp = server.SpeciesRoster[slot];
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = temperament;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.Size = 1f;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        sp.AngeredByMining = false;
        return sp;
    }

    private static PlayerSession OnFoot(SvGameServer server, string name, Vector3f at, float yaw)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Yaw = yaw;
        return p;
    }

    [Fact]
    public void NoGroundAnimalSpawnsOnARoofOrInALane_FliersSpawnOverTheTown_RuinsStayWild()
    {
        var server = Started("town_spawn", out var repo);
        using (repo)
        {
            var walker = Land(server, 0);
            var flier = server.SpeciesRoster[1];
            flier.Habitat = CreatureHabitat.Air;
            flier.Activity = CreatureActivity.Cathemeral;
            flier.BodyPlan = CreatureBodyPlan.Standard;
            flier.Size = 1f;
            flier.SocialGroupSize = 1;
            flier.BiomeExclusive = false;

            int padY = Pad(server, 24);
            House(server, padY);
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(Cx + 4, padY - 7, Cz + 4), stone); // a cave floor well under the town

            var roof = new Vector3f(Cx + 0.5f, padY + 5, Cz + 0.5f);       // standing on the house's roof
            var lane = new Vector3f(Cx + 4.5f, padY + 1, Cz + 4.5f);       // the open pad between the houses
            var underRoof = new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f);  // inside the house
            var margin = new Vector3f(Cx + Half + 2.5f, padY + 1, Cz + 0.5f); // two blocks outside the box, inside the margin
            var outside = new Vector3f(Cx + 14.5f, padY + 1, Cz + 0.5f);   // five blocks past the zone
            var overRoof = new Vector3f(Cx + 0.5f, padY + 9, Cz + 0.5f);   // a flier's band over the roof
            var overLane = new Vector3f(Cx + 4.5f, padY + 5, Cz + 4.5f);   // …and over the lane
            var deepCave = new Vector3f(Cx + 4.5f, padY - 6, Cz + 4.5f);   // a cave seven under the foundation row
            var ruin = new Vector3f(Cx + 15.5f, padY + 1, Cz - 15.5f);
            var nearLodge = new Vector3f(Cx - 23.5f, padY + 1, Cz + 14.5f); // two blocks west of the lodge below

            // The control: without a town every one of these spots is good ground — the rule alone rejects them below.
            foreach (var spot in new[] { roof, lane, underRoof, margin, outside, ruin, nearLodge })
            {
                Assert.True(server.SpawnSpotClearForSpeciesTest(walker.Id, spot), $"control: {spot} is wild ground without a town");
            }

            Assert.True(server.SpawnSpotClearForSpeciesTest(flier.Id, underRoof), "control: a flier fits under the roof");
            Assert.True(server.CaveSpawnSpotClearForTest(lane), "control: a cave dweller fits on the pad");

            RegisterTown(server, padY);
            server.RegisterTestRuinForTest("Ruins of Testvale", new Vector3i(Cx + 12, padY, Cz - 18), new Vector3i(Cx + 18, padY + 6, Cz - 12));
            server.SpawnLodgeForTest(Cx - 22, padY, Cz + 8, "gatherer");

            // Ground life: never on a roof, in a lane, in the house or in the margin round the town (or the lodge).
            foreach (var spot in new[] { roof, lane, underRoof, margin, nearLodge })
            {
                Assert.True(server.TownZoneDepthForTest(spot) > 0, $"{spot} lies in a town zone");
                Assert.False(server.SpawnSpotClearForSpeciesTest(walker.Id, spot), $"no land animal spawns at {spot}");
            }

            Assert.False(server.CaveSpawnSpotClearForTest(lane), "nor a cave dweller in the lanes");

            // Past the margin, in a ruin and in a cave well under the foundation the world is wild as ever.
            Assert.Equal(0, server.TownZoneDepthForTest(outside));
            Assert.True(server.SpawnSpotClearForSpeciesTest(walker.Id, outside));
            Assert.Equal(0, server.TownZoneDepthForTest(ruin));
            Assert.True(server.SpawnSpotClearForSpeciesTest(walker.Id, ruin), "a ruin stays wild ground");
            Assert.Equal(0, server.TownZoneDepthForTest(deepCave));
            Assert.True(server.CaveSpawnSpotClearForTest(deepCave), "a cave well under the town keeps its cave life");

            // Fliers: over the roofs and the lanes, yes — under a roof, no.
            Assert.True(server.SpawnSpotClearForSpeciesTest(flier.Id, overRoof), "a flier may spawn over the roof");
            Assert.True(server.SpawnSpotClearForSpeciesTest(flier.Id, overLane), "a flier may spawn over a lane");
            Assert.False(server.SpawnSpotClearForSpeciesTest(flier.Id, underRoof), "a flier never spawns under a town's roof");
        }
    }

    [Fact]
    public void AWildAnimalTurnsAwayAtTheTown_OneInsideMayOnlyLeave_AndGoesUnseen_APetFollowsItsOwnerIn()
    {
        var server = Started("town_move", out var repo);
        using (repo)
        {
            var walker = Land(server, 0);
            var hunter = Land(server, 1, CreatureTemperament.Aggressive);
            int padY = Pad(server, 16);
            int feet = padY + 1;
            RegisterTown(server, padY);

            // The step barrier: into the zone is a wall, along its edge is not.
            string edgeId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 10.5f, feet, Cz + 0.5f), walker.Id);
            Assert.Equal(0, server.TownZoneDepthForTest(new Vector3f(Cx + 10.5f, feet, Cz + 0.5f)));
            Assert.True(server.CreatureStepBlockedForTest(edgeId, new Vector3f(Cx + 9.6f, feet, Cz + 0.5f)), "the step into the town is a wall");
            Assert.False(server.CreatureStepBlockedForTest(edgeId, new Vector3f(Cx + 10.5f, feet, Cz + 1.4f)), "along the edge is free");

            // One already inside is never trapped: out or along is allowed, only deeper is not.
            string insideId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 3.5f, feet, Cz + 0.5f), walker.Id);
            Assert.False(server.CreatureStepBlockedForTest(insideId, new Vector3f(Cx + 4.5f, feet, Cz + 0.5f)), "a step out is free");
            Assert.False(server.CreatureStepBlockedForTest(insideId, new Vector3f(Cx + 3.5f, feet, Cz + 1.4f)), "a step along is free");
            Assert.True(server.CreatureStepBlockedForTest(insideId, new Vector3f(Cx + 2.5f, feet, Cz + 0.5f)), "a step deeper is not");
            Assert.Contains(server.Creatures, c => c.Id == edgeId);

            // The owner stands in town; her pet waits outside, a hunter prowls south of the town within reach of her.
            var owner = OnFoot(server, "Owner", new Vector3f(Cx + 0.5f, feet, Cz - 4.5f), 0f);
            owner.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "tc_pet",
                HomeBodyId = server.World.LocationId,
                Name = "Krümel",
                SpeciesId = walker.Id,
                Species = walker,
                SizeScale = 1f,
            });
            server.ReconcileCompanionsForTest();
            var pet = server.Creatures.First(c => c.IsCompanion && c.CompanionId == "tc_pet");
            pet.Position = new Vector3f(Cx + 10.5f, feet, Cz - 4.5f);
            Assert.False(server.CreatureStepBlockedForTest(pet.Id, new Vector3f(Cx + 9.6f, feet, Cz - 4.5f), terrainGates: false),
                "a pet walks into town with its owner");

            string hunterId = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, feet, Cz - 12f), hunter.Id);
            var prowler = server.Creatures.First(c => c.Id == hunterId);
            float nearest = prowler.Position.Z;
            for (int i = 0; i < 60; i++)
            {
                owner.State.Position = new Vector3f(Cx + 0.5f, feet, Cz - 4.5f);
                server.TickForTest(0.1);
                Assert.Equal(0, server.TownZoneDepthForTest(prowler.Position)); // never a step into the town
                nearest = System.Math.Max(nearest, prowler.Position.Z);
            }

            Assert.True(nearest > Cz - 10.5f, $"the hunter came up to the edge (closest z {nearest}) — and turned away there");
            Assert.True(server.TownZoneDepthForTest(pet.Position) > 0, "the pet followed its owner into town");

            // The animal still inside leaves unseen after a while: not while she looks at it, not before its grace is up.
            server.Creatures.First(c => c.Id == insideId).Position = new Vector3f(Cx - 3.5f, feet, Cz + 3.5f);
            server.PauseCreatureForTest(insideId, 60f);
            server.SweepBaseIntrudersForTest();
            var lingerer = server.Creatures.First(c => c.Id == insideId);
            Assert.True(lingerer.InTownSince >= 0, "the town clock started");
            server.AdvanceUptimeForTest(SvGameServer.TownStayGraceSeconds + 1);
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, c => c.Id == insideId); // in plain view

            owner.State.Yaw = 180f;
            Assert.True(server.SweepBaseIntrudersForTest());
            Assert.DoesNotContain(server.Creatures, c => c.Id == insideId);
            Assert.Contains(server.Creatures, c => c.Id == pet.Id);     // the pet is hers
            Assert.Contains(server.Creatures, c => c.Id == hunterId);   // the hunter never got in
        }
    }
}
