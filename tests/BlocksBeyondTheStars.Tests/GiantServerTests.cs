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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The giants on a live server (#1998–#2002): the colossus stays outside the fauna cap and the far prune, stomps a player
/// in reach but not one under a roof, is hit on its body and leaves a return time and an achievement when it falls; the
/// sandworm lives only on sand-sea worlds, hears footsteps on the sand but not on rock, comes for what it heard, can only
/// be hit while it is above the sand, and swallows a thumper that lured it.
/// </summary>
public sealed class GiantServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_giants_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, long seed = 4242)
    {
        string world = "giants_" + planet + "_" + (++_worlds); // every server its own save — a second one would reload the first
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = WorldDescription.GiantsGeneration },
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

    private BlocksBeyondTheStars.GameServer.PlayerSession OnFoot(SvGameServer server, int x, int z)
    {
        var p = server.AddLocalPlayer("Justus");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(x + 0.5f, SurfaceY(server, x, z) + 1, z + 0.5f);
        return p;
    }

    // ---------------- colossus ----------------

    [Fact]
    public void Colossus_StandsOnTheHorizon_OutsideTheFaunaCap_AndIsNeverFarPruned()
    {
        var server = Started("salt_flats", out var repo);
        using (repo)
        {
            var p = OnFoot(server, 0, 0);
            server.SummonGiantForTest("Justus", "colossus");
            var giant = Assert.Single(server.GiantsForTest(), g => g.Kind == CreatureBodyPlan.Colossus);
            float dx = giant.Position.X - p.State.Position.X, dz = giant.Position.Z - p.State.Position.Z;
            Assert.InRange(System.Math.Sqrt(dx * dx + dz * dz), 120.0, 220.0);
            var entity = server.Creatures.First(c => c.Id == giant.Id);
            Assert.True(entity.IsGiant);

            for (int i = 0; i < 40; i++)
            {
                p.State.Position = new Vector3f(0.5f, p.State.Position.Y, 0.5f);
                server.TickForTest(0.25);
            }

            Assert.Contains(server.Creatures, c => c.Id == giant.Id); // far beyond the 70/110-block prune, still there
            var net = server.NetCreatureForTest(giant.Id);
            Assert.Equal("Colossus", net.BodyPlan);
            Assert.InRange(net.GiantHeight, 40f, 60f);
            Assert.False(string.IsNullOrEmpty(net.Phase));
        }
    }

    [Fact]
    public void Colossus_Stomp_HurtsAPlayerInReach_ButNotOneUnderARoof()
    {
        foreach (bool roof in new[] { false, true })
        {
            var server = Started("salt_flats", out var repo);
            using (repo)
            {
                var p = OnFoot(server, 10, 10);
                server.SummonGiantForTest("Justus", "colossus");
                var sp = server.GiantSpeciesForTest().Colossus!;
                sp.Temperament = CreatureTemperament.Aggressive;
                var giant = server.GiantsForTest().Single(g => g.Kind == CreatureBodyPlan.Colossus);

                // Stand the colossus so its front-left foot comes down right beside the player.
                var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
                var (hx, _, hz) = body.Hip(0);
                var at = p.State.Position;
                var hipOffset = ColossusBody.ToWorld(new Vector3f(0f, 0f, 0f), 0f, hx, 0f, hz);
                var centre = new Vector3f(at.X - hipOffset.X + 2f, at.Y, at.Z - hipOffset.Z); // the foot lands 2 blocks off
                server.SetGiantForTest(giant.Id, centre, 0f);
                if (roof)
                {
                    int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z), y = (int)System.Math.Floor(at.Y);
                    server.World.SetBlock(new Vector3i(x, y + 3, z), _content.GetBlock("stone")!.NumericId);
                }

                float before = p.State.Health;
                for (int i = 0; i < 16; i++)
                {
                    p.State.Position = at;
                    server.TickForTest(0.25);
                }

                if (roof)
                {
                    Assert.Equal(before, p.State.Health); // a house is a real shelter: the foot never reaches in
                }
                else
                {
                    Assert.True(p.State.Health < before, "the stomp did not hurt the player beside the foot");
                }
            }
        }
    }

    [Fact]
    public void Colossus_IsHitOnItsLegs_AndItsDefeatLeavesAReturnTimeAndTheAchievement()
    {
        var server = Started("salt_flats", out var repo);
        using (repo)
        {
            var p = OnFoot(server, 20, 20);
            p.State.Inventory.SetSlot(0, new ItemStack("vibro_knife", 1));
            p.State.SelectedHotbarSlot = 0;
            server.SummonGiantForTest("Justus", "colossus");
            var sp = server.GiantSpeciesForTest().Colossus!;
            sp.Temperament = CreatureTemperament.Passive;
            var giant = server.GiantsForTest().Single(g => g.Kind == CreatureBodyPlan.Colossus);
            var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
            var at = p.State.Position;
            var (hx, _, hz) = body.Hip(0);
            var hip = ColossusBody.ToWorld(new Vector3f(0f, 0f, 0f), 0f, hx, 0f, hz);
            server.SetGiantForTest(giant.Id, new Vector3f(at.X - hip.X + 3f, at.Y, at.Z - hip.Z), 0f);

            // The centre of the colossus is far out of reach — its leg is not.
            var entity = server.Creatures.First(c => c.Id == giant.Id);
            float dx = entity.Position.X - at.X, dz = entity.Position.Z - at.Z;
            Assert.True(dx * dx + dz * dz > 6f * 6f, "the test wants the body centre out of melee reach");
            var (hittable, aim) = server.GiantHitForTest(giant.Id, at);
            Assert.True(hittable);
            float ax = aim.X - at.X, ay = aim.Y + 1.5f - (at.Y + 1.5f), az = aim.Z - at.Z;
            float hull = entity.Hull;
            server.AttackEntity("Justus", giant.Id, ax, ay, az);
            Assert.True(entity.Hull < hull, "a swing at the leg did not land");

            entity.Hull = 1f;
            server.TickForTest(2.0); // the knife's cooldown (the colossus wanders on meanwhile — stand it back)
            p.State.Position = at;
            server.SetGiantForTest(giant.Id, new Vector3f(at.X - hip.X + 3f, at.Y, at.Z - hip.Z), 0f);
            (_, aim) = server.GiantHitForTest(giant.Id, at);
            server.AttackEntity("Justus", giant.Id, aim.X - at.X, aim.Y - at.Y, aim.Z - at.Z);
            Assert.DoesNotContain(server.Creatures, c => c.Id == giant.Id);
            Assert.True(server.GiantBackAtForTest(CreatureBodyPlan.Colossus) > System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.Equal(1, p.State.AchievementCounters.GetValueOrDefault("defeat:colossus"));
        }
    }

    [Fact]
    public void Giants_NeverTakeTheSpawnersPlace_TheCapCountsOnlyTheFauna()
    {
        var server = Started("salt_flats", out var repo);
        using (repo)
        {
            OnFoot(server, 0, 0);
            server.SummonGiantForTest("Justus", "colossus");
            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.Single(server.Creatures, c => c.IsGiant);
            Assert.DoesNotContain(server.Creatures, c => !c.IsGiant && c.SpeciesId == "gi_colossus");
        }
    }

    // ---------------- sandworm ----------------

    /// <summary>A sand-sea world with the player on a sea column and the sandworm moved underground 30 blocks away.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, BlocksBeyondTheStars.GameServer.PlayerSession Player, string Worm)
        SandSeaScene(CreatureTemperament temper)
    {
        var server = Started("sand_sea", out var repo, seed: 2026);
        (int X, int Z) spot = default;
        bool found = false;
        for (int r = 0; r < 1200 && !found; r += 17)
            for (int a = 0; a < 16 && !found; a++)
            {
                int x = (int)(System.Math.Cos(a * 0.3927) * r), z = (int)(System.Math.Sin(a * 0.3927) * r);
                if (SeaRun(server, x, z) && IsSand(server, x, z) && IsSand(server, x + 30, z) && IsSand(server, x + 4, z))
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
        worm!.Temperament = temper;
        server.SummonGiantForTest("Justus", "sandworm");
        var w = Assert.Single(server.GiantsForTest(), g => g.Kind == CreatureBodyPlan.Sandworm);
        int wx = spot.X + 30, wz = spot.Z;
        server.SetGiantForTest(w.Id, new Vector3f(wx + 0.5f, SurfaceY(server, wx, wz) - 14f, wz + 0.5f), (float)System.Math.PI);
        return (server, repo, p, w.Id);
    }

    /// <summary>Sand-sea ground all along the worm's way in: 30 blocks east of the spot and 30 beyond (where it may rear).</summary>
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

    private bool IsSand(SvGameServer server, int x, int z)
        => _content.BlockById(server.World.GetBlock(new Vector3i(x, SurfaceY(server, x, z), z)))?.Key == "sand";

    [Fact]
    public void Sandworm_IsHostedOnSandSeaWorlds_AndNowhereElse()
    {
        var server = Started("sand_sea", out var repo);
        using (repo)
        {
            Assert.NotNull(server.GiantSpeciesForTest().Sandworm);
        }

        var dry = Started("desert", out var repo2);
        using (repo2)
        {
            Assert.Null(dry.GiantSpeciesForTest().Sandworm);
        }
    }

    [Fact]
    public void Sandworm_HearsSteps_OnTheSand_ButNotOnRock()
    {
        var (server, repo, p, worm) = SandSeaScene(CreatureTemperament.Aggressive);
        using (repo)
        {
            var at = p.State.Position;
            server.EmitVibrationForTest(at, VibrationSource.Step);
            float heard = server.GiantsForTest().Single(g => g.Id == worm).Attention;
            Assert.True(heard > 0f, "a step on the sand sea was not heard");

            // The same step on a stone floor laid over the sand: nothing.
            int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z), y = (int)System.Math.Floor(at.Y);
            server.World.SetBlock(new Vector3i(x, y - 1, z), _content.GetBlock("stone")!.NumericId);
            server.EmitVibrationForTest(at, VibrationSource.Step);
            Assert.Equal(heard, server.GiantsForTest().Single(g => g.Id == worm).Attention);
        }
    }

    [Fact]
    public void Sandworm_ComesForTheShaking_StrikesTheSpot_AndIsHitOnlyAboveTheSand()
    {
        var (server, repo, p, worm) = SandSeaScene(CreatureTemperament.Aggressive);
        using (repo)
        {
            p.State.Inventory.SetSlot(0, new ItemStack("vibro_knife", 1));
            p.State.SelectedHotbarSlot = 0;
            var at = p.State.Position;

            // Buried: nothing to hit.
            Assert.False(server.GiantHitForTest(worm, at).Hittable);

            for (int i = 0; i < 6; i++)
            {
                server.EmitVibrationForTest(at, VibrationSource.Mining);
            }

            float before = p.State.Health;
            bool rose = false, exposed = false;
            for (int i = 0; i < 160 && !(rose && exposed && p.State.Health < before); i++)
            {
                p.State.Position = at;
                server.TickForTest(0.25);
                var w = server.GiantsForTest().Single(g => g.Id == worm);
                rose |= w.Phase is "rear" or "breach";
                exposed |= server.GiantHitForTest(worm, at).Hittable;
            }

            Assert.True(rose, "the worm never came up");
            Assert.True(exposed, "the worm was never above the sand");
            Assert.True(p.State.Health < before, "the strike on the shaking spot did not hurt the player standing there");
        }
    }

    [Fact]
    public void Thumper_LuresTheWorm_WhichSwallowsIt()
    {
        var (server, repo, p, worm) = SandSeaScene(CreatureTemperament.Territorial);
        using (repo)
        {
            // The player walks away onto nothing in particular; the thumper does the shaking.
            var at = p.State.Position;
            int x = (int)System.Math.Floor(at.X) + 4, z = (int)System.Math.Floor(at.Z);
            int top = SurfaceY(server, x, z);
            p.State.Inventory.SetSlot(0, new ItemStack("thumper", 1));
            p.State.SelectedHotbarSlot = 0;
            server.PlaceBlock("Justus", x, top + 1, z, "thumper");
            var thumper = _content.GetBlock("thumper")!.NumericId;
            Assert.Equal(thumper, server.World.GetBlock(new Vector3i(x, top + 1, z)));
            Assert.Equal(1, server.ThumperCountForTest);

            // Stand well clear of the strike; wait for the worm to swallow the thumper.
            bool gone = false;
            for (int i = 0; i < 320 && !gone; i++)
            {
                p.State.Position = new Vector3f(at.X - 25f, at.Y, at.Z);
                server.TickForTest(0.25);
                gone = server.World.GetBlock(new Vector3i(x, top + 1, z)) != thumper;
            }

            Assert.True(gone, "the worm never came for the thumper");
            Assert.Equal(0, server.ThumperCountForTest);
        }
    }

    [Fact]
    public void Thumper_MinedBack_StopsThumping()
    {
        var (server, repo, p, _) = SandSeaScene(CreatureTemperament.Territorial);
        using (repo)
        {
            var at = p.State.Position;
            int x = (int)System.Math.Floor(at.X) + 2, z = (int)System.Math.Floor(at.Z);
            int top = SurfaceY(server, x, z);
            p.State.Inventory.SetSlot(0, new ItemStack("thumper", 1));
            p.State.SelectedHotbarSlot = 0;
            server.PlaceBlock("Justus", x, top + 1, z, "thumper");
            Assert.Equal(1, server.ThumperCountForTest);
            server.MineBlock("Justus", x, top + 1, z);
            server.TickForTest(0.25);
            Assert.Equal(0, server.ThumperCountForTest);
        }
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
        catch
        {
            // best effort
        }
    }
}
