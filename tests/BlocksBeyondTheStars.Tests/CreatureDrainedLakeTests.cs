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
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3476 — "a creature wanders in my concrete floor". A player drained the lake his base stands on and laid a concrete
/// floor on its mud bed (bed 65, concrete 66, walking on 67). A legless, finned animal was drawn sunk into that floor, only
/// its back and fin showing. Its height came from the GENERATOR's water — the lake the world was born with, which knows
/// nothing about the draining: a fish porpoised in the old lake's band (the bed + 1 is the concrete), and an amphibian set
/// down by the taming scare bolt or as a companion stood on the old water's top cell. Real blocks decide now; the generator
/// answers only where its water still stands or nothing is streamed in. Every lake here is a real generated one, found
/// from the generator alone, so the tests follow any change to terrain generation instead of pinning a spot.
/// </summary>
public sealed class CreatureDrainedLakeTests : IDisposable
{
    private const int BasinR = 4;   // the drained basin's half size; its outer ring is the base's wall
    private const int MinDepth = 3; // deep enough that the old water band reaches past the one-layer floor
    private const int MaxDepth = 5; // shallow enough that a probe from the old waterline still sees the floor

    private readonly string _root;
    private readonly GameContent _content;

    public CreatureDrainedLakeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_drained_" + Guid.NewGuid().ToString("N"));
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
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "drained"));
        var config = new ServerConfig
        {
            WorldName = "drained",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Forces roster slot <paramref name="slot"/> into a plain, always-awake, solitary animal of the habitat —
    /// legless and finned like the reported one.</summary>
    private static CreatureSpecies Force(SvGameServer server, CreatureHabitat habitat, int slot = 0)
    {
        var sp = server.SpeciesRoster[slot];
        sp.Habitat = habitat;
        sp.Legs = 0;
        sp.HasFins = true;
        sp.LocoStyle = habitat == CreatureHabitat.Water ? LocomotionStyle.Schooler : LocomotionStyle.Slitherer;
        sp.Temperament = CreatureTemperament.Passive;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HoverAltitude = 0f;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        return sp;
    }

    private sealed record Lake(int X, int Z, int Top, int Bed);

    /// <summary>A generated lake column <see cref="MinDepth"/>..<see cref="MaxDepth"/> cells deep whose 3×3 neighbourhood is
    /// the same water body, searched on a coarse grid from the generator alone. With <paramref name="unloaded"/> its chunk
    /// must not be streamed in.</summary>
    private static Lake FindLake(SvGameServer server, bool unloaded = false)
    {
        for (int x = -1536; x <= 1536; x += 24)
        {
            for (int z = -1536; z <= 1536; z += 24)
            {
                if (server.WaterSurfaceForTest(x, z) is not { } w || w.Top - w.Bed < MinDepth || w.Top - w.Bed > MaxDepth
                    || !SameBodyAround(server, x, z, w.Top))
                {
                    continue;
                }

                if (unloaded && server.World.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, w.Top, z))))
                {
                    continue;
                }

                return new Lake(x, z, w.Top, w.Bed);
            }
        }

        throw new InvalidOperationException("no generated lake of the wanted depth on this world");
    }

    private static bool SameBodyAround(SvGameServer server, int x, int z, int top)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (server.WaterSurfaceForTest(x + dx, z + dz) is not { } n || n.Top != top || n.Top - n.Bed < MinDepth)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>What the player did: drained the lake around <paramref name="lake"/> and laid a concrete floor on its bed —
    /// one layer on the bed (as reported), or with <paramref name="filled"/> up to the old waterline. Everything above the
    /// floor is air up to well past the old water; the basin's outer ring is a concrete wall that high, so the lake outside
    /// stays out. Returns the floor's walking Y.</summary>
    private int DrainAndFloor(SvGameServer server, Lake lake, bool filled)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var concrete = _content.GetBlock("concrete")!.NumericId;
        int floorTop = filled ? lake.Top : lake.Bed + 1;
        int lowest = int.MaxValue, highest = int.MinValue;
        for (int dx = -BasinR; dx <= BasinR; dx++)
        {
            for (int dz = -BasinR; dz <= BasinR; dz++)
            {
                var w = server.WaterSurfaceForTest(lake.X + dx, lake.Z + dz);
                int ground = server.SurfaceHeightForTest(lake.X + dx, lake.Z + dz);
                lowest = Math.Min(lowest, w?.Bed ?? ground);
                highest = Math.Max(highest, Math.Max(ground, w?.Top ?? ground));
            }
        }

        for (int dx = -BasinR; dx <= BasinR; dx++)
        {
            for (int dz = -BasinR; dz <= BasinR; dz++)
            {
                bool wall = Math.Abs(dx) == BasinR || Math.Abs(dz) == BasinR;
                for (int y = Math.Min(lowest, lake.Bed); y <= highest + 12; y++)
                {
                    BlockId id = y <= lake.Bed ? stone : y <= floorTop || wall ? concrete : BlockId.Air;
                    server.World.SetBlock(new Vector3i(lake.X + dx, y, lake.Z + dz), id);
                }
            }
        }

        return floorTop + 1;
    }

    private static void Watcher(SvGameServer server, Vector3f at)
    {
        // Creatures only move while a player is on the ground near them.
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
    }

    // ---------------- the drained lake: real blocks decide ----------------

    /// <summary>The one-off placement (a companion's spot, the taming scare bolt): whether the probe starts on the floor,
    /// where the old water stood or inside the concrete, a fish and an amphibian alike are set down ON the floor. The old
    /// rule put the amphibian on the old water's top cell — inside a floor filled to the waterline, a cell above a thin one
    /// — and the fish into the old water's band, through the concrete laid on the bed.</summary>
    [Theory]
    [InlineData(CreatureHabitat.Water, false)]
    [InlineData(CreatureHabitat.Water, true)]
    [InlineData(CreatureHabitat.Amphibian, false)]
    [InlineData(CreatureHabitat.Amphibian, true)]
    public void AWaterAnimal_SetDownInADrainedLake_StandsOnTheFloor(CreatureHabitat habitat, bool filled)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Force(server, habitat);
            var lake = FindLake(server);
            int walk = DrainAndFloor(server, lake, filled);
            Assert.NotNull(server.WaterSurfaceForTest(lake.X, lake.Z)); // the generator still believes in its lake

            foreach (float fromY in new[] { walk, lake.Top + 0.5f, lake.Bed + 1.5f })
            {
                var at = server.HabitatHeightForTest(sp.Id, new Vector3f(lake.X + 0.5f, fromY, lake.Z + 0.5f));
                Assert.True(Math.Abs(at.Y - walk) < 1e-3,
                    $"a {habitat} animal placed from y={fromY} over the drained lake (bed {lake.Bed}, old water top {lake.Top}) " +
                    $"must stand on the floor at {walk}, not at {at.Y}");
            }
        }
    }

    /// <summary>The report itself, on the tick: a fish left behind in the drained lake — one holding still, one roaming the
    /// base — lies on the floor every tick, never inside it. It used to porpoise through the old lake's band, bed + 1 to the
    /// old top, with the concrete in the lower part of it.</summary>
    [Fact]
    public void AStrandedFish_InADrainedLake_LiesOnTheFloor_NeverInIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Water);
            var lake = FindLake(server);
            int walk = DrainAndFloor(server, lake, filled: false);
            Watcher(server, new Vector3f(lake.X + BasinR - 1.5f, walk, lake.Z + BasinR - 1.5f));

            string still = server.SpawnCreatureAtForTest(new Vector3f(lake.X + 0.5f, walk, lake.Z + 0.5f));
            server.PauseCreatureForTest(still, 60f);
            string roamer = server.SpawnCreatureAtForTest(new Vector3f(lake.X - 1.5f, walk, lake.Z + 1.5f));

            for (int i = 0; i < 80; i++)
            {
                server.TickForTest(0.1);
                foreach (string id in new[] { still, roamer })
                {
                    var c = Assert.Single(server.Creatures, x => x.Id == id);
                    var cell = new Vector3i((int)MathF.Floor(c.Position.X), (int)MathF.Floor(c.Position.Y), (int)MathF.Floor(c.Position.Z));
                    Assert.True(c.Position.Y >= walk - 1e-3 && server.World.GetBlock(cell).IsAir,
                        $"tick {i}: the fish at {c.Position} is inside the floor (walking Y {walk}, old lake {lake.Bed}..{lake.Top})");
                }
            }

            Assert.Equal(walk, Assert.Single(server.Creatures, x => x.Id == still).Position.Y, 3);
        }
    }

    // ---------------- positive controls: a real lake and an unloaded one are as before ----------------

    /// <summary>A lake the player left alone: an amphibian is still set down at its surface cell (the generator's top, as
    /// the spawner seats it), a fish in its water, and a fish ticking there stays in the water.</summary>
    [Fact]
    public void InAGenuineLake_AnAmphibianIsSetDownAtTheSurface_AndAFishSwimsInTheWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var fish = Force(server, CreatureHabitat.Water, slot: 0);
            var frog = Force(server, CreatureHabitat.Amphibian, slot: 1);
            var lake = FindLake(server);
            var water = _content.GetBlock("water")!.NumericId;
            Assert.Contains(Enumerable.Range(lake.Bed + 1, lake.Top - lake.Bed), // streamed in: the lake is real water here
                y => server.World.GetBlock(new Vector3i(lake.X, y, lake.Z)).Value == water.Value);

            var at = server.HabitatHeightForTest(frog.Id, new Vector3f(lake.X + 0.5f, lake.Top + 3f, lake.Z + 0.5f));
            Assert.Equal(lake.Top, at.Y, 3);

            var swim = server.HabitatHeightForTest(fish.Id, new Vector3f(lake.X + 0.5f, lake.Top + 3f, lake.Z + 0.5f));
            Assert.InRange(swim.Y, lake.Bed + 1f, lake.Top - 0.5f);

            Watcher(server, new Vector3f(lake.X + 3.5f, lake.Top + 1, lake.Z + 0.5f));
            string id = server.SpawnCreatureAtForTest(new Vector3f(lake.X + 0.5f, swim.Y, lake.Z + 0.5f), fish.Id);
            server.PauseCreatureForTest(id, 60f);
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.1);
                var c = Assert.Single(server.Creatures, x => x.Id == id);
                Assert.InRange(c.Position.Y, lake.Bed + 1f, lake.Top - 0.5f);
            }
        }
    }

    /// <summary>A lake whose chunk is not streamed in has no real blocks to read: the generator answers exactly as it
    /// always did, and asking loads nothing (#1719).</summary>
    [Fact]
    public void OverAnUnloadedLake_TheGeneratorStillAnswers_AndNothingIsLoaded()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var fish = Force(server, CreatureHabitat.Water, slot: 0);
            var frog = Force(server, CreatureHabitat.Amphibian, slot: 1);
            var lake = FindLake(server, unloaded: true);
            int loaded = server.World.LoadedChunkCount;

            var at = server.HabitatHeightForTest(frog.Id, new Vector3f(lake.X + 0.5f, lake.Top + 3f, lake.Z + 0.5f));
            Assert.Equal(lake.Top, at.Y, 3);
            var swim = server.HabitatHeightForTest(fish.Id, new Vector3f(lake.X + 0.5f, lake.Top + 3f, lake.Z + 0.5f));
            Assert.InRange(swim.Y, lake.Bed + 1f, lake.Top - 0.5f);

            Assert.Equal(loaded, server.World.LoadedChunkCount);
        }
    }
}
