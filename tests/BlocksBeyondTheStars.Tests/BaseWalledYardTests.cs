// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Walled base areas (#1315): a closed ring of walls within a founded base's reach keeps wild spawns out —
/// no roof required (that is the sealed-room rule, #1314). Outside-in fill, cached per feet level; a gap
/// or an open door lets the fill (and the animals) in; a shut door counts as wall.
/// </summary>
public sealed class BaseWalledYardTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BaseWalledYardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_walled_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world, string planet = "jungle")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = planet, // jungle: a full roster for the spawn-seam checks
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
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

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                max = System.Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }

        return max;
    }

    private const int Cx = 40, Cz = 40, Ring = 6;

    /// <summary>A stone pad floating above every natural feature, a founded base core at its centre, and
    /// a 2-high stone ring of radius <see cref="Ring"/> around it — Lyxette's roofless yard. Returns the
    /// builder and the pad's floor Y (feet level = padY + 1).</summary>
    private (PlayerSession Builder, int PadY) Yard(SvGameServer server, bool ring = true, bool core = true)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int padY = MaxTopY(server, Cx, Cz, 12) + 8;
        for (int dx = -10; dx <= 10; dx++)
            for (int dz = -10; dz <= 10; dz++)
            {
                server.World.SetBlock(new Vector3i(Cx + dx, padY, Cz + dz), stone);
            }

        if (ring)
        {
            for (int dx = -Ring; dx <= Ring; dx++)
                for (int dz = -Ring; dz <= Ring; dz++)
                {
                    if (System.Math.Abs(dx) == Ring || System.Math.Abs(dz) == Ring)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 1, Cz + dz), stone);
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 2, Cz + dz), stone);
                    }
                }
        }

        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f);
        if (core)
        {
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock("Builder", Cx, padY + 1, Cz, "base_core");
            Assert.Single(server.BaseSnapshots);
        }

        return (p, padY);
    }

    private static void Settle(SvGameServer server) // past the 1.5 s recompute interval
    {
        for (int i = 0; i < 4; i++)
        {
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void AWalledYard_IsFencedIn_AndAGapLetsTheAnimalsIn()
    {
        var server = Started(out var repo, "yard");
        using (repo)
        {
            var (_, padY) = Yard(server);
            int feet = padY + 1;

            Assert.True(server.InWalledBaseAreaForTest(Cx + 2, feet, Cz), "inside the ring must read as fenced in");
            Assert.True(server.InWalledBaseAreaForTest(Cx - 4, feet, Cz + 3));
            Assert.False(server.InWalledBaseAreaForTest(Cx + 9, feet, Cz), "outside the ring is open terrain");
            Assert.False(server.InWalledBaseAreaForTest(Cx + 30, feet, Cz + 30), "far out, still inside the reach box, is open");
            Assert.False(server.InWalledBaseAreaForTest(Cx + 2, feet + 2, Cz), "above the wall top the fill passes over — the yard is roofless");

            // Knock one column out of the ring: the fill leaks in on the next refresh.
            server.RemoveBlockForTest(Cx + Ring, padY + 1, Cz);
            server.RemoveBlockForTest(Cx + Ring, padY + 2, Cz);
            Settle(server);
            Assert.False(server.InWalledBaseAreaForTest(Cx + 2, feet, Cz), "one gap in the wall and the yard is open again");
        }
    }

    [Fact]
    public void AShutDoorCountsAsWall_AnOpenOneIsAGap()
    {
        var server = Started(out var repo, "gate");
        using (repo)
        {
            var (p, padY) = Yard(server);
            int feet = padY + 1;

            // A 1-wide gateway in the +X wall, filled with a wooden door (placed shut).
            server.RemoveBlockForTest(Cx + Ring, padY + 1, Cz);
            server.RemoveBlockForTest(Cx + Ring, padY + 2, Cz);
            p.State.Inventory.Add("door_wood", 2, 16);
            p.State.Position = new Vector3f(Cx + Ring + 1.5f, feet, Cz + 0.5f);
            server.PlaceBlock("Builder", Cx + Ring, padY + 1, Cz, "door_wood");
            var door = server.DoorSnapshots.Single(d => d.Kind == "wood");
            Assert.False(door.Open);
            Settle(server);
            Assert.True(server.InWalledBaseAreaForTest(Cx + 2, feet, Cz), "a shut gate keeps the yard closed");

            // Open the gate: the fill walks through it.
            p.State.Position = new Vector3f(Cx + Ring + 0.5f, feet, Cz + 0.5f);
            server.InteractDoorForTest(p, door.Id);
            Assert.True(server.DoorSnapshots.Single(d => d.Id == door.Id).Open);
            Settle(server);
            Assert.False(server.InWalledBaseAreaForTest(Cx + 2, feet, Cz), "an open gate is a gap");
        }
    }

    [Fact]
    public void WithoutABaseCore_WallsFenceNothing()
    {
        var server = Started(out var repo, "nocore");
        using (repo)
        {
            var (_, padY) = Yard(server, core: false);
            Assert.False(server.InWalledBaseAreaForTest(Cx + 2, padY + 1, Cz));
        }
    }

    [Fact]
    public void TheSpawner_RejectsAWalledSpot_ForLandLife_ButNotForFliers()
    {
        var server = Started(out var repo, "spawn");
        using (repo)
        {
            var (_, padY) = Yard(server);
            var sp = server.SpeciesRoster[0];
            sp.Habitat = CreatureHabitat.Land;
            sp.BodyPlan = CreatureBodyPlan.Standard;
            sp.Size = 1f;

            var inside = new Vector3f(Cx + 2.5f, padY + 1, Cz + 0.5f);
            var outside = new Vector3f(Cx + 9.5f, padY + 1, Cz + 0.5f);
            Assert.False(server.SpawnSpotClearForTest(inside), "a land spawn inside the yard is rejected");
            Assert.True(server.SpawnSpotClearForTest(outside), "outside the ring spawns continue");

            sp.Habitat = CreatureHabitat.Air;
            Assert.True(server.SpawnSpotClearForTest(new Vector3f(Cx + 2.5f, padY + 5, Cz + 0.5f)), "a flier does not care about walls");
        }
    }

    // ---------------- #1358: a proximity door is a wall whatever it shows ----------------

    [Fact]
    public void ASlidingGate_KeepsTheYardClosed_WhileThePlayerStandsAtIt()
    {
        var server = Started(out var repo, "slidegate");
        using (repo)
        {
            var (p, padY) = Yard(server);
            int feet = padY + 1;

            // The same 1-wide gateway as the wooden-door test, filled with a sliding door.
            server.RemoveBlockForTest(Cx + Ring, padY + 1, Cz);
            server.RemoveBlockForTest(Cx + Ring, padY + 2, Cz);
            p.State.Inventory.Add("door_slide", 2, 16);
            p.State.Position = new Vector3f(Cx + Ring + 1.5f, feet, Cz + 0.5f);
            server.PlaceBlock("Builder", Cx + Ring, padY + 1, Cz, "door_slide");
            var door = server.DoorSnapshots.Single(d => d.Kind == "slide");

            // The player stays right at the gate: the proximity tick holds it OPEN…
            Settle(server);
            Assert.True(server.DoorSnapshots.Single(d => d.Id == door.Id).Open, "sanity: a player beside a sliding door opens it");
            // …and it still reads as wall — it opens only for players and closes by itself, no animal passes it.
            Assert.True(server.InWalledBaseAreaForTest(Cx + 2, feet, Cz), "an open sliding gate is no gap for the wildlife (#1358)");
        }
    }

    // ---------------- #1347: real terrain is not masonry ----------------

    /// <summary>A terraced bowl in a terraced plateau, built on REAL generated terrain at the ring's centre:
    /// Chebyshev ring k from the centre carries its top at H−3 (k ≤ 1, the bowl floor), H−2 (k = 2), H−1
    /// (k = 3), H (k = 4…7, the plateau), H−1 (k = 8), H−2 (k = 9) and H−3 (k = 10); beyond that the natural
    /// ground stays. Every ring differs from its neighbours by exactly one block, so a walking animal — and the
    /// walking fill — can get in and out; each top sits on four blocks of stone (a tree's air pockets below
    /// don't matter) and everything above it is cleared. H is the highest natural cell within 30 blocks, so
    /// nothing nearby towers over the plateau. Returns H.</summary>
    /// <summary>The first of a few fixed candidate centres whose 25×25 surroundings are dry ground (no fluid at
    /// the top of any sampled column) — the seed's start area at (40, 40) turned out to be open sea.</summary>
    private (int Cx, int Cz) LandSpot(SvGameServer server)
    {
        foreach (var (cx, cz) in new[] { (40, 40), (-120, 80), (200, -160), (-260, -220), (340, 300), (-400, 120) })
        {
            bool dry = true;
            for (int dx = -12; dx <= 12 && dry; dx += 2)
                for (int dz = -12; dz <= 12 && dry; dz += 2)
                {
                    int top = SurfaceTopY(server, cx + dx, cz + dz);
                    var key = _content.BlockById(server.World.GetBlock(new Vector3i(cx + dx, top, cz + dz)))?.Key;
                    dry = key is not ("water" or "lava");
                }

            if (dry)
            {
                return (cx, cz);
            }
        }

        throw new InvalidOperationException("no dry 25×25 spot among the candidates — pick another seed/planet");
    }

    private int TerracedBowl(SvGameServer server, int cx, int cz)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int h = MaxTopY(server, cx, cz, 30);
        for (int dx = -10; dx <= 10; dx++)
            for (int dz = -10; dz <= 10; dz++)
            {
                int k = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz));
                int top = k switch
                {
                    <= 1 => h - 3,
                    2 => h - 2,
                    3 => h - 1,
                    <= 7 => h,
                    8 => h - 1,
                    9 => h - 2,
                    _ => h - 3,
                };
                for (int y = top - 4; y <= top; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), stone);
                }

                for (int y = top + 1; y <= h + 8; y++)
                {
                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), BlockId.Air);
                }
            }

        return h;
    }

    [Fact]
    public void ABaseInAHollow_LeavesTheHollowOpen_UntilARealWallRingsIt()
    {
        // The first fill flooded ONE horizontal slice and treated natural terrain as a wall: a spawn candidate
        // on the floor of any hollow within a base's reach had a closed solid contour at its own level and read
        // as "fenced in" — no land animal around a base built in a valley.
        var server = Started(out var repo, "hollow", planet: "desert"); // dry land, gentle relief
        using (repo)
        {
            var (cx, cz) = LandSpot(server);
            int h = TerracedBowl(server, cx, cz);
            int floorFeet = h - 2; // the bowl floor's feet cell (floor top at h − 3)

            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(cx - 1.5f, floorFeet, cz + 0.5f);
            p.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock("Builder", cx, floorFeet, cz, "base_core"); // founded on the bowl floor
            Assert.Single(server.BaseSnapshots);

            Assert.False(server.InWalledBaseAreaForTest(cx + 1, floorFeet, cz), "a terraced hollow is open ground, not a yard (#1347)");
            var (reachable, failOpen) = server.WalledFillForTest(cx + 1, floorFeet, cz);
            Assert.False(failOpen, $"the fill must not run out of budget on real jungle terrain ({reachable} cells)");
            Assert.False(server.InWalledBaseAreaForTest(cx + 5, h + 1, cz), "the plateau above it is open too");
            Assert.False(server.InWalledBaseAreaForTest(cx, h, cz + 2), "a one-block terrain step is no wall");

            // A 2-high stone ring on the plateau (k = 6, flat ground on both sides) encloses the bowl…
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int dx = -6; dx <= 6; dx++)
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (System.Math.Abs(dx) == 6 || System.Math.Abs(dz) == 6)
                    {
                        server.World.SetBlock(new Vector3i(cx + dx, h + 1, cz + dz), stone);
                        server.World.SetBlock(new Vector3i(cx + dx, h + 2, cz + dz), stone);
                    }
                }

            Settle(server);
            Assert.True(server.InWalledBaseAreaForTest(cx + 1, floorFeet, cz), "a 2-high wall ring on real ground fences the hollow in");
            Assert.False(server.InWalledBaseAreaForTest(cx + 9, h - 1, cz), "outside the ring the terraces stay open");

            // …while the same ring one block high is a garden edge the animals step over.
            for (int dx = -6; dx <= 6; dx++)
                for (int dz = -6; dz <= 6; dz++)
                {
                    if (System.Math.Abs(dx) == 6 || System.Math.Abs(dz) == 6)
                    {
                        server.World.SetBlock(new Vector3i(cx + dx, h + 2, cz + dz), BlockId.Air);
                    }
                }

            Settle(server);
            Assert.False(server.InWalledBaseAreaForTest(cx + 1, floorFeet, cz), "a one-block raised edge is no fence");
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
