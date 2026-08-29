// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Motion classes on a real server (#1331–#1334): stone arenas floating above all natural terrain, one
/// creature forced into the class under test, and the real tick loop — walkers jump a one-block ledge and are
/// walled by two, crawlers and giants haul over without ever leaving the ground, a removed floor is a fall,
/// fliers perch when they pause and flush when a player nears, hoverers never come down, ground birds bound
/// when startled, amphibians swap class at the waterline.
/// </summary>
public sealed class CreatureMotionArenaTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureMotionArenaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_motion_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "motion"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "motion",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Forces roster slot 0 into a plain, always-awake, solitary species of the wanted shape.</summary>
    private static CreatureSpecies Force(SvGameServer server, CreatureHabitat habitat, int legs, LocomotionStyle style,
        CreatureTemperament temperament = CreatureTemperament.Passive, bool wings = false, bool gasSac = false,
        float size = 1f, CreatureBodyPlan plan = CreatureBodyPlan.Standard, float hover = 0f)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = habitat;
        sp.Legs = legs;
        sp.LocoStyle = style;
        sp.Temperament = temperament;
        sp.HasWings = wings;
        sp.HasGasSac = gasSac;
        sp.Size = size;
        sp.BodyPlan = plan;
        sp.HoverAltitude = hover;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        return sp;
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        return max;
    }

    private void BuildPad(SvGameServer server, int cx, int cz, int r, int padY)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }
    }

    /// <summary>Raises the pad's +X half by <paramref name="height"/> extra layers from <paramref name="fromDx"/> on.</summary>
    private void BuildLedge(SvGameServer server, int cx, int cz, int r, int padY, int fromDx, int height)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = fromDx; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dy = 1; dy <= height; dy++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, padY + dy, cz + dz), stone);
                }
            }
        }
    }

    /// <summary>A hunter on the low half of the pad, the player on the raised half past a ledge of
    /// <paramref name="ledgeHeight"/>: returns whether it crossed, and whether it was ever airborne doing so.</summary>
    private (bool crossed, bool airborne, float finalY) HuntAcrossLedge(SvGameServer server, int cx, int cz, int ledgeHeight, int ticks)
    {
        var p = server.AddLocalPlayer("Bait");
        p.State.AboardShip = false;
        int padY = MaxTopY(server, cx, cz, 10) + 8;
        BuildPad(server, cx, cz, 8, padY);
        BuildLedge(server, cx, cz, 8, padY, fromDx: 1, height: ledgeHeight);

        p.State.Position = new Vector3f(cx + 5.5f, padY + 1 + ledgeHeight, cz + 0.5f);
        string id = server.SpawnCreatureAtForTest(new Vector3f(cx - 1.5f, padY + 1, cz + 0.5f));

        bool airborne = false, crossed = false;
        var live = server.Creatures.First(x => x.Id == id);
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(0.1);
            live = server.Creatures.First(x => x.Id == id);
            airborne |= live.Vert.Airborne;
            crossed |= live.Position.X >= cx + 1.2f;
        }

        return (crossed, airborne, live.Position.Y);
    }

    [Fact]
    public void Walker_JumpsAOneBlockLedge_AndStandsOnIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider, CreatureTemperament.Aggressive);
            var (crossed, airborne, y) = HuntAcrossLedge(server, 60, 60, ledgeHeight: 1, ticks: 120);
            Assert.True(crossed, "a walker must take a one-block ledge (Q1a)");
            Assert.True(airborne, "…by JUMPING it, not gliding up (#1331)");
        }
    }

    [Fact]
    public void Walker_IsWalledByATwoBlockLedge_LikeThePlayer()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider, CreatureTemperament.Aggressive);
            var (crossed, _, _) = HuntAcrossLedge(server, -60, 60, ledgeHeight: 2, ticks: 150);
            Assert.False(crossed, "two blocks is a wall for a walker, exactly as for the player (Q1a)");
        }
    }

    [Fact]
    public void Crawler_HaulsOverAOneBlockLedge_WithoutEverLeavingTheGround()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Land, legs: 0, LocomotionStyle.Slitherer, CreatureTemperament.Aggressive);
            var (crossed, airborne, _) = HuntAcrossLedge(server, 60, -60, ledgeHeight: 1, ticks: 150);
            Assert.True(crossed, "a crawler climbs a one-block rise");
            Assert.False(airborne, "…and never jumps (#1331)");
        }
    }

    [Fact]
    public void Giant_StepsOneBlock_WithoutJumping()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Size 2.5: a giant by size (Q3) but below the large-body volume checks, so only the jump rule differs.
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider, CreatureTemperament.Aggressive, size: 2.5f);
            var (crossed, airborne, _) = HuntAcrossLedge(server, -60, -60, ledgeHeight: 1, ticks: 150);
            Assert.True(crossed, "a giant still takes a one-block step");
            Assert.False(airborne, "…but a giant never jumps (Q3)");
        }
    }

    [Fact]
    public void ACreatureAboveTheFloor_Falls_UnderGravity_InsteadOfEasingDown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Grazer);

            const int cx = 120, cz = 120;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 6, padY);
            p.State.Position = new Vector3f(cx + 12, padY + 1, cz);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 4, cz + 0.5f)); // 3 above the floor

            bool airborne = false;
            for (int i = 0; i < 3; i++)
            {
                server.TickForTest(0.1);
                airborne |= server.Creatures.First(x => x.Id == id).Vert.Airborne;
            }

            float early = server.Creatures.First(x => x.Id == id).Position.Y;
            Assert.True(airborne, "it must be falling (#1331)");
            Assert.True(early >= padY + 3.0f, $"a fall starts slowly (Y {early:F2} after 0.3 s) — the old 6 b/s elevator would already be at {padY + 2.2f:F1}");

            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.1);
            }

            var c = server.Creatures.First(x => x.Id == id);
            Assert.Equal(padY + 1, c.Position.Y, 2);
            Assert.False(c.Vert.Airborne);
        }
    }

    [Fact]
    public void Flier_PerchesWhenItPauses_AndFlushesWhenThePlayerNears()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Birder");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Air, legs: 2, LocomotionStyle.Glider, CreatureTemperament.Skittish, wings: true, hover: 4f);

            const int cx = -120, cz = 120;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 6, padY);
            p.State.Position = new Vector3f(cx + 20, padY + 1, cz); // well outside the flee reflex
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 5, cz + 0.5f));
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 10f);

            var c = server.Creatures.First(x => x.Id == id);
            for (int i = 0; i < 60 && c.Vert.Flight != FlightPhase.Perched; i++)
            {
                server.TickForTest(0.1);
                c = server.Creatures.First(x => x.Id == id);
            }

            Assert.Equal(FlightPhase.Perched, c.Vert.Flight);
            Assert.Equal(padY + 1, c.Position.Y, 2); // sitting ON the pad (#1332)

            // A skittish bird flushes when the player comes within its flee reflex.
            p.State.Position = new Vector3f(c.Position.X + 3f, padY + 1, c.Position.Z);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
            }

            c = server.Creatures.First(x => x.Id == id);
            Assert.NotEqual(FlightPhase.Perched, c.Vert.Flight);
            Assert.True(c.Position.Y > padY + 2.5f, $"it must have taken off (Y {c.Position.Y:F2})");
        }
    }

    [Fact]
    public void Hoverer_NeverComesDown_EvenWhenItPauses()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Watcher");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Air, legs: 2, LocomotionStyle.Drifter, wings: true, gasSac: true, hover: 4f);

            const int cx = 120, cz = -120;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 6, padY);
            p.State.Position = new Vector3f(cx + 20, padY + 1, cz);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 5, cz + 0.5f));
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 10f);

            for (int i = 0; i < 80; i++)
            {
                server.TickForTest(0.1);
                var c = server.Creatures.First(x => x.Id == id);
                Assert.True(c.Position.Y >= padY + 4.4f, $"a gas sac stays in its hover band (Q5) — Y {c.Position.Y:F2} at tick {i}");
                Assert.Equal(FlightPhase.Flying, c.Vert.Flight);
            }
        }
    }

    [Fact]
    public void GroundBird_BoundsWhenStartled()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Fox");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Land, legs: 2, LocomotionStyle.Darter, CreatureTemperament.Skittish, wings: true);

            const int cx = -120, cz = -120;
            int padY = MaxTopY(server, cx, cz, 10) + 8;
            BuildPad(server, cx, cz, 9, padY);
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
            p.State.Position = new Vector3f(cx + 3f, padY + 1, cz + 0.5f); // inside the flee reflex

            bool airborne = false;
            for (int i = 0; i < 40 && !airborne; i++)
            {
                server.TickForTest(0.1);
                airborne = server.Creatures.First(x => x.Id == id).Vert.Airborne;
            }

            Assert.True(airborne, "a startled ground bird bounds (#1334)");
        }
    }

    [Fact]
    public void Amphibian_SwimsInWater_AndWalksAshore()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Frogspotter");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Amphibian, legs: 4, LocomotionStyle.Strider);

            const int cx = 180, cz = 180;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 6, padY);
            var water = _content.GetBlock("water")!.NumericId;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, padY + 1, cz + dz), water); // a shallow pool
                }
            }

            p.State.Position = new Vector3f(cx + 15, padY + 1, cz);
            string wet = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
            string dry = server.SpawnCreatureAtForTest(new Vector3f(cx + 5.5f, padY + 1, cz + 0.5f));
            server.TickForTest(0.1);
            server.TickForTest(0.1);

            Assert.True(server.Creatures.First(x => x.Id == wet).Vert.InWater, "in the pool it swims (#1334)");
            Assert.False(server.Creatures.First(x => x.Id == dry).Vert.InWater, "ashore it walks");
        }
    }

    // ---------------- #1349: the ground probe prefers the floor below ----------------

    /// <summary>A grazer holding still on a pad under a stone ceiling (feet cells padY+1..+3 free, slab at
    /// padY+4, upper-floor feet at padY+5); the player well outside. Returns the creature id.</summary>
    private string GrazerUnderACeiling(SvGameServer server, int cx, int cz, int padY)
    {
        var p = server.AddLocalPlayer("Digger");
        p.State.AboardShip = false;
        Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Grazer);
        BuildPad(server, cx, cz, 6, padY);
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY + 4, cz + dz), stone); // the ceiling / upper floor
            }
        }

        p.State.Position = new Vector3f(cx + 14, padY + 1, cz);
        string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
        server.PauseCreatureForTest(id, 30f);
        server.TickForTest(0.1);
        server.TickForTest(0.1);
        Assert.Equal(padY + 1, server.Creatures.First(x => x.Id == id).Position.Y, 2);
        return id;
    }

    [Fact]
    public void APitDugUnderAGroundFloorAnimal_DropsItIntoThePit_NotThroughTheCeiling()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int cx = 240, cz = 240;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            string id = GrazerUnderACeiling(server, cx, cz, padY);

            // A six-deep pit under its feet: the upper floor (+4) is now the NEAREST standable cell, the pit
            // floor (−6) the right one.
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(cx, padY - 6, cz), stone);
            server.World.SetBlock(new Vector3i(cx, padY, cz), BlockId.Air);

            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
                var c = server.Creatures.First(x => x.Id == id);
                Assert.True(c.Position.Y <= padY + 1.01f, $"it must never rise (Y {c.Position.Y:F2} at tick {i}) — the old probe lifted it through the ceiling");
            }

            var landed = server.Creatures.First(x => x.Id == id);
            Assert.Equal(padY - 5, landed.Position.Y, 2);
            Assert.False(landed.Vert.Airborne);
        }
    }

    [Fact]
    public void AnAnimalWithNoFloorBelowAtAll_StillRecoversUpward()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // The pad floats more than a full scan (24) above the terrain, so once its floor is gone nothing
            // below is standable — the upward fallback (the entombed-recovery path) must still lift it.
            const int cx = -240, cz = 240;
            int padY = MaxTopY(server, cx, cz, 8) + 30;
            string id = GrazerUnderACeiling(server, cx, cz, padY);

            server.World.SetBlock(new Vector3i(cx, padY, cz), BlockId.Air);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
            }

            var c = server.Creatures.First(x => x.Id == id);
            Assert.Equal(padY + 5, c.Position.Y, 2); // on the upper floor
        }
    }
}
