// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The water model of #2515 (ADR 0017): a fall keeps its level, only endless water is a source (at or below the sea
/// level, rivers, generated falls, placed water), and still water above the sea level is finite — it settles as a whole,
/// drains through a breach down to the sill, refills a hole from its top, and keeps its volume while it does.
/// </summary>
public sealed class WaterModelTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly ITestOutputHelper _out;
    private readonly ushort _water;
    private readonly BlockId _stone;

    public WaterModelTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), "bbts_water_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        _water = _content.GetBlock("water")!.NumericId.Value;
        _stone = _content.GetBlock("stone")!.NumericId;
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
            // a test host still holding the save is no reason to fail the run
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "water")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void Run(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.3)
        {
            server.Tick(0.3);
        }
    }

    private bool IsWater(SvGameServer server, int x, int y, int z) => server.World.GetBlock(new Vector3i(x, y, z)).Value == _water;

    private int CountWater(SvGameServer server, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        int n = 0;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (IsWater(server, x, y, z))
                    {
                        n++;
                    }

        return n;
    }

    /// <summary>A broad staircase going down along +x: two cells per step, one block per step, floor at
    /// <c>topFloor - x/2</c>, with a flat landing behind x = 0. Optionally carved into the ground (air above each step),
    /// and optionally <paramref name="sealed"/>: a stone shell (side walls, end walls, a ceiling over the carved space and
    /// a second floor under it) so no generated sea, lake or cave beside the carving can pour in once the flow wakes it —
    /// a carving below the sea line otherwise opens onto endless sea water along its whole length.</summary>
    private void Staircase(SvGameServer server, SqliteWorldRepository repo, int topFloor, bool carve, bool @sealed = false)
    {
        repo.RunInTransaction(() =>
        {
            for (int x = -10; x <= 40; x++)
                for (int z = -10; z <= 10; z++)
                {
                    int floor = topFloor - Math.Max(0, x) / 2;
                    server.World.SetBlock(new Vector3i(x, floor, z), _stone);
                    if (carve)
                    {
                        for (int y = floor + 1; y <= floor + 4; y++)
                        {
                            server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                        }
                    }
                }

            if (!@sealed)
            {
                return;
            }

            for (int x = -11; x <= 41; x++)
                for (int z = -11; z <= 11; z++)
                {
                    int floor = topFloor - Math.Max(0, Math.Min(40, x)) / 2;
                    bool wall = x == -11 || x == 41 || z == -11 || z == 11;
                    for (int y = floor - 1; y <= floor + 5; y++)
                    {
                        if (wall || y == floor - 1 || y == floor + 5)
                        {
                            server.World.SetBlock(new Vector3i(x, y, z), _stone);
                        }
                    }
                }
        });
    }

    /// <summary>The farthest step (x) that holds water just above its floor.</summary>
    private int FarthestWet(SvGameServer server, int topFloor)
    {
        int farthest = int.MinValue;
        for (int x = 0; x <= 40; x++)
        {
            int floor = topFloor - x / 2;
            for (int z = -10; z <= 10; z++)
            {
                if (IsWater(server, x, floor + 1, z) || IsWater(server, x, floor + 2, z))
                {
                    farthest = x;
                }
            }
        }

        return farthest;
    }

    // ---------------- a fall keeps its level ----------------

    [Fact]
    public void AFall_KeepsItsLevel_SoPlacedWaterRunsAboutSevenStepsDownAStaircase_NotAllTheWay()
    {
        // Before #2515 a falling cell refilled to full strength: every step down re-armed a fresh seven-cell spread, and
        // one placed block ran down the whole staircase (and down every cave shaft and floor below).
        var server = Started(out var repo);
        using (repo)
        {
            Staircase(server, repo, 129, carve: false);
            server.PlaceFluidSource("water", 0, 130, 0);
            Run(server, 15);

            int farthest = FarthestWet(server, 129);
            _out.WriteLine($"farthest wet step x={farthest}");
            Assert.True(farthest >= 4, "the placed water should run a few steps down");
            Assert.True(farthest <= 9, $"a fall keeps its level, so the run ends about seven cells out (reached x={farthest})");
            Assert.True(IsWater(server, 0, 130, 0), "placed water stays a source");
        }
    }

    [Fact]
    public void WaterAtTheSeaLevel_IsEndless_ButItsSpillDownACaveStaysBounded()
    {
        // The tester's flood: sea water beside a cave poured down every shaft and across every floor (15 000+ cells from
        // one touch). The sea stays endless (W7), but its spill is bounded like any flow.
        // The staircase is sealed: carved below the sea line, it otherwise opens onto whatever sea, lake or cave the world
        // generated beside it — every such sea cell is an endless source of its own and spills its own bounded run, so the
        // farthest wet step measured the world around the carving (x = 14–26 on several seeds of generation 25 and 26),
        // not the one touched cell. Sealed, every seed tried reaches x = 7.
        var server = Started(out var repo);
        using (repo)
        {
            int sea = server.World.Generator.SeaLevel(server.World.Planet);
            int top = sea - 2; // the top step's floor; the water above it sits at or below the sea line
            Staircase(server, repo, top, carve: true, @sealed: true);
            server.World.SetBlock(new Vector3i(0, top + 1, 0), new BlockId(_water)); // unmarked: endless by position only
            server.RemoveBlockForTest(1, top + 1, 0); // touch the cave beside it

            Run(server, 15);

            int farthest = FarthestWet(server, top);
            _out.WriteLine($"sea {sea}: farthest wet step x={farthest}");
            Assert.True(IsWater(server, 0, top + 1, 0), "the sea cell stays: water at the sea level is endless");
            Assert.True(farthest >= 3, "the sea should spill into the cave");
            Assert.True(farthest <= 9, $"the spill stays bounded (reached x={farthest})");
            Assert.False(server.IsPlacedFluidSourceForTest(0, top + 1, 0));
        }
    }

    [Fact]
    public void AGeneratedRiverWaterfall_KeepsFalling_ButNeverSpreadsSideways()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var gen = server.World.Generator;
            var planet = server.World.Planet;
            int sea = gen.SeaLevel(planet);
            (int X, int Z, RiverField.RiverColumn Col)? found = null;
            foreach (var (pos, col) in gen.RiverFieldFor(planet).ColumnsByPosition)
            {
                if (!col.Underground && col.WaterfallDrop >= 3 && col.WaterSurfaceY > sea
                    && gen.WaterKindAt(planet, pos.X, col.WaterSurfaceY + 1, pos.Z) == GeneratedWaterKind.RiverFall)
                {
                    found = (pos.X, pos.Z, col);
                    break;
                }
            }

            Assert.True(found.HasValue, "the seed-1 world should have a river waterfall");
            var (fx, fz, fall) = found!.Value;
            int y = fall.WaterSurfaceY + 2; // a cell of the standing column, above the lower reach
            Assert.True(IsWater(server, fx, y, fz), "the waterfall column should be generated water");

            // Open the column's flank and touch it — before #2515 every cell of a woken fall spread at its own height.
            server.RemoveBlockForTest(fx + 1, y, fz);
            server.RemoveBlockForTest(fx - 1, y, fz);
            Run(server, 10);

            Assert.True(IsWater(server, fx, y, fz), "the fall keeps running");
            for (int dx = -6; dx <= 6; dx++)
                for (int dz = -6; dz <= 6; dz++)
                    for (int dy = -1; dy <= fall.WaterfallDrop + 1; dy++)
                    {
                        Assert.False(server.IsFlowingFluidForTest(fx + dx, fall.WaterSurfaceY + dy, fz + dz),
                            $"no flow may leave the fall (flowing water at {fx + dx},{fall.WaterSurfaceY + dy},{fz + dz})");
                    }
        }
    }

    // ---------------- finite still water ----------------

    private const int Bx = 300, By = 130, Bz = 300, Size = 7, Depth = 3;

    /// <summary>A closed 7x7x3 stone basin in the air (two-cell floor, walls one cell thick) filled the way the generator
    /// fills water — untracked cells above the sea level, so finite.</summary>
    private void Basin(SvGameServer server, SqliteWorldRepository repo)
    {
        repo.RunInTransaction(() =>
        {
            for (int x = Bx - 1; x <= Bx + Size; x++)
                for (int z = Bz - 1; z <= Bz + Size; z++)
                {
                    server.World.SetBlock(new Vector3i(x, By - 1, z), _stone);
                    server.World.SetBlock(new Vector3i(x, By - 2, z), _stone);
                    bool wall = x == Bx - 1 || x == Bx + Size || z == Bz - 1 || z == Bz + Size;
                    for (int y = By; y < By + Depth; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), wall ? _stone : new BlockId(_water));
                    }
                }
        });
    }

    private int BasinWater(SvGameServer server) => CountWater(server, Bx, Bx + Size - 1, By, By + Depth - 1, Bz, Bz + Size - 1);

    private int LayerWater(SvGameServer server, int y) => CountWater(server, Bx, Bx + Size - 1, y, y, Bz, Bz + Size - 1);

    [Fact]
    public void AFinitePond_BreachedIntoAClosedShaft_SettlesToTheSill_AndKeepsItsVolume()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            int zc = Bz + 3, wall = Bx + Size;
            // A closed channel through the east wall (two cells high, the upper two layers) into a closed 12-deep shaft.
            repo.RunInTransaction(() =>
            {
                for (int x = wall; x <= wall + 3; x++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int y = By - 13; y <= By + Depth; y++)
                        {
                            server.World.SetBlock(new Vector3i(x, y, zc + dz), _stone);
                        }

                server.World.SetBlock(new Vector3i(wall + 1, By + 1, zc), BlockId.Air);
                server.World.SetBlock(new Vector3i(wall + 1, By + 2, zc), BlockId.Air);
                for (int y = By - 11; y <= By + 2; y++)
                {
                    server.World.SetBlock(new Vector3i(wall + 2, y, zc), BlockId.Air);
                }

                server.World.SetBlock(new Vector3i(wall, By + 2, zc), BlockId.Air);
            });
            server.RemoveBlockForTest(wall, By + 1, zc); // the player digs the gap

            Run(server, 10);

            int basin = BasinWater(server);
            int outside = CountWater(server, wall, wall + 2, By - 12, By + 2, zc, zc);
            _out.WriteLine($"basin {basin}, channel + shaft {outside}, top layer {LayerWater(server, By + 2)}");
            Assert.Equal(Size * Size * Depth, basin + outside); // the water moved, none was made or lost
            Assert.Equal(Size * Size, LayerWater(server, By));   // below the sill nothing moved
            Assert.Equal(Size * Size, LayerWater(server, By + 1));
            Assert.True(outside > 0, "the water should run into the shaft");
            Assert.Equal(0, server.SettleJobCountForTest);
        }
    }

    [Fact]
    public void AFinitePond_RefillsAHoleUnderItsFloor_FromItsTop()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            var hole = new Vector3i(Bx + 3, By - 1, Bz + 3); // the upper floor cell; the lower one keeps it closed
            server.RemoveBlockForTest(hole.X, hole.Y, hole.Z);

            Run(server, 5);

            Assert.True(IsWater(server, hole.X, hole.Y, hole.Z), "the hole fills");
            Assert.Equal(Size * Size * Depth - 1, BasinWater(server));      // one cell went down into it…
            Assert.Equal(Size * Size - 1, LayerWater(server, By + Depth - 1)); // …from the top layer
        }
    }

    [Fact]
    public void WallingInAPondAndFillingItWithSand_LeavesNoWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            var p = server.AddLocalPlayer("Builder");
            p.State.Role = PlayerRole.Admin;
            p.State.Inventory.Add("sand", 400, 1024);
            for (int y = By + Depth - 1; y >= By; y--)
                for (int x = Bx; x < Bx + Size; x++)
                    for (int z = Bz; z < Bz + Size; z++)
                    {
                        if (!IsWater(server, x, y, z))
                        {
                            continue;
                        }

                        p.State.Position = new Vector3f(x + 0.5f, By + Depth + 2.2f, z + 0.5f);
                        server.PlaceBlock("Builder", x, y, z, "sand");
                        Run(server, 0.3);
                    }

            Run(server, 10);

            Assert.Equal(0, BasinWater(server));
            Assert.Equal(0, CountWater(server, Bx - 12, Bx + Size + 12, By - 12, By + Depth + 2, Bz - 12, Bz + Size + 12));
        }
    }

    [Fact]
    public void ADrainingPond_PoursAStream_AndPluggingTheGapStopsTheDrain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            var p = server.AddLocalPlayer("Builder");
            p.State.Role = PlayerRole.Admin;
            p.State.Inventory.Add("stone", 10, 1024);
            var gap = new Vector3i(Bx + Size, By + Depth - 1, Bz + 3); // a gap in the east wall at the top layer
            server.RemoveBlockForTest(gap.X, gap.Y, gap.Z);

            bool stream = false;
            for (int i = 0; i < 4; i++)
            {
                server.Tick(0.3);
                stream |= server.IsFlowingFluidForTest(gap.X, gap.Y, gap.Z);
            }

            Assert.True(stream, "a draining pond pours a stream out of its gap");
            int beforePlug = BasinWater(server);
            Assert.True(beforePlug < Size * Size * Depth, "the top layer should be draining");

            p.State.Position = new Vector3f(gap.X + 0.5f, gap.Y + 3.2f, gap.Z + 0.5f);
            server.PlaceBlock("Builder", gap.X, gap.Y, gap.Z, "stone");
            Assert.Equal(_stone.Value, server.World.GetBlock(gap).Value);
            Run(server, 3);
            int afterPlug = BasinWater(server);
            Run(server, 6);

            _out.WriteLine($"before plug {beforePlug}, 3 s after {afterPlug}, 9 s after {BasinWater(server)}");
            Assert.Equal(afterPlug, BasinWater(server)); // the drain stopped
            Assert.True(afterPlug > Size * Size * (Depth - 1), "plugged early, the top layer kept part of its water");
            Assert.Equal(0, server.SettleJobCountForTest);
        }
    }

    // ---------------- placed sources (W1) ----------------

    [Fact]
    public void PlacedWater_StaysAMarkedSource_AcrossARestart_AndMiningItDropsTheMark()
    {
        var at = new Vector3i(Bx, By, Bz);
        var server = Started(out var repo, "placed");
        using (repo)
        {
            server.World.SetBlock(new Vector3i(at.X, at.Y - 1, at.Z), _stone);
            server.PlaceFluidSource("water", at.X, at.Y, at.Z);
            Assert.True(server.IsPlacedFluidSourceForTest(at.X, at.Y, at.Z));
            Run(server, 3);
        }

        var again = Started(out var repo2, "placed");
        using (repo2)
        {
            Assert.True(again.IsPlacedFluidSourceForTest(at.X, at.Y, at.Z), "the mark survives a restart");
            Assert.True(IsWater(again, at.X, at.Y, at.Z));

            again.RemoveBlockForTest(at.X, at.Y, at.Z);
            Assert.False(again.IsPlacedFluidSourceForTest(at.X, at.Y, at.Z), "a mined source loses its mark");
            Assert.DoesNotContain(repo2.ListFluidSources(again.World.LocationId), c => c.Equals(at));
        }
    }

    [Fact]
    public void TheMigration_MarksWaterPlayersPlacedBeforeTheMarksExisted_OnlyOnce()
    {
        // Learn the start world's id from a throwaway save (the galaxy is a pure function of the seed).
        string location;
        var probe = Started(out var probeRepo, "probe");
        using (probeRepo)
        {
            location = probe.World.LocationId;
        }

        var placed = new Vector3i(Bx, By, Bz);
        var flowedIn = new Vector3i(Bx + 2, By, Bz);
        using (var old = new SqliteWorldRepository(new SaveGamePaths(_root, "old")))
        {
            old.Initialize();
            old.SetBlock(location, placed, _water, owner: "Ann");                  // a block she placed
            old.SetBlock(location, flowedIn, _water, owner: "Ann");                // a hole she dug that water ran into
            old.SaveFluidCell(location, flowedIn, 5, falling: false);
        }

        var server = Started(out var repo, "old");
        using (repo)
        {
            Assert.True(server.IsPlacedFluidSourceForTest(placed.X, placed.Y, placed.Z), "placed water is marked");
            Assert.False(server.IsPlacedFluidSourceForTest(flowedIn.X, flowedIn.Y, flowedIn.Z), "flowing water is not");
            Assert.NotNull(repo.LoadNamedBlob("migration.fluid_source.v1"));

            repo.DeleteFluidSource(location, placed); // a later loss of the mark is never undone by the migration
        }

        var again = Started(out var repo2, "old");
        using (repo2)
        {
            Assert.False(again.IsPlacedFluidSourceForTest(placed.X, placed.Y, placed.Z), "the migration runs once");
        }
    }

    // ---------------- an old flood heals ----------------

    [Fact]
    public void ASaveFloodedUnderTheOldRules_RecedesOnLoad()
    {
        string location;
        var server = Started(out var repo, "flooded");
        using (repo)
        {
            Staircase(server, repo, 129, carve: false);
            server.PlaceFluidSource("water", 0, 130, 0);
            location = server.World.LocationId;
            // What the old rules left behind: full-strength flowing water on every step of the staircase.
            repo.RunInTransaction(() =>
            {
                for (int x = 1; x <= 40; x++)
                {
                    var cell = new Vector3i(x, 129 - x / 2 + 1, 0);
                    server.World.SetBlock(cell, new BlockId(_water));
                    repo.SaveFluidCell(location, cell, 8, falling: x % 2 == 0);
                }
            });
        }

        var reloaded = Started(out var repo2, "flooded");
        using (repo2)
        {
            Run(reloaded, 20);
            int farthest = FarthestWet(reloaded, 129);
            _out.WriteLine($"after the reload the water reaches x={farthest}, {repo2.ListFluidCells(location).Count} level rows left");
            Assert.True(farthest <= 9, $"the old flood recedes to the bounded run (reaches x={farthest})");
            Assert.True(IsWater(reloaded, 0, 130, 0), "the placed source stays");
        }
    }

    // ---------------- review fixes (pre-push review of the package) ----------------

    [Fact]
    public void AnEndlessSourceBesideADrainedPond_WakesWhenTheDrainEnds()
    {
        // A drain closes the cells it empties to flows (so its own stream cannot run back in). A placed source beside
        // those cells saw no air while they were closed and went to sleep — and nothing woke it when the drain ended.
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            var source = new Vector3i(Bx, By + Depth - 1, Bz);                // a top-layer corner of the basin
            server.World.SetBlock(source, BlockId.Air);
            server.PlaceFluidSource("water", source.X, source.Y, source.Z);   // endless: a boundary of the finite pond
            Run(server, 1);
            var gap = new Vector3i(Bx + Size, By + Depth - 1, Bz + Size - 1); // the far corner of the east wall
            server.RemoveBlockForTest(gap.X, gap.Y, gap.Z);

            Run(server, 25); // the top layer drains, the settle ends

            Assert.Equal(0, server.SettleJobCountForTest);
            Assert.True(IsWater(server, source.X, source.Y, source.Z), "the placed source stays");
            Assert.True(server.IsFlowingFluidForTest(source.X + 1, source.Y, source.Z)
                        || server.IsFlowingFluidForTest(source.X, source.Y, source.Z + 1),
                "the source flows into the drained layer once the drain is over");
        }
    }

    [Fact]
    public void ALakeJustOverTheCap_StaysOneReservoir_WhenTouchedAgainElsewhere()
    {
        // The first touch marked the 2 049 cells it reached as a reservoir; a later touch at the far end used to treat
        // those as a boundary and drain the rest of the lake as its own finite body.
        var server = Started(out var repo);
        using (repo)
        {
            const int width = 33, length = 32; // 33 x 32 x 2 = 2 112 cells: just over the cap
            repo.RunInTransaction(() =>
            {
                for (int x = Bx - 1; x <= Bx + width; x++)
                    for (int z = Bz - 1; z <= Bz + length; z++)
                    {
                        server.World.SetBlock(new Vector3i(x, By - 1, z), _stone);
                        bool wall = x == Bx - 1 || x == Bx + width || z == Bz - 1 || z == Bz + length;
                        for (int y = By; y < By + 2; y++)
                        {
                            server.World.SetBlock(new Vector3i(x, y, z), wall ? _stone : new BlockId(_water));
                        }
                    }
            });

            server.RemoveBlockForTest(Bx - 1, By + 1, Bz);                     // a gap at one corner
            Run(server, 1);
            server.RemoveBlockForTest(Bx + width, By + 1, Bz + length - 1);    // and later one at the far corner
            Run(server, 5);

            Assert.Equal(0, server.SettleJobCountForTest);
            Assert.Equal(width * length * 2, CountWater(server, Bx, Bx + width - 1, By, By + 1, Bz, Bz + length - 1));
        }
    }

    [Fact]
    public void ACellThatMetABusySettle_IsRetriedWhenTheSettleEnds()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            // A one-move settle: the floor under the centre opens into a closed one-cell hole.
            server.World.SetBlock(new Vector3i(Bx + 3, By - 1, Bz + 3), BlockId.Air);
            Assert.True(server.TrySettleForTest(Bx + 3, By, Bz + 3));
            Assert.Equal(1, server.SettleJobCountForTest);

            // A puddle outside the west wall, joined to the settling pond through the wall — and open to the air on its far
            // side. Its settle meets the busy pond and must not be forgotten.
            var bridge = new Vector3i(Bx - 1, By + Depth - 1, Bz + 3);
            var puddle = new Vector3i(Bx - 2, By + Depth - 1, Bz + 3);
            server.World.SetBlock(new Vector3i(puddle.X, puddle.Y - 1, puddle.Z), _stone);
            server.World.SetBlock(bridge, new BlockId(_water));
            server.World.SetBlock(puddle, new BlockId(_water));
            Assert.True(server.TrySettleForTest(puddle.X, puddle.Y, puddle.Z));
            Assert.Equal(1, server.SettleWaitingCountForTest);

            Run(server, 20); // the one move runs, the settle ends and wakes the waiting puddle, which then settles

            Assert.Equal(0, server.SettleWaitingCountForTest);
            Assert.False(IsWater(server, puddle.X, puddle.Y, puddle.Z),
                "the retried puddle settles with the pond it joined: the top layer runs out through its open side");
        }
    }

    [Fact]
    public void AWokenPondAtRest_IsPlannedAboutOnceASecond_NotOncePerCellAndStep()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            // Six one-cell pockets beside the top layer: open sides at the water's own height with nowhere lower to go.
            var pockets = new List<Vector3i>();
            for (int i = 0; i < 6; i++)
            {
                var pocket = new Vector3i(Bx + Size, By + Depth - 1, Bz + i);
                server.World.SetBlock(pocket, BlockId.Air);
                server.World.SetBlock(new Vector3i(pocket.X + 1, pocket.Y, pocket.Z), _stone);
                server.World.SetBlock(new Vector3i(pocket.X, pocket.Y + 1, pocket.Z), _stone);
                pockets.Add(pocket);
            }

            int before = server.SettleStatsForTest.Count;
            for (int t = 0; t < 10; t++) // ten fluid steps, every pocket touched before each
            {
                foreach (var p in pockets)
                {
                    server.RemoveBlockForTest(p.X, p.Y, p.Z);
                }

                server.Tick(0.3);
            }

            int plans = server.SettleStatsForTest.Count - before;
            _out.WriteLine($"{plans} plan(s) over ten steps");
            Assert.True(plans <= 3, $"one pond at rest, touched six times a step for ten steps, was planned {plans} times");
            Assert.Equal(Size * Size * Depth, BasinWater(server));
        }
    }

    [Fact]
    public void ALongSealedChannel_IsNotMistakenForAnOpenDrain_ByItsWalls()
    {
        // A one-wide channel of 1 500 cells has about 4 500 wall cells around it. The exploration cap counted those, so
        // the sealed channel was judged open and its whole layer deleted when a one-cell hole opened at its end.
        var server = Started(out var repo);
        using (repo)
        {
            const int length = 1500;
            repo.RunInTransaction(() =>
            {
                for (int x = Bx - 1; x <= Bx + length; x++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        server.World.SetBlock(new Vector3i(x, By - 1, Bz + dz), _stone);
                        server.World.SetBlock(new Vector3i(x, By - 2, Bz + dz), _stone);
                    }

                    bool end = x == Bx - 1 || x == Bx + length;
                    server.World.SetBlock(new Vector3i(x, By, Bz - 1), _stone);
                    server.World.SetBlock(new Vector3i(x, By, Bz + 1), _stone);
                    server.World.SetBlock(new Vector3i(x, By, Bz), end ? _stone : new BlockId(_water));
                }
            });

            server.RemoveBlockForTest(Bx + length - 1, By - 1, Bz); // a closed one-cell hole under the far end
            Run(server, 5);

            int water = CountWater(server, Bx, Bx + length - 1, By - 1, By, Bz, Bz);
            Assert.Equal(length, water); // one cell moved down into the hole, none was lost
            Assert.True(IsWater(server, Bx + length - 1, By - 1, Bz));
        }
    }

    [Fact]
    public void PlacedWaterMarks_LoadOnAStationWorld_Too()
    {
        // A station is a void world: it never runs the planet fluid boot that used to load the marks.
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "station"));
        var config = new ServerConfig { WorldName = "station", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string playerId = pilot.State.PlayerId;
            server.EnterSpace(playerId);
            pilot.State.InEva = true;
            pilot.State.InstantBuild = true;
            server.DeployStationCoreForTest(playerId);
            string id = server.OwnedStationIdForTest(playerId)!;
            for (int i = 1; i <= 11; i++)
            {
                server.HandleStructureEditForTest(playerId, new BlocksBeyondTheStars.Networking.Messages.StructureEditIntent
                {
                    StructureId = id,
                    X = i,
                    Y = 0,
                    Z = 0,
                    Mine = false,
                    ItemKey = "iron_wall",
                });
            }

            server.HandleStructureEditForTest(playerId, new BlocksBeyondTheStars.Networking.Messages.StructureEditIntent
            {
                StructureId = id,
                X = 0,
                Y = 1,
                Z = 0,
                Mine = false,
                ItemKey = "door_slide",
            });
            var contact = server.SpaceEntitiesFor(playerId).First(e => e.Id == id);
            server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            server.BoardStation(playerId, id);
            Assert.True(server.InStation(playerId));

            string location = server.World.LocationId;
            var cell = new Vector3i(9, 66, 9);
            repo.SaveFluidSource(location, cell); // a mark a previous session left on this station
            Assert.True(server.IsPlacedFluidSourceForTest(cell.X, cell.Y, cell.Z), "the station's marks load on first use");
        }
    }

    // ---------------- cost ----------------

    [Fact]
    public void TheSettle_StaysCheap_AndABodyBeyondTheCapIsAReservoir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // A 32x32x2 tank (2 048 cells — exactly the settle cap) and, beside it, a 33x32x2 one (2 112 — beyond it).
            void Tank(int x0, int width)
            {
                repo.RunInTransaction(() =>
                {
                    for (int x = x0 - 1; x <= x0 + width; x++)
                        for (int z = Bz - 1; z <= Bz + 32; z++)
                        {
                            server.World.SetBlock(new Vector3i(x, By - 1, z), _stone);
                            bool wall = x == x0 - 1 || x == x0 + width || z == Bz - 1 || z == Bz + 32;
                            for (int y = By; y < By + 2; y++)
                            {
                                server.World.SetBlock(new Vector3i(x, y, z), wall ? _stone : new BlockId(_water));
                            }
                        }
                });
            }

            Tank(Bx, 32);
            Tank(Bx + 60, 33);
            server.RemoveBlockForTest(Bx - 1, By + 1, Bz + 16);      // breach the small tank
            server.RemoveBlockForTest(Bx + 59, By + 1, Bz + 16);     // and the big one
            server.Tick(0.3);
            var (count, maxMs, totalMs) = server.SettleStatsForTest;
            _out.WriteLine($"{count} settle plan(s), slowest {maxMs:F2} ms, total {totalMs:F2} ms");
            Assert.Equal(1, server.SettleJobCountForTest); // the tank at the cap settles; the one beyond it is a reservoir
            Assert.True(maxMs < 250, $"one settle of a body at the cap took {maxMs:F1} ms");

            Run(server, 3);
            Assert.True(server.IsFlowingFluidForTest(Bx + 59, By + 1, Bz + 16), "the reservoir spills like an endless source");

            // The running settle re-plans once a second over the same body — warm: no JIT and the chunks it reads are
            // resident, as around a player. The first plan above also generated the terrain under the breach.
            var (replans, replanMax, replanTotal) = server.SettleReplanStatsForTest;
            _out.WriteLine($"{replans} re-plan(s) of the 2 048-cell body: mean {replanTotal / Math.Max(1, replans):F2} ms, slowest {replanMax:F2} ms");
            Assert.True(replans > 0, "a running settle re-plans about once a second");
        }
    }
}
