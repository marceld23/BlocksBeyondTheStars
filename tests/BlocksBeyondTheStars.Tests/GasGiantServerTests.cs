// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gas giant on a live server (#2112): the ship lands on a metal deck hanging over the gas, the gas kills whoever
/// falls in (no armour helps, its own death line), a sky city on an island holds a pocket of air, and the sky giant is
/// hosted, appears by itself over the islands, drifts, calls and can be hit — while no other world hosts one. The world's
/// animals (#2583) live over the gas, never in it: their bands ride over its surface or over an island's rock, nothing
/// spawns where it burns, one that got in rises out, and nothing that walks steps off a deck into it.
/// </summary>
public sealed class GasGiantServerTests : IDisposable
{
    private const string Key = "gas_giant";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_gasgiant_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    /// <summary>A started server on <paramref name="planet"/>. <paramref name="quiet"/>: no natural fauna, only the animals
    /// a test places; <paramref name="machines"/> false: no planet machines either; <paramref name="configure"/> sets any
    /// other rule before the start.</summary>
    private SvGameServer Started(string planet, out SqliteWorldRepository repo, long seed = 2026,
        IGameLogger? log = null, bool quiet = false, bool machines = true,
        Action<ServerConfig>? configure = null)
    {
        string world = "gasgiant_" + planet + "_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = WorldDescription.GasGiantGeneration },
        };
        if (quiet)
        {
            config.Rules.CreatureAbundance = AlienActivity.Off;
        }

        if (!machines)
        {
            config.Rules.PlanetEnemies = AlienActivity.Off;
        }

        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo, log);
        server.Start();
        return server;
    }

    private static int TopSolidY(SvGameServer server, int x, int z, int from)
    {
        for (int y = from; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return int.MinValue;
    }

    [Fact]
    public void TheHomePad_IsAMetalDeck_ThreeBlocksOverTheGas_WithGasUnderIt()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            Assert.NotEqual(int.MinValue, sea);
            int top = TopSolidY(server, 0, 0, sea + 40);
            Assert.Equal(sea + 3, top); // the islet rise, over the gas instead of over water
            Assert.Equal("steel_floor", _content.BlockById(server.World.GetBlock(new Vector3i(0, top, 0)))?.Key);
            for (int dy = 1; dy <= 3; dy++)
            {
                Assert.Equal("metal_panel", _content.BlockById(server.World.GetBlock(new Vector3i(0, top - dy, 0)))?.Key);
            }

            Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(0, top - 4, 0)))?.Key); // a platform, not a mound
            // #2134: under the deck the gas reaches down to the dense gas, like everywhere on the sea — never rock.
            int denseTop = sea - BlocksBeyondTheStars.WorldGeneration.WorldGenerator.GasSeaDepth;
            Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(0, denseTop + 1, 0)))?.Key);
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, denseTop, 0)))?.Key);
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, sea - 40, 0)))?.Key);
        }
    }

    [Fact]
    public void TheGas_Kills_FasterThanLava_WithItsOwnDeathLine_AndNoArmourHelps()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            TestGear.Wear(p.State, "armor_chest"); // worn armour mitigates lava — not the gas
            p.State.Position = new Vector3f(0.5f, sea - 3, 0.5f); // fell off the deck into the gas
            float before = p.State.Health;
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            float lost = before - p.State.Health;
            Assert.True(lost >= 50f, $"only {lost} health lost in two seconds of gas"); // 30/s, unmitigated
        }
    }

    [Fact]
    public void TheDenseGas_UnderTheSea_BurnsFasterThanTheGas()
    {
        // #2134: whoever sinks through the gas into the dense gas under it is gone faster still — no armour helps.
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            int deep = sea - BlocksBeyondTheStars.WorldGeneration.WorldGenerator.GasSeaDepth - 6;
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, deep, 0)))?.Key);

            float LostInOneSecond(string name, float y)
            {
                var p = server.AddLocalPlayer(name);
                p.State.AboardShip = false;
                TestGear.Wear(p.State, "armor_chest");
                p.State.Position = new Vector3f(0.5f, y, 0.5f);
                float before = p.State.Health;
                server.TickForTest(0.5);
                server.TickForTest(0.5);
                float lost = before - p.State.Health;
                p.State.Position = new Vector3f(0.5f, sea + 10, 0.5f); // out again before the next probe ticks
                return lost;
            }

            float gas = LostInOneSecond("Floater", sea - 3);
            float dense = LostInOneSecond("Sinker", deep);
            Assert.True(gas >= 25f, $"only {gas} health lost in a second of gas");
            Assert.True(dense >= 40f, $"only {dense} health lost in a second of dense gas");
            Assert.True(dense > gas + 10f, $"the dense gas ({dense}/s) must burn faster than the gas ({gas}/s)");
        }
    }

    [Fact]
    public void ASkyCity_HoldsAPocketOfAir_TheDeckDoesNot()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            Assert.False(server.InSkyCityAirForTest(new Vector3f(0.5f, sea + 4, 0.5f)), "the landing deck is open to the storm");

            var cities = server.SettlementBoxesForTest.Where(s => !s.Ruined).ToList();
            var onIsland = server.SettlementsForTest.Where(s => !s.Ruined && s.OnIsland).ToList();
            Assert.Equal(server.SettlementsForTest.Count, server.SettlementsForTest.Count(s => s.OnIsland)); // nothing ever seats on the gas
            if (cities.Count > 0 && onIsland.Count > 0)
            {
                var c = cities[0];
                var inside = new Vector3f((c.Min.X + c.Max.X) * 0.5f, c.GroundY + 1.5f, (c.Min.Z + c.Max.Z) * 0.5f);
                Assert.True(server.InSkyCityAirForTest(inside), "a sky city breathes");
                Assert.False(server.InSkyCityAirForTest(new Vector3f(inside.X, c.GroundY + 40f, inside.Z)), "…but not the whole sky over it");
            }
        }
    }

    [Fact]
    public void TheSkyGiant_IsHosted_DriftsOverTheIslands_Calls_AndCanBeHit_AndNoOtherWorldHasOne()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            var sp = server.SkyGiantSpeciesForTest();
            Assert.NotNull(sp);
            Assert.Equal(CreatureBodyPlan.SkyGiant, sp!.BodyPlan);
            Assert.Null(server.GiantSpeciesForTest().Sandworm);
            Assert.Null(server.LeviathanSpeciesForTest().Leviathan);

            int sea = server.SeaLevelForTest();
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, sea + 4, 0.5f); // on the deck
            Assert.Empty(server.GiantsForTest());
            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
            }

            var g = Assert.Single(server.GiantsForTest(), x => x.Kind == CreatureBodyPlan.SkyGiant);
            Assert.Equal("drift", g.Phase);
            Assert.True(g.Position.Y >= sea + GiantRules.SkyGiantAltitudeMin - 4f, $"the sky giant hangs at {g.Position.Y} over a sea at {sea}");
            double dist = Math.Sqrt(WorldConstants.WrapDistanceSquared(g.Position, p.State.Position, server.World.Circumference));
            Assert.InRange(dist, 30.0, 340.0); // a lane 120–200 out, 70–130 across

            var first = g.Position;
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.25);
            }

            var later = server.GiantsForTest().Single(x => x.Id == g.Id);
            Assert.True(Math.Sqrt(WorldConstants.WrapDistanceSquared(first, later.Position, server.World.Circumference)) > 5.0, "it does not drift");
            var lane = server.SkyGiantLaneForTest(g.Id);
            Assert.NotNull(lane);
            Assert.True(lane!.Value.Trail > 1, "the body leaves no trail to be hit along");
            var hit = server.GiantHitForTest(g.Id, p.State.Position);
            Assert.True(hit.Hittable, "a sky giant is always in the open");
            Assert.True(hit.AimPoint.Y > sea + 10f, "the aim point lies on the body up in the sky");
        }

        var ocean = Started("ocean", out var repo2);
        using (repo2)
        {
            Assert.Null(ocean.SkyGiantSpeciesForTest());
        }
    }

    // ---- #2583: the animals and the gas sea ----

    /// <summary>Counts the burn deaths the server logs — what a burned animal leaves behind even when it dies in one pass.</summary>
    private sealed class BurnLog : IGameLogger
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public int Burned => _lines.Count(l => l.Contains("burned to death", StringComparison.Ordinal));

        public string BurnedLines => string.Join(" / ", _lines.Where(l => l.Contains("burned to death", StringComparison.Ordinal)));

        public void Info(string message) => _lines.Enqueue(message);

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }
    }

    /// <summary>A generator of the active world's own body (seed, mode), to ask where the islands float.</summary>
    private static BlocksBeyondTheStars.WorldGeneration.WorldGenerator GeneratorFor(SvGameServer server)
    {
        var w = server.World;
        var gen = server.FreshGeneratorForTest();
        gen.SetWorldMode(w.Circumference, w.Cratered, w.LandingPadFlats, w.LocationId, w.FrontierOreBoost);
        Assert.Equal(server.SeaLevelForTest(), gen.SeaLevel(w.Planet)); // the same body as the server's
        return gen;
    }

    private static float HoverOf(CreatureSpecies sp) => sp.HoverAltitude > 0f ? sp.HoverAltitude : 5f;

    /// <summary>The gas contact rule from the test's side: the feet cell or the cell under it is gas or dense gas.</summary>
    private bool InGas(SvGameServer server, Vector3f pos)
    {
        var feet = pos.ToBlock();
        string? at = _content.BlockById(server.World.GetBlock(feet))?.Key;
        string? under = _content.BlockById(server.World.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)))?.Key;
        return at is "gas" or "gas_dense" || under is "gas" or "gas_dense";
    }

    /// <summary>A column of open gas <paramref name="radius"/> blocks out from the home deck: no island band over it, gas at
    /// the sea level, air right above. The bearing turns from <paramref name="bearing"/> until one is found.</summary>
    private (int X, int Z) OpenGasColumn(SvGameServer server, BlocksBeyondTheStars.WorldGeneration.WorldGenerator gen,
        int sea, float radius, int bearing)
    {
        for (int k = 0; k < 64; k++)
        {
            double a = (bearing + k) * 0.7;
            int x = (int)Math.Floor(Math.Cos(a) * radius), z = (int)Math.Floor(Math.Sin(a) * radius);
            if (gen.TryGetHighestBand(server.World.Planet, x, z, out _)
                || _content.BlockById(server.World.GetBlock(new Vector3i(x, sea, z)))?.Key != "gas"
                || !server.World.GetBlock(new Vector3i(x, sea + 1, z)).IsAir)
            {
                continue;
            }

            return (x, z);
        }

        throw new InvalidOperationException($"no open gas {radius} blocks from the deck");
    }

    [Fact]
    public void EveryRosterSpecies_SpawnsOverOpenGas_AtItsBand_AndIsAliveAMinuteLater()
    {
        // #2583: the band was measured from the heightfield buried under the gas, five to nine cells under its surface; even
        // an animal planted at the right band sank to the buried one within seconds and burned.
        var log = new BurnLog();
        var server = Started(Key, out var repo, log: log, quiet: true, machines: false);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var gen = GeneratorFor(server);
            var (x, z) = OpenGasColumn(server, gen, sea, 24f, 0);
            int buried = server.SurfaceHeightForTest(x, z);
            Assert.True(buried < sea - 4, $"the generator surface ({buried}) the spawner hands in lies under the gas ({sea})");
            Assert.NotEmpty(server.SpeciesRoster);
            foreach (var sp in server.SpeciesRoster)
            {
                Assert.Equal(CreatureHabitat.Air, sp.Habitat);
                float y = server.AirSpawnYForTest(sp.Id, x, z, buried);
                Assert.True(y >= sea + 2, $"{sp.Name} spawns at {y}, the gas stands at {sea}: its feet would burn");
                Assert.Equal(sea + 1 + HoverOf(sp), y, 3); // its band over the gas's surface
            }

            // Nothing spawns where the gas burns: in it, or right on it.
            Assert.False(server.SpawnSpotClearForTest(new Vector3f(x + 0.5f, sea - 2, z + 0.5f)), "a spawn spot in the gas");
            Assert.False(server.SpawnSpotClearForTest(new Vector3f(x + 0.5f, sea + 1, z + 0.5f)), "a spawn spot right on the gas");
            Assert.True(server.SpawnSpotClearForTest(new Vector3f(x + 0.5f, sea + 2, z + 0.5f)), "the first cell clear of the gas");

            // One of every species at its band over open gas, a minute of ticking with a player on the deck.
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            var deck = new Vector3f(0.5f, sea + 4, 0.5f);
            var planted = new List<string>();
            int bearing = 0;
            foreach (var sp in server.SpeciesRoster)
            {
                var (px, pz) = OpenGasColumn(server, gen, sea, 20f, bearing);
                bearing += 2;
                float band = server.AirSpawnYForTest(sp.Id, px, pz, server.SurfaceHeightForTest(px, pz));
                planted.Add(server.SpawnCreatureAtForTest(new Vector3f(px + 0.5f, band, pz + 0.5f), sp.Id));
            }

            var last = new Dictionary<string, Vector3f>();
            for (int i = 0; i < 600; i++)
            {
                p.State.Position = deck;
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
                foreach (var c in server.Creatures.Where(c => planted.Contains(c.Id)))
                {
                    last[c.Id] = c.Position;
                    Assert.True(c.Position.Y >= sea + 2, $"{c.SpeciesId} sank to Y {c.Position.Y}, the gas stands at {sea} (tick {i})");
                    Assert.True(c.Hull >= c.HullMax, $"{c.SpeciesId} lost {c.HullMax - c.Hull} health (tick {i})");
                }
            }

            Assert.True(log.Burned == 0, log.BurnedLines);
            foreach (string id in planted)
            {
                // Alive — or it roamed out of everybody's range and was let go (the far leash is 70 blocks at the least).
                double dist = Math.Sqrt(WorldConstants.WrapDistanceSquared(last[id], deck, server.World.Circumference));
                Assert.True(server.Creatures.Any(c => c.Id == id) || dist > 60.0, $"{id} is gone, last seen {dist:0} blocks from the deck");
            }
        }
    }

    [Fact]
    public void NaturalSpawning_OverTheGas_NeverBurnsAnAnimalOrAMachine_AndEveryOneStaysClearOfTheGas()
    {
        // #2583: 90 s with a player on a deck spawned 102 animals and burned 91 of them; the spawner never stopped. #2586: the
        // planet machines did the same — 4 burned in a minute, all on one spot. Default rules: animals and machines alike.
        var log = new BurnLog();
        var server = Started(Key, out var repo, log: log);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            var deck = new Vector3f(0.5f, sea + 4, 0.5f);
            var seen = new HashSet<string>();
            var machines = new Dictionary<string, Vector3f>(); // where each machine was first seen
            float machineTravel = 0f;
            for (int i = 0; i < 600; i++)
            {
                p.State.Position = deck; // the toxic storm (and a drone's fire) must not send the watcher away mid-minute
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
                foreach (var c in server.Creatures.Where(c => !c.IsGiant))
                {
                    seen.Add(c.Id);
                    Assert.True(c.Position.Y >= sea + 2, $"{c.SpeciesId} at Y {c.Position.Y}, the gas stands at {sea} (tick {i})");
                    Assert.False(InGas(server, c.Position), $"{c.SpeciesId} touches the gas at Y {c.Position.Y} (tick {i})");
                    Assert.True(c.Hull >= c.HullMax, $"{c.SpeciesId} lost {c.HullMax - c.Hull} health (tick {i})");
                }

                foreach (var m in server.PlanetEnemies)
                {
                    if (!machines.TryGetValue(m.Id, out var first))
                    {
                        machines[m.Id] = first = m.Position;
                    }

                    machineTravel = Math.Max(machineTravel, (float)Math.Sqrt(WorldConstants.WrapDistanceSquared(
                        new Vector3f(first.X, 0f, first.Z), new Vector3f(m.Position.X, 0f, m.Position.Z), server.World.Circumference)));
                    Assert.False(InGas(server, m.Position), $"a {m.Kind} touches the gas at {m.Position.X:0},{m.Position.Y:0.0},{m.Position.Z:0} (tick {i})");
                    Assert.True(m.Hull >= m.HullMax, $"a {m.Kind} lost {m.HullMax - m.Hull} health (tick {i})");
                }
            }

            Assert.True(seen.Count >= 5, $"only {seen.Count} animals in a minute — the gas giant must still come alive");
            Assert.NotEmpty(machines); // the machines still come — a drone over the gas at the least
            Assert.True(machineTravel > 2f, $"the machines never moved ({machineTravel:0.0} blocks) — a drone over open gas must not freeze");
            Assert.True(log.Burned == 0, log.BurnedLines);
        }
    }

    [Fact]
    public void AnAnimalInTheGas_RisesOutToItsBand()
    {
        // #2583: even an animal planted at the right band sank to the buried one within seconds and died; one that is in the
        // gas now (pushed, an old save) rises out instead of holding its depth — at once, not over the seconds of its ease.
        var server = Started(Key, out var repo, quiet: true);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var gen = GeneratorFor(server);
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            var deck = new Vector3f(0.5f, sea + 4, 0.5f);
            var placed = new List<(string Id, CreatureSpecies Sp)>();
            int bearing = 0;
            foreach (var sp in server.SpeciesRoster)
            {
                var (x, z) = OpenGasColumn(server, gen, sea, 18f, bearing);
                bearing += 2;
                string id = server.SpawnCreatureAtForTest(new Vector3f(x + 0.5f, sea - 3, z + 0.5f), sp.Id);
                var c = server.Creatures.Single(c => c.Id == id);
                c.Hull = c.HullMax = 1e6f; // it must live to show where it goes — how fast the gas kills is another test
                placed.Add((id, sp));
            }

            for (int i = 0; i < 100; i++)
            {
                p.State.Position = deck;
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
                if (i == 0)
                {
                    foreach (var (id, sp) in placed)
                    {
                        var c = server.Creatures.Single(c => c.Id == id);
                        Assert.False(InGas(server, c.Position), $"{sp.Name} is still in the gas after its first tick, at Y {c.Position.Y}");
                    }
                }
            }

            foreach (var (id, sp) in placed)
            {
                var c = server.Creatures.FirstOrDefault(c => c.Id == id);
                Assert.NotNull(c);
                Assert.True(c!.Position.Y >= sea + 2, $"{sp.Name} is still at Y {c.Position.Y} after ten seconds, the gas stands at {sea}");
                Assert.False(InGas(server, c.Position), $"{sp.Name} still touches the gas at Y {c.Position.Y}");
            }
        }
    }

    [Fact]
    public void AFlierSpawnedOverAnIsland_HoversOverTheIslandTop()
    {
        // #2583: over a column an island covers, the spawner measured from the heightfield buried under the gas (six cells
        // around it) — the animal appeared far below the rock, in the gas. It measures from the island's real top now.
        var server = Started(Key, out var repo, quiet: true);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var gen = GeneratorFor(server);
            var planet = server.World.Planet;
            int Top(int x, int z) => gen.TryGetHighestBand(planet, x, z, out var b) ? b.Top : int.MinValue;

            // An island's flat middle: a top well over the gas, the same within a block or two six blocks round.
            (int X, int Z, int Top)? found = null;
            for (int r = 40; r < 600 && found is null; r += 9)
            {
                for (int k = 0; k < 24 && found is null; k++)
                {
                    double a = k * Math.PI / 12.0;
                    int x = (int)Math.Floor(Math.Cos(a) * r), z = (int)Math.Floor(Math.Sin(a) * r);
                    int top = Top(x, z);
                    if (top < sea + 8)
                    {
                        continue;
                    }

                    bool flat = true;
                    foreach (var (dx, dz) in new[] { (6, 0), (-6, 0), (0, 6), (0, -6), (6, 6), (-6, -6), (6, -6), (-6, 6) })
                    {
                        flat &= Math.Abs(Top(x + dx, z + dz) - top) <= 2;
                    }

                    if (flat)
                    {
                        found = (x, z, top);
                    }
                }
            }

            Assert.NotNull(found);
            var (ix, iz, islandTop) = found!.Value;
            Assert.Equal(islandTop, TopSolidY(server, ix, iz, islandTop + 40)); // the real rock, streamed in, stands there

            var sp = server.SpeciesRoster.First();
            float hover = HoverOf(sp);
            float y = server.AirSpawnYForTest(sp.Id, ix, iz, server.SurfaceHeightForTest(ix, iz));
            Assert.Equal(islandTop + 1 + hover, y, 3);

            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            var watch = new Vector3f(ix + 15.5f, islandTop + 1, iz + 0.5f);
            string id = server.SpawnCreatureAtForTest(new Vector3f(ix + 0.5f, y, iz + 0.5f), sp.Id);
            for (int i = 0; i < 10; i++)
            {
                p.State.Position = watch;
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
            }

            var c = server.Creatures.Single(c => c.Id == id);
            Assert.True(c.Position.Y >= islandTop + 1 + hover - 2.5f,
                $"the flier sank to Y {c.Position.Y} over an island whose top is {islandTop} (its band: {islandTop + 1 + hover}, the gas: {sea})");
        }
    }

    /// <summary>The X of the home deck's last floor cell along +X at Z 0 (its rim there), with the rail around it taken out —
    /// every post within four blocks — so a body on the rim faces open gas, as on an island's rim or a gap in the rail.</summary>
    private int DeckRimWithAGap(SvGameServer server, int sea)
    {
        ushort floor = _content.GetBlock("steel_floor")!.NumericId.Value;
        int edge = 0;
        while (edge < 64 && server.World.GetBlock(new Vector3i(edge + 1, sea + 3, 0)).Value == floor)
        {
            edge++;
        }

        Assert.True(server.World.GetBlock(new Vector3i(edge + 1, sea + 3, 0)).IsAir, "past the deck's rim lies open air");
        Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(edge + 1, sea, 0)))?.Key);
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dz = -4; dz <= 4; dz++)
            {
                var post = new Vector3i(edge + dx, sea + 4, dz);
                if (_content.BlockById(server.World.GetBlock(post))?.Key is "energy_fence" or "light_white")
                {
                    server.World.SetBlock(post, BlockId.Air);
                }
            }
        }

        return edge;
    }

    [Fact]
    public void AWalkerOnADeck_NeverStepsOffIntoTheGas_WildOrTame()
    {
        // #2583: the gas carries nobody. The deck's rail holds everybody on it; where the rail has a gap (a post taken out, a
        // bridge built off — and an island's rim has no rail at all) a wild walker is held by the drop gate as well, while a
        // companion keeps its freedom from the terrain gate: the gas gate is what holds it.
        var log = new BurnLog();
        var server = Started(Key, out var repo, log: log, quiet: true, machines: false);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            int edge = DeckRimWithAGap(server, sea);

            // A plain land walker (the gas giant rolls none — a clone of another world's species, a companion brought along).
            var sp = server.SpeciesRoster.First();
            sp.Habitat = CreatureHabitat.Land;
            sp.Legs = 4;
            sp.LocoStyle = LocomotionStyle.Grazer;
            sp.HasWings = false;
            sp.HasGasSac = false;
            sp.HoverAltitude = 0f;
            sp.Size = 1f;
            sp.BodyPlan = CreatureBodyPlan.Standard;
            sp.Temperament = CreatureTemperament.Passive;
            sp.Activity = CreatureActivity.Cathemeral;
            sp.SocialGroupSize = 1;
            Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(sp));

            var onRim = new Vector3f(edge + 0.5f, sea + 4, 0.5f);
            string id = server.SpawnCreatureAtForTest(onRim, sp.Id);
            Assert.True(server.CreatureStepBlockedForTest(id, new Vector3f(edge + 1.5f, sea + 4, 0.5f), terrainGates: false),
                "a companion steps off the deck into the gas");
            Assert.True(server.CreatureStepBlockedForTest(id, new Vector3f(edge + 1.5f, sea + 4, 0.5f)), "a wild one steps off the deck");
            Assert.False(server.CreatureStepBlockedForTest(id, new Vector3f(edge - 0.5f, sea + 4, 0.5f), terrainGates: false),
                "…but along the deck it walks on");

            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            var deck = new Vector3f(0.5f, sea + 4, 0.5f);
            for (int i = 0; i < 200; i++)
            {
                p.State.Position = deck;
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
                var c = server.Creatures.Single(c => c.Id == id);
                Assert.True(c.Position.Y >= sea + 4 - 0.01f, $"the walker left the deck: Y {c.Position.Y} (tick {i})");
                Assert.False(InGas(server, c.Position), $"the walker touches the gas at {c.Position.X},{c.Position.Y},{c.Position.Z} (tick {i})");
            }

            Assert.True(log.Burned == 0, log.BurnedLines);
        }
    }

    /// <summary>A strip of open gas along +X, clear of the home deck: no island band over any column of
    /// [x − 2, x + 10] × [z − 1, z + 1], and gas at the sea level at its start.</summary>
    private (int X, int Z) OpenGasStrip(SvGameServer server, BlocksBeyondTheStars.WorldGeneration.WorldGenerator gen, int sea)
    {
        var planet = server.World.Planet;
        for (int r = 20; r <= 60; r += 4)
        {
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8.0;
                int x = (int)Math.Floor(Math.Cos(a) * r), z = (int)Math.Floor(Math.Sin(a) * r);
                bool open = true;
                for (int dx = -2; dx <= 10 && open; dx++)
                {
                    for (int dz = -1; dz <= 1 && open; dz++)
                    {
                        int cx = x + dx, cz = z + dz;
                        open = (cx * cx) + (cz * cz) > 18 * 18 && !gen.TryGetHighestBand(planet, cx, cz, out _);
                    }
                }

                if (open && _content.BlockById(server.World.GetBlock(new Vector3i(x, sea, z)))?.Key == "gas")
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("no strip of open gas around the deck");
    }

    [Fact]
    public void AWalkingRobot_NeverStepsOrIsPushedIntoTheGas_AndADrone_CrossesTheGasOverIt()
    {
        // #2586: a walking robot stands on a little platform set into the gas line, hunting a player who hangs over the gas.
        // Along its east rim a row of blocks stands two cells under the gas surface: standable (the gas is no colliding
        // block), two cells down — inside the drop limit — with the feet in the gas. Neither the hunt nor a shock-glove push
        // takes the robot onto it; the drop rule alone allowed both. A scan-drone over open gas hovers over its surface
        // (not four cells over a block sunk in it) and moves (it read every open-gas column as a wall and froze).
        var log = new BurnLog();
        var server = Started(Key, out var repo, log: log, quiet: true);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var (cx, cz) = OpenGasStrip(server, GeneratorFor(server), sea);
            var panel = _content.GetBlock("metal_panel")!.NumericId;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, sea, cz + dz), panel); // the platform: feet at sea + 1
                }
            }

            for (int dz = -1; dz <= 1; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + 2, sea - 2, cz + dz), panel); // the trap along the east rim: feet at sea − 1, in the gas
            }

            Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(cx + 2, sea - 1, cz)))?.Key);

            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Inventory.SetSlot(0, new ItemStack("shock_gloves", 1));
            p.State.SelectedHotbarSlot = 0;
            server.SpawnPlanetEnemyAtForTest(new Vector3f(cx + 0.5f, sea + 1, cz + 0.5f), CombatEntityKind.Creature, 0f);
            var robot = server.PlanetEnemies[^1];

            // The push: from the west, five blocks east — over the platform's rim and onto the trap, were it allowed.
            p.State.Position = new Vector3f(cx - 1.5f, sea + 1, cz + 0.5f);
            Assert.True(server.KnockbackForTest(p.State.PlayerId, robot.Id), "the shock glove must reach the robot");
            Assert.False(InGas(server, robot.Position), $"the push carried the robot into the gas, to {robot.Position.X:0.0},{robot.Position.Y:0.0}");
            Assert.True(robot.Position.X < cx + 2f, $"the push carried the robot off the platform to X {robot.Position.X - cx:0.0}");

            // The hunt: the player hangs over the gas east of the trap, well inside the hunting range.
            var bait = new Vector3f(cx + 8.5f, sea + 1, cz + 0.5f);
            server.SpawnPlanetEnemyAtForTest(new Vector3f(cx + 5.5f, sea + 1 + 4, cz + 0.5f), CombatEntityKind.ScanDrone, 0f);
            var drone = server.PlanetEnemies[^1];
            var droneStart = drone.Position;
            float droneTravel = 0f;
            for (int i = 0; i < 150; i++)
            {
                p.State.Position = bait;
                p.State.Health = 100f;
                p.State.Oxygen = 100f;
                server.TickForTest(0.1);
                Assert.Contains(robot, server.PlanetEnemies);
                Assert.True(robot.Position.Y >= sea + 1 - 0.01f, $"the robot left the platform: Y {robot.Position.Y} (tick {i})");
                Assert.True(robot.Position.X < cx + 2f, $"the robot stepped off the platform to X {robot.Position.X - cx:0.0} (tick {i})");
                foreach (var m in server.PlanetEnemies)
                {
                    Assert.False(InGas(server, m.Position), $"a {m.Kind} touches the gas at {m.Position.X:0},{m.Position.Y:0.0},{m.Position.Z:0} (tick {i})");
                }

                if (server.PlanetEnemies.Contains(drone))
                {
                    Assert.True(drone.Position.Y >= sea + 4f, $"the drone sank to Y {drone.Position.Y}, the gas stands at {sea} (tick {i})");
                    droneTravel = Math.Max(droneTravel, (float)Math.Sqrt(WorldConstants.WrapDistanceSquared(
                        new Vector3f(droneStart.X, 0f, droneStart.Z), new Vector3f(drone.Position.X, 0f, drone.Position.Z), server.World.Circumference)));
                }
            }

            Assert.True(droneTravel > 2f, $"the drone over open gas moved only {droneTravel:0.0} blocks in fifteen seconds");
            Assert.True(log.Burned == 0, log.BurnedLines);
        }
    }

    [Fact]
    public void ARobber_AndABasesScouts_NeverAppearInTheGas()
    {
        // #2586: a lone robber and a base's scouts were placed on the generator surface — on a gas giant the heightfield
        // under the gas — and the gas took them. Now they stand on real ground above the gas, or are not placed at all. A
        // floor laid over the gas in a 70° wedge of the ring they appear in (33–52 blocks out; their eight bearings are 45°
        // apart, so one always falls on it) makes sure there is somewhere to stand: the probe must find it, not just
        // reject the gas around it.
        var server = Started(Key, out var repo, quiet: true, configure: c =>
        {
            c.Rules.Bandits = AlienActivity.Normal;
            c.Rules.BaseVisitors = true;
        });
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var panel = _content.GetBlock("metal_panel")!.NumericId;
            const double From = 200.0 * Math.PI / 180.0, To = 270.0 * Math.PI / 180.0;
            for (int x = -52; x <= 52; x++)
            {
                for (int z = -52; z <= 52; z++)
                {
                    double r = Math.Sqrt((x * x) + (z * z)), a = Math.Atan2(z, x);
                    a = a < 0 ? a + (2 * Math.PI) : a;
                    if (r >= 33.0 && r <= 52.0 && a >= From && a <= To && server.World.GetBlock(new Vector3i(x, sea + 3, z)).IsAir)
                    {
                        server.World.SetBlock(new Vector3i(x, sea + 3, z), panel); // a floor over the gas: feet at sea + 4
                    }
                }
            }

            var owner = server.AddLocalPlayer("Owner");
            owner.State.AboardShip = false;
            owner.State.Position = new Vector3f(0.5f, sea + 4, 0.5f);
            for (int i = 0; i < 12; i++)
            {
                server.SpawnLoneBanditNearForTest(owner.State.PlayerId);
            }

            Assert.NotEmpty(server.Bandits); // the floor is found
            server.PlaceBaseForTest(owner, new Vector3i(3, sea + 8, 0));
            int baseId = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;
            Assert.True(server.SpawnScoutsForTest(baseId), "the scouts find the floor too");
            Assert.Contains(server.Bandits, b => b.ScoutBaseId == baseId);
            foreach (var b in server.Bandits)
            {
                Assert.False(InGas(server, b.Position), $"a robber appeared in the gas at {b.Position.X:0},{b.Position.Y:0},{b.Position.Z:0}");
                Assert.True(b.Position.Y >= sea + 2, $"a robber appeared at Y {b.Position.Y}, the gas stands at {sea}");
            }
        }
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
            // best effort — a locked SQLite file must not fail the test run
        }
    }
}
