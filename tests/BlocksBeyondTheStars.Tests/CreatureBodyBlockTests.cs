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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The drawn body against the blocks (Lyxette's reports 2026-10-23) on a real server: stone arenas floating above all
/// natural terrain, one creature forced into the shape under test, the real tick loop. #2943: a sky ray's wings ride over
/// a roof beside it and never enter a wall. #2944: a big walker stops with its head outside a wall and still climbs a
/// stair. #2945: a land animal never climbs or stays on a tree crown; a bird still perches on one.
/// </summary>
public sealed class CreatureBodyBlockTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureBodyBlockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bodyblock_" + Guid.NewGuid().ToString("N"));
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
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bodyblock"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "bodyblock",
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
        CreatureTemperament temperament, float size, CreatureBodyPlan plan = CreatureBodyPlan.Standard, float hover = 0f,
        bool wings = false, int segments = 1)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = habitat;
        sp.Legs = legs;
        sp.LocoStyle = style;
        sp.Temperament = temperament;
        sp.HasWings = wings;
        sp.HasGasSac = false;
        sp.Size = size;
        sp.BodyPlan = plan;
        sp.BodySegments = segments;
        sp.NeckLength = 0;
        sp.HoverAltitude = hover;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        return sp;
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

    /// <summary>Fills the box [x0..x1]×[y0..y1]×[z0..z1] with <paramref name="block"/>.</summary>
    private void Fill(SvGameServer server, string block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var id = _content.GetBlock(block)!.NumericId;
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), id);
                }
            }
        }
    }

    /// <summary>A stone pad 8 above the highest natural ground round the spot; returns its Y.</summary>
    private int Pad(SvGameServer server, int cx, int cz, int r)
    {
        int padY = MaxTopY(server, cx, cz, r) + 8;
        Fill(server, "stone", cx - r, cx + r, padY, padY, cz - r, cz + r);
        return padY;
    }

    private static string Spawn(SvGameServer server, Vector3f at)
    {
        string id = server.SpawnCreatureAtForTest(at);
        server.Creatures.First(x => x.Id == id).SizeScale = 1f; // the exact species size — no individual variation
        return id;
    }

    [Fact]
    public void SkyRay_BesideARoof_RidesItsWingsOverTheRoof()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Roofer");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Air, legs: 0, LocomotionStyle.Drifter, CreatureTemperament.Passive, size: 3f,
                CreatureBodyPlan.Ray, hover: 4f, wings: true);

            const int cx = 140, cz = 140;
            int padY = Pad(server, cx, cz, 10);
            // A roof at the ray's wing height, starting two blocks to its +X side — the reported case: centre in free air,
            // half a wing in the concrete.
            Fill(server, "stone", cx + 2, cx + 10, padY + 5, padY + 5, cz - 10, cz + 10);
            p.State.Position = new Vector3f(cx - 20, padY + 1, cz);
            string id = Spawn(server, new Vector3f(cx + 0.5f, padY + 5, cz + 0.5f));
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 30f);

            for (int i = 0; i < 80; i++)
            {
                server.TickForTest(0.1);
            }

            var c = server.Creatures.First(x => x.Id == id);
            Assert.True(Math.Abs(c.Position.X - (cx + 0.5f)) < 0.01f, "the paused ray must stay over its spot");
            Assert.Equal(int.MinValue, server.WingDiscTopHitForTest(id));
            Assert.True(c.Position.Y > padY + 5.6f, $"its band rises so the wings clear the roof — Y {c.Position.Y:F2}, roof top {padY + 6}");
        }
    }

    [Fact]
    public void SkyRay_HuntingPastAWall_NeverPutsItsWingsIntoTheWall()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Air, legs: 0, LocomotionStyle.Drifter, CreatureTemperament.Aggressive, size: 3f,
                CreatureBodyPlan.Ray, hover: 4f, wings: true);

            const int cx = -140, cz = 140;
            int padY = Pad(server, cx, cz, 12);
            Fill(server, "stone", cx + 3, cx + 3, padY + 1, padY + 20, cz - 12, cz + 12); // far above any lift
            p.State.Position = new Vector3f(cx + 4.5f, padY + 1, cz + 0.5f); // within the hunter's reach (8 + size − 2)
            string id = Spawn(server, new Vector3f(cx - 3.5f, padY + 5, cz + 0.5f));

            float maxX = float.MinValue;
            for (int i = 0; i < 150; i++)
            {
                server.TickForTest(0.1);
                Assert.Equal(int.MinValue, server.WingDiscTopHitForTest(id));
                maxX = Math.Max(maxX, server.Creatures.First(x => x.Id == id).Position.X);
            }

            Assert.True(maxX > cx - 2f, $"the hunter must have flown up to the wall (max X {maxX:F2})");
        }
    }

    [Fact]
    public void BigWalker_StopsWithItsHeadOutsideAWall()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            var sp = Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider, CreatureTemperament.Aggressive, size: 2.5f,
                segments: 3);
            float reach = CreatureFootprint.HeadReach(sp, 1f);
            Assert.True(reach > 2f, $"the test needs a head well past the centre column (reach {reach:F2})");

            const int cx = 140, cz = -140;
            int padY = Pad(server, cx, cz, 12);
            Fill(server, "stone", cx + 3, cx + 3, padY + 1, padY + 6, cz - 12, cz + 12);
            p.State.Position = new Vector3f(cx + 5.5f, padY + 1, cz + 0.5f); // within the hunter's reach (8 + size − 2)
            string id = Spawn(server, new Vector3f(cx - 2.5f, padY + 1, cz + 0.5f));

            // Along every step it takes, the head (the middle of the head box, 0.9 of its reach) stays outside the wall —
            // walking straight at it, the centre stops that far short; skirting it, the head points along the wall.
            float wallFace = cx + 3, head = 0.9f * reach, maxHeadX = float.MinValue;
            var prev = server.Creatures.First(x => x.Id == id).Position;
            for (int i = 0; i < 200; i++)
            {
                server.TickForTest(0.1);
                var at = server.Creatures.First(x => x.Id == id).Position;
                float dx = at.X - prev.X, dz = at.Z - prev.Z, len = MathF.Sqrt((dx * dx) + (dz * dz));
                if (len > 1e-3f)
                {
                    float headX = at.X + (dx / len * head);
                    Assert.True(headX < wallFace + 0.01f, $"tick {i}: head at X {headX:F2} inside the wall at {wallFace} (centre {at.X:F2})");
                    maxHeadX = Math.Max(maxHeadX, headX);
                }

                prev = at;
            }

            Assert.True(maxHeadX >= wallFace - 1.5f, $"…but it must have come up to it (head X {maxHeadX:F2})");
        }
    }

    [Fact]
    public void BigWalker_StillClimbsAStair()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider, CreatureTemperament.Aggressive, size: 2.5f,
                segments: 3);

            const int cx = -140, cz = -140;
            int padY = Pad(server, cx, cz, 12);
            for (int step = 1; step <= 4; step++)
            {
                Fill(server, "stone", cx + step, cx + 12, padY + step, padY + step, cz - 12, cz + 12); // one block a column
            }

            p.State.Position = new Vector3f(cx + 6.5f, padY + 5, cz + 0.5f); // the hunter's reach is horizontal
            string id = Spawn(server, new Vector3f(cx - 1.5f, padY + 1, cz + 0.5f));
            float maxY = float.MinValue;
            for (int i = 0; i < 250; i++)
            {
                server.TickForTest(0.1);
                maxY = Math.Max(maxY, server.Creatures.First(x => x.Id == id).Position.Y);
            }

            Assert.True(maxY >= padY + 5 - 0.01f, $"a stair is not a wall for the head line — it must reach the top (max Y {maxY:F2}, top {padY + 5})");
        }
    }

    [Fact]
    public void Crawler_NeverClimbsAStairOfLeaves()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Land, legs: 0, LocomotionStyle.Slitherer, CreatureTemperament.Aggressive, size: 0.8f);

            const int cx = 200, cz = 140;
            int padY = Pad(server, cx, cz, 12);
            // A crown that reaches the ground, rising one block a column — exactly the slope a crawler may haul up.
            for (int step = 1; step <= 5; step++)
            {
                Fill(server, "tree_leaves", cx + step, cx + 12, padY + step, padY + step, cz - 12, cz + 12);
            }

            p.State.Position = new Vector3f(cx + 5.5f, padY + 6, cz + 0.5f); // within the hunter's reach
            string id = Spawn(server, new Vector3f(cx - 1.5f, padY + 1, cz + 0.5f));
            float maxY = float.MinValue;
            for (int i = 0; i < 200; i++)
            {
                server.TickForTest(0.1);
                maxY = Math.Max(maxY, server.Creatures.First(x => x.Id == id).Position.Y);
            }

            Assert.True(maxY < padY + 1.5f, $"a crown carries fliers only — the crawler must stay on the ground (max Y {maxY:F2})");
        }
    }

    [Fact]
    public void LandAnimal_OnACrown_ComesDownToTheGround()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Watcher");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Land, legs: 6, LocomotionStyle.Strider, CreatureTemperament.Passive, size: 1f);

            const int cx = -200, cz = 140;
            int padY = Pad(server, cx, cz, 8);
            Fill(server, "tree_leaves", cx - 3, cx + 3, padY + 6, padY + 8, cz - 3, cz + 3); // a crown floating over the pad
            p.State.Position = new Vector3f(cx + 20, padY + 1, cz);
            string id = Spawn(server, new Vector3f(cx + 0.5f, padY + 9, cz + 0.5f)); // asleep on top of it, as reported
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 30f);
            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
            }

            var c = server.Creatures.First(x => x.Id == id);
            Assert.Equal(padY + 1, c.Position.Y, 2);
        }
    }

    [Fact]
    public void GroundProbe_ReadsACrownAsFloorForFliersOnly()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Force(server, CreatureHabitat.Land, legs: 6, LocomotionStyle.Strider, CreatureTemperament.Passive, size: 1f);

            const int cx = 200, cz = -140;
            int padY = Pad(server, cx, cz, 4);
            // The reported "lift": the animal's own cells filled (ash fallen on a sleeper), a crown above them.
            Fill(server, "stone", cx, cx, padY + 1, padY + 2, cz, cz);
            Fill(server, "tree_leaves", cx, cx, padY + 3, padY + 5, cz, cz);
            int crownTop = padY + 6;

            Assert.NotEqual(crownTop, server.GroundFeetForTest(sp.Id, cx, cz, padY + 1));

            sp.Habitat = CreatureHabitat.Air;
            Assert.Equal(crownTop, server.GroundFeetForTest(sp.Id, cx, cz, padY + 1));
        }
    }

    [Fact]
    public void Flier_StillPerchesOnACrown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Birder");
            p.State.AboardShip = false;
            Force(server, CreatureHabitat.Air, legs: 2, LocomotionStyle.Glider, CreatureTemperament.Passive, size: 1f, hover: 4f, wings: true);

            const int cx = -200, cz = -140;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            Fill(server, "tree_leaves", cx - 6, cx + 6, padY, padY, cz - 6, cz + 6);
            p.State.Position = new Vector3f(cx + 20, padY + 1, cz);
            string id = Spawn(server, new Vector3f(cx + 0.5f, padY + 5, cz + 0.5f));
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 10f);

            var c = server.Creatures.First(x => x.Id == id);
            for (int i = 0; i < 60 && c.Vert.Flight != FlightPhase.Perched; i++)
            {
                server.TickForTest(0.1);
                c = server.Creatures.First(x => x.Id == id);
            }

            Assert.Equal(FlightPhase.Perched, c.Vert.Flight);
            Assert.Equal(padY + 1, c.Position.Y, 2); // sitting ON the crown (#1332)
        }
    }
}
