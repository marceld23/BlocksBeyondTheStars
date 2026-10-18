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
/// Personal space between animals (#2753, "some big animals stand inside each other"): two big animals of different
/// species keep their drawn bodies apart, an awake animal yields to a sleeper, overlapping sleepers shuffle apart with only
/// the later one moving, the spawner refuses a spot inside another animal's body, and the footprint grows with the body.
/// </summary>
public sealed class CreatureSpacingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureSpacingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_spacing_" + Guid.NewGuid().ToString("N"));
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

    // --- The footprint rule -------------------------------------------------------------------------------------------

    [Fact]
    public void Footprint_GrowsWithSizeAndSegments_AndStaysUnderTheCap()
    {
        var small = Species(CreatureBodyPlan.Standard, size: 1f, segments: 1);
        var big = Species(CreatureBodyPlan.Standard, size: 2.5f, segments: 1);
        var longBody = Species(CreatureBodyPlan.Standard, size: 2.5f, segments: 4);

        Assert.True(CreatureFootprint.Radius(big, 1f) > CreatureFootprint.Radius(small, 1f), "a bigger body covers more ground");
        Assert.True(CreatureFootprint.Radius(longBody, 1f) > CreatureFootprint.Radius(big, 1f), "every segment lengthens the body");
        Assert.True(CreatureFootprint.Radius(big, 1.3f) > CreatureFootprint.Radius(big, 0.7f), "the individual's own size counts");

        // The standard body of size 1, one segment: half a unit of 0.5 for the body, half a unit for the head.
        Assert.Equal(0.525f, CreatureFootprint.Radius(small, 1f), 3);

        var titan = Species(CreatureBodyPlan.Titan, size: 4f, segments: 2);
        var giraffe = Species(CreatureBodyPlan.Titan, size: 4f, segments: 2);
        giraffe.NeckLength = 3;
        Assert.True(CreatureFootprint.Radius(giraffe, 1f) > CreatureFootprint.Radius(titan, 1f), "a neck carries the head further out");
        // The report's animals: titans 5–7 blocks long must keep far more than the 2.6 blocks they stood apart.
        Assert.True(CreatureFootprint.SeparationDistance(CreatureFootprint.Radius(titan, 1f), CreatureFootprint.Radius(titan, 1f)) > 5f);

        // No ordinary body plan, at its largest, outgrows the server's pre-check radius.
        foreach (var plan in new[]
                 {
                     CreatureBodyPlan.Standard, CreatureBodyPlan.Floral, CreatureBodyPlan.Titan, CreatureBodyPlan.Arachnid,
                     CreatureBodyPlan.Biped, CreatureBodyPlan.Worm, CreatureBodyPlan.Ray, CreatureBodyPlan.Medusa,
                 })
        {
            var huge = Species(plan, size: 10f, segments: 48);
            huge.NeckLength = 9;
            huge.HeadRatio = 3f;
            float r = CreatureFootprint.Radius(huge, 1.3f);
            Assert.True(r > 0f && r <= CreatureFootprint.MaxRadius, $"{plan}: radius {r:0.00} must stay within {CreatureFootprint.MaxRadius}");
        }
    }

    // --- On a live server -------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoBigAnimalsOfDifferentSpecies_PlantedInsideEachOther_StepApart()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var titan = ForceBig(server, 0, CreatureBodyPlan.Titan, size: 4f, segments: 2);
            var grazer = ForceBig(server, 1, CreatureBodyPlan.Standard, size: 2.5f, segments: 3);
            var (cx, cz) = DryGround.Find(server, 16);
            int padY = BuildPad(server, cx, cz, 20);
            OnFoot(server, new Vector3f(cx + 0.5f, padY + 1, cz + 14.5f));

            string a = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), titan.Id);
            string b = server.SpawnCreatureAtForTest(new Vector3f(cx + 2.0f, padY + 1, cz + 0.5f), grazer.Id);
            server.PauseCreatureForTest(a, 60f); // standing still: only the spacing may move them
            server.PauseCreatureForTest(b, 60f);
            var a0 = Pos(server, a);
            var b0 = Pos(server, b);
            float sep = CreatureFootprint.SeparationDistance(server.CreatureFootprintRadiusForTest(a), server.CreatureFootprintRadiusForTest(b));
            Assert.True(sep > 4f, $"two big bodies need more than four blocks, not {sep:0.00}");

            Tick(server, 100);

            float d = Dist(Pos(server, a), Pos(server, b));
            Assert.True(d >= sep - 0.05f, $"they must stand {sep:0.00} apart, stand {d:0.00}");
            Assert.True(Dist(Pos(server, a), a0) > 0.1f && Dist(Pos(server, b), b0) > 0.1f, "both awake animals give way");
            Assert.True(Dist(Pos(server, a), a0) < sep && Dist(Pos(server, b), b0) < sep, "a nudge, not a launch");
        }
    }

    [Fact]
    public void AnAwakeAnimal_YieldsToASleeper_AndOverlappingSleepers_ComeApart_TheLaterOneMoving()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sleeper = ForceBig(server, 0, CreatureBodyPlan.Standard, size: 2f, segments: 2, activity: CreatureActivity.Nocturnal);
            var walker = ForceBig(server, 1, CreatureBodyPlan.Standard, size: 2f, segments: 2);
            var (cx, cz) = DryGround.Find(server, 16);
            int padY = BuildPad(server, cx, cz, 20);
            OnFoot(server, new Vector3f(cx + 0.5f, padY + 1, cz + 14.5f)); // well outside the wake distance

            string first = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sleeper.Id);
            string second = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz - 0.5f), sleeper.Id);
            string awake = server.SpawnCreatureAtForTest(new Vector3f(cx + 2.0f, padY + 1, cz + 0.5f), walker.Id);
            server.PauseCreatureForTest(awake, 60f);

            // A daytime for this longitude: the nocturnal pair sleeps through it.
            bool asleep = false;
            foreach (double fraction in new[] { 0.5, 0.0, 0.25, 0.75 })
            {
                server.SetLocalDayFractionForTest(fraction, cx);
                if (server.NetCreatureForTest(first).Asleep && server.NetCreatureForTest(second).Asleep)
                {
                    asleep = true;
                    break;
                }
            }

            Assert.True(asleep, "the nocturnal test animals must be asleep at one of the four times of day");
            Assert.False(server.NetCreatureForTest(awake).Asleep);
            var firstAt = Pos(server, first);
            var secondAt = Pos(server, second);

            Tick(server, 120);

            Assert.True(server.NetCreatureForTest(first).Asleep && server.NetCreatureForTest(second).Asleep, "the shuffle never wakes them");
            Assert.True(Dist(Pos(server, first), firstAt) < 0.05f, "the earlier sleeper lies still — the awake one and the later sleeper give way");
            Assert.True(Dist(Pos(server, second), secondAt) > 0.1f, "the later sleeper shuffled aside");

            float sepSleepers = Sep(server, first, second);
            float sepAwake = Sep(server, first, awake);
            Assert.True(Dist(Pos(server, first), Pos(server, second)) >= sepSleepers - 0.05f,
                $"the sleepers lie {sepSleepers:0.00} apart, not {Dist(Pos(server, first), Pos(server, second)):0.00}");
            Assert.True(Dist(Pos(server, first), Pos(server, awake)) >= sepAwake - 0.05f,
                $"the awake animal stands {sepAwake:0.00} off the sleeper, not {Dist(Pos(server, first), Pos(server, awake)):0.00}");
        }
    }

    [Fact]
    public void ASpawnSpotInsideABigAnimal_IsRefused_AFarOneIsFine()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var big = ForceBig(server, 0, CreatureBodyPlan.Standard, size: 2.5f, segments: 3);
            var (cx, cz) = DryGround.Find(server, 16);
            int padY = BuildPad(server, cx, cz, 20);
            OnFoot(server, new Vector3f(cx + 0.5f, padY + 1, cz + 14.5f));

            var near = new Vector3f(cx + 2.0f, padY + 1, cz + 0.5f);
            var far = new Vector3f(cx + 15.5f, padY + 1, cz + 0.5f);
            Assert.True(server.SpawnSpotClearForSpeciesTest(big.Id, near), "an open pad is a spawn spot for this test to mean anything");
            server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), big.Id);
            Assert.False(server.SpawnSpotClearForSpeciesTest(big.Id, near), "a spot inside the resident's body is refused");
            Assert.True(server.SpawnSpotClearForSpeciesTest(big.Id, far), "a spot well clear of it is fine");
        }
    }

    [Fact]
    public void ABigHerd_SpawnsWithRoomBetweenEveryPair()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var big = ForceBig(server, 0, CreatureBodyPlan.Standard, size: 2.5f, segments: 3);
            big.SocialGroupSize = 12;
            var (gx, gz) = DryGround.Find(server, 24);

            // A herd on the natural ground, around a leader that already stands there — as the spawner places one.
            int ground = server.SurfaceHeightForTest(gx, gz) + 1;
            server.SpawnCreatureAtForTest(new Vector3f(gx + 0.5f, ground, gz + 0.5f), big.Id);
            server.SpawnGroupAroundForTest(big.Id, gx, gz);
            var herd = server.Creatures.Where(c => c.SpeciesId == big.Id).ToList();
            Assert.True(herd.Count >= 4, $"a herd still finds room around the leader, placed {herd.Count - 1} members");
            for (int i = 0; i < herd.Count; i++)
            {
                for (int j = i + 1; j < herd.Count; j++)
                {
                    float sep = Sep(server, herd[i].Id, herd[j].Id);
                    float d = Dist(herd[i].Position, herd[j].Position);
                    Assert.True(d >= sep - 0.01f, $"{herd[i].Id} and {herd[j].Id} spawned {d:0.00} apart, inside {sep:0.00}");
                }
            }
        }
    }

    /// <summary>The review find on #2753: the footprint check refused every member of a titan herd placed on the classic
    /// 6.5–11.5-block rings, so "megafauna in a small herd" (#638/#639) spawned as one animal. The rings now grow with the
    /// body: on the flat salt pans a titan herd (and a big grazer herd) spawns whole, every member clear of the ones before it.</summary>
    [Theory]
    [InlineData(CreatureBodyPlan.Titan, 4.75f, 2, 1, 3, 3)]
    [InlineData(CreatureBodyPlan.Titan, 4.75f, 2, 1, 4, 4)]
    [InlineData(CreatureBodyPlan.Titan, 6f, 3, 3, 4, 4)]
    [InlineData(CreatureBodyPlan.Standard, 2.2f, 4, 0, 9, 9)]
    public void ABigBodiedHerd_SpawnsWhole_EveryMemberClearOfTheOnesBeforeIt(CreatureBodyPlan plan, float size, int segments,
        int neck, int group, int expected)
    {
        var server = Started(out var repo, "salt_flats");
        using (repo)
        {
            var sp = ForceBig(server, 0, plan, size, segments);
            sp.NeckLength = neck;
            sp.SocialGroupSize = group;
            var (gx, gz) = DryGround.Find(server, 40);

            int ground = server.SurfaceHeightForTest(gx, gz) + 1;
            server.SpawnCreatureAtForTest(new Vector3f(gx + 0.5f, ground, gz + 0.5f), sp.Id);
            server.SpawnGroupAroundForTest(sp.Id, gx, gz);
            var herd = server.Creatures.Where(c => c.SpeciesId == sp.Id).ToList();
            Assert.True(herd.Count >= expected, $"a herd of {group} must spawn {expected}, spawned {herd.Count}");

            // In spawn order: every member stands clear of each animal placed before it — its own body at the species size
            // plus that animal's real one (the spawner's check).
            float newcomer = CreatureFootprint.Radius(sp, 1f);
            for (int i = 0; i < herd.Count; i++)
            {
                for (int j = i + 1; j < herd.Count; j++)
                {
                    float sep = newcomer + server.CreatureFootprintRadiusForTest(herd[i].Id);
                    float d = Dist(herd[i].Position, herd[j].Position);
                    Assert.True(d >= sep - 0.01f, $"{herd[j].Id} spawned {d:0.00} from {herd[i].Id}, inside {sep:0.00}");
                }
            }
        }
    }

    // --- Helpers ----------------------------------------------------------------------------------------------------------

    private SvGameServer Started(out SqliteWorldRepository repo, string planet = "jungle")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "spacing"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "spacing",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off; // only the animals the test plants
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static CreatureSpecies Species(CreatureBodyPlan plan, float size, int segments)
        => new() { Id = "t", BodyPlan = plan, Size = size, BodySegments = segments };

    /// <summary>Forces a roster slot into a big, passive, solitary walker of the wanted body that never begs.</summary>
    private static CreatureSpecies ForceBig(SvGameServer server, int slot, CreatureBodyPlan plan, float size, int segments,
        CreatureActivity activity = CreatureActivity.Cathemeral)
    {
        var sp = server.SpeciesRoster[slot];
        sp.Habitat = CreatureHabitat.Land;
        sp.BodyPlan = plan;
        sp.HeadShape = CreatureHeadShape.Box;
        sp.Legs = 4;
        sp.BodySegments = segments;
        sp.NeckLength = 0;
        sp.Size = size;
        sp.Temperament = CreatureTemperament.Passive;
        sp.LocoStyle = LocomotionStyle.Strider;
        sp.Activity = activity;
        sp.SocialGroupSize = 1;
        sp.BegsForFood = false;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.AttackDamage = 0f;
        sp.BiomeExclusive = false;
        sp.AngeredByMining = false;
        sp.GiftsWhenCalm = false;
        return sp;
    }

    private static void OnFoot(SvGameServer server, Vector3f at)
    {
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
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

    /// <summary>A flat stone pad well above the terrain (and its trees), so the body checks see open ground.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int top = int.MinValue;
        for (int dx = -r - 4; dx <= r + 4; dx++)
        {
            for (int dz = -r - 4; dz <= r + 4; dz++)
            {
                top = Math.Max(top, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        int padY = top + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    private static float Sep(SvGameServer server, string a, string b)
        => CreatureFootprint.SeparationDistance(server.CreatureFootprintRadiusForTest(a), server.CreatureFootprintRadiusForTest(b));

    private static float Dist(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    private static void Tick(SvGameServer server, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(0.1);
        }
    }

    private static Vector3f Pos(SvGameServer server, string id) => server.Creatures.First(c => c.Id == id).Position;
}
