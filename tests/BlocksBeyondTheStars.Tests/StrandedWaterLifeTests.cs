// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "The thing with the fish doesn't quite work yet" (player report, v2026.11.17, after #3476): a fish of the water roster —
/// legless, finned, nocturnal — lay asleep on the player's walkway, out of any water, in plain view. Three paths put a water
/// animal on dry land and kept it there; these tests pin each one:
/// <list type="bullet">
/// <item>#3551: a swimmer's height looked for real water only downward from the generator surface, so a pond standing
/// ABOVE the terrain the world was born with (a pool on a platform, a moat dug into a filled-up lake) was invisible and
/// the fish was set on the ground under it — or inside the fill.</item>
/// <item>#3551: a body touching a block was rescued to the nearest DRY standable cell — water is never standable, so for
/// a fish that was always land. It is moved into water that holds its body now, or removed when there is none.</item>
/// <item>#3552: a fish already on dry land kept "its legacy freedom": it roamed over land, slept there, and only a
/// player-built floor ever sent it away. It only ever steps back into water now, never sleeps ashore, and leaves while
/// nobody looks, wherever it lies.</item>
/// </list>
/// </summary>
public sealed class StrandedWaterLifeTests : IDisposable
{
    private const int MinDepth = 3;
    private const int MaxDepth = 5;

    private readonly string _root;
    private readonly GameContent _content;

    public StrandedWaterLifeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stranded_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stranded"));
        var config = new ServerConfig
        {
            WorldName = "stranded",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Roster slot 0 as the reported fish: water life, legless, finned, passive, solitary, two cells tall.
    /// Awake around the clock, or nocturnal with <paramref name="nocturnal"/> (pin the local noon to see it asleep).</summary>
    private static CreatureSpecies Fish(SvGameServer server, bool nocturnal = false)
    {
        var sp = server.SpeciesRoster[0];
        sp.Habitat = CreatureHabitat.Water;
        sp.Legs = 0;
        sp.HasFins = true;
        sp.LocoStyle = LocomotionStyle.Schooler;
        sp.Temperament = CreatureTemperament.Passive;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HoverAltitude = 0f;
        sp.Activity = nocturnal ? CreatureActivity.Nocturnal : CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false;
        return sp;
    }

    private sealed record Lake(int X, int Z, int Top, int Bed);

    /// <summary>A generated lake column whose 7×7 neighbourhood is the same water body, searched from the generator alone so
    /// the test follows any change to terrain generation.</summary>
    private static Lake FindLake(SvGameServer server)
    {
        for (int x = -1536; x <= 1536; x += 24)
        {
            for (int z = -1536; z <= 1536; z += 24)
            {
                if (server.WaterSurfaceForTest(x, z) is not { } w || w.Top - w.Bed < MinDepth || w.Top - w.Bed > MaxDepth)
                {
                    continue;
                }

                bool wide = true;
                for (int dx = -3; dx <= 3 && wide; dx++)
                {
                    for (int dz = -3; dz <= 3 && wide; dz++)
                    {
                        wide = server.WaterSurfaceForTest(x + dx, z + dz) is { } n && n.Top == w.Top && n.Top - n.Bed >= MinDepth;
                    }
                }

                if (wide)
                {
                    return new Lake(x, z, w.Top, w.Bed);
                }
            }
        }

        throw new InvalidOperationException("no generated lake of the wanted size on this world");
    }

    /// <summary>A spot whose 41×41 surroundings hold no generated water at all — dry land, so nothing a test builds there
    /// stands over a lake the swim probe would find underneath.</summary>
    private static (int X, int Z) FindDryLand(SvGameServer server)
    {
        for (int x = 64; x <= 1536; x += 48)
        {
            for (int z = 64; z <= 1536; z += 48)
            {
                bool dry = true;
                for (int dx = -20; dx <= 20 && dry; dx += 4)
                {
                    for (int dz = -20; dz <= 20 && dry; dz += 4)
                    {
                        dry = server.WaterSurfaceForTest(x + dx, z + dz) is null;
                    }
                }

                if (dry)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("no dry land on this world");
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

    private static PlayerSession OnFoot(SvGameServer server, Vector3f at, float yaw = 0f)
    {
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = at;
        p.State.Yaw = yaw;
        return p;
    }

    private static Vector3i Cell(Vector3f p)
        => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    /// <summary>In water: the cell is water, or a plant with water standing above it (seagrass on a lake bed is part of the
    /// lake for every swim probe).</summary>
    private bool InWater(SvGameServer server, Vector3f p)
    {
        var water = _content.GetBlock("water")!.NumericId.Value;
        var cell = Cell(p);
        for (int dy = 0; dy < 16; dy++)
        {
            var b = server.World.GetBlock(new Vector3i(cell.X, cell.Y + dy, cell.Z));
            if (b.Value == water)
            {
                return true;
            }

            if (b.IsAir || _content.Blocks.Values.FirstOrDefault(d => d.NumericId.Value == b.Value) is not { } def
                || !def.Key.StartsWith("flora_", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Clear water around a lake column: every plant in its water (seagrass on the bed) becomes water, so the only
    /// cells an animal could stand on are the shore and whatever the test builds — a planted bed is "standable" between
    /// the stalks, and a rescue that picked it would land in water by luck.</summary>
    private void ClearWater(SvGameServer server, Lake lake)
    {
        var water = _content.GetBlock("water")!.NumericId;
        for (int dx = -7; dx <= 7; dx++)
        {
            for (int dz = -7; dz <= 7; dz++)
            {
                for (int y = lake.Top; y > lake.Bed - 6; y--)
                {
                    var at = new Vector3i(lake.X + dx, y, lake.Z + dz);
                    var b = server.World.GetBlock(at);
                    if (b.IsAir || _content.Blocks.Values.FirstOrDefault(d => d.NumericId.Value == b.Value) is not { } def)
                    {
                        break;
                    }

                    if (def.Key.StartsWith("flora_", StringComparison.Ordinal))
                    {
                        server.World.SetBlock(at, water);
                    }
                    else if (b.Value != water.Value)
                    {
                        break; // the bed (or the shore): stop at the first solid cell
                    }
                }
            }
        }
    }

    /// <summary>A stone basin floating well above every natural feature, a 7×7 pond inside a one-block stone rim, the water
    /// <paramref name="depth"/> cells deep. Its water stands far above the generator's surface, as a pool on a platform or
    /// on a lake the player filled up does. Written without an owner, so no player-floor rule speaks. Returns the bed Y.</summary>
    private int RaisedPond(SvGameServer server, int cx, int cz, int depth)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        int bed = MaxTopY(server, cx, cz, 6) + 10;
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dz = -4; dz <= 4; dz++)
            {
                bool rim = Math.Abs(dx) == 4 || Math.Abs(dz) == 4;
                server.World.SetBlock(new Vector3i(cx + dx, bed, cz + dz), stone);
                for (int y = bed + 1; y <= bed + depth; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), rim ? stone : water);
                }
            }
        }

        return bed;
    }

    /// <summary>A natural (owner-less) stone pad floating above everything — dry ground no player built.</summary>
    private int DryPad(SvGameServer server, int cx, int cz, int r)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int padY = MaxTopY(server, cx, cz, r) + 8;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    // ---------------- #3551: the water it lives in is found wherever it stands ----------------

    /// <summary>A fish in a pond that stands above the generator's terrain stays in its water, tick after tick, awake or
    /// asleep. The height probe looked only downward from the generator surface, missed the pond and set the fish on the
    /// ground under it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFish_InAPondAboveTheGeneratedTerrain_StaysInItsWater(bool asleep)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server, nocturnal: asleep);
            var (cx, cz) = FindDryLand(server);
            server.SetLocalDayFractionForTest(0.5, cx); // noon here: a nocturnal fish sleeps
            int bed = RaisedPond(server, cx, cz, depth: 3);
            Assert.True(bed > server.SurfaceHeightForTest(cx, cz) + 5); // the pond really stands above the born terrain
            OnFoot(server, new Vector3f(cx + 30.5f, bed + 4, cz + 0.5f)); // creatures tick near a player; out of wake range

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, bed + 2f, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 60f);
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.1);
                var c = Assert.Single(server.Creatures, x => x.Id == id);
                Assert.True(InWater(server, c.Position) && c.Position.Y >= bed + 1 && c.Position.Y < bed + 4,
                    $"tick {i}: the fish left its raised pond (bed {bed}, water {bed + 1}..{bed + 3}) — it is at {c.Position}");
            }

            Assert.Equal(asleep, server.NetCreatureForTest(id).Asleep);
        }
    }

    // ---------------- #3551: a body that touches a block is never rescued onto land ----------------

    /// <summary>The reported scene: a walkway laid straight on the water, over a fish of the reported size — its body is four
    /// cells tall, taller than the water under the deck, so the deck sits in its body. The fish moves into the open water
    /// beside the walkway — never onto the walkway, which is where the dry-land rescue put it (the nearest standable cell).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFish_UnderAWalkwayLaidOnItsWater_MovesIntoOpenWater_NotOntoTheWalkway(bool asleep)
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server, nocturnal: asleep);
            sp.Size = 1.71f; // the reported fish: ceil(1.71 × 1.8) = 4 cells tall
            var lake = FindLake(server);
            server.SetLocalDayFractionForTest(0.5, lake.X);
            ClearWater(server, lake);
            OnFoot(server, new Vector3f(lake.X + 30.5f, lake.Top + 1, lake.Z + 0.5f));
            float swimY = (lake.Bed + 1 + lake.Top) * 0.5f;
            string id = server.SpawnCreatureAtForTest(new Vector3f(lake.X + 0.5f, swimY, lake.Z + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 60f);
            server.TickForTest(0.1);
            Assert.True(InWater(server, Assert.Single(server.Creatures, x => x.Id == id).Position));

            var sand = _content.GetBlock("sandstone")!.NumericId;
            for (int dx = -1; dx <= 1; dx++) // a walkway three wide, laid on the water across the fish
            {
                for (int dz = -6; dz <= 6; dz++)
                {
                    server.World.SetBlock(new Vector3i(lake.X + dx, lake.Top + 1, lake.Z + dz), sand, owner: "builder");
                }
            }

            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
                var c = Assert.Single(server.Creatures, x => x.Id == id);
                Assert.True(InWater(server, c.Position),
                    $"tick {i}: the fish under the walkway was moved to {c.Position} — not into water (walkway at {lake.Top + 1})");
            }
        }
    }

    /// <summary>A deck laid on ALL the water in reach, over the same tall fish: no water holds its body any more, and the
    /// fish is removed — at once, by the rescue, even with the player standing right on the deck looking at it — instead
    /// of being set on the deck. Checked every tick, so the sweep (which only takes what nobody sees) cannot pass it.</summary>
    [Fact]
    public void AFish_UnderADeckOverAllItsWater_IsRemoved_NeverSetOnTheDeck()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            sp.Size = 1.71f; // four cells tall, as reported
            var lake = FindLake(server);
            ClearWater(server, lake);
            float swimY = (lake.Bed + 1 + lake.Top) * 0.5f;
            string id = server.SpawnCreatureAtForTest(new Vector3f(lake.X + 0.5f, swimY, lake.Z + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 60f);
            OnFoot(server, new Vector3f(lake.X + 0.5f, lake.Top + 2, lake.Z + 2.5f), yaw: 180f); // on the deck, facing the fish
            server.TickForTest(0.1);
            Assert.True(InWater(server, Assert.Single(server.Creatures, x => x.Id == id).Position));

            var sand = _content.GetBlock("sandstone")!.NumericId;
            for (int dx = -9; dx <= 9; dx++)
            {
                for (int dz = -9; dz <= 9; dz++)
                {
                    server.World.SetBlock(new Vector3i(lake.X + dx, lake.Top + 1, lake.Z + dz), sand, owner: "builder");
                }
            }

            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(0.1);
                if (server.Creatures.FirstOrDefault(x => x.Id == id) is { } c)
                {
                    Assert.True(InWater(server, c.Position), $"tick {i}: the fish was set on the deck at {c.Position}");
                }
            }

            Assert.DoesNotContain(server.Creatures, x => x.Id == id);
        }
    }

    // ---------------- #3552: a fish on dry land does not live on there ----------------

    /// <summary>A fish lying on dry natural ground goes while nobody looks — not only on a floor a player built — and stays
    /// while someone does. A fish in its lake is never swept.</summary>
    [Fact]
    public void AStrandedFish_OnNaturalGround_LeavesWhileNobodyLooks_AFishInItsLakeStays()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int padY = DryPad(server, cx, cz, 10);
            var ashore = new Vector3f(cx + 0.5f, padY + 1, cz + 5.5f);
            Assert.False(server.OnPlayerBuiltFloorForTest(ashore)); // natural ground: the player-floor rule says nothing

            var p = OnFoot(server, new Vector3f(cx + 0.5f, padY + 1, cz - 7.5f), yaw: 0f); // looking straight at it
            string id = server.SpawnCreatureAtForTest(ashore, sp.Id);
            server.PauseCreatureForTest(id, 30f);
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, x => x.Id == id); // seen: it stays

            p.State.Yaw = 180f; // turned away
            server.SweepBaseIntrudersForTest();
            Assert.DoesNotContain(server.Creatures, x => x.Id == id);

            var lake = FindLake(server);
            p.State.Position = new Vector3f(lake.X + 0.5f, lake.Top + 1, lake.Z - 12.5f);
            p.State.Yaw = 180f; // facing away from the lake
            float swimY = (lake.Bed + 1 + lake.Top) * 0.5f;
            string swimmer = server.SpawnCreatureAtForTest(new Vector3f(lake.X + 0.5f, swimY, lake.Z + 0.5f), sp.Id);
            server.TickForTest(0.1);
            Assert.True(InWater(server, server.Creatures.Single(x => x.Id == swimmer).Position));
            server.SweepBaseIntrudersForTest();
            Assert.Contains(server.Creatures, x => x.Id == swimmer);
        }
    }

    /// <summary>A nocturnal fish ashore by day does not lie down to sleep there — the "Z Z Z" over a fish on the walkway —
    /// while the same fish in its pond sleeps as before.</summary>
    [Fact]
    public void AStrandedFish_DoesNotSleepAshore_InItsPondItDoes()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server, nocturnal: true);
            var (cx, cz) = FindDryLand(server);
            server.SetLocalDayFractionForTest(0.5, cx);
            int bed = RaisedPond(server, cx, cz, depth: 3);
            int padY = DryPad(server, cx + 14, cz, 3);
            OnFoot(server, new Vector3f(cx + 30.5f, padY + 1, cz + 0.5f));
            string swimmer = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, bed + 2f, cz + 0.5f), sp.Id);
            string ashore = server.SpawnCreatureAtForTest(new Vector3f(cx + 14.5f, padY + 1, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(ashore, 30f);

            for (int i = 0; i < 5; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.True(server.NetCreatureForTest(swimmer).Asleep); // control: by day a nocturnal fish sleeps in its pond
            Assert.False(server.NetCreatureForTest(ashore).Asleep);
        }
    }

    /// <summary>A fish on dry land never steps onto more dry land — it does not roam the walkway; it only ever steps back
    /// into water.</summary>
    [Fact]
    public void AStrandedFish_DoesNotStepOntoMoreDryLand()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Fish(server);
            var (cx, cz) = FindDryLand(server);
            int padY = DryPad(server, cx, cz, 6);
            OnFoot(server, new Vector3f(cx + 30.5f, padY + 1, cz + 0.5f));
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);
            server.PauseCreatureForTest(id, 30f);
            server.TickForTest(0.1);

            var c = Assert.Single(server.Creatures, x => x.Id == id);
            Assert.Equal(padY + 1, c.Position.Y, 3); // lying on the pad
            Assert.True(server.TerrainStepBlockedForTest(id, new Vector3f(cx + 1.5f, padY + 1, cz + 0.5f)));
        }
    }
}
