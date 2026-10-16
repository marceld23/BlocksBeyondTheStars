// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The worlds half of the hash-variety package (terrain generation 26): world-seeded body sizes (#2691) and mixed
/// sky/cloud, flora-base and "auto" water colour families (#2692). A world of generation 25 or older keeps every size
/// and every colour bit for bit — the pinned values below were taken from the code before the change.
/// </summary>
public sealed class HashVarietyWorldsTests : IDisposable
{
    private const int Gen = WorldDescription.HashVarietyGeneration;
    private const int OldGen = Gen - 1;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_hashworlds_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void NewWorlds_AreCreatedWithTheHashVarietyGeneration()
    {
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        Assert.False(WorldConstants.HasSeededSizes(OldGen));
        Assert.True(WorldConstants.HasSeededSizes(Gen));
        Assert.False(AtmosphereTints.HasMixedTints(OldGen));
        Assert.True(AtmosphereTints.HasMixedTints(Gen));
    }

    // ---------- #2691: sizes ----------

    [Fact]
    public void OldGeneration_Sizes_AreTheClassicNumbers()
    {
        // Pinned from the formula before #2691 — the bug itself is visible here: four consecutive planet ids, one size.
        foreach (var id in new[] { "sys0-p0", "sys0-p1", "sys0-p2", "sys0-p3" })
        {
            Assert.Equal(11760, WorldConstants.CircumferenceFor(id, WorldConstants.WorldSizeClass.Planet));
        }

        Assert.Equal(6544, WorldConstants.CircumferenceFor("sys1-p0", WorldConstants.WorldSizeClass.Planet));
        Assert.Equal(8336, WorldConstants.CircumferenceFor("sys2-p0", WorldConstants.WorldSizeClass.Planet));
        Assert.Equal(2720, WorldConstants.CircumferenceFor("sys0-p0-m0", WorldConstants.WorldSizeClass.Moon));
        Assert.Equal(1280, WorldConstants.CircumferenceFor("sys0-a0", WorldConstants.WorldSizeClass.Asteroid));
        Assert.Equal(1296, WorldConstants.CircumferenceFor("sys0-a1", WorldConstants.WorldSizeClass.Asteroid));
        Assert.Equal(14832, WorldConstants.CircumferenceFor("sys3-p0", WorldConstants.WorldSizeClass.Planet, 0.8f));
        Assert.Equal(9152, WorldConstants.CircumferenceFor("sys4-p2", WorldConstants.WorldSizeClass.Planet, -0.35f));
        Assert.Equal(0.13895167f, WorldConstants.BiasToward("sys5-p1", WorldConstants.WorldSizeClass.Planet, 8000));

        // The world-aware entry point reproduces them for every generation below 26, whatever the seed.
        var classes = new[] { WorldConstants.WorldSizeClass.Asteroid, WorldConstants.WorldSizeClass.Moon, WorldConstants.WorldSizeClass.Planet };
        foreach (int generation in new[] { 0, 1, 21, OldGen })
            foreach (long seed in new[] { 0L, 42L, -7L, long.MaxValue })
                foreach (var cls in classes)
                    foreach (float bias in new[] { -1f, -0.35f, 0f, 0.42f, 1f })
                        for (int i = 0; i < 12; i++)
                        {
                            string id = $"sys{i}-p{i % 4}";
                            Assert.Equal(WorldConstants.CircumferenceFor(id, cls, bias), WorldConstants.CircumferenceFor(id, cls, bias, seed, generation));
                            Assert.Equal(WorldConstants.BiasToward(id, cls, 8000), WorldConstants.BiasToward(id, cls, 8000, seed, generation));
                        }
    }

    [Fact]
    public void OldGeneration_Galaxy_KeepsEverySize()
    {
        // A generation-25 galaxy with the archetypes on (lone giants, swarms, twins): every body's size through the
        // world-aware entry point folds to the hash taken before the change.
        var galaxy = new UniverseGenerator(42, new WorldDescription { StarSystemCount = 40, SystemVariance = true, TerrainGeneration = OldGen }, _content).Generate();
        ulong hash = 1469598103934665603UL;
        int bodies = 0;
        foreach (var b in galaxy.AllBodies())
        {
            bodies++;
            if (b.PlanetType is null || _content.GetPlanet(b.PlanetType) is not { } p)
            {
                continue;
            }

            int c = WorldConstants.CircumferenceFor(b.Id, WorldConstants.SizeClassFor(b.Kind, p.Key), b.SizeBias, 42, OldGen);
            hash = unchecked((hash ^ (uint)c) * 1099511628211UL);
        }

        Assert.Equal(402, bodies);
        Assert.Equal(6559692532296082603UL, hash);
    }

    [Fact]
    public void NewGeneration_PlanetsOfASystem_DifferInSize()
    {
        // The classic sizes put every planet of a standard system into one chunk of each other (100 %); seeded by the
        // world, the sizes spread over the whole band like independent draws.
        var galaxy = new UniverseGenerator(42, new WorldDescription { StarSystemCount = 80, TerrainGeneration = Gen }, _content).Generate();
        int systems = 0, oneChunk = 0, pairs = 0, equalPairs = 0;
        foreach (var system in galaxy.Systems)
        {
            var sizes = system.Bodies.Where(b => b.Kind == CelestialKind.Planet)
                .Select(b => WorldConstants.CircumferenceFor(b.Id, WorldConstants.SizeClassFor(b.Kind, b.PlanetType ?? string.Empty), b.SizeBias, 42, Gen))
                .ToList();
            if (sizes.Count < 3)
            {
                continue;
            }

            systems++;
            if (sizes.Max() - sizes.Min() <= WorldConstants.ChunkSize)
            {
                oneChunk++;
            }

            for (int a = 0; a < sizes.Count; a++)
                for (int b = a + 1; b < sizes.Count; b++)
                {
                    pairs++;
                    if (sizes[a] == sizes[b])
                    {
                        equalPairs++;
                    }
                }
        }

        Assert.True(systems >= 20, $"need systems with three planets or more, got {systems}");
        Assert.True(oneChunk <= systems / 20, $"{oneChunk} of {systems} systems still have all planets within one chunk");
        Assert.True(equalPairs <= pairs / 20, $"{equalPairs} of {pairs} planet pairs share a size");
    }

    [Fact]
    public void NewGeneration_SizesDifferBetweenWorlds_StayInTheirBand_AndOnWholeChunks()
    {
        var bands = new (WorldConstants.WorldSizeClass Cls, string Id, int Lo, int Hi)[]
        {
            (WorldConstants.WorldSizeClass.Planet, "sys0-p0", 5000, 12000),
            (WorldConstants.WorldSizeClass.Moon, "sys0-p0-m0", 2500, 4000),
            (WorldConstants.WorldSizeClass.Asteroid, "sys0-a0", 800, 1600),
        };
        foreach (var (cls, id, lo, hi) in bands)
        {
            var seen = new HashSet<int>();
            for (long seed = 1; seed <= 60; seed++)
            {
                int c = WorldConstants.CircumferenceFor(id, cls, 0f, seed, Gen);
                Assert.Equal(0, c % WorldConstants.ChunkSize);
                Assert.InRange(c, lo - WorldConstants.ChunkSize, hi + WorldConstants.ChunkSize);
                Assert.Equal(c, WorldConstants.CircumferenceFor(id, cls, 0f, seed, Gen)); // deterministic
                seen.Add(c);
            }

            // The classic size is the same in every world; seeded, the same system index differs between worlds.
            Assert.True(seen.Count >= 20, $"{cls}: only {seen.Count} sizes across 60 worlds");
        }

        Assert.NotEqual(
            WorldConstants.CircumferenceFor("sys0-p0", WorldConstants.WorldSizeClass.Planet, 0f, 1, Gen),
            WorldConstants.CircumferenceFor("sys0-p0", WorldConstants.WorldSizeClass.Planet, 0f, 2, Gen));
    }

    [Fact]
    public void NewGeneration_Archetypes_KeepTheirSizeIdentity()
    {
        // Lone giants are giants, swarm worlds run small, twins are twins — on top of the seeded base size.
        const long seed = 7;
        var galaxy = new UniverseGenerator(seed, new WorldDescription { StarSystemCount = 150, SystemVariance = true, TerrainGeneration = Gen }, _content).Generate();
        int Size(CelestialBody b, float bias) =>
            WorldConstants.CircumferenceFor(b.Id, WorldConstants.SizeClassFor(b.Kind, b.PlanetType ?? string.Empty), bias, seed, Gen);

        int giants = 0, swarms = 0, twins = 0;
        for (int i = 0; i < galaxy.Systems.Count; i++)
        {
            var planets = galaxy.Systems[i].Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            switch (SystemArchetypes.ForIndex(seed, i))
            {
                case SystemArchetype.LoneGiant:
                    giants++;
                    Assert.True(Size(planets[0], planets[0].SizeBias) >= 11500, $"{planets[0].Id}: giant sized {Size(planets[0], planets[0].SizeBias)}");
                    break;
                case SystemArchetype.Swarm:
                    swarms++;
                    Assert.All(planets, p => Assert.True(Size(p, p.SizeBias) <= Size(p, 0f), $"{p.Id}: swarm worlds run small"));
                    break;
                case SystemArchetype.TwinWorlds:
                    twins++;
                    Assert.Equal(2, planets.Count);
                    int c0 = Size(planets[0], planets[0].SizeBias), c1 = Size(planets[1], planets[1].SizeBias);
                    Assert.True(Math.Abs(c0 - c1) <= WorldConstants.ChunkSize, $"{galaxy.Systems[i].Id}: twins sized {c0} vs {c1}");
                    break;
            }
        }

        Assert.True(giants > 0 && swarms > 0 && twins > 0, $"giants {giants}, swarms {swarms}, twins {twins}");
    }

    [Fact]
    public void Server_SizesItsWorldsByTheSavesGeneration()
    {
        // The server's loaded world and a travelled-to world read the one world-aware size of the save.
        foreach (int generation in new[] { OldGen, Gen })
        {
            var (server, repo) = NewServer("sizes" + generation, 4242, generation);
            using (repo)
            {
                var start = server.Galaxy.FindBody(server.ActiveLocationId)!;
                Assert.Equal(Expected(start, 4242, generation), server.World.Circumference);

                var other = server.Galaxy.Systems.Single(s => s.Id == start.SystemId).Bodies
                    .First(b => b.Id != start.Id && b.Kind is CelestialKind.Planet or CelestialKind.Moon && b.PlanetType is not null);
                server.Travel("Pilot", other.Id);
                Assert.Equal(other.Id, server.ActiveLocationId);
                Assert.Equal(Expected(other, 4242, generation), server.World.Circumference);
                if (generation < Gen)
                {
                    Assert.Equal(
                        WorldConstants.CircumferenceFor(other.Id, WorldConstants.SizeClassFor(other.Kind, other.PlanetType!), other.SizeBias),
                        server.World.Circumference);
                }
            }
        }

        static int Expected(CelestialBody b, long seed, int generation)
            => WorldConstants.CircumferenceFor(b.Id, WorldConstants.SizeClassFor(b.Kind, b.PlanetType!), b.SizeBias, seed, generation);
    }

    // ---------- #2692: colour families ----------

    [Fact]
    public void OldGeneration_Tints_AreTheClassicColours()
    {
        // Pinned from the formulas before #2692.
        var rocky = _content.GetPlanet("rocky")!;
        var ocean = _content.GetPlanet("ocean")!;
        int sky = AtmosphereTints.SkyRgb(42, "sys0-p1", rocky, OldGen);
        Assert.Equal(0xEEB788, sky);
        Assert.Equal(0xEEF5F1, AtmosphereTints.CloudRgb(42, "sys0-p1", rocky, sky, OldGen));
        int sky2 = AtmosphereTints.SkyRgb(7, "sys3-p2", rocky, OldGen);
        Assert.Equal(0xEFD8A5, sky2);
        Assert.Equal(0xEFEFEB, AtmosphereTints.CloudRgb(7, "sys3-p2", rocky, sky2, OldGen));
        Assert.Equal(0x62B842, FloraTints.ForWorld(4242, "sys0-p1", OldGen));
        Assert.Equal(0x977730, FloraTints.ForWorld(99, "sys2-p0-m0", OldGen));
        Assert.Equal((0x2678CC, FluidTints.Mode.Tint), FluidTints.ForWorld(4242, "sys0-p1", ocean, OldGen));
        Assert.Equal((0x82C521, FluidTints.Mode.Tint), FluidTints.ForWorld(4242, "sys0-p1", ocean, OldGen, toxicWater: true));

        // Every body of a generation-25 galaxy, folded to the hash taken before the change.
        var galaxy = new UniverseGenerator(42, new WorldDescription { StarSystemCount = 40, SystemVariance = true, TerrainGeneration = OldGen }, _content).Generate();
        ulong hash = 1469598103934665603UL;
        foreach (var b in galaxy.AllBodies())
        {
            if (b.PlanetType is null || _content.GetPlanet(b.PlanetType) is not { } p)
            {
                continue;
            }

            int s = AtmosphereTints.SkyRgb(42, b.Id, p, OldGen);
            int cl = AtmosphereTints.CloudRgb(42, b.Id, p, s, OldGen);
            int f = FloraTints.ForWorld(42, b.Id, OldGen);
            var w = FluidTints.ForWorld(42, b.Id, p, OldGen);
            foreach (int v in new[] { s, cl, f, w.Rgb, (int)w.Mode })
            {
                hash = unchecked((hash ^ (uint)v) * 1099511628211UL);
            }
        }

        Assert.Equal(8660131596107556750UL, hash);
    }

    [Fact]
    public void NewGeneration_NeighbouringBodies_PickTheirColourFamiliesLikeChance()
    {
        // "Same family" as the eye sees it: two colours within one jitter span (every channel within 31) — two picks of
        // one palette anchor always are. Classic: neighbouring ids landed there five times in six. Mixed: about the
        // chance of two independent draws (sky ≈ 18 %, flora ≈ 18 %, water ≈ 35 % — the classic blue is half the
        // water palette), plus the odd near miss between two close anchors.
        var rocky = _content.GetPlanet("rocky")!;
        var ocean = _content.GetPlanet("ocean")!;
        var looks = new (string Name, Func<long, string, int, int> Rgb, double MaxMixed)[]
        {
            ("sky", (seed, id, g) => AtmosphereTints.SkyRgb(seed, id, rocky, g), 0.40),
            ("flora", (seed, id, g) => FloraTints.ForWorld(seed, id, g), 0.40),
            ("water", (seed, id, g) => FluidTints.ForWorld(seed, id, ocean, g).Rgb, 0.55),
        };
        foreach (var (name, rgb, maxMixed) in looks)
        {
            double classic = NeighbourShare(rgb, OldGen), mixed = NeighbourShare(rgb, Gen);
            Assert.True(classic > 0.7, $"{name}: the classic hash should show the bug ({classic:P0})");
            Assert.True(mixed < maxMixed, $"{name}: neighbouring bodies still share a family {mixed:P0} of the time");
        }

        static double NeighbourShare(Func<long, string, int, int> rgb, int generation)
        {
            int same = 0, pairs = 0;
            for (long seed = 1; seed <= 150; seed++)
                for (int p = 0; p < 5; p++)
                {
                    int a = rgb(seed, $"sys2-p{p}", generation), b = rgb(seed, $"sys2-p{p + 1}", generation);
                    pairs++;
                    if (Near(a, b))
                    {
                        same++;
                    }
                }

            return same / (double)pairs;
        }

        static bool Near(int a, int b)
            => Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= 31
               && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= 31
               && Math.Abs((a & 0xFF) - (b & 0xFF)) <= 31;
    }

    [Fact]
    public void NewGeneration_Tints_StayOnTheirPalettes_AndKeepTheAuthoredColours()
    {
        var rocky = _content.GetPlanet("rocky")!;
        var toxica = _content.GetPlanet("toxica_maxima")!;
        Assert.Equal(toxica.SkyColor, AtmosphereTints.SkyRgb(42, "sys0-p1", toxica, Gen)); // an authored sky stays authored
        var skies = new HashSet<int>();
        for (int i = 0; i < 64; i++)
        {
            string id = "sys1-p" + i;
            int sky = AtmosphereTints.SkyRgb(42, id, rocky, Gen);
            skies.Add(sky);
            Assert.Equal(sky, AtmosphereTints.SkyRgb(42, id, rocky, Gen));
            int cloud = AtmosphereTints.CloudRgb(42, id, rocky, sky, Gen);
            Assert.Equal(cloud, AtmosphereTints.CloudRgb(42, id, rocky, sky, Gen));
            // The cloud stays the type's colour ± the small jitter (or the contrast push away from the sky).
            Assert.InRange(((cloud >> 16) & 0xFF) + ((cloud >> 8) & 0xFF) + (cloud & 0xFF), 300, 765);
            Assert.NotEqual(FluidTints.Mode.Classic, FluidTints.ForWorld(42, id, _content.GetPlanet("ocean"), Gen).Mode);
        }

        Assert.True(skies.Count > 16, $"only {skies.Count} sky hues over 64 bodies");
    }

    [Fact]
    public void Server_ShipsTheSavesTints()
    {
        foreach (int generation in new[] { OldGen, Gen })
        {
            var (server, repo) = NewServer("tints" + generation, 4242, generation);
            using (repo)
            {
                var body = server.Galaxy.FindBody(server.ActiveLocationId)!;
                var planet = _content.GetPlanet(body.PlanetType!);
                int sky = AtmosphereTints.SkyRgb(4242, body.Id, planet, generation);
                Assert.Equal(sky, server.SkyColor);
                Assert.Equal(AtmosphereTints.CloudRgb(4242, body.Id, planet, sky, generation), server.CloudColorForTest);
                Assert.Equal(FloraTints.ForWorld(4242, body.Id, generation), server.FloraTint);
            }
        }
    }

    private (SvGameServer Server, SqliteWorldRepository Repo) NewServer(string tag, int seed, int generation)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig { WorldName = tag, Seed = seed, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer("Pilot");
        return (server, repo);
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
            // best effort — a still-open SQLite handle on Windows
        }
    }
}
