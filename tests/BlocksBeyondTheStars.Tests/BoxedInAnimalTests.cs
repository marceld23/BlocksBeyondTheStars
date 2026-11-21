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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// F1 report of 2026-11-21 (v2026.11.26): "a big animal in the water keeps turning back and forth hectically" — a skittish
/// land animal stood on the bed of a pond three blocks deep, the reporter 5.5 blocks away.
/// <list type="bullet">
/// <item>#3881: the wading gate let a walker or crawler step only into water at most one deep, so an animal already in deep
/// water found every way blocked and was trapped for good. It may take any step that does not get deeper now, and the sweep
/// sends one that stays in deep water away while nobody looks.</item>
/// <item>#3880: with every step and detour blocked, a fleer kept its flight's jink on the spot and a roamer re-rolled one
/// random heading after the other. A boxed-in animal now stands calmly, and walks out once a way opens.</item>
/// </list>
/// </summary>
public sealed class BoxedInAnimalTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BoxedInAnimalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_boxed_" + Guid.NewGuid().ToString("N"));
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

    // ---------------- #3881: the pure wading gate ----------------

    [Fact]
    public void TheWadingGate_LetsAnAnimalInDeepWaterWadeOut_ButNeverIntoDeepWaterFromAShallowSpot()
    {
        foreach (var motion in new[] { MotionClass.Walker, MotionClass.Crawler })
        {
            // Already in deep water: along at the same depth and toward the shore are free, deeper is still a wall.
            Assert.False(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 60, 3, 3));
            Assert.False(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 61, 3, 2));
            Assert.False(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 61, 3, 0));
            Assert.True(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 59, 3, 4));

            // From dry ground or a wading depth deep water stays shut, as before.
            Assert.True(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 60, 0, 2));
            Assert.True(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 60, 1, 2));
            Assert.False(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 60, 0, 1));

            // The step limits still hold in the water.
            Assert.True(CreatureBehaviour.TerrainStepBlocked(motion, false, false, 60, 62, 3, 1));
        }
    }

    // ---------------- #3881: in the world ----------------

    [Fact]
    public void ALandAnimalOnAPondBed_MayWadeAlongButNotDeeper_AndLeavesWhileNobodyLooks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = LandAnimal(server, CreatureTemperament.Passive);
            var (cx, cz) = FindDryLand(server);
            int bed = RaisedPond(server, cx, cz, depth: 3);
            var water = _content.GetBlock("water")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(cx, bed, cz - 2), water); // one column of the bed dug a block deeper
            server.World.SetBlock(new Vector3i(cx, bed - 1, cz - 2), stone);

            var player = OnFoot(server, new Vector3f(cx + 2.5f, bed + 1, cz + 0.5f), yaw: 0f); // two blocks off: seen
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, bed + 1, cz - 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 120f);
            Assert.True(server.LandInDeepWaterForTest(id));

            Assert.False(server.TerrainStepBlockedForTest(id, new Vector3f(cx + 1.5f, bed + 1, cz - 0.5f)),
                "along the bed at the same depth must be free — the animal was trapped");
            Assert.True(server.TerrainStepBlockedForTest(id, new Vector3f(cx + 0.5f, bed, cz - 1.5f)),
                "into the deeper column must stay shut");

            // Seen: it stays, however long. Unseen: it gets its grace first, then it goes.
            server.SweepBaseIntrudersForTest();
            server.AdvanceUptimeForTest(SvGameServer.DeepWaterGraceSeconds + 1);
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, c => c.Id == id);

            player.State.Position = new Vector3f(cx + 0.5f, bed + 1, cz + 40.5f); // far off, facing away (+Z)
            server.SweepBaseIntrudersForTest();
            Assert.DoesNotContain(server.Creatures, c => c.Id == id);

            string fresh = server.SpawnCreatureAtForTest(new Vector3f(cx - 1.5f, bed + 1, cz + 1.5f), sp.Id);
            server.PauseCreatureForTest(fresh, 120f);
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, c => c.Id == fresh); // its clock has only just started
            server.AdvanceUptimeForTest(SvGameServer.DeepWaterGraceSeconds + 1);
            server.SweepBaseIntrudersForTest();
            Assert.DoesNotContain(server.Creatures, c => c.Id == fresh);
        }
    }

    [Fact]
    public void ALandAnimalOnDryGround_OrAnAmphibianInThePond_IsNotInDeepWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = LandAnimal(server, CreatureTemperament.Passive);
            var (cx, cz) = FindDryLand(server);
            int bed = RaisedPond(server, cx, cz, depth: 3);
            OnFoot(server, new Vector3f(cx + 2.5f, bed + 1, cz + 0.5f));
            string onRim = server.SpawnCreatureAtForTest(new Vector3f(cx + 4.5f, bed + 4, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(onRim, 60f);
            Assert.False(server.LandInDeepWaterForTest(onRim));

            var frog = server.SpeciesRoster[1];
            frog.Habitat = CreatureHabitat.Amphibian;
            string swimmer = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, bed + 1, cz + 0.5f), frog.Id);
            server.PauseCreatureForTest(swimmer, 60f);
            Assert.False(server.LandInDeepWaterForTest(swimmer));
        }
    }

    // ---------------- #3880: a boxed-in animal calms down ----------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABoxedInAnimal_StandsCalmly_AndWalksOutOnceAWayOpens(bool fleeing)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = LandAnimal(server, fleeing ? CreatureTemperament.Skittish : CreatureTemperament.Passive);
            var (cx, cz) = FindDryLand(server);
            int padY = DryPad(server, cx, cz, 12);
            Pit(server, cx, cz, padY, wallHeight: 2);

            // Skittish: a player four blocks off, inside the flight range. Passive: ten off, just near enough for the tick.
            OnFoot(server, new Vector3f(cx + 0.5f, padY + 1, cz + (fleeing ? 4.5f : 10.5f)), yaw: 180f);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);

            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.1); // it tries to move and finds every way shut
            }

            int still = 0;
            float last = Creature(server, id).Loco.Heading;
            var trace = new System.Text.StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
                var c = Creature(server, id);
                trace.Append($"{i}: h={c.Loco.Heading:F3} m={c.Loco.Mode} b={c.Loco.BoxedTimer:F2} v={c.Loco.Speed:F2} p={c.Position}\n");
                Assert.Equal(cx, (int)MathF.Floor(c.Position.X));
                Assert.Equal(cz, (int)MathF.Floor(c.Position.Z));
                if (MathF.Abs(c.Loco.Heading - last) < 1e-4f)
                {
                    still++;
                }

                last = c.Loco.Heading;
            }

            Assert.True(still >= 30, $"a boxed-in animal should mostly hold its heading — it held it on {still} of 60 ticks\n{trace}");

            // The far side of the pit opens: it walks out (a fleer away from the player, a roamer sooner or later).
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int y = padY + 1; y <= padY + 2; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz - 1), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
                }
            }

            bool left = false;
            for (int i = 0; i < 300 && !left; i++)
            {
                server.TickForTest(0.1);
                var c = Creature(server, id);
                left = (int)MathF.Floor(c.Position.Z) < cz;
            }

            Assert.True(left, "once a way opened the animal should have walked out of the pit");
        }
    }

    // ---------------- fixture ----------------

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "boxed"));
        var config = new ServerConfig
        {
            WorldName = "boxed",
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

    /// <summary>Roster slot 0 as a plain land walker: four legs, one block, solitary, awake around the clock.</summary>
    private static CreatureSpecies LandAnimal(SvGameServer server, CreatureTemperament temperament)
    {
        var sp = server.SpeciesRoster[0];
        sp.Habitat = CreatureHabitat.Land;
        sp.Legs = 4;
        sp.HasFins = false;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.LocoStyle = LocomotionStyle.Strider;
        sp.Temperament = temperament;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HoverAltitude = 0f;
        sp.GiantHeight = 0f;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        return sp;
    }

    private static CombatEntity Creature(SvGameServer server, string id) => server.Creatures.Single(c => c.Id == id);

    private static PlayerSession OnFoot(SvGameServer server, Vector3f at, float yaw = 0f)
    {
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Yaw = yaw;
        return p;
    }

    /// <summary>A spot whose surroundings hold no generated water — dry land.</summary>
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

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        max = Math.Max(max, y);
                        break;
                    }
                }
            }
        }

        return max;
    }

    /// <summary>A natural (owner-less) stone pad floating above everything — dry ground no player built. Returns its Y.</summary>
    private int DryPad(SvGameServer server, int cx, int cz, int r)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int padY = MaxTopY(server, cx, cz, r) + 8;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    /// <summary>Walls of <paramref name="wallHeight"/> stone on the eight columns round (cx, cz) on the pad — a one-column
    /// pit no walker can climb out of.</summary>
    private void Pit(SvGameServer server, int cx, int cz, int padY, int wallHeight)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                for (int y = padY + 1; y <= padY + wallHeight; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), stone);
                }
            }
        }
    }

    /// <summary>A stone basin floating above everything: a 7×7 pond inside a one-block stone rim, the water
    /// <paramref name="depth"/> cells deep. Written without an owner. Returns the bed Y.</summary>
    private int RaisedPond(SvGameServer server, int cx, int cz, int depth)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        int bed = MaxTopY(server, cx, cz, 6) + 10;
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dz = -4; dz <= 4; dz++)
            {
                bool rim = Math.Abs(dx) == 4 || Math.Abs(dz) == 4;
                server.World.SetBlock(new Vector3i(cx + dx, bed, cz + dz), stone);
                for (int y = bed + 1; y <= bed + depth; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), rim ? stone : water);
                }
            }
        }

        return bed;
    }
}
