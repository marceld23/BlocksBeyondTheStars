// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Giant ship wrecks in space (#3614), the pure parts: the galaxy placement (one guaranteed in the start system, rare elsewhere,
/// Off = none, never the finale or a grown system, no body moves), the weighted, stable design pick, the size-aware flight spot
/// that clears every other hull even for the largest ship, and the derelict decay in ship mode (rooms from the template's
/// doorways, doors, breaches, emergency lights, one guardian bay, the chests kept, a standable arrival) — the station paths
/// untouched (DerelictStationTests pins their fingerprints).
/// </summary>
public sealed class GiantWreckPlacerTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private List<StarSystem> Galaxy(long seed, int systems = 12)
    {
        var desc = new WorldDescription { StarSystemCount = systems };
        return new UniverseGenerator(seed, desc, _content).Generate(systems).Systems;
    }

    private static List<CelestialBody> Giants(IEnumerable<StarSystem> systems)
        => systems.SelectMany(s => s.Bodies).Where(b => GiantShipRules.IsGiantWreckId(b.Id)).ToList();

    private static int SystemIndex(string bodyId)
    {
        string rest = bodyId.Substring(GiantWreckPlacer.IdPrefix.Length + 3); // "derelict:giant-sys" stripped
        return int.Parse(rest.Substring(0, rest.IndexOf('-')), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- placement ----------------

    [Fact]
    public void Placer_GuaranteesOneInTheStartSystem_IsRareElsewhere_AndOffPlacesNone()
    {
        int systemsWithOne = 0, candidates = 0;
        for (long seed = 1; seed <= 40; seed++) // small typed seeds on purpose: they must roll extras too
        {
            var systems = Galaxy(seed);
            GiantWreckPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0");
            var start = Giants(systems.Where(s => s.Id == "sys0"));
            var only = Assert.Single(start);
            Assert.Equal(GiantWreckPlacer.BodyId("sys0", 0), only.Id);
            Assert.Equal(CelestialKind.SpaceStation, only.Kind);
            Assert.True(DerelictPlacer.IsDerelictId(only.Id), "a giant wreck is a derelict of its own family");
            Assert.False(string.IsNullOrWhiteSpace(only.Name));

            foreach (var sys in systems.Where(s => s.Id != "sys0"))
            {
                int n = Giants(new[] { sys }).Count;
                Assert.InRange(n, 0, GiantShipRules.MaxPerSystem);
                candidates++;
                systemsWithOne += n;
            }

            Assert.Equal(0, GiantWreckPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0")); // idempotent

            var off = Galaxy(seed);
            Assert.Equal(0, GiantWreckPlacer.Place(off, seed, Frequency.Off, off.Count, "sys0"));
            Assert.Empty(Giants(off));
        }

        double share = (double)systemsWithOne / candidates;
        Assert.True(share < 0.15, $"giant wrecks should be rare outside the start system, found them in {share:P0} of the systems");
        Assert.True(systemsWithOne > 0, "rare is not never: some system beyond the start must hold one");
    }

    [Fact]
    public void Placer_IsSeedStable_NeverTouchesAGrownSystemOrTheFinale_AndMovesNoBody()
    {
        var a = Galaxy(42, systems: 14);
        var b = Galaxy(42, systems: 14);
        a.Add(new StarSystem { Id = SvGameServer.GuardianFinaleSystemId, Name = "Guardian Core" });
        a[^1].Bodies.Add(new CelestialBody { Id = "finale-planet", Kind = CelestialKind.Planet, SystemId = SvGameServer.GuardianFinaleSystemId });
        DerelictPlacer.Place(a, 42, Frequency.Normal, fixedCount: 10, "sys2");
        DerelictPlacer.Place(b, 42, Frequency.Normal, fixedCount: 10, "sys2");
        var before = a.SelectMany(s => s.Bodies).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ, x.Kind)).ToList();

        GiantWreckPlacer.Place(a, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        GiantWreckPlacer.Place(b, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        var ga = Giants(a).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ, x.ParentId)).ToList();
        var gb = Giants(b).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ, x.ParentId)).ToList();
        Assert.Equal(ga, gb);
        Assert.Contains(ga, g => g.Id == GiantWreckPlacer.BodyId("sys2", 0));
        Assert.All(ga, g => Assert.True(SystemIndex(g.Id) < 10, $"{g.Id} sits in a system beyond the fixed ten"));
        Assert.DoesNotContain(a[^1].Bodies, x => GiantShipRules.IsGiantWreckId(x.Id));

        // Nothing the generator or the derelicts put there moved.
        var after = a.SelectMany(s => s.Bodies).Where(x => !GiantShipRules.IsGiantWreckId(x.Id))
            .Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ, x.Kind)).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Placer_AddsOnlyTheWrecksTheServerAccepts()
    {
        var systems = Galaxy(5);
        Assert.Equal(0, GiantWreckPlacer.Place(systems, 5, Frequency.Frequent, systems.Count, "sys0", accept: _ => false));
        Assert.Empty(Giants(systems));
        Assert.Equal(1, GiantWreckPlacer.Place(systems, 5, Frequency.Frequent, systems.Count, "sys0",
            accept: id => id == GiantWreckPlacer.BodyId("sys0", 0)));
        Assert.Equal(GiantWreckPlacer.BodyId("sys0", 0), Assert.Single(Giants(systems)).Id);
    }

    // ---------------- the design ----------------

    [Fact]
    public void PickDesign_FollowsTheWeights_IsStable_AndIgnoresTheListOrder()
    {
        StructureTemplate Ship(string key, int weight) => new() { Key = key, GiantShip = true, Weight = weight };
        var pool = new List<StructureTemplate> { Ship("carrier", 3), Ship("battleship", 3), Ship("liner", 3), Ship("ark", 1), Ship("squid", 1) };
        var shuffled = new List<StructureTemplate> { pool[4], pool[2], pool[0], pool[3], pool[1] };
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 2200; i++)
        {
            string id = GiantWreckPlacer.BodyId("sys" + (i % 30), i / 30);
            var pick = GiantWreckPlacer.PickDesign(pool, 77, id)!;
            Assert.Same(pick, GiantWreckPlacer.PickDesign(shuffled, 77, id));
            Assert.Same(pick, GiantWreckPlacer.PickDesign(pool, 77, id));
            counts[pick.Key] = counts.TryGetValue(pick.Key, out int n) ? n + 1 : 1;
        }

        Assert.All(new[] { "carrier", "battleship", "liner" }, k => Assert.InRange(counts[k], 450, 750)); // 3 of 11 ≈ 600
        Assert.All(new[] { "ark", "squid" }, k => Assert.InRange(counts[k], 120, 290)); // 1 of 11 ≈ 200
        Assert.Null(GiantWreckPlacer.PickDesign(new List<StructureTemplate>(), 77, "x"));
    }

    [Fact]
    public void TheWrecksOfOneWorld_AreDifferentShips_WhileADesignIsLeft()
    {
        // #3764 (Justus: "in every system I have a wreck I already know from the first one"): each wreck picked on its own,
        // so two wrecks of a world were the same ship about one time in four. Now a wreck picks among the designs no other
        // wreck of the world is yet.
        StructureTemplate Ship(string key, int weight) => new() { Key = key, GiantShip = true, Weight = weight };
        var pool = new List<StructureTemplate> { Ship("carrier", 3), Ship("battleship", 3), Ship("liner", 3), Ship("ark", 1), Ship("squid", 1) };
        int repeatsBefore = 0;
        for (long seed = 1; seed <= 300; seed++)
        {
            var taken = new HashSet<string>();
            var alone = new HashSet<string>();
            for (int sys = 0; sys < pool.Count; sys++)
            {
                string id = GiantWreckPlacer.BodyId("sys" + sys, 0);
                var before = taken.ToList();
                var pick = GiantWreckPlacer.PickDesign(GiantWreckPlacer.Untaken(pool, taken), seed, id)!;
                Assert.True(taken.Add(pick.Key), $"seed {seed}: {id} repeats '{pick.Key}' although a design was free");
                Assert.Same(pick, GiantWreckPlacer.PickDesign(GiantWreckPlacer.Untaken(pool, before), seed, id)); // stable
                repeatsBefore += alone.Add(GiantWreckPlacer.PickDesign(pool, seed, id)!.Key) ? 0 : 1;
            }

            // Once every design is in the world, a sixth wreck may be any of them again.
            Assert.NotNull(GiantWreckPlacer.PickDesign(GiantWreckPlacer.Untaken(pool, taken), seed, GiantWreckPlacer.BodyId("sys9", 0)));
        }

        Assert.True(repeatsBefore > 300, "the old independent pick repeated a design in most worlds with five wrecks");

        // A pinned ship counts as taken, and nothing taken leaves the pool as it is.
        Assert.Same(pool, GiantWreckPlacer.Untaken(pool, null));
        Assert.Same(pool, GiantWreckPlacer.Untaken(pool, new List<string>()));
        Assert.Equal(new[] { "battleship", "liner", "ark", "squid" }, GiantWreckPlacer.Untaken(pool, new[] { "carrier" }).Select(t => t.Key));
        Assert.Same(pool, GiantWreckPlacer.Untaken(pool, pool.Select(t => t.Key).ToList())); // none left: all of them again

        // The weights still decide among the free designs: with the carrier taken, the two heavy ships lead.
        var counts = new Dictionary<string, int>();
        var free = GiantWreckPlacer.Untaken(pool, new[] { "carrier" });
        for (int i = 0; i < 1600; i++)
        {
            string key = GiantWreckPlacer.PickDesign(free, 77, GiantWreckPlacer.BodyId("sys" + (i % 40), i / 40))!.Key;
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        Assert.All(new[] { "battleship", "liner" }, k => Assert.InRange(counts[k], 450, 750)); // 3 of 8 ≈ 600
        Assert.All(new[] { "ark", "squid" }, k => Assert.InRange(counts[k], 120, 290));        // 1 of 8 ≈ 200
    }

    // ---------------- in flight ----------------

    /// <summary>The largest giant ship (256 a side) beside three of the largest derelicts, the NPC station hulls ahead, The Long
    /// Quiet, the planets and the space wreck in the flight plane, and a second giant behind the first: no two boxes touch.</summary>
    [Fact]
    public void FlightSpot_ClearsEveryOtherHull_EvenForTheLargestShip()
    {
        var half = new Vector3f(128f, 128f, 128f);
        var derelicts = new List<(Vector3f Centre, Vector3f Half)>();
        var biggest = DerelictGenerator.Generate(7, _content);
        for (int i = 0; i < 3; i++)
        {
            derelicts.Add((SvGameServer.DerelictSpacePosition(i, biggest), new Vector3f(biggest.Width / 2f, biggest.Height / 2f, biggest.Length / 2f)));
        }

        var first = SvGameServer.GiantWreckFlightPosition(half, derelicts);
        var second = SvGameServer.GiantWreckFlightPosition(half, derelicts.Append((first, half)).ToList());
        var others = new List<(Vector3f Centre, Vector3f Half)>(derelicts)
        {
            (new Vector3f(74f, 8f, -52f), new Vector3f(8f, 6f, 6f)), // The Long Quiet
            (new Vector3f(0f, 0f, 0f), new Vector3f(60f, 60f, 60f)), // the launch point with room to fly
            (new Vector3f(0f, 0f, 0f), new Vector3f(5000f, 45f, 5000f)), // the flight plane: planet spheres (r ≤ 41), the space wreck
        };
        var stationHalves = Enumerable.Range(0, 6).Select(_ => new Vector3f(40f, 40f, 40f)).ToList();
        others.AddRange(SvGameServer.LayoutHullCentres(stationHalves).Select((c, i) => (c, stationHalves[i])));

        foreach (var giant in new[] { first, second })
        {
            Assert.True(giant.Y - half.Y >= 44f, $"the floor {giant.Y - half.Y} clears every planet sphere");
            Assert.All(others, o => Assert.False(Overlap(giant, half, o.Centre, o.Half), $"the giant at {giant} touches a box at {o.Centre}"));
        }

        Assert.False(Overlap(first, half, second, half), "two giants of one system never touch");
    }

    private static bool Overlap(Vector3f a, Vector3f ha, Vector3f b, Vector3f hb)
        => Math.Abs(a.X - b.X) < ha.X + hb.X && Math.Abs(a.Y - b.Y) < ha.Y + hb.Y && Math.Abs(a.Z - b.Z) < ha.Z + hb.Z;

    // ---------------- ship mode ----------------

    [Fact]
    public void ShipMode_ReadsTheRoomsFromTheDoorways_HangsDoors_LightsTheClosedRooms_AndPlacesOneBay()
    {
        var t = GiantDerelictTestShips.Liner();
        var lightOff = _content.GetBlock("light_white_off")!.NumericId.Value;
        var red = _content.GetBlock("light_red")!.NumericId.Value;
        var bay = _content.GetBlock("guardian_spawner")!.NumericId.Value;
        bool sawClosed = false, sawTorn = false;
        for (long seed = 1; seed <= 12; seed++)
        {
            var space = GiantShipGenerator.BuildSpace(t, seed, _content);
            var wreck = DerelictGenerator.GenerateShip(space, seed, _content, alien: false);
            var l = wreck.Layout;
            Assert.Equal(DerelictGenerator.GiantSourcePrefix + t.Key, l.Source);
            Assert.Equal(4, l.RoomCount); // the four rooms behind the doorways (the first one is open: the way in)
            Assert.Equal(l.RoomCount, l.SealedRoomCount + l.BreachedRoomCount);
            Assert.True(l.BreachedRoomCount >= 1, "a wreck that big is open somewhere");
            sawClosed |= l.SealedRoomCount > 0;
            sawTorn |= l.BreachedRoomCount > 0;
            Assert.InRange(l.Doors.Count, l.SealedRoomCount > 0 ? 1 : 0, 4);
            Assert.All(l.Doors, d => Assert.False(d.AxisX, "an x-plane bulkhead's leaf runs along z"));
            Assert.Empty(l.CrateSpots);
            Assert.Empty(l.TraderPosts);

            // One bay, on a floor, never on a chest.
            int bays = 0;
            for (int x = 0; x < l.Width; x++)
                for (int y = 0; y < l.Height; y++)
                    for (int z = 0; z < l.Length; z++)
                    {
                        bays += l.Get(x, y, z) == bay ? 1 : 0;
                    }

            Assert.Equal(1, bays);
            var spot = l.SpawnerSpot!.Value;
            Assert.NotEqual(0, l.Get(spot.X, spot.Y - 1, spot.Z));
            Assert.DoesNotContain(wreck.Chests, c => c.LocalPos == spot);

            // Lights: dead in the open, red emergency lamps in the closed rooms only.
            Assert.True(CountBlocks(l, lightOff) > 0, "the lamps in the open rooms are dead");
            Assert.Equal(l.SealedRoomCount > 0, CountBlocks(l, red) > 0);

            // The arrival is the entrance, standing on a floor; the chests and the sound survive, their cells free.
            Assert.Equal(GiantDerelictTestShips.Entrance, l.Arrival);
            Assert.Equal(0, l.Get(l.Arrival.X, l.Arrival.Y, l.Arrival.Z));
            Assert.Equal(0, l.Get(l.Arrival.X, l.Arrival.Y + 1, l.Arrival.Z));
            Assert.NotEqual(0, l.Get(l.Arrival.X, l.Arrival.Y - 1, l.Arrival.Z));
            Assert.Equal(3, wreck.Chests.Count);
            Assert.All(wreck.Chests, c => Assert.Equal(0, l.Get(c.LocalPos.X, c.LocalPos.Y, c.LocalPos.Z)));
            Assert.Single(wreck.Sounds);
        }

        Assert.True(sawClosed && sawTorn, "over a dozen seeds some rooms stay closed and some are torn open");
    }

    [Fact]
    public void ShipMode_IsSeedStable_AndKeepsTheSamplesChestsAndArrival()
    {
        var sample = _content.GetGiantShip("sample_hulk")!;
        foreach (var t in new[] { sample, GiantDerelictTestShips.Liner() })
        {
            var a = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, 99, _content), 99, _content, alien: false);
            var b = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, 99, _content), 99, _content, alien: false);
            Assert.Equal(Fingerprint(a.Layout), Fingerprint(b.Layout));
            Assert.Equal(a.Layout.Doors.Select(d => (d.Cell, d.AxisX)), b.Layout.Doors.Select(d => (d.Cell, d.AxisX)));
            Assert.Equal(a.Layout.SpawnerSpot, b.Layout.SpawnerSpot);
        }

        var w = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(sample, 3, _content), 3, _content, alien: false);
        Assert.Equal(2, w.Chests.Count);
        var arrival = w.Layout.Arrival;
        Assert.Equal(0, w.Layout.Get(arrival.X, arrival.Y, arrival.Z));
        Assert.NotEqual(0, w.Layout.Get(arrival.X, arrival.Y - 1, arrival.Z));
    }

    /// <summary>The process-wide build cache serves the very wreck it built for the same ship, seed and content — also to another
    /// load of the same content — and that wreck is exactly what a fresh build makes; another seed is another key.</summary>
    [Fact]
    public void TheBuildCache_ServesTheSameWreck_ForTheSameShipSeedAndContent()
    {
        var t = GiantDerelictTestShips.Liner(w: 93); // a ship of its own: no other test shares its cache key
        var other = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var a = BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.Build(t, 4242, _content);
        var b = BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.Build(t, 4242, other);
        Assert.Same(a, b);
        Assert.NotEqual(BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.KeyFor(t, 4242, _content),
            BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.KeyFor(t, 4243, _content));
        var fresh = DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, 4242, _content), 4242, _content, alien: false);
        Assert.Equal(Fingerprint(fresh.Layout), Fingerprint(a.Layout));
        Assert.Equal(fresh.Layout.Doors.Select(d => (d.Cell, d.AxisX)), a.Layout.Doors.Select(d => (d.Cell, d.AxisX)));
    }

    /// <summary>The base hash the server pins: the same for the same ship and seed, another for another wreck.</summary>
    [Fact]
    public void TheBaseHash_IsStable_AndFollowsTheWreck()
    {
        var t = GiantDerelictTestShips.Liner();
        GiantDerelictLayout Make(long seed) => DerelictGenerator.GenerateShip(GiantShipGenerator.BuildSpace(t, seed, _content), seed, _content, alien: false);
        var a = Make(5);
        Assert.Matches("^[0-9a-f]{16}$", a.BaseHash);
        Assert.Equal(a.BaseHash, Make(5).BaseHash);
        var hashes = Enumerable.Range(6, 6).Select(s => Make(s)).Where(w => Fingerprint(w.Layout) != Fingerprint(a.Layout)).Select(w => w.BaseHash).ToList();
        Assert.NotEmpty(hashes);
        Assert.DoesNotContain(a.BaseHash, hashes);
    }

    private static int CountBlocks(DerelictLayout l, ushort block)
    {
        int n = 0;
        for (int x = 0; x < l.Width; x++)
            for (int y = 0; y < l.Height; y++)
                for (int z = 0; z < l.Length; z++)
                {
                    n += l.Get(x, y, z) == block ? 1 : 0;
                }

        return n;
    }

    private static string Fingerprint(DerelictLayout l)
    {
        ulong h = 1469598103934665603UL;
        for (int x = 0; x < l.Width; x++)
            for (int y = 0; y < l.Height; y++)
                for (int z = 0; z < l.Length; z++)
                {
                    var (tint, glow) = l.GetModifier(x, y, z);
                    h = unchecked((h ^ (ulong)(l.Get(x, y, z) + (tint * 31) + (glow * 17) + (l.GetShape(x, y, z) * 7))) * 1099511628211UL);
                }

        return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
