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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The landmark stamper (#3486, with the chests of #3488 and the underground part of #3490): one giant authored building
/// per planet at most, from terrain generation 37, never on an asteroid or an airless world, pinned by a placement
/// record, stamped once, its chests filled once from the author's settings, a mine's tunnels carved and sealed.
/// </summary>
public sealed class LandmarkStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landmark_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    /// <summary>The shipped content plus the given landmarks and a small loot table for the mine's chest.</summary>
    private static GameContent ContentWith(params StructureTemplate[] landmarks)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(landmarks);
        Assert.Equal(landmarks.Length, content.Landmarks.Count);
        var tables = content.LootTables.Values.ToList();
        tables.Add(new LootTableDefinition
        {
            Key = "test_table",
            Rolls = { new LootRoll { Picks = 2, Entries = { new LootEntry { Item = "gold_ingot", Min = 2, Max = 3 } } } },
        });
        content.SetLootTables(tables);
        return content;
    }

    private SvGameServer Start(GameContent content, out SqliteWorldRepository repo, string planet = "meadowlands", long seed = 20261111,
        int generation = WorldDescription.CurrentTerrainGeneration, string? name = null, bool settlements = false,
        double chance = 1.0, bool landmarks = true)
    {
        name ??= $"lm_{planet}_{generation}_{seed}";
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceLandmarks = landmarks,
            LandmarkChance = chance,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The canonical world cell of a structure-local cell (X wrapped into the world, like every stored position).</summary>
    private static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance lm, Vector3i local)
        => WorldConstants.CanonicalBlock(lm.Cell(local), server.World.Circumference);

    private static ushort Id(GameContent content, string key) => content.GetBlock(key)!.NumericId.Value;

    private static IEnumerable<Vector3i> Cells(SvGameServer.LandmarkInstance lm)
    {
        var s = lm.Structure;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    yield return new Vector3i(x, y, z);
                }
            }
        }
    }

    [Fact]
    public void AFreshWorld_CarriesItsLandmark_AsBuilt_PinnedAndStampedOnce()
    {
        var content = ContentWith(LandmarkShapes.Compacted(LandmarkShapes.Tower()));
        var server = Start(content, out var repo, name: "lm_once");
        Vector3i origin, mined, chestCell;
        using (repo)
        {
            var lm = Assert.Single(server.LandmarksForTest);
            Assert.Equal("test_tower", lm.Key);
            Assert.True(lm.Layout.Ruined);
            origin = lm.Origin;

            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.True(rec.Placed);
            Assert.Equal("test_tower", rec.Template);
            Assert.Equal((lm.Origin.X, lm.GroundY, lm.Origin.Z), (rec.X, rec.GroundY, rec.Z));
            Assert.Contains("turns=", rec.KitLayout);
            Assert.Contains("rev=1", rec.KitLayout);
            Assert.Contains(server.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind && r.Requested == 1 && r.Placed == 1);

            // Every block of the layout stands in the world (the chest's crate sits in its marker's free cell).
            var chest = lm.Structure.Markers.Single(m => m.Type == LandmarkRules.ChestMarker);
            chestCell = At(server, lm, chest.LocalPos);
            foreach (var c in Cells(lm))
            {
                ushort want = lm.Structure.Get(c.X, c.Y, c.Z);
                if (want != 0)
                {
                    Assert.Equal(want, server.World.GetBlock(At(server, lm, c)).Value);
                }
            }

            Assert.Equal(Id(content, "wood_crate"), server.World.GetBlock(chestCell).Value);
            var box = Assert.Single(server.Containers, c => c.Position.Equals(chestCell));
            Assert.StartsWith(SvGameServer.LandmarkChestContainerPrefix, box.Id);
            Assert.Equal(new[] { "iron_plate", "gold_ingot" }, box.Items.Select(i => i.Item));
            Assert.InRange(box.Items[0].Count, 2, 4);
            Assert.Equal(1, box.Items[1].Count);

            // The stampers that run afterwards keep clear of it.
            Assert.True(server.OverlapsAnySettlement(lm.Rect.Cx, lm.Rect.Cz));

            // Away from the pad's levelled ground.
            var (padX, _, padZ) = server.LandingPadForTest(0);
            int dx = Math.Abs(WorldConstants.WrapDeltaX(lm.Rect.Cx - padX, server.World.Circumference)), dz = Math.Abs(lm.Rect.Cz - padZ);
            Assert.True(dx > lm.Rect.Hw + 16 || dz > lm.Rect.Hl + 16, "the landmark stands on the landing pad");

            mined = At(server, lm, Cells(lm).First(c => c.Y == lm.Layout.GroundRow + 1 && lm.Structure.Get(c.X, c.Y, c.Z) == Id(content, "stone")));
            server.World.SetBlock(mined, BlockId.Air); // a player mined a stone of the ruin
            server.Stop();
        }

        // Re-entry: the same ruin from its record, nothing written again — the mined stone stays mined, the chest is one.
        var again = Start(content, out var repo2, name: "lm_once");
        using (repo2)
        {
            var lm = Assert.Single(again.LandmarksForTest);
            Assert.Equal(origin, lm.Origin);
            Assert.True(again.World.GetBlock(mined).IsAir, "a mined stone of the ruin came back on re-entry");
            Assert.Single(again.Containers, c => c.Position.Equals(chestCell));
            Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
        }
    }

    [Fact]
    public void TwoWorldsOfTheSameSeed_GetTheSameLandmark_AndAnotherSeedAnotherRuin()
    {
        var content = ContentWith(LandmarkShapes.Tower());
        ulong Print(SvGameServer server)
        {
            var lm = Assert.Single(server.LandmarksForTest);
            ulong h = (ulong)(uint)lm.Origin.X * 31 + (ulong)(uint)lm.Origin.Z;
            foreach (var c in Cells(lm))
            {
                h = (h ^ lm.Structure.Get(c.X, c.Y, c.Z)) * 1099511628211UL;
            }

            return h;
        }

        ulong a, b, other;
        var s1 = Start(content, out var r1, name: "lm_same_a");
        using (r1)
        {
            a = Print(s1);
        }

        var s2 = Start(content, out var r2, name: "lm_same_b");
        using (r2)
        {
            b = Print(s2);
        }

        var s3 = Start(content, out var r3, seed: 4711, name: "lm_other");
        using (r3)
        {
            other = Print(s3);
        }

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void TheRoll_IsAboutEveryThirdPlanet_Deterministic_AndOffAtChanceZero()
    {
        int hits = 0;
        for (int i = 0; i < 4000; i++)
        {
            string body = "sys" + (i / 8) + "-p" + (i % 8);
            bool roll = SvGameServer.LandmarkRolls(20261111, body, LandmarkRules.Chance);
            Assert.Equal(roll, SvGameServer.LandmarkRolls(20261111, body, LandmarkRules.Chance));
            Assert.False(SvGameServer.LandmarkRolls(20261111, body, 0.0));
            hits += roll ? 1 : 0;
        }

        Assert.InRange(hits / 4000.0, 0.30, 0.38);
    }

    [Theory]
    [InlineData("meadowlands", WorldDescription.LandmarkGeneration - 1, true, 1.0)] // a world created before the landmarks
    [InlineData("asteroid", WorldDescription.CurrentTerrainGeneration, true, 1.0)]   // an asteroid, and airless
    [InlineData("meadowlands", WorldDescription.CurrentTerrainGeneration, false, 1.0)] // switched off by the host
    [InlineData("meadowlands", WorldDescription.CurrentTerrainGeneration, true, 0.0)]  // the roll said no
    public void NoLandmark_WhereNoneMayStand(string planet, int generation, bool enabled, double chance)
    {
        var content = ContentWith(LandmarkShapes.Tower());
        var server = Start(content, out var repo, planet, generation: generation, chance: chance, landmarks: enabled,
            name: $"lm_none_{planet}_{generation}_{enabled}_{chance}");
        using (repo)
        {
            Assert.Empty(server.LandmarksForTest);
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind && r.Placed);
            Assert.DoesNotContain(server.Containers, c => c.Id.StartsWith(SvGameServer.LandmarkChestContainerPrefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ALandmarkForInhabitedPlanets_NeedsAnInhabitedSettlement()
    {
        var burger = LandmarkShapes.Tower("test_burger", ruin: false);
        burger.Requires = new List<string> { LandmarkRules.RequiresInhabited };
        var content = ContentWith(burger);

        var empty = Start(content, out var r1, name: "lm_uninhabited");
        using (r1)
        {
            Assert.Equal(0, empty.InhabitedSettlementCount);
            Assert.Empty(empty.LandmarksForTest);
        }

        var lived = Start(content, out var r2, name: "lm_inhabited", settlements: true);
        using (r2)
        {
            if (lived.InhabitedSettlementCount > 0)
            {
                var lm = Assert.Single(lived.LandmarksForTest);
                Assert.False(lm.Layout.Ruined);
            }
            else
            {
                Assert.Empty(lived.LandmarksForTest); // this seed's settlements are all ruins — still the rule
            }
        }
    }

    [Fact]
    public void AMine_StandsWithItsHutOnTheGround_ItsTunnelsCarvedAndSealed_ItsChestFromTheTable()
    {
        var content = ContentWith(LandmarkShapes.Compacted(LandmarkShapes.Mine()));
        var server = Start(content, out var repo, name: "lm_mine");
        using (repo)
        {
            var lm = Assert.Single(server.LandmarksForTest);
            var layout = lm.Layout;
            var s = lm.Structure;
            Assert.Equal(24, layout.GroundRow);
            Assert.Equal(lm.GroundY - layout.GroundRow, lm.Origin.Y);

            // The hut floor lies on the ground: the seat is the surface of the footprint's centre column.
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Contains(rec.Seat, new[] { "flat", "slope" });

            int cavities = 0;
            foreach (var c in Cells(lm))
            {
                var world = At(server, lm, c);
                ushort want = s.Get(c.X, c.Y, c.Z);
                if (want != 0)
                {
                    Assert.Equal(want, server.World.GetBlock(world).Value);
                    continue;
                }

                if (!layout.IsCavity(c.X, c.Y, c.Z))
                {
                    continue;
                }

                cavities++;
                bool chest = server.Containers.Any(k => k.Position.Equals(world));
                Assert.True(chest || server.World.GetBlock(world).IsAir, $"cavity {c} is not open");

                if (c.Y >= layout.GroundRow)
                {
                    continue; // the shaft's mouth in the hut floor opens into the hut — that is the way in
                }

                // Sealed: beside a cavity under the ground lies the landmark itself or something solid that does not run.
                foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                {
                    int nx = c.X + dx, ny = c.Y + dy, nz = c.Z + dz;
                    if (s.InBounds(nx, ny, nz) && (s.Get(nx, ny, nz) != 0 || layout.IsCavity(nx, ny, nz)))
                    {
                        continue;
                    }

                    var def = content.BlockById(server.World.GetBlock(At(server, lm, new Vector3i(nx, ny, nz))));
                    Assert.True(def is { Solid: true, Liquid: false, Granular: false }, $"cavity {c} opens into {def?.Key ?? "air"}");
                }
            }

            Assert.True(cavities > 800, $"only {cavities} cavity cells survived the cave-ins");

            // The way in: the shaft's mouth in the hut floor is open down to the tunnel's level.
            for (int y = layout.GroundRow; y >= layout.GroundRow - 4; y--)
            {
                Assert.True(server.World.GetBlock(At(server, lm, ShaftCell(lm, y))).IsAir);
            }

            var marker = s.Markers.Single(m => m.Type == LandmarkRules.ChestMarker);
            var box = Assert.Single(server.Containers, c => c.Position.Equals(At(server, lm, marker.LocalPos)));
            Assert.Equal(2, box.Items.Count);
            Assert.All(box.Items, i => Assert.Equal("gold_ingot", i.Item));
            Assert.All(box.Items, i => Assert.InRange(i.Count, 2, 3));
        }
    }

    [Fact]
    public void AChest_FollowsItsAuthor_Chance_Block_Hidden_AndTheDefaultTable()
    {
        var tower = LandmarkShapes.Tower(ruin: false, chest: "chance=0");
        tower.Cells.Add(LandmarkShapes.Marker(6, 1, 6, LandmarkRules.ChestMarker, "block=crate;hidden=1"));
        tower.Cells.Add(LandmarkShapes.Marker(8, 1, 6, LandmarkRules.ChestMarker, "table=ruin_rich;items=cable:5"));
        var content = ContentWith(tower);
        var server = Start(content, out var repo, name: "lm_chests");
        using (repo)
        {
            var lm = Assert.Single(server.LandmarksForTest);
            Vector3i Chest(int x, int z) => At(server, lm, lm.Structure.Markers.Single(m =>
                m.Type == LandmarkRules.ChestMarker && LandmarkTurnsBack(lm, m.LocalPos) == (x, z)).LocalPos);

            // chance=0: no crate, no container.
            var none = Chest(4, 4);
            Assert.True(server.World.GetBlock(none).IsAir);
            Assert.DoesNotContain(server.Containers, c => c.Position.Equals(none));

            // The default table in a steel crate, hidden under a stone.
            var hidden = Chest(6, 6);
            Assert.Equal(Id(content, "crate"), server.World.GetBlock(hidden).Value);
            Assert.Equal(Id(content, "stone"), server.World.GetBlock(new Vector3i(hidden.X, hidden.Y + 1, hidden.Z)).Value);
            var common = Assert.Single(server.Containers, c => c.Position.Equals(hidden));
            Assert.InRange(common.Items.Count, 3, 4); // ruin_common: three picks and a 30 % bonus

            // Fixed items AND a table.
            var both = Assert.Single(server.Containers, c => c.Position.Equals(Chest(8, 6)));
            Assert.Equal("cable", both.Items[0].Item);
            Assert.Equal(5, both.Items[0].Count);
            Assert.True(both.Items.Count >= 4, "the ruin_rich table adds at least three picks");
        }
    }

    /// <summary>The mine's shaft column (template cell 4, 4) at row <paramref name="y"/>, turned like the stamp turned it.</summary>
    private static Vector3i ShaftCell(SvGameServer.LandmarkInstance lm, int y)
    {
        var (x, z) = WorldGeneration.LandmarkGenerator.Turn(4, 4, lm.Template.Width, lm.Template.Length, lm.Layout.Turns);
        return new Vector3i(x, y, z);
    }

    /// <summary>A marker's template (x, z) from its turned cell — the inverse of the stamp's quarter turns.</summary>
    private static (int X, int Z) LandmarkTurnsBack(SvGameServer.LandmarkInstance lm, Vector3i local)
    {
        var t = lm.Template;
        for (int x = 0; x < t.Width; x++)
        {
            for (int z = 0; z < t.Length; z++)
            {
                var (tx, tz) = WorldGeneration.LandmarkGenerator.Turn(x, z, t.Width, t.Length, lm.Layout.Turns);
                if (tx == local.X && tz == local.Z)
                {
                    return (x, z);
                }
            }
        }

        return (-1, -1);
    }
}
