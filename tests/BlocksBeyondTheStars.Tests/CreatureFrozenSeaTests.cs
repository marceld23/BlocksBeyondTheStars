// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
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
/// #2955: air jellyfish over a deep, frozen sea. A tundra world's open ocean is a three-block ice sheet on ~50 cells of
/// water; the band probes followed a water body only 32 cells up and never looked through the ice, so from under the
/// sheet the middle of the ocean read as its surface and from above it the seabed did — two answers more than 24 blocks
/// apart, which the hover ease snaps to: the jellyfish teleported between the ice and the deep water every tick, and the
/// players saw them blink. The sea here is built of real blocks (40 cells of water, deeper than every scan window), once
/// frozen over and once open.
/// </summary>
public sealed class CreatureFrozenSeaTests : IDisposable
{
    private const float Hover = 10f;
    private const int ArenaR = 12; // the basin's half size; the sea fills it inside a one-cell stone rim
    private const int WaterDepth = 40;
    private const int IceSheet = 3;

    private readonly string _root;
    private readonly GameContent _content;

    public CreatureFrozenSeaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_frozensea_" + Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
            // A leftover temp dir is not worth failing a test run over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "frozensea"));
        var config = new ServerConfig
        {
            WorldName = "frozensea",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Roster slot 0 as the reported species: an air medusa drifting on a gas sac, always awake, alone.</summary>
    private static CreatureSpecies ForceAirJellyfish(SvGameServer server)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Air;
        sp.BodyPlan = CreatureBodyPlan.Medusa;
        sp.LocoStyle = LocomotionStyle.Drifter;
        sp.Legs = 0;
        sp.HasWings = false;
        sp.HasGasSac = true;
        sp.Size = 1.3f;
        sp.HoverAltitude = Hover;
        sp.Temperament = CreatureTemperament.Passive;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(sp));
        return sp;
    }

    /// <summary>The built sea: <see cref="Floor"/> is the top of its stone bed, <see cref="Rest"/> the cell a body rests
    /// on over it — the first air above the ice, or above the water when it is open.</summary>
    private sealed record Sea(int Cx, int Cz, int Floor, int Rest)
    {
        public bool Inside(Vector3f p)
            => Math.Abs(Math.Floor(p.X) - Cx) < ArenaR - 1 && Math.Abs(Math.Floor(p.Z) - Cz) < ArenaR - 1;
    }

    /// <summary>A stone basin high above the terrain holding <see cref="WaterDepth"/> cells of water — with
    /// <paramref name="frozen"/> its top <see cref="IceSheet"/> cells are ice — and a rim wall tall enough to keep a
    /// band inside.</summary>
    private Sea BuildSea(SvGameServer server, int cx, int cz, bool frozen)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        var ice = _content.GetBlock("ice")!.NumericId;

        int b = MaxTopY(server, cx, cz, ArenaR + 1) + 8;
        int top = b + WaterDepth; // the topmost water (or ice) cell
        for (int dx = -ArenaR; dx <= ArenaR; dx++)
        {
            for (int dz = -ArenaR; dz <= ArenaR; dz++)
            {
                bool rim = Math.Abs(dx) == ArenaR || Math.Abs(dz) == ArenaR;
                for (int y = b; y <= top + (rim ? 20 : 0); y++)
                {
                    BlockId id = rim || y == b ? stone : frozen && y > top - IceSheet ? ice : water;
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), id);
                }
            }
        }

        return new Sea(cx, cz, b, top + 1);
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

    private static void Watcher(SvGameServer server, Sea sea)
    {
        // Creatures only move while a player is on the ground near them: stand one on the rim.
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(sea.Cx - ArenaR + 0.5f, sea.Rest + 20, sea.Cz + 0.5f);
    }

    /// <summary>The probe, from every depth: whatever the reference cell — on the bed, deep in the water past the wide
    /// scan, right under the sheet, inside it or in the air above — the band rests on the sea's surface: the ice, or
    /// the open water.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestSurface_OverADeepSea_IsItsSurface_FromEveryDepth(bool frozen)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sea = BuildSea(server, 3600, 3600, frozen);
            foreach (int refY in new[] { sea.Floor + 1, sea.Floor + 5, sea.Floor + 30, sea.Rest - 2, sea.Rest, sea.Rest + 3 })
            {
                Assert.Equal(sea.Rest, server.RestSurfaceYForTest(sea.Cx + 2, sea.Cz - 3, refY));
            }
        }
    }

    /// <summary>Only ice (and snow on it) is a lid: a stone floor laid on the sheet — a house over a frozen pond — is a roof,
    /// and the probe does not lift a reference from the water onto it; the roofed-column rules keep deciding there.</summary>
    [Fact]
    public void AFloorOnTheIce_IsNoLid_TheProbeDoesNotLiftOntoIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sea = BuildSea(server, 3700, 3700, frozen: true);
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dz = -3; dz <= 3; dz++)
                {
                    server.World.SetBlock(new Vector3i(sea.Cx + dx, sea.Rest, sea.Cz + dz), stone);
                }
            }

            Assert.Equal(sea.Rest, server.RestSurfaceYForTest(sea.Cx + 6, sea.Cz, sea.Floor + 30)); // beside the floor: the ice
            Assert.NotEqual(sea.Rest + 1, server.RestSurfaceYForTest(sea.Cx, sea.Cz, sea.Floor + 30)); // under it: not its top
        }
    }

    /// <summary>The reported jellyfish: spawned over the frozen sea at its band (not under the ice), and every jellyfish —
    /// spawned at the band, in the deep water or on the ice — settles above the ice and drifts there without a jump.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAirJellyfish_OverADeepSea_SpawnsAtItsBand_AndStaysThereWithoutJumping(bool frozen)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceAirJellyfish(server);
            var sea = BuildSea(server, 3800, 3800, frozen);
            Watcher(server, sea);

            // The spawner's surface over a sea is the generator's — here the top of the built bed.
            float spawnY = server.AirSpawnYForTest(sp.Id, sea.Cx, sea.Cz, sea.Floor);
            Assert.Equal(sea.Rest + Hover, spawnY, 3);

            var ids = new List<string>
            {
                server.SpawnCreatureAtForTest(new Vector3f(sea.Cx + 0.5f, spawnY, sea.Cz + 0.5f), sp.Id),
                server.SpawnCreatureAtForTest(new Vector3f(sea.Cx + 3.5f, sea.Floor + 20.5f, sea.Cz - 2.5f), sp.Id),
                server.SpawnCreatureAtForTest(new Vector3f(sea.Cx - 3.5f, sea.Rest + 1.3f, sea.Cz + 2.5f), sp.Id),
            };

            var last = new Dictionary<string, float>();
            int samples = 0;
            for (int i = 0; i < 600; i++)
            {
                server.TickForTest(0.05);
                foreach (var c in server.Creatures.Where(c => ids.Contains(c.Id)))
                {
                    bool inside = sea.Inside(c.Position);
                    if (i >= 120 && inside) // 6 s to come up out of the water and off the ice (a band eases at 2 blocks/s)
                    {
                        samples++;
                        Assert.True(c.Position.Y >= sea.Rest + Hover - 1f,
                            $"a jellyfish is below its band at tick {i}: Y {c.Position.Y - sea.Rest:F2} over the surface");
                        if (last.TryGetValue(c.Id, out float prev) && !float.IsNaN(prev))
                        {
                            Assert.True(Math.Abs(c.Position.Y - prev) < 0.5f,
                                $"a jellyfish jumped {c.Position.Y - prev:F2} in one tick at tick {i}");
                        }
                    }

                    last[c.Id] = inside ? c.Position.Y : float.NaN; // over the rim it rides the wall's band
                }
            }

            Assert.True(samples > 500, $"the jellyfish spent the run over the sea ({samples} samples)");
        }
    }
}
