// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A fortress at the walls reach cap (#3209, #3210): a 161-wide two-high ring on a stone pad, the core four blocks inside
/// its east wall, and a player-built marker 186 blocks west of the core, so the base's walls box is the capped 385 × 385.
/// <list type="bullet">
/// <item>#3209: the yard fill of such a base used to run in one tick (and above reach ~136 with less than its per-column
/// budget). It now runs over several ticks with a bounded amount of work per tick, finds exactly the yard the one-shot fill
/// finds, and keeps answering from the last finished fill until the new one is done.</item>
/// <item>#3210: a bed 150 blocks from the core, inside the walls, is indexed — the index box stopped at ±96 — a resident
/// takes it, and the resident's route search reaches it.</item>
/// </list>
/// The pacing is set deterministic (no time budget, fixed cells per tick), so no outcome depends on the clock.
/// </summary>
public sealed class BaseWallsReachTests : IDisposable
{
    private const int Cx = 600, Cz = 0;   // the ring's centre — far from the spawn pad, on the equator
    private const int Half = 80;          // the ring: 161 wide, two high, player-built
    private const int PadHalf = 84;       // the pad runs four blocks past the ring
    private const int CoreX = Cx + 76;    // the core, four blocks inside the east wall
    private const int MarkerX = CoreX - 186; // a player-built block 186 west of the core: the box grows to the cap
    private const int Side = 385;         // the capped box, 2 · 192 + 1
    private const int MinCellsPerTick = 8192;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_wallsreach_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly ITestOutputHelper _output;
    private SqliteWorldRepository? _repo;

    public BaseWallsReachTests(ITestOutputHelper output) => _output = output;

    private sealed record Fortress(SvGameServer Server, PlayerSession Owner, int Feet, int BaseId);

    /// <summary>The fortress; with <paramref name="ring"/> false an open pad instead (no wall at all), the core at
    /// <paramref name="coreX"/> and the player-built marker at <paramref name="markerX"/>.</summary>
    private Fortress Build(string world, bool ring = true, int coreX = CoreX, int markerX = MarkerX)
    {
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
        }, _content, new LoopbackServerTransport(new LoopbackLink()), _repo);
        server.Start();

        int top = int.MinValue;
        for (int dx = -PadHalf; dx <= PadHalf; dx += 2)
            for (int dz = -PadHalf; dz <= PadHalf; dz += 2)
            {
                top = Math.Max(top, server.SurfaceHeightForTest(Cx + dx, Cz + dz));
            }

        int padY = top + 48; // the walking band (feet ± 12) stays far above every tree
        var stone = _content.GetBlock("stone")!.NumericId;
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        string owner = p.State.PlayerId;
        _repo.RunInTransaction(() =>
        {
            for (int dx = -PadHalf; dx <= PadHalf; dx++)
                for (int dz = -PadHalf; dz <= PadHalf; dz++)
                {
                    server.World.SetBlock(new Vector3i(Cx + dx, padY, Cz + dz), stone);
                }

            for (int dx = -Half; dx <= Half && ring; dx++)
                for (int dz = -Half; dz <= Half; dz++)
                {
                    if (Math.Abs(dx) == Half || Math.Abs(dz) == Half)
                    {
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 1, Cz + dz), stone, owner: owner);
                        server.World.SetBlock(new Vector3i(Cx + dx, padY + 2, Cz + dz), stone, owner: owner);
                    }
                }

            server.World.SetBlock(new Vector3i(markerX, padY + 1, Cz), stone, owner: owner);
        });

        int feet = padY + 1;
        p.State.Position = new Vector3f(coreX - 1.5f, feet, Cz + 0.5f);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock("Builder", coreX, feet, Cz, "base_core");
        int baseId = Assert.Single(server.BaseSnapshots).Id;
        int reach = Math.Max(48, Math.Max(ring ? Math.Abs(coreX - (Cx - Half)) : 0, Math.Abs(markerX - coreX)) + 6);
        Assert.Equal(Math.Min(192, reach), server.WalledReachForTest(coreX, feet, Cz)); // the marker pulls the box out

        // Deterministic pacing: no time budget, so every step does exactly its guaranteed cells.
        server.SetWalledPacingForTest(tickBudgetMs: 0, firstFillBudgetMs: 0, minCellsPerTick: MinCellsPerTick, startMinCells: 4096);
        return new Fortress(server, p, feet, baseId);
    }

    /// <summary>Ticks the wall driver until no fill runs; returns the ticks it took and the most cells one tick worked.</summary>
    private static (int Ticks, int MaxCells) DriveWallFills(SvGameServer server)
    {
        int ticks = 0, max = 0;
        while (server.WalledMemoryForTest.Jobs > 0)
        {
            max = Math.Max(max, server.TickBaseWallFillsForTest());
            Assert.True(++ticks < 1000, "the wall fill must finish");
        }

        return (ticks, max);
    }

    [Fact]
    public void AFillAtTheReachCap_RunsOverSeveralTicks_WithBoundedWork_AndFindsTheOneShotYard()
    {
        var f = Build("sliced");
        var server = f.Server;
        int yardX = Cx - 40, outsideX = Cx + 120;

        // The first query starts the fill and works only its start minimum: no fill finished yet, so nothing reads
        // fenced in — the fail-open direction — while the fill runs.
        var first = server.WalledVerdictForTest(yardX, f.Feet, Cz);
        Assert.True(first.Pending, "a reach-192 fill must not finish inside the query that starts it");
        Assert.False(first.Enclosed, "a level that never finished a fill reads open");

        var (ticks, maxCells) = DriveWallFills(server);
        _output.WriteLine($"sliced fill: {ticks} ticks, at most {maxCells} cells per tick");
        Assert.True(ticks >= 5, $"the fill must be spread over several ticks ({ticks})");
        Assert.True(maxCells <= MinCellsPerTick + 64, $"one tick worked {maxCells} cells — more than its bound");

        // The sliced result is the one-shot fill's, cell for cell, with the full per-column budget (no knee at reach 192).
        var sliced = server.WalledLevelForTest(yardX, f.Feet, Cz);
        var watch = Stopwatch.StartNew();
        var oneShot = server.WalledOneShotFillForTest(yardX, f.Feet, Cz);
        _output.WriteLine($"one-shot fill: {oneShot.Reachable} cells in {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(192, sliced.Reach);
        Assert.Equal(Side * Side * 8, sliced.Budget);
        Assert.False(sliced.BudgetOut);
        Assert.Equal(oneShot.Reachable, sliced.Reachable);
        Assert.Equal(oneShot.BudgetOut, sliced.BudgetOut);
        Assert.Equal(oneShot.Bits, sliced.Bits);

        // Independently: the feet level outside the ring (the box minus ring and yard, minus the marker's cell) plus the
        // one cell standing on the marker — the yard the synchronous fill always found here.
        const int ringAndYard = ((2 * Half) + 1) * ((2 * Half) + 1);
        Assert.Equal((Side * Side) - ringAndYard - 1 + 1, sliced.Reachable);

        Assert.True(server.WalledVerdictForTest(yardX, f.Feet, Cz).Enclosed, "inside the ring, 116 blocks from the core");
        Assert.True(server.WalledVerdictForTest(Cx - Half + 2, f.Feet, Cz - Half + 2).Enclosed, "the far corner of the yard");
        Assert.False(server.WalledVerdictForTest(outsideX, f.Feet, Cz).Enclosed, "outside the ring");

        // Memory: no fill runs, its scratch is given back, the level holds one bitset of the 25-row band.
        var memory = server.WalledMemoryForTest;
        Assert.Equal(0, memory.Jobs);
        Assert.Equal(0, memory.ScratchBytes);
        Assert.Equal((((long)Side * Side * 25) + 63) / 64 * 8, memory.CacheBytes);
    }

    [Fact]
    public void ARefill_KeepsAnsweringFromTheLastFinishedFill_UntilItIsDone()
    {
        var f = Build("refill");
        var server = f.Server;
        int yardX = Cx - 40;
        server.WalledVerdictForTest(yardX, f.Feet, Cz);
        DriveWallFills(server);
        Assert.True(server.WalledVerdictForTest(yardX, f.Feet, Cz).Enclosed);

        // A gap in the west wall: the level is refilled — but until that fill is done, the yard keeps its last answer.
        server.RemoveBlockForTest(Cx - Half, f.Feet, Cz);
        server.RemoveBlockForTest(Cx - Half, f.Feet + 1, Cz);
        var during = server.WalledVerdictForTest(yardX, f.Feet, Cz);
        Assert.True(during.Pending, "the refill runs over ticks");
        Assert.True(during.Enclosed, "nobody loses the yard mid-fill: the last finished fill answers");

        var (ticks, _) = DriveWallFills(server);
        Assert.True(ticks >= 5);
        var after = server.WalledVerdictForTest(yardX, f.Feet, Cz);
        Assert.False(after.Pending);
        Assert.False(after.Enclosed, "the finished refill sees the gap");
        Assert.Equal(server.WalledOneShotFillForTest(yardX, f.Feet, Cz).Bits, server.WalledLevelForTest(yardX, f.Feet, Cz).Bits);
    }

    [Fact]
    public void ABedInTheOpen_PastTheOldBox_DoesNotCount_WhileTheGrownBoxRefills_NorAfterwards()
    {
        // #3209 review: the box grows with every build, and its refill runs over ticks. Until it is done the level answers from
        // the last finished fill — the OLD, smaller box — and a cell outside that box used to read "never reached", i.e.
        // walled in. A bed set in the open field 80 blocks out (which grows the box from 56 to 86) then counted as inside and
        // stuck there for good (the bulkhead rule).
        var f = Build("grow", ring: false, coreX: Cx, markerX: Cx + 50); // an open pad, the reach 56
        var server = f.Server;
        server.WalledVerdictForTest(Cx + 10, f.Feet, Cz);
        DriveWallFills(server); // the old box's fill is finished

        var bed = new Vector3i(Cx - 80, f.Feet, Cz);
        int head = ShapeCode.Pack((int)BlockShape.BedHead, 0);
        var bedId = _content.GetBlock("bed")!.NumericId;
        server.World.SetBlock(bed, bedId, 0, 0, head, f.Owner.State.PlayerId);
        server.World.SetBlock(new Vector3i(bed.X, bed.Y, bed.Z + 1), bedId, 0, 0, FurnitureShapes.BedPartnerDescriptor(head), f.Owner.State.PlayerId);
        Assert.Equal(86, server.WalledReachForTest(Cx, f.Feet, Cz));

        var during = server.BaseIndexForTest(f.BaseId)!;
        Assert.True(server.WalledVerdictForTest(bed.X + 1, f.Feet, bed.Z).Pending, "the grown box's refill is still running");
        Assert.False(server.WalledVerdictForTest(bed.X + 1, f.Feet, bed.Z).Enclosed, "outside the old box is open, never walled");
        Assert.DoesNotContain(bed, during.BedHeads);
        Assert.False(during.StickyInside.ContainsKey(bed));

        DriveWallFills(server);
        var after = server.BaseIndexForTest(f.BaseId)!;
        Assert.DoesNotContain(bed, after.BedHeads);
        Assert.False(after.StickyInside.ContainsKey(bed), "a bed in the open field never sticks as inside");
    }

    [Fact]
    public void ABed150BlocksFromTheCore_InsideTheWalls_IsIndexed_TakenAndWalkedTo()
    {
        var f = Build("farbed");
        var server = f.Server;
        var bed = new Vector3i(Cx - 74, f.Feet, Cz); // 150 blocks west of the core, six inside the west wall
        int head = ShapeCode.Pack((int)BlockShape.BedHead, 0);
        var bedId = _content.GetBlock("bed")!.NumericId;
        server.World.SetBlock(bed, bedId, 0, 0, head, f.Owner.State.PlayerId);
        server.World.SetBlock(new Vector3i(bed.X, bed.Y, bed.Z + 1), bedId, 0, 0, FurnitureShapes.BedPartnerDescriptor(head), f.Owner.State.PlayerId);

        // The first scan asks the yard at the bed's level: the fill starts there and runs on over the ticks; once it is done
        // the changed yard rebuilds the index, and the next scan counts the bed.
        server.ScanBaseLifeForTest();
        DriveWallFills(server);
        server.ScanBaseLifeForTest();
        Assert.Contains(bed, server.BaseIndexForTest(f.BaseId)!.BedHeads);
        var sleeper = Assert.Single(server.BaseResidentsForTest(f.BaseId), r => r.Bed == bed);

        // The resident walks there from the core: the search box reaches the far side of the base.
        server.MoveNpcForTest(sleeper.NpcId, new Vector3f(CoreX - 1.5f, f.Feet, Cz + 1.5f));
        var limits = server.NpcPathLimitsForTest(sleeper.NpcId);
        Assert.True(limits.HalfXZ >= 192, $"the search box (±{limits.HalfXZ}) must hold the base's walls box");
        Assert.Equal(16000, limits.MaxExpansions); // 8000 per 96 blocks of reach
        server.SetNpcGoalForTest(sleeper.NpcId, new Vector3f(bed.X + 1.5f, f.Feet, bed.Z + 0.5f)); // beside the bed
        Assert.True(server.SearchNpcPathForTest(sleeper.NpcId), "a route to a bed 150 blocks from the core");
    }

    public void Dispose()
    {
        _repo?.Dispose();
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
            // best-effort temp cleanup
        }
    }
}
