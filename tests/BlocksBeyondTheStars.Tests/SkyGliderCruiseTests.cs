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
/// Player report 2026-11-27 (Lyxette, "a fish climbs the mountain"): an air fish glides ten blocks over the ground and traced
/// every slope at its full ease, so seen from a plateau above it crawled up the hillside. A sky glider now reads the ground
/// ahead of it and climbs early, and when the ground falls away it holds its height and sinks only slowly. Real server, a
/// stone pad floating above all natural terrain, one creature forced into an air fish.
/// </summary>
public sealed class SkyGliderCruiseTests : IDisposable
{
    private const float Hover = 4f;
    private readonly string _root;
    private readonly GameContent _content;

    public SkyGliderCruiseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_skyglide_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void AirFish_HeadingForANaturalHill_ClimbsBeforeItGetsThere_ButNotWhenItFliesAway()
    {
        var server = Started(out var repo, "tablelands"); // cliffs and plateaus: natural rises are easy to find
        using (repo)
        {
            var (x, z, here, ahead) = FindNaturalRise(server);
            Near(server, new Vector3f(x - 20.5f, here + 1, z + 0.5f));
            ForceAirFish(server);
            // Clear the column over the spot (a tree crown there would be a roof the fish must not lift through) — this
            // changes real blocks only; the look-ahead reads the generator's surface, which stays as it was born.
            Fill(server, "air", x - 1, x + 1, here + 1, here + 40, z - 1, z + 1);
            var at = new Vector3f(x + 0.5f, here + 1 + Hover, z + 0.5f);
            string id = Spawn(server, at);

            float away = server.SkyGliderCruiseBaseForTest(id, new Vector3f(at.X - 0.5f, at.Y, at.Z), 0.1);
            Assert.Equal(here + 1 + Hover, away, 3); // flying away from the hill: its base stays over its own ground

            float toward = server.SkyGliderCruiseBaseForTest(id, new Vector3f(at.X + 0.5f, at.Y, at.Z), 0.1);
            Assert.Equal(ahead + 1 + Hover, toward, 3); // the hill six blocks ahead lifts the base before it gets there
        }
    }

    [Fact]
    public void SkyGlider_NeverReadsABuiltWallAhead_AsAHill()
    {
        // The look-ahead is the generator's surface: a player's wall stays a wall (a hunting sky ray must not lift itself
        // over a base wall, #2943), only the ground under the glider decides there, as before.
        var server = Started(out var repo);
        using (repo)
        {
            ForceAirFish(server);
            const int cx = 140, cz = -140;
            int padY = Pad(server, cx, cz, 14);
            Near(server, new Vector3f(cx - 20.5f, padY + 1, cz + 0.5f));
            Fill(server, "stone", cx + 4, cx + 12, padY + 1, padY + 6, cz - 3, cz + 3); // a built "hill" ahead in +X
            var at = new Vector3f(cx + 0.5f, padY + 1 + Hover, cz + 0.5f);
            string id = Spawn(server, at);

            float toward = server.SkyGliderCruiseBaseForTest(id, new Vector3f(at.X + 0.5f, at.Y, at.Z), 0.1);
            Assert.Equal(padY + 1 + Hover, toward, 3);
        }
    }

    [Fact]
    public void AirFish_WhenTheGroundFallsAway_HoldsItsHeight_AndGlidesDownSlowly()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceAirFish(server);
            float amp = server.LocomotionProfileForTest(sp.Id).VertAmp;
            const int cx = -140, cz = -140;
            int padY = Pad(server, cx, cz, 10);
            Near(server, new Vector3f(cx - 20.5f, padY + 1, cz + 0.5f)); // a player nearby keeps the animal alive
            Fill(server, "stone", cx - 1, cx + 1, padY + 1, padY + 6, cz - 1, cz + 1); // a tower under the fish
            string id = Spawn(server, new Vector3f(cx + 0.5f, padY + 7 + Hover, cz + 0.5f));
            server.TickForTest(0.1);
            server.PauseCreatureForTest(id, 60f);
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
            }

            float before = Y(server, id);
            Assert.True(Math.Abs(before - (padY + 7 + Hover)) <= amp + 0.1f, $"settled over the tower: Y {before:F2}");

            Fill(server, "air", cx - 1, cx + 1, padY + 1, padY + 6, cz - 1, cz + 1); // the ground drops six blocks
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.1);
            }

            // One second later: the held base came down GlideSinkRate (1 block); the wave moves it at most 2 × amp. Tracing
            // the ground at the full 3.5 blocks/s ease it would have dropped ~3.5.
            float drop = before - Y(server, id);
            Assert.True(drop <= VerticalMotion.GlideSinkRate + (2f * amp) + 0.2f,
                $"the fish must glide down slowly, not trace the drop (fell {drop:F2} in 1 s, wave amplitude {amp:F2})");

            for (int i = 0; i < 100; i++)
            {
                server.TickForTest(0.1);
            }

            float after = Y(server, id);
            Assert.True(Math.Abs(after - (padY + 1 + Hover)) <= amp + 0.1f, $"it comes down onto the lower ground in the end: Y {after:F2}");
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string planet = "jungle")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "skyglide"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "skyglide",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at <paramref name="at"/> — near enough that the test animal is not despawned.</summary>
    private static void Near(SvGameServer server, Vector3f at)
    {
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
    }

    /// <summary>A dry spot (its neighbours x−1, x+1 no higher) whose NATURAL ground rises 4..20 blocks
    /// at the toward probe (column x + 7) and not at all at the away probe (column x − 6): (x, z, ground here, ground ahead),
    /// from the generator's surface. The server looks 6 blocks ahead of the step's END: from x + 0.5, a half-block step in
    /// +X ends at x + 1.0 and probes x + 7.0; one in −X ends at x + 0.0 and probes x − 6.0.</summary>
    private static (int X, int Z, int Here, int Ahead) FindNaturalRise(SvGameServer server)
    {
        for (int z = -600; z <= 600; z += 5)
        {
            for (int x = -600; x <= 600; x += 2)
            {
                int here = server.SurfaceHeightForTest(x, z);
                int ahead = server.SurfaceHeightForTest(x + 7, z);
                if (ahead < here + 4 || ahead > here + 20
                    || server.SurfaceHeightForTest(x - 6, z) > here
                    || server.SurfaceHeightForTest(x - 1, z) > here || server.SurfaceHeightForTest(x + 1, z) > here
                    || server.WaterSurfaceForTest(x, z) is not null || server.WaterSurfaceForTest(x + 1, z) is not null
                    || server.WaterSurfaceForTest(x - 6, z) is not null)
                {
                    continue;
                }

                // The real ground at the spot must be what the generator says (no river bed, path or stamp carved in).
                if (!server.World.GetBlock(new Vector3i(x, here, z)).IsAir && !server.World.GetBlock(new Vector3i(x + 1, here, z)).IsAir)
                {
                    return (x, z, here, ahead);
                }
            }
        }

        throw new InvalidOperationException("no natural rise found on the test planet");
    }

    /// <summary>Roster slot 0 as a calm, always-awake, solitary air fish (Air, legless, Standard plan) with a small hover
    /// and the gentle Drifter wave, so the held base — not the wave — decides what the test sees.</summary>
    private static CreatureSpecies ForceAirFish(SvGameServer server)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Air;
        sp.Legs = 0;
        sp.LocoStyle = LocomotionStyle.Drifter;
        sp.Temperament = CreatureTemperament.Passive;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.BodySegments = 1;
        sp.NeckLength = 0;
        sp.HoverAltitude = Hover;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        Assert.True(CreatureMotion.IsAirFish(sp) && CreatureMotion.IsSkyGlider(sp));
        return sp;
    }

    private static string Spawn(SvGameServer server, Vector3f at)
    {
        string id = server.SpawnCreatureAtForTest(at);
        server.Creatures.First(x => x.Id == id).SizeScale = 1f;
        return id;
    }

    private static float Y(SvGameServer server, string id) => server.Creatures.First(x => x.Id == id).Position.Y;

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

    /// <summary>Fills the box [x0..x1]×[y0..y1]×[z0..z1] with <paramref name="block"/> ("air" clears it).</summary>
    private void Fill(SvGameServer server, string block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var id = block == "air" ? BlockId.Air : _content.GetBlock(block)!.NumericId;
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
}
