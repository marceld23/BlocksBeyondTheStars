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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 17, Arena Nigra (#2073) — the server half: the authored Ignivermis takes the world's worm slots and the spawner
/// never rolls it (#2075), the black sand carries a step like the classic sand (#2074), the animals walking the sea shake it
/// and a breach swallows them, leaving meat and sparing a pet (#2076), and the Crystal Net's horn and siren shake the sand
/// while a device on rock stays silent (#2077). An older sand-sea world keeps its rolled worm and its manners.
/// </summary>
public sealed class ArenaNigraServerTests : IDisposable
{
    private const string Key = "arena_nigra";
    private const int Gen = WorldDescription.ArenaNigraGeneration;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_arena_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, long seed = 2026, int generation = Gen)
    {
        string world = "arena_" + planet + "_" + (++_worlds); // every server its own save — a second one would reload the first
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = generation },
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        int y = 200;
        while (y > 1 && server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
        {
            y--;
        }

        return y;
    }

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    private static float Attention(SvGameServer server, string worm) => server.GiantsForTest().Single(g => g.Id == worm).Attention;

    private BlocksBeyondTheStars.GameServer.PlayerSession OnFoot(SvGameServer server, int x, int z)
    {
        var p = server.AddLocalPlayer("Theo");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(x + 0.5f, SurfaceY(server, x, z) + 1, z + 0.5f);
        return p;
    }

    private bool IsBlock(SvGameServer server, int x, int z, string key)
        => _content.BlockById(server.World.GetBlock(new Vector3i(x, SurfaceY(server, x, z), z)))?.Key == key;

    /// <summary>Sea ground all along the worm's way in: 30 blocks east of the spot and 30 beyond (where it may rear).</summary>
    private static bool SeaRun(SvGameServer server, int x, int z)
    {
        for (int dx = -30; dx <= 60; dx += 3)
        {
            if (!server.IsSandSeaAtForTest(x + dx, z))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A world of this type with the player on a sea column and the worm moved underground 30 blocks east.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, BlocksBeyondTheStars.GameServer.PlayerSession Player, string Worm)
        SeaScene(string planet, int generation)
    {
        var server = Started(planet, out var repo, generation: generation);
        string sand = GiantRules.SeaSandBlock(_content.GetPlanet(planet));
        (int X, int Z) spot = default;
        bool found = false;
        for (int r = 0; r < 1200 && !found; r += 17)
            for (int a = 0; a < 16 && !found; a++)
            {
                int x = (int)(Math.Cos(a * 0.3927) * r), z = (int)(Math.Sin(a * 0.3927) * r);
                if (SeaRun(server, x, z) && IsBlock(server, x, z, sand) && IsBlock(server, x + 4, z, sand) && IsBlock(server, x + 8, z, sand)
                    && IsBlock(server, x + 30, z, sand))
                {
                    spot = (x, z);
                    found = true;
                }
            }

        Assert.True(found, "no sand sea near the spawn");
        var p = OnFoot(server, spot.X, spot.Z);
        var (_, worm, count) = server.GiantSpeciesForTest();
        Assert.NotNull(worm);
        Assert.True(count >= 1);
        worm!.Temperament = CreatureTemperament.Aggressive;
        server.SummonGiantForTest("Theo", "sandworm");
        var w = Assert.Single(server.GiantsForTest(), g => g.Kind == CreatureBodyPlan.Sandworm);
        int wx = spot.X + 30, wz = spot.Z;
        server.SetGiantForTest(w.Id, new Vector3f(wx + 0.5f, SurfaceY(server, wx, wz) - 14f, wz + 0.5f), (float)Math.PI);
        return (server, repo, p, w.Id);
    }

    // ---------------- the authored giant (#2075) ----------------

    [Fact]
    public void TheIgnivermis_TakesTheWormSlots_AndTheSpawnerNeverRollsIt_WhileAnOlderSeaKeepsItsRolledWorm()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            var (_, worm, count) = server.GiantSpeciesForTest();
            Assert.NotNull(worm);
            Assert.Equal("au_ignivermis", worm!.Id);
            Assert.StartsWith("Ignivermis", worm.Name);
            Assert.Equal(3, count);
            Assert.True(worm.SwallowsCreatures);
            Assert.Equal(0xB01818, worm.ColorRgb);

            var p = OnFoot(server, 0, 0);
            for (int i = 0; i < 40; i++)
            {
                p.State.Position = new Vector3f(0.5f, p.State.Position.Y, 0.5f);
                server.TickForTest(0.5);
            }

            Assert.DoesNotContain(server.Creatures, c => !c.IsGiant && c.SpeciesId == "au_ignivermis"); // never an ordinary animal
            Assert.All(server.Creatures.Where(c => c.IsGiant), c => Assert.Equal("au_ignivermis", c.SpeciesId));
        }

        var older = Started("sand_sea", out var repo2, generation: WorldDescription.GiantsGeneration);
        using (repo2)
        {
            var (_, rolled, count) = older.GiantSpeciesForTest();
            Assert.Equal("gi_sandworm", rolled!.Id);
            Assert.False(rolled.SwallowsCreatures); // generation 9: the old manners
            Assert.InRange(count, 1, 2);
        }

        var fresh = Started("sand_sea", out var repo3);
        using (repo3)
        {
            Assert.True(fresh.GiantSpeciesForTest().Sandworm!.SwallowsCreatures); // generation 17: the rolled worm hunts too
        }
    }

    // ---------------- the black sand (#2074) ----------------

    [Fact]
    public void BlackSand_CarriesAStep_ToTheWorm_ButAStoneFloorDoesNot()
    {
        var (server, repo, p, worm) = SeaScene(Key, Gen);
        using (repo)
        {
            var at = p.State.Position;
            int x = (int)Math.Floor(at.X), z = (int)Math.Floor(at.Z), y = (int)Math.Floor(at.Y);
            Assert.Equal("black_sand", _content.BlockById(server.World.GetBlock(new Vector3i(x, y - 1, z)))?.Key);

            server.EmitVibrationForTest(at, VibrationSource.Step);
            float heard = Attention(server, worm);
            Assert.True(heard > 0f, "a step on the black sand was not heard");

            server.World.SetBlock(new Vector3i(x, y - 1, z), _content.GetBlock("stone")!.NumericId);
            server.EmitVibrationForTest(at, VibrationSource.Step);
            Assert.Equal(heard, Attention(server, worm));
        }
    }

    // ---------------- the hunting worm (#2076) ----------------

    [Fact]
    public void Animals_WalkingTheSea_ShakeIt_AndTheBreachSwallowsThem_LeavingMeat_ButNeverAPet()
    {
        var (server, repo, p, worm) = SeaScene(Key, Gen);
        using (repo)
        {
            var at = p.State.Position;
            var spot = new Vector3f(at.X + 4f, at.Y, at.Z);
            var land = server.SpeciesRoster.First(s => s.Habitat == CreatureHabitat.Land && !s.IsGiant);
            string preyId = server.SpawnCreatureAtForTest(spot, land.Id);
            Assert.Contains(server.Creatures, c => c.Id == preyId && !c.IsGiant);

            // The animal ambles east across the sand at two blocks a second while the player stands still: it is heard.
            float before = Attention(server, worm);
            for (int i = 0; i < 16; i++)
            {
                var prey = server.Creatures.First(c => c.Id == preyId);
                prey.Position = new Vector3f(spot.X + 0.5f * i, spot.Y, spot.Z);
                p.State.Position = at;
                server.TickForTest(0.25);
            }

            Assert.True(Attention(server, worm) > before, "the walking animal was not heard");

            // A pet at the same spot — held there — and enough shaking at the spot to bring the worm to strike it.
            p.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "tc_pet",
                HomeBodyId = server.World.LocationId,
                Name = "Krümel",
                SpeciesId = land.Id,
                Species = land,
                SizeScale = 1f,
            });
            server.ReconcileCompanionsForTest(); // the pet entity spawns for its present owner
            var pet = server.Creatures.First(c => c.IsCompanion && c.CompanionId == "tc_pet");
            string petId = pet.Id;
            for (int i = 0; i < 6; i++)
            {
                server.EmitVibrationForTest(spot, VibrationSource.Mining);
            }

            bool eaten = false;
            for (int i = 0; i < 320 && !eaten; i++)
            {
                p.State.Position = new Vector3f(at.X - 25f, at.Y, at.Z);
                foreach (var c in server.Creatures.Where(c => c.Id == preyId || c.Id == petId))
                {
                    c.Position = spot; // parked on the shaking spot
                }

                server.TickForTest(0.25);
                eaten = server.Creatures.All(c => c.Id != preyId);
            }

            Assert.True(eaten, "the worm never swallowed the animal on the spot it struck");
            Assert.Contains(server.Creatures, c => c.Id == petId); // the pet at the same spot survived
            Assert.Contains(server.DropPackets, pk => pk.Items.Any(s => s.Item == "creature_meat")); // and a piece of meat lies on the sand
        }
    }

    // ---------------- the sound devices (#2077) ----------------

    [Fact]
    public void AHorn_OnTheSea_ShakesIt_ASiren_KeepsPulsing_AndADeviceOnRockIsSilent()
    {
        var (server, repo, p, worm) = SeaScene(Key, Gen);
        using (repo)
        {
            var at = p.State.Position;
            int z = (int)Math.Floor(at.Z), x = -1, top = -1;
            // Three sea cells in a row at one height, a few blocks east of the player: switch, conduit, horn.
            for (int dx = 2; dx < 12 && x < 0; dx++)
            {
                int cx = (int)Math.Floor(at.X) + dx;
                int t = SurfaceY(server, cx, z);
                if (t == SurfaceY(server, cx + 1, z) && t == SurfaceY(server, cx + 2, z)
                    && IsBlock(server, cx, z, "black_sand") && IsBlock(server, cx + 1, z, "black_sand") && IsBlock(server, cx + 2, z, "black_sand"))
                {
                    x = cx;
                    top = t;
                }
            }

            Assert.True(x >= 0, "no level row of sea cells near the player");
            Place(server, p, x, top + 1, z, "crystal_switch");
            Place(server, p, x + 1, top + 1, z, "crystal_conduit");
            Place(server, p, x + 2, top + 1, z, "horn");
            Ticks(server, 0.7);

            float quiet = Attention(server, worm);
            server.SetCrystalDeviceForTest(p, new Vector3i(x, top + 1, z), action: 0); // flip the lever: the horn sounds once
            Ticks(server, 0.7);
            float horn = Attention(server, worm);
            Assert.True(horn > quiet, "the horn on the sand was not heard");

            // The same horn on a stone block: silent to the worm.
            server.SetCrystalDeviceForTest(p, new Vector3i(x, top + 1, z), action: 0); // off
            Ticks(server, 0.7);
            server.World.SetBlock(new Vector3i(x + 2, top, z), _content.GetBlock("stone")!.NumericId);
            float before = Attention(server, worm);
            server.SetCrystalDeviceForTest(p, new Vector3i(x, top + 1, z), action: 0); // on again
            server.TickForTest(0.1);
            Assert.True(Attention(server, worm) <= before, "a horn on rock was heard");
            server.SetCrystalDeviceForTest(p, new Vector3i(x, top + 1, z), action: 0); // off
            Ticks(server, 0.7);

            // A siren beside the conduit, on the sand: it keeps pulsing while the net is on.
            Place(server, p, x + 1, top + 1, z + 1, "alarm_siren");
            Ticks(server, 0.7);
            float start = Attention(server, worm);
            server.SetCrystalDeviceForTest(p, new Vector3i(x, top + 1, z), action: 0); // on
            int rises = 0;
            float last = start;
            for (int i = 0; i < 46; i++)
            {
                server.TickForTest(0.1);
                float now = Attention(server, worm);
                if (now > last + 0.5f)
                {
                    rises++;
                }

                last = now;
            }

            Assert.True(rises >= 2, $"the siren pulsed {rises} time(s) in 4.6 s — expected a pulse every two seconds");
        }
    }

    private static void Place(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, int x, int y, int z, string key)
    {
        p.State.Inventory.SetSlot(0, new ItemStack(key, 1));
        p.State.SelectedHotbarSlot = 0;
        server.PlaceBlock("Theo", x, y, z, key);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
