// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// Fauna vs. player builds — Lyxette's third round (2026-08-27/29): a sleeping titan stayed embedded in a
/// walkway built around it and lay half inside a parked ship (#1320), wildlife spawned inside sealed base
/// rooms (#1314), and one species filled the whole world cap around a swamp base (#1325).
/// </summary>
public sealed class CreatureBuildRespectTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureBuildRespectTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fauna_builds_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string name, out SqliteWorldRepository repo, bool ship = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna → a full species roster
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = ship,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- arena helpers (a stone pad floating above every natural feature) ----------------

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
                max = System.Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        return max;
    }

    private void Fill(SvGameServer server, int x0, int y0, int z0, int x1, int y1, int z1, string key = "stone")
    {
        var id = key == "air" ? BlocksBeyondTheStars.Shared.Primitives.BlockId.Air : _content.GetBlock(key)!.NumericId;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), id);
                }
    }

    private void BuildPad(SvGameServer server, int cx, int cz, int r, int padY)
        => Fill(server, cx - r, padY, cz - r, cx + r, padY, cz + r);

    /// <summary>Roster slot <paramref name="slot"/> forced into a known body: a small plain land walker, or a
    /// size-4 titan (collision body 8 cells, footprint radius 2). Solitary, passive, and — when
    /// <paramref name="nocturnal"/> — asleep at the noon the tests pin the clock to.</summary>
    private static CreatureSpecies Force(SvGameServer server, int slot, bool titan, bool nocturnal)
    {
        var sp = server.SpeciesRoster[slot];
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = CreatureTemperament.Passive;
        sp.Activity = nocturnal ? CreatureActivity.Nocturnal : CreatureActivity.Cathemeral;
        sp.BodyPlan = titan ? CreatureBodyPlan.Titan : CreatureBodyPlan.Standard;
        sp.Size = titan ? 4f : 1f;
        sp.SocialGroupSize = 1;
        return sp;
    }

    private static bool ColumnClear(SvGameServer server, Vector3f at, int cells)
    {
        int x = (int)System.Math.Floor(at.X), y = (int)System.Math.Floor(at.Y), z = (int)System.Math.Floor(at.Z);
        for (int dy = 0; dy < cells; dy++)
        {
            if (!server.World.GetBlock(new Vector3i(x, y + dy, z)).IsAir)
            {
                return false;
            }
        }

        return true;
    }

    private static void Ticks(SvGameServer server, int n, double dt = 0.2)
    {
        for (int i = 0; i < n; i++)
        {
            server.TickForTest(dt);
        }
    }

    // ---------------- #1325: no monoculture ----------------

    [Fact]
    public void BiomePass_StarvesWithASingleNative_NotWithTwoOrAnAgnosticSpecies()
    {
        var a = new CreatureSpecies { Id = "a", BiomeAffinity = 0 };
        var b = new CreatureSpecies { Id = "b", BiomeAffinity = 1 };
        var c = new CreatureSpecies { Id = "c", BiomeAffinity = 1 };
        var any = new CreatureSpecies { Id = "any", BiomeAffinity = -1 };

        Assert.True(CreatureBehaviour.BiomePassStarved(new[] { a, b, c }, biome: 0), "one native → starved");
        Assert.False(CreatureBehaviour.BiomePassStarved(new[] { a, b, c }, biome: 1), "two natives → fine");
        Assert.False(CreatureBehaviour.BiomePassStarved(new[] { a, b, any }, biome: 0), "a native + an agnostic → fine");
        Assert.True(CreatureBehaviour.BiomePassStarved(new[] { b, c }, biome: 0), "no native at all → starved");
    }

    [Fact]
    public void ASpeciesAtItsShare_StopsSpawning_AndOtherSpeciesFillTheCap()
    {
        // Lyxette's base: 6 → 12 → 36 animals within days, every one of them the same titan herd species,
        // because nothing ever bounded one species' slice of the world cap.
        var server = Started("share", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            var sp0 = Force(server, 0, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5); // noon: the planted nocturnals sleep in place

            const int cx = 0, cz = 0;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 8, padY);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);

            int share = server.SpeciesShareForTest();
            Assert.True(share >= 3, $"a herd must always be possible (share {share})");
            for (int i = 0; i < share; i++)
            {
                server.SpawnCreatureAtForTest(new Vector3f(cx - 4 + i % 8 + 0.5f, padY + 1, cz + 2 + i / 8 + 0.5f));
            }

            for (int i = 0; i < 30; i++)
            {
                server.Tick(2.0); // thirty spawn attempts
            }

            var wild = server.Creatures.Where(c => !c.IsCompanion).ToList();
            int ofSp0 = wild.Count(c => c.SpeciesId == sp0.Id);
            Assert.Equal(share, ofSp0);
            Assert.True(wild.Count > share,
                $"other species must keep filling the cap once one holds its share (total {wild.Count}, share {share})");
        }
    }

    [Fact]
    public void AHerd_StopsAddingMembersAtTheSpeciesShare()
    {
        var server = Started("herdshare", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            var sp0 = Force(server, 0, titan: false, nocturnal: true);
            sp0.SocialGroupSize = 5;
            server.SetDayFractionForTest(0.5);

            const int cx = 0, cz = 0;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 8, padY);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);

            int share = server.SpeciesShareForTest();
            for (int i = 0; i < share - 1; i++)
            {
                server.SpawnCreatureAtForTest(new Vector3f(cx - 4 + i % 8 + 0.5f, padY + 1, cz + 2 + i / 8 + 0.5f));
            }

            for (int i = 0; i < 40; i++)
            {
                server.Tick(2.0);
                int ofSp0 = server.Creatures.Count(c => !c.IsCompanion && c.SpeciesId == sp0.Id);
                Assert.True(ofSp0 <= share, $"a herd spawned past the species share ({ofSp0} > {share})");
            }
        }
    }

    [Fact]
    public void AnOverShareSpecies_ShedsItsOutOfSightMembers_AndKeepsThoseInView()
    {
        var server = Started("crowd", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Settler");
            p.State.AboardShip = false;
            var sp0 = Force(server, 0, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            const int cx = 0, cz = 0;
            int padY = MaxTopY(server, cx, cz, 60) + 8;
            BuildPad(server, cx, cz, 6, padY);
            BuildPad(server, cx + 50, cz, 6, padY); // 50 blocks out: out of sight (40), inside the despawn leash (70)
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);

            int share = server.SpeciesShareForTest();
            string nearA = server.SpawnCreatureAtForTest(new Vector3f(cx + 2.5f, padY + 1, cz + 0.5f));
            string nearB = server.SpawnCreatureAtForTest(new Vector3f(cx - 2.5f, padY + 1, cz + 0.5f));
            for (int i = 0; i < share + 5; i++)
            {
                server.SpawnCreatureAtForTest(new Vector3f(cx + 50 - 4 + i % 8 + 0.5f, padY + 1, cz - 4 + i / 8 + 0.5f));
            }

            Ticks(server, 3);

            var ofSp0 = server.Creatures.Where(c => !c.IsCompanion && c.SpeciesId == sp0.Id).ToList();
            Assert.Equal(share, ofSp0.Count);
            Assert.Contains(ofSp0, c => c.Id == nearA);
            Assert.Contains(ofSp0, c => c.Id == nearB);
        }
    }

    // ---------------- #1314: sealed base rooms ----------------

    [Fact]
    public void NothingSpawnsInsideASealedBaseRoom_TheSpawnerConsultsTheAirFill()
    {
        // The same room geometry as the sealed-room air tests: a stone shell with an energy door facing a
        // base core; the interior beyond the radius-8 cube breathes only through the sealed-room fill.
        var server = Started("sealed", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            Force(server, 0, titan: false, nocturnal: false);

            int oy = MaxTopY(server, 10, 0, 24) + 10; // mid-air, clear of every natural feature
            int coreX = 1, coreZ = 0;
            p.State.Position = new Vector3f(coreX - 1, oy, coreZ);
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock("Builder", coreX, oy, coreZ, "base_core");
            int baseId = server.BaseSnapshots.Single().Id;

            // Shell x[5..15] y[oy-1..oy+3] z[-3..3], interior hollow, a 1×3 doorway at x=5 facing the core.
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int x = 5; x <= 15; x++)
                for (int y = oy - 1; y <= oy + 3; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        bool interior = x > 5 && x < 15 && y > oy - 1 && y < oy + 3 && z > -3 && z < 3;
                        bool doorway = x == 5 && z == 0 && y >= oy && y <= oy + 2;
                        if (!interior && !doorway)
                        {
                            server.World.SetBlock(new Vector3i(x, y, z), stone);
                        }
                    }

            p.State.Inventory.Add("door_energy", 2, 16);
            p.State.Position = new Vector3f(4, oy, 0.5f);
            server.PlaceBlock("Builder", 5, oy, 0, "door_energy");

            var inside = new Vector3f(13.5f, oy, 0.5f); // beyond the cube — supplied only by the sealed fill
            for (int i = 0; i < 6; i++)
            {
                p.State.Position = inside;
                server.TickForTest(0.5); // covers the 1.5 s recompute interval
            }

            Assert.True(server.BaseAirForTest(baseId).Cells > 0, "the room must read as sealed for this test to mean anything");

            Assert.False(server.SpawnSpotClearForTest(inside), "a spawn candidate inside the sealed room must be rejected");
            Assert.True(server.SpawnSpotClearForTest(new Vector3f(24.5f, oy, 0.5f)), "open air beside the room stays spawnable");

            // Knock a hole in the roof: the pocket leaks, the fill empties, and the spot is spawnable again.
            server.World.SetBlock(new Vector3i(13, oy + 3, 0), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            for (int i = 0; i < 6; i++)
            {
                p.State.Position = inside;
                server.TickForTest(0.5);
            }

            Assert.True(server.SpawnSpotClearForTest(inside), "an unsealed room is ordinary terrain to the spawner");
        }
    }

    // ---------------- #1320: sleepers vs walls, floors and hulls ----------------

    [Fact]
    public void ASleeperWalledIn_WakesAndStepsClear()
    {
        var server = Started("walledin", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Mason");
            p.State.AboardShip = false;
            Force(server, 0, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            const int cx = 40, cz = 40;
            int padY = MaxTopY(server, cx, cz, 10) + 8;
            BuildPad(server, cx, cz, 8, padY);
            p.State.Position = new Vector3f(cx + 10, padY + 1, cz); // beyond wake distance, inside the leash

            var planted = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            string id = server.SpawnCreatureAtForTest(planted);
            Ticks(server, 2);
            var asleep = server.Creatures.Single(c => c.Id == id);
            Assert.Equal(0.0, asleep.AwakeOverrideTimer);
            Assert.Equal(planted.X, asleep.Position.X);

            // Build a wall straight through the sleeper (feet + head cells).
            Fill(server, cx, padY + 1, cz - 3, cx, padY + 2, cz + 3);
            Ticks(server, 10);

            var live = server.Creatures.Single(c => c.Id == id);
            Assert.True(live.AwakeOverrideTimer > 0, "a walled-in sleeper must be roused");
            Assert.True(ColumnClear(server, live.Position, 2),
                $"its body must be clear of the wall (at {live.Position.X:F1}/{live.Position.Y:F1}/{live.Position.Z:F1})");
            Assert.True(System.Math.Abs(live.Position.X - planted.X) >= 0.9f || System.Math.Abs(live.Position.Z - planted.Z) >= 0.9f,
                "it must have stepped aside, not stayed inside the masonry");
        }
    }

    [Fact]
    public void ASleeperBoxedInOnEverySide_Despawns()
    {
        var server = Started("boxedin", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Mason");
            p.State.AboardShip = false;
            Force(server, 0, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            // A tiny 5×5 pad: beyond it there is no floor within the vertical relocation scan (the natural
            // terrain lies 8+ below), so filling the pad's own columns solid boxes the sleeper in completely.
            const int cx = -40, cz = 40;
            int padY = MaxTopY(server, cx, cz, 10) + 8;
            BuildPad(server, cx, cz, 2, padY);
            p.State.Position = new Vector3f(cx + 12, padY + 1, cz);

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
            Ticks(server, 2);

            Fill(server, cx - 2, padY + 1, cz - 2, cx + 2, padY + 8, cz + 2); // solid stone, 8 high
            Ticks(server, 40);

            Assert.DoesNotContain(server.Creatures, c => c.Id == id);
        }
    }

    [Fact]
    public void ASleepingTitanUnderAFloorSlab_EndsUpWithItsBodyClear()
    {
        // Lyxette's walkway: concrete two blocks above a sleeping titan's feet. The two-cell feet probe
        // called the hollow "standable" and held the ten-block body inside the floor above.
        var server = Started("titanslab", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Mason");
            p.State.AboardShip = false;
            Force(server, 0, titan: true, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            const int cx = -60, cz = -60;
            int padY = MaxTopY(server, cx, cz, 12) + 8;
            BuildPad(server, cx, cz, 10, padY);
            p.State.Position = new Vector3f(cx + 12, padY + 1, cz);

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f));
            Ticks(server, 2);

            Fill(server, cx - 2, padY + 3, cz - 2, cx + 2, padY + 3, cz + 2); // the slab, two cells over its feet
            Ticks(server, 40);

            var live = server.Creatures.Single(c => c.Id == id);
            Assert.True(ColumnClear(server, live.Position, 8),
                $"the titan's body column must be clear (at {live.Position.X:F1}/{live.Position.Y:F1}/{live.Position.Z:F1}, slab at y={padY + 3})");
        }
    }

    [Fact]
    public void ACreatureWhosePillarIsRemoved_LandsOnTheRealFloor_NotTheNoiseSurface()
    {
        // The ±6 feet probe fell back to the generator's surface — the PRE-excavation height — so a
        // creature over a fresh 8-block drop floated (or, here, fell through the pad to the terrain below).
        var server = Started("pillar", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            p.State.AboardShip = false;
            Force(server, 0, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            const int cx = 80, cz = -80;
            int padY = MaxTopY(server, cx, cz, 8) + 8;
            BuildPad(server, cx, cz, 6, padY);
            Fill(server, cx, padY + 1, cz, cx, padY + 8, cz); // an 8-block pillar
            p.State.Position = new Vector3f(cx + 6, padY + 1, cz);

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 9, cz + 0.5f));
            Ticks(server, 2);
            Assert.Equal(padY + 9, server.Creatures.Single(c => c.Id == id).Position.Y, 1);

            Fill(server, cx, padY + 1, cz, cx, padY + 8, cz, "air"); // knock the pillar away
            Ticks(server, 10);

            var live = server.Creatures.Single(c => c.Id == id);
            Assert.True(System.Math.Abs(live.Position.Y - (padY + 1)) <= 1.2f,
                $"it must come to rest on the real pad floor (Y {live.Position.Y:F1}, pad feet {padY + 1})");
        }
    }

    [Fact]
    public void ASleepingTitanBesideTheHull_IsPushedClear_ASmallAnimalIsNot()
    {
        // "Elephant im Kühlschrank": a herd asleep with its centres a block outside the hull and its bodies
        // filling the cabin — the point-in-box guard never saw it.
        var server = Started("hull", out var repo, ship: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host"); // parks the ship
            var titan = Force(server, 0, titan: true, nocturnal: true);
            var small = Force(server, 1, titan: false, nocturnal: true);
            server.SetDayFractionForTest(0.5);

            var (origin, size) = server.LandedShipBoundsForTest("Host");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(origin.X + size.X + 14f, origin.Y, origin.Z + size.Z / 2f);

            // One block outside the -X face, halfway along the hull.
            var beside = new Vector3f(origin.X - 1f, origin.Y, origin.Z + size.Z / 2f + 0.5f);
            string big = server.SpawnCreatureAtForTest(beside, titan.Id);
            string tiny = server.SpawnCreatureAtForTest(new Vector3f(beside.X, beside.Y, beside.Z + 3f), small.Id);

            Ticks(server, 5);

            var t = server.Creatures.Single(c => c.Id == big);
            float margin = 0.5f + titan.Size * 0.5f;
            bool bodyInHull = t.Position.X - origin.X >= -margin && t.Position.X - origin.X <= size.X + margin
                && t.Position.Z - origin.Z >= -margin && t.Position.Z - origin.Z <= size.Z + margin;
            Assert.False(bodyInHull, $"the titan's body must be pushed clear of the hull (at dx={t.Position.X - origin.X:F2})");

            var s = server.Creatures.Single(c => c.Id == tiny);
            Assert.Equal(beside.X, s.Position.X, 2); // a small animal a block from the hull is nobody's problem
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
