// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Still fish on dry land" (Lyxette report 2026-11-18, v2026.11.22, after #3551/#3552): two awake fish of the water roster
/// lay on the base floor, 35 s after the world loaded. Water ONE cell deep was water for the spawner and the swimmer's step
/// gate, but "a puddle, no column to swim in" for the swimmer's height probe — and the probe's dry answer could LIFT the
/// fish: to the nearest standable cell above it, or to the generator's surface when it found none. A one-cell pool flush
/// with a floor laid on the old ground therefore put the fish on the floor, beside its water. These tests pin both halves:
/// <list type="bullet">
/// <item>One rule for water life: water must be at least two cells deep to spawn in or to swim into.</item>
/// <item>The dry answer never lifts a water animal that is not stuck inside a block: in its puddle it rests on the
/// puddle's bed, and a floor above it is never where it ends up.</item>
/// </list>
/// </summary>
public sealed class ShallowWaterFishTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShallowWaterFishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shallow_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp dir is not worth failing a test run over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shallow"));
        var config = new ServerConfig
        {
            WorldName = "shallow",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Roster slot 0 as the reported fish: water life, legless, finned, passive, awake around the clock.</summary>
    private static CreatureSpecies Fish(SvGameServer server, int school = 1)
    {
        var sp = server.SpeciesRoster[0];
        sp.Habitat = CreatureHabitat.Water;
        sp.Legs = 0;
        sp.HasFins = true;
        sp.LocoStyle = LocomotionStyle.Schooler;
        sp.Temperament = CreatureTemperament.Passive;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HoverAltitude = 0f;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = school;
        sp.BiomeExclusive = false;
        return sp;
    }

    /// <summary>A spot whose 41×41 surroundings hold no generated water at all — dry land.</summary>
    private static (int X, int Z) FindDryLand(SvGameServer server)
    {
        for (int x = 64; x <= 1536; x += 48)
        {
            for (int z = 64; z <= 1536; z += 48)
            {
                bool dry = true;
                for (int dx = -20; dx <= 20 && dry; dx += 4)
                {
                    for (int dz = -20; dz <= 20 && dry; dz += 4)
                    {
                        dry = server.WaterSurfaceForTest(x + dx, z + dz) is null;
                    }
                }

                if (dry)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("no dry land on this world");
    }

    /// <summary>
    /// The reported scene, built on dry land: the ground around the spot is levelled to the generator's own surface
    /// there (<c>g</c>) — stone up to <c>g − 1</c>, a floor layer at <c>g</c> and air above, so the floor's walking height
    /// <c>g + 1</c> is exactly the generator surface + 1. Inside <paramref name="poolR"/> the floor layer is water instead:
    /// a pool one cell deep, flush with the floor round it, as a strip of water left in a base floor laid on the old
    /// ground. Inside <paramref name="deepR"/> (when given) the pool goes down to <c>g − 2</c>, three cells deep.
    /// Written without an owner. Returns <c>g</c>.
    /// </summary>
    private int LevelledPool(SvGameServer server, int cx, int cz, int r, int poolR, int deepR = -1)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        int g = server.SurfaceHeightForTest(cx, cz);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                bool pool = Math.Abs(dx) <= poolR && Math.Abs(dz) <= poolR;
                bool deep = Math.Abs(dx) <= deepR && Math.Abs(dz) <= deepR;
                for (int y = g - 6; y <= g + 24; y++)
                {
                    BlockId id = y > g ? BlockId.Air
                        : y == g ? (pool ? water : stone)
                        : deep && y >= g - 2 ? water
                        : stone;
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), id);
                }
            }
        }

        return g;
    }

    private static PlayerSession OnFoot(SvGameServer server, Vector3f at, float yaw = 0f)
    {
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Yaw = yaw;
        return p;
    }

    private bool IsWater(SvGameServer server, Vector3f p)
        => server.World.GetBlock(new Vector3i((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z))).Value
           == _content.GetBlock("water")!.NumericId.Value;

    // ---------------- one rule: water one cell deep is no water to live in ----------------

    /// <summary>The pure gate: a swimmer never steps into water one cell deep — the height probe calls that a puddle and
    /// lifts the fish out of it. Two cells is water to swim in; an amphibian may go anywhere.</summary>
    [Fact]
    public void TheSwimmerGate_RefusesWaterOneCellDeep()
    {
        Assert.True(CreatureBehaviour.TerrainStepBlocked(MotionClass.Swimmer, false, false, 60, 60, 3, 1));
        Assert.False(CreatureBehaviour.TerrainStepBlocked(MotionClass.Swimmer, false, false, 60, 60, 3, 2));
        Assert.False(CreatureBehaviour.TerrainStepBlocked(MotionClass.Swimmer, false, true, 60, 60, 3, 1));
    }

    /// <summary>On the world: a fish at the top of a deep pool does not step onto the one-cell shelf beside it, and a fish
    /// in the one-cell pool does not swim on through it.</summary>
    [Fact]
    public void AFish_DoesNotSwimIntoWaterOneCellDeep()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int g = LevelledPool(server, cx, cz, r: 8, poolR: 5, deepR: 1);
            OnFoot(server, new Vector3f(cx + 0.5f, g + 1, cz + 7.5f));

            string deep = server.SpawnCreatureAtForTest(new Vector3f(cx + 1.5f, g, cz + 0.5f), sp.Id); // the deep pool's top cell
            Assert.True(server.TerrainStepBlockedForTest(deep, new Vector3f(cx + 2.5f, g, cz + 0.5f)),
                "a fish stepped from the deep pool onto the one-cell shelf beside it");
            Assert.False(server.TerrainStepBlockedForTest(deep, new Vector3f(cx + 0.5f, g, cz + 0.5f))); // control: deep water

            string shallow = server.SpawnCreatureAtForTest(new Vector3f(cx + 3.5f, g, cz + 0.5f), sp.Id);
            Assert.True(server.TerrainStepBlockedForTest(shallow, new Vector3f(cx + 4.5f, g, cz + 0.5f)),
                "a fish swam on through water one cell deep");
        }
    }

    /// <summary>The spawner: a school is never seated in water one cell deep. A pool one cell deep, flush with the floor
    /// round it, is all the water in reach — not one fish appears in it. Control: the same pool three cells deep gets its
    /// school.</summary>
    [Fact]
    public void TheSpawner_NeverSeatsAFishInWaterOneCellDeep()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server, school: 6);
            var (cx, cz) = FindDryLand(server);
            int g = LevelledPool(server, cx, cz, r: 14, poolR: 12);
            int before = server.Creatures.Count(x => x.SpeciesId == sp.Id);

            server.SpawnGroupAroundForTest(sp.Id, cx, cz);

            var seated = server.Creatures.Where(x => x.SpeciesId == sp.Id).Skip(before).Select(x => x.Position).ToList();
            Assert.True(seated.Count == 0,
                $"the spawner seated {seated.Count} fish in water one cell deep (pool at y={g}): {string.Join(", ", seated)}");

            LevelledPool(server, cx, cz, r: 14, poolR: 12, deepR: 12);
            server.SpawnGroupAroundForTest(sp.Id, cx, cz);
            Assert.True(server.Creatures.Count(x => x.SpeciesId == sp.Id) > before, "control: the deep pool got no school");
        }
    }

    // ---------------- the dry answer never lifts a fish onto a floor ----------------

    /// <summary>The reported scene on the tick: a fish in a pool one cell deep, flush with a floor laid on the old ground,
    /// stays in its water. The height probe called the pool a puddle, found no standable cell in the column (water carries
    /// nobody) and answered the generator surface + 1 — the floor's walking height: the fish lay on the floor beside its
    /// pool, awake and in plain view.</summary>
    [Fact]
    public void AFish_InAPoolOneCellDeep_FlushWithTheFloor_IsNeverLiftedOntoTheFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int g = LevelledPool(server, cx, cz, r: 6, poolR: 1);
            OnFoot(server, new Vector3f(cx + 0.5f, g + 1, cz + 2.5f), yaw: 180f); // on the floor beside the pool, watching

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, g, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 60f);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
                var c = server.Creatures.FirstOrDefault(x => x.Id == id);
                Assert.True(c is not null, $"tick {i}: the fish in the one-cell pool was removed");
                Assert.True(c!.Position.Y < g + 1 && IsWater(server, c.Position),
                    $"tick {i}: the fish left its one-cell pool (water at y={g}) — it lies at {c.Position}, the floor walks at {g + 1}");
            }
        }
    }

    /// <summary>The upward half of the same probe: a fish in a one-cell pool under an upper floor (a storey four cells up)
    /// is never lifted through the air onto that floor — the probe looked up for the nearest standable cell when nothing
    /// below its feet was standable, and the top of the upper floor was one.</summary>
    [Fact]
    public void AFish_InAPoolOneCellDeep_UnderAnUpperFloor_IsNeverLiftedOntoIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int g = LevelledPool(server, cx, cz, r: 6, poolR: 1);
            var stone = _content.GetBlock("stone")!.NumericId;
            int upper = g + 4; // two-cell fish: water at g, air g+1..g+3, the upper floor at g+4
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, upper, cz + dz), stone, owner: "builder");
                }
            }

            OnFoot(server, new Vector3f(cx + 0.5f, g + 1, cz + 2.5f), yaw: 180f);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, g, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 60f);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
                var c = server.Creatures.FirstOrDefault(x => x.Id == id);
                Assert.True(c is not null, $"tick {i}: the fish in the one-cell pool was removed");
                Assert.True(c!.Position.Y < upper,
                    $"tick {i}: the fish was lifted onto the upper floor (pool at y={g}, upper floor at {upper}) — it is at {c.Position}");
            }
        }
    }

    /// <summary>The one-off placement (a companion's spot, the taming scare bolt) obeys the same rule: set down in the
    /// one-cell pool, the fish is in the pool — not on the floor beside it.</summary>
    [Fact]
    public void AFish_SetDownInAPoolOneCellDeep_StaysInThePool()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int g = LevelledPool(server, cx, cz, r: 6, poolR: 1);
            var at = server.HabitatHeightForTest(sp.Id, new Vector3f(cx + 0.5f, g + 0.5f, cz + 0.5f));
            Assert.True(at.Y < g + 1, $"the fish was set down on the floor at {at.Y} instead of in its pool at {g}");
        }
    }
}
