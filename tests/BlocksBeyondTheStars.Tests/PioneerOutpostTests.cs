// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Pioneer outposts (#2681): the closed room the generator builds (materials, the decorative core, the owned crates, the
/// bed and door, the tunnel to the cellar — visible or hidden under a planter — and the boat), where the stamper puts them
/// (new breathable worlds only, within their own cap, never on a visited body), and what the server makes of a stamped one
/// (protection with the one free soil cell, owned stores, no base, the map). The pioneer is <see cref="PioneerTests"/>.
/// </summary>
public sealed class PioneerOutpostTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A lush generation-25 world with lodges …</summary>
    private const string LushPlanet = "meadowlands";

    /// <summary>… and with this seed an outpost (at the time of writing) — the seed the gate tests switch things off on.</summary>
    private const long OutpostSeed = 1;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_outpost_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private SvGameServer Start(string save, string planet, long seed, out SqliteWorldRepository repo, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static PioneerOutpostGenerator.Outpost Build(long seed, string? wall = null, bool? hidden = null, int chests = 0, string surface = "grass")
        => PioneerOutpostGenerator.Generate(seed, surface, Content, wall, hidden, chests);

    /// <summary>The block at a local cell of the whole outpost — the structure from the floor row up, the cellar below.</summary>
    private static ushort At(PioneerOutpostGenerator.Outpost o, int x, int y, int z)
    {
        if (y >= 0)
        {
            var s = o.Structure;
            return x < 0 || z < 0 || x >= s.Width || z >= s.Length || y >= s.Height ? (ushort)0 : s.Get(x, y, z);
        }

        foreach (var (local, block, _) in o.Below)
        {
            if (local.X == x && local.Y == y && local.Z == z)
            {
                return block;
            }
        }

        return Id("stone"); // the ground round the cellar
    }

    private static bool Open(ushort block)
        => block == 0 || WalkThroughBlocks.IsProp(Content.BlockById(new Shared.Primitives.BlockId(block))?.Key);

    // ---- the generator -------------------------------------------------------------------------------------------

    [Fact]
    public void TheGenerator_IsDeterministic()
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var a = Build(seed);
            var b = Build(seed);
            Assert.Equal((a.WallKind, a.HiddenShaft, a.Chests), (b.WallKind, b.HiddenShaft, b.Chests));
            for (int x = 0; x < a.Structure.Width; x++)
                for (int y = 0; y < a.Structure.Height; y++)
                    for (int z = 0; z < a.Structure.Length; z++)
                    {
                        Assert.Equal(a.Structure.Get(x, y, z), b.Structure.Get(x, y, z));
                        Assert.Equal(a.Structure.GetShape(x, y, z), b.Structure.GetShape(x, y, z));
                    }

            Assert.Equal(a.Below, b.Below);
            Assert.Equal(a.Structure.Markers.Select(m => (m.Type, m.LocalPos)), b.Structure.Markers.Select(m => (m.Type, m.LocalPos)));
        }
    }

    [Fact]
    public void TheWalls_AreStoneEarthIronOrSteel_OnePerOutpost_AndEarthNeverSlumps()
    {
        Assert.Equal("dirt", PioneerOutpostGenerator.EarthBlockKey("grass"));
        Assert.Equal("sandstone", PioneerOutpostGenerator.EarthBlockKey("sand"));
        Assert.Equal("ice", PioneerOutpostGenerator.EarthBlockKey("snow"));
        Assert.Equal("basalt", PioneerOutpostGenerator.EarthBlockKey("ash"));
        Assert.Equal("mud", PioneerOutpostGenerator.EarthBlockKey("mud"));
        var expected = new Dictionary<string, string>
        {
            [PioneerOutpostGenerator.WallStone] = "stone",
            [PioneerOutpostGenerator.WallEarth] = "dirt",
            [PioneerOutpostGenerator.WallIron] = "iron_wall",
            [PioneerOutpostGenerator.WallSteel] = "steel_wall",
        };
        foreach (var (kind, key) in expected)
        {
            var o = Build(5, wall: kind);
            Assert.Equal(kind, o.WallKind);
            Assert.Equal(Id(key), o.WallBlock);
            Assert.Equal(Id(key), At(o, 1, 1, 1)); // the corner
            Assert.Equal(Id(key), At(o, 5, 4, 5)); // the roof
        }

        var rolled = Enumerable.Range(1, 60).Select(s => Build(s).WallKind).Distinct().ToList();
        Assert.Equal(4, rolled.Count); // every material turns up across a few outposts
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRoom_IsFullyClosed_SaveItsDoorsAndTheShaft(bool hidden)
    {
        foreach (string wall in PioneerOutpostGenerator.WallKinds)
        {
            var o = Build(11, wall: wall, hidden: hidden);
            var doors = o.Structure.Markers.Where(m => m.Type == PioneerOutpostGenerator.MarkerDoor).ToList();
            var doorCells = new HashSet<(int, int, int)>();
            foreach (var d in doors)
            {
                for (int y = 1; y <= 3; y++)
                {
                    doorCells.Add((d.LocalPos.X, y, d.LocalPos.Z));
                    doorCells.Add((d.LocalPos.X + 1, y, d.LocalPos.Z)); // a two-wide doorway's other half
                }
            }

            // Every open cell inside the room only borders the room, a wall, a doorway (a hinge door hangs there) or the shaft.
            for (int x = 2; x <= 8; x++)
                for (int y = 1; y <= 3; y++)
                    for (int z = 2; z <= 8; z++)
                    {
                        if (!Open(At(o, x, y, z)))
                        {
                            continue;
                        }

                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        {
                            int nx = x + dx, ny = y + dy, nz = z + dz;
                            bool inside = nx is >= 2 and <= 8 && ny is >= 1 and <= 3 && nz is >= 2 and <= 8;
                            bool shaft = nx == PioneerOutpostGenerator.ShaftX && nz == PioneerOutpostGenerator.ShaftZ && ny == 0;
                            Assert.True(inside || shaft || doorCells.Contains((nx, ny, nz)) || !Open(At(o, nx, ny, nz)),
                                $"{wall}/{(hidden ? "hidden" : "visible")}: the room leaks at {nx},{ny},{nz}");
                        }
                    }

            // The roof and the floor are whole (the floor holds the shaft's top only).
            for (int x = 1; x <= 9; x++)
                for (int z = 1; z <= 9; z++)
                {
                    Assert.False(Open(At(o, x, 4, z)), "the roof has a hole");
                    bool hatch = x == PioneerOutpostGenerator.ShaftX && z == PioneerOutpostGenerator.ShaftZ;
                    Assert.True(hatch || !Open(At(o, x, 0, z)), "the floor has a hole");
                }
        }
    }

    [Fact]
    public void Inside_ADecorativeCore_TwoToFourCrates_ABed_AChair_AndTheDoor()
    {
        ushort core = Id("base_core"), crate = Id("crate"), bed = Id("bed");
        var counts = new HashSet<int>();
        for (long seed = 1; seed <= 30; seed++)
        {
            var o = Build(seed);
            var m = o.Structure.Markers;
            var c = Assert.Single(m, x => x.Type == PioneerOutpostGenerator.MarkerCore);
            Assert.Equal(core, At(o, c.LocalPos.X, c.LocalPos.Y, c.LocalPos.Z));
            var stores = m.Where(x => x.Type == PioneerOutpostGenerator.MarkerStore).ToList();
            Assert.InRange(stores.Count, PioneerOutpostGenerator.MinChests, PioneerOutpostGenerator.MaxChests);
            Assert.Equal(o.Chests, stores.Count);
            Assert.All(stores, s => Assert.Equal(crate, At(o, s.LocalPos.X, s.LocalPos.Y, s.LocalPos.Z)));
            counts.Add(stores.Count);
            var b = Assert.Single(m, x => x.Type == PioneerOutpostGenerator.MarkerBed);
            Assert.Equal(bed, At(o, b.LocalPos.X, b.LocalPos.Y, b.LocalPos.Z));
            Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(o.Structure.GetShape(b.LocalPos.X, b.LocalPos.Y, b.LocalPos.Z)));
            var seat = Assert.Single(m, x => x.Type == PioneerOutpostGenerator.MarkerSeat);
            Assert.True(FurnitureShapes.IsSeat(ShapeCode.ShapeOf(o.Structure.GetShape(seat.LocalPos.X, seat.LocalPos.Y, seat.LocalPos.Z))));
            var front = m.Where(x => x.Type == PioneerOutpostGenerator.MarkerDoor).ToList();
            Assert.Contains(front, d => d.LocalPos.Z == 9 && d.DoorAxis == DoorWall.AlongX); // the front door
            Assert.Equal(o.HiddenShaft ? 1 : 2, front.Count); // the visible tunnel has its closet door
            Assert.Single(m, x => x.Type == PioneerOutpostGenerator.MarkerYard);
            Assert.Single(o.BelowMarkers, x => x.Type == PioneerOutpostGenerator.MarkerCellarStore);
        }

        Assert.Equal(new[] { 2, 3, 4 }, counts.OrderBy(n => n));
    }

    /// <summary>A walk through open cells (air, ladders, plants, doorways) from the room to the cellar box — no block is
    /// mined; with <paramref name="digSoil"/> the planter soil over a hidden shaft counts as dug.</summary>
    private static bool CellarReachable(PioneerOutpostGenerator.Outpost o, bool digSoil)
    {
        var box = o.BelowMarkers.Single(m => m.Type == PioneerOutpostGenerator.MarkerCellarStore).LocalPos;
        var soil = o.Structure.Markers.Where(m => m.Type == PioneerOutpostGenerator.MarkerSoil).Select(m => m.LocalPos).ToHashSet();
        var start = (4, 1, 8); // just inside the front door
        var seen = new HashSet<(int, int, int)> { start };
        var queue = new Queue<(int X, int Y, int Z)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            if (Math.Abs(x - box.X) + Math.Abs(y - box.Y) + Math.Abs(z - box.Z) == 1)
            {
                return true; // standing beside the box
            }

            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                var n = (x + dx, y + dy, z + dz);
                if (n.Item1 is < 1 or > 9 || n.Item3 is < 1 or > 9 || n.Item2 is < -PioneerOutpostGenerator.CellarDepth or > 3 || !seen.Add(n))
                {
                    continue;
                }

                bool dug = digSoil && soil.Contains(new Vector3i(n.Item1, n.Item2, n.Item3));
                if (dug || Open(At(o, n.Item1, n.Item2, n.Item3)))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return false;
    }

    [Fact]
    public void TheTunnel_LeadsDownALadder_ToTheCellar_WithoutMining_AndTheHiddenOneOnlyOnceTheSoilIsDug()
    {
        ushort ladder = Id("ladder");
        var visible = Build(3, hidden: false);
        Assert.True(CellarReachable(visible, digSoil: false), "the visible shaft is reachable on foot");
        for (int y = -PioneerOutpostGenerator.CellarDepth + 1; y <= 1; y++)
        {
            Assert.Equal(ladder, At(visible, PioneerOutpostGenerator.ShaftX, y, PioneerOutpostGenerator.ShaftZ)); // one ladder all the way
        }

        var hidden = Build(3, hidden: true);
        var soil = Assert.Single(hidden.Structure.Markers, m => m.Type == PioneerOutpostGenerator.MarkerSoil).LocalPos;
        Assert.Equal(Id("dirt"), At(hidden, soil.X, soil.Y, soil.Z)); // loose soil …
        Assert.StartsWith("flora_", Content.BlockById(new Shared.Primitives.BlockId(At(hidden, soil.X, 1, soil.Z)))!.Key, StringComparison.Ordinal); // … under a plant
        Assert.False(CellarReachable(hidden, digSoil: false), "the hidden shaft is hidden");
        Assert.True(CellarReachable(hidden, digSoil: true), "behind the soil the ladder leads down");
        for (int y = -PioneerOutpostGenerator.CellarDepth + 1; y <= -1; y++)
        {
            Assert.Equal(ladder, At(hidden, PioneerOutpostGenerator.ShaftX, y, PioneerOutpostGenerator.ShaftZ));
        }

        // The cellar is closed all round: its open cells only border the cellar or the shaft.
        var box = hidden.BelowMarkers.Single(m => m.Type == PioneerOutpostGenerator.MarkerCellarStore).LocalPos;
        Assert.Equal(Id("wood_crate"), At(hidden, box.X, box.Y, box.Z)); // one small box
        Assert.Single(hidden.Below, c => c.Block == Id("wood_crate"));
    }

    [Fact]
    public void TheBoat_IsLogs_SixLongThreeWide_WithADeckAndALantern()
    {
        foreach (bool alongX in new[] { true, false })
        {
            var cells = PioneerOutpostGenerator.Boat(alongX, Content);
            Assert.NotEmpty(cells);
            int maxAlong = cells.Max(c => alongX ? c.Local.X : c.Local.Z), maxAcross = cells.Max(c => alongX ? c.Local.Z : c.Local.X);
            Assert.Equal((PioneerOutpostGenerator.BoatLength - 1, PioneerOutpostGenerator.BoatWidth - 1), (maxAlong, maxAcross));
            Assert.All(cells, c => Assert.Contains(Content.BlockById(new Shared.Primitives.BlockId(c.Block))!.Key, new[] { "wood_log", "lantern" }));
            Assert.Contains(cells, c => ShapeCode.ShapeOf(c.Shape) == (int)BlockShape.Slab); // the deck
            Assert.Single(cells, c => c.Block == Id("lantern"));
            Assert.Equal(cells.Count, cells.Select(c => c.Local).Distinct().Count());
        }
    }

    [Fact]
    public void TheBoatsSpot_IsOpenWaterBesideTheOutpost_AndThereIsNoneOnADryWorld()
    {
        var ocean = Content.GetPlanet("ocean")!;
        var gen = new WorldGenerator(7, Content);
        const int circ = 1_000_000; // no seam within the search
        const int len = PioneerOutpostGenerator.BoatLength, wid = PioneerOutpostGenerator.BoatWidth;

        bool Fits(int x0, int z0, out int top)
        {
            top = int.MinValue;
            for (int dx = 0; dx < len; dx++)
                for (int dz = 0; dz < wid; dz++)
                {
                    if (!gen.TryGetWaterSurface(ocean, x0 + dx, z0 + dz, out int t, out int bed) || t - bed < 2
                        || gen.SurfaceIceThickness(ocean, x0 + dx, z0 + dz) > 0 || (top != int.MinValue && t != top))
                    {
                        return false;
                    }

                    top = t;
                }

            return true;
        }

        int wx = int.MinValue, wz = 0, wtop = 0;
        for (int x = 0; x < 3000 && wx == int.MinValue; x += 9)
        {
            for (int z = -300; z <= 300; z += 9)
            {
                if (Fits(x, z, out int t))
                {
                    (wx, wz, wtop) = (x, z, t);
                    break;
                }
            }
        }

        Assert.True(wx != int.MinValue, "the ocean world has open water somewhere near its origin");
        // An outpost whose front edge stands two blocks from that water, its floor one above the water.
        var origin = new Vector3i(wx, wtop + 1, wz - (PioneerOutpostGenerator.Size - 1) - 2);
        string spec = SvGameServer.FindOutpostBoatSpot(gen, ocean, circ, origin, wtop + 1);
        Assert.NotEqual(string.Empty, spec);
        var parts = spec.Split(',');
        int bx = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        int by = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        int bz = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        bool alongX = parts[3] == "x";
        Assert.InRange(by, wtop + 1 - 6, wtop + 1); // at most six below the floor
        for (int a = 0; a < len; a++)
            for (int b = 0; b < wid; b++)
            {
                int x = alongX ? bx + a : bx + b, z = alongX ? bz + b : bz + a;
                Assert.True(gen.TryGetWaterSurface(ocean, x, z, out int t, out _) && t == by, $"the boat floats on water at {x},{z}");
            }

        // A world without water has no boat anywhere.
        var dry = Content.GetPlanet("ashen_ocean")!;
        Assert.Equal(string.Empty, SvGameServer.FindOutpostBoatSpot(new WorldGenerator(7, Content), dry, circ, new Vector3i(0, 60, 0), 60));
    }

    // ---- placement -----------------------------------------------------------------------------------------------

    [Fact]
    public void ALushGeneration25World_GrowsItsOwnOutposts_WithinTheirCap_ClearOfTheHuts_AndTheSameAfterAReload()
    {
        List<(string Key, Vector3i Origin, string Wall, bool Hidden, string Stores)> first = new();
        long seedWithOutposts = -1;
        for (long seed = 1; seed <= 8 && seedWithOutposts < 0; seed++)
        {
            var server = Start("lush" + seed, LushPlanet, seed, out var repo);
            using (repo)
            {
                var outposts = server.OutpostsForTest;
                Assert.InRange(outposts.Count, 0, SvGameServer.PioneerHardCap);
                Assert.True(server.FeatureStampedForTest("outposts") == outposts.Count > 0);
                if (outposts.Count > 0)
                {
                    seedWithOutposts = seed;
                    int circ = server.World.Circumference;
                    foreach (var o in outposts)
                    {
                        foreach (var l in server.LodgesForTest)
                        {
                            int dx = Math.Abs(WorldConstants.WrapDeltaX(o.Origin.X - l.Origin.X, circ));
                            int dz = Math.Abs(o.Origin.Z - l.Origin.Z);
                            Assert.True(Math.Max(dx, dz) >= LodgeGenerator.Size + 20, $"outpost {o.Key} crowds the hut {l.Key}");
                        }

                        Assert.True(server.IsLodgeBlockForTest(new Vector3i(o.Origin.X + 5, o.Origin.Y + 2, o.Origin.Z + 1)), "the wall is protected");
                        Assert.True(server.IsLodgeBlockForTest(new Vector3i(o.Origin.X + 6, o.Origin.Y - 3, o.Origin.Z + 6)), "the cellar is protected");
                        Assert.False(o.Revealed);
                        Assert.InRange(o.StoreIds.Count, PioneerOutpostGenerator.MinChests + 1, PioneerOutpostGenerator.MaxChests + 1);
                        Assert.All(o.StoreIds, id => Assert.Contains(server.Containers, c => c.Id == id && c.Kind == "crate" && c.Items.Count > 0));
                        Assert.NotNull(server.PioneerForTest(o.Key));
                        first.Add((o.Key, o.Origin, o.WallKind, o.Hidden, string.Join("|", o.StoreIds)));
                    }

                    var records = server.PlacementRecordsForTest.Where(r => r.Kind == "outpost" && r.Placed).ToList();
                    Assert.Equal(outposts.Count, records.Count);
                    Assert.All(records, r => Assert.Contains(r.Template, new[] { "gatherer", "miner", "hunter" })); // never felling
                }

                server.Stop();
            }
        }

        Assert.True(seedWithOutposts > 0, "a lush world among the first eight seeds has an outpost");
        var again = Start("lush" + seedWithOutposts, LushPlanet, seedWithOutposts, out var repo2);
        using (repo2)
        {
            Assert.Equal(first, again.OutpostsForTest.Select(o => (o.Key, o.Origin, o.WallKind, o.Hidden, string.Join("|", o.StoreIds))).ToList());
            int stores = first.Sum(o => o.Stores.Split('|').Length);
            Assert.Equal(stores, again.Containers.Count(c => c.Id.StartsWith(SvGameServer.OutpostStoreIdPrefix, StringComparison.Ordinal))); // never twice
            again.Stop();
        }
    }

    [Fact]
    public void NoOutpost_WithPioneersOrLodgesOff_OnAnOlderOrUnbreathableWorld_OrOnABodySomebodyVisited()
    {
        foreach (var (name, planet, configure) in new (string, string, Action<ServerConfig>)[]
                 {
                     ("off", LushPlanet, c => c.PlacePioneers = false),
                     ("nolodges", LushPlanet, c => c.PlaceLodges = false),
                     ("gen24", LushPlanet, c => c.World.TerrainGeneration = 24),
                     ("toxic", "toxic_world", _ => { }),
                     ("crystal", "crystal", _ => { }),
                 })
        {
            var server = Start(name, planet, OutpostSeed, out var repo, configure);
            using (repo)
            {
                Assert.Empty(server.OutpostsForTest);
                Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == "outpost" && r.Placed);
                server.Stop();
            }
        }

        // The same world new does grow one …
        var fresh = Start("fresh", LushPlanet, OutpostSeed, out var r0);
        using (r0)
        {
            Assert.NotEmpty(fresh.OutpostsForTest);
            fresh.Stop();
        }

        // … but materialised without outposts (here: pioneers off at first), it never grows one later.
        var before = Start("visited", LushPlanet, OutpostSeed, out var r1, c => c.PlacePioneers = false);
        using (r1)
        {
            before.Stop();
        }

        var after = Start("visited", LushPlanet, OutpostSeed, out var r2);
        using (r2)
        {
            Assert.Empty(after.OutpostsForTest);
            Assert.Contains(after.PlacementRecordsForTest, r => r.Kind == "outpost" && !r.Placed); // the skip is pinned
            after.Stop();
        }
    }

    // ---- on the arena: protection, the stores, the base core, the map ----------------------------------------------

    private static string Outpost(NpcLifeWorld w, bool hidden, long seed = 3, string wall = "stone", int chests = 3)
        => w.Server.SpawnPioneerOutpostForTest(NpcLifeWorld.Cx - 7, w.PadY, NpcLifeWorld.Cz - 7, seed, wall, hidden, chests);

    private static Vector3i Local(NpcLifeWorld w, int x, int y, int z) => new(NpcLifeWorld.Cx - 7 + x, w.PadY + y, NpcLifeWorld.Cz - 7 + z);

    private static string? LastRejection(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;

    [Fact]
    public void AnOutpost_IsProtected_WallsCoreAndCellar_ButThePlanterSoilAndItsPlantAreFree()
    {
        using var w = new NpcLifeWorld(Content, "outpost_protect");
        string key = Outpost(w, hidden: true);
        string name = w.Server.PioneerForTest(key)!.Value.Name;
        var p = w.Player.State;
        p.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

        void StandBy(Vector3i c) => p.Position = new Vector3f(c.X + 0.5f, c.Y + 1, c.Z - 1.5f);

        foreach (var cell in new[] { Local(w, 1, 1, 1), Local(w, 5, 1, 5), Local(w, 5, 4, 5), Local(w, 4, -5, 4) })
        {
            StandBy(cell);
            var before = w.Server.World.GetBlock(cell);
            Assert.True(w.Server.IsLodgeBlockForTest(cell), $"{cell} is the outpost's");
            w.Server.MineBlock(NpcLifeWorld.Owner, cell.X, cell.Y, cell.Z);
            Assert.Equal(before, w.Server.World.GetBlock(cell));
            Assert.Equal("@srv.protect.outpost:" + name, LastRejection(w));
        }

        // The decorative core founds no base, and the boat-less outpost protects nothing outside its footprint.
        Assert.False(w.Server.BaseFoundedAtForTest(Local(w, 5, 1, 5)));
        Assert.False(w.Server.IsLodgeBlockForTest(Local(w, 5, 1, PioneerOutpostGenerator.Size + 2)));

        // The plant on the planter is picked like any plant; the soil under it is the one cell a visitor may dig.
        var plant = Local(w, PioneerOutpostGenerator.ShaftX, 1, PioneerOutpostGenerator.ShaftZ);
        var soil = Local(w, PioneerOutpostGenerator.ShaftX, 0, PioneerOutpostGenerator.ShaftZ);
        Assert.False(w.Server.IsLodgeBlockForTest(soil));
        Assert.True(w.Server.IsLodgeBlockForTest(Local(w, PioneerOutpostGenerator.ShaftX - 1, 0, PioneerOutpostGenerator.ShaftZ)), "the other planter is the outpost's");
        p.Position = new Vector3f(plant.X - 1.5f, w.Feet, plant.Z + 0.5f);
        w.Server.MineBlock(NpcLifeWorld.Owner, plant.X, plant.Y, plant.Z);
        Assert.True(w.Server.World.GetBlock(plant).IsAir, "the plant is picked");
        w.Server.MineBlock(NpcLifeWorld.Owner, soil.X, soil.Y, soil.Z);
        Assert.True(w.Server.World.GetBlock(soil).IsAir, "the soil is dug");
        Assert.Equal("ladder", Content.BlockById(w.Server.World.GetBlock(Local(w, PioneerOutpostGenerator.ShaftX, -1, PioneerOutpostGenerator.ShaftZ)))!.Key);
    }

    [Fact]
    public void TheCratesAndTheCellarBox_HoldGoods_AndAreThePioneers_LookDontTake()
    {
        using var w = new NpcLifeWorld(Content, "outpost_stores");
        string key = Outpost(w, hidden: false, chests: 4);
        string name = w.Server.PioneerForTest(key)!.Value.Name;
        var o = w.Server.OutpostsForTest.Single(x => x.Key == key);
        Assert.Equal(5, o.StoreIds.Count); // four crates and the cellar's box
        Assert.Contains(o.CellarStoreId, o.StoreIds);
        var p = w.Player.State;
        foreach (string id in o.StoreIds)
        {
            Assert.True(SvGameServer.IsWildernessStoreId(id));
            var crate = w.Server.Containers.Single(c => c.Id == id);
            Assert.Equal("crate", crate.Kind);
            Assert.NotEmpty(crate.Items);
            p.Position = new Vector3f(crate.Position.X + 0.5f, crate.Position.Y, crate.Position.Z + 1.5f);
            int mark = w.Transport.Sent.Count;
            w.Server.OpenContainerForTest(NpcLifeWorld.Owner, id);
            Assert.Contains(w.Transport.Sent.Skip(mark).Select(s => s.Msg).OfType<ContainerContents>(), c => c.ContainerId == id); // looking is fine
            var items = crate.Items.Select(s => (s.Item, s.Count)).ToList();
            w.Server.LootContainer(p.PlayerId, id);
            Assert.Equal("@srv.lodge.store_owned:" + name, LastRejection(w));
            Assert.Equal(items, w.Server.Containers.Single(c => c.Id == id).Items.Select(s => (s.Item, s.Count)));
        }

        // The cellar box is a small wooden one, below the floor.
        var box = w.Server.Containers.Single(c => c.Id == o.CellarStoreId);
        Assert.Equal("wood_crate", Content.BlockById(w.Server.World.GetBlock(box.Position))!.Key);
        Assert.True(box.Position.Y < w.PadY);
    }

    [Fact]
    public void AnOutpost_IsOffTheMap_UntilFound_ThenItIsTheirOutpost()
    {
        using var w = new NpcLifeWorld(Content, "outpost_poi");
        string key = Outpost(w, hidden: false);
        string name = w.Server.PioneerForTest(key)!.Value.Name;
        var center = w.Server.OutpostsForTest.Single(o => o.Key == key).Center;
        var p = w.Player.State;
        p.Position = new Vector3f(center.X - 60f, w.Feet, center.Z);
        w.Server.TickLodgesForTest();
        Assert.DoesNotContain(w.Server.PlanetPoisForTest(p.PlayerId), poi => poi.Type == "outpost");

        p.Position = new Vector3f(center.X - 30f, w.Feet, center.Z);
        w.Server.TickLodgesForTest();
        var poi = Assert.Single(w.Server.PlanetPoisForTest(p.PlayerId), x => x.Type == "outpost");
        Assert.Equal(name + "'s outpost", poi.Name);
        Assert.DoesNotContain(w.Server.PlanetPoisForTest(p.PlayerId), x => x.Type == "lodge"); // an outpost is no hut
        Assert.Empty(w.Server.LodgesForTest); // nor does it count as one
    }

    [Fact]
    public void ABoatBesideTheOutpost_IsProtectedLikeIt()
    {
        using var w = new NpcLifeWorld(Content, "outpost_boat");
        // The arena has no water, so the boat is laid by hand beside the outpost (the spot search is tested above).
        var corner = Local(w, 3, 0, PioneerOutpostGenerator.Size + 3);
        string spec = $"{corner.X},{corner.Y},{corner.Z},x";
        string key = w.Server.SpawnPioneerOutpostForTest(NpcLifeWorld.Cx - 7, w.PadY, NpcLifeWorld.Cz - 7, 3, "steel", false, 2, boat: spec);
        var cells = w.Server.OutpostBoatForTest(key);
        Assert.Equal(PioneerOutpostGenerator.Boat(true, Content).Count, cells.Count);
        Assert.All(cells, c => Assert.True(w.Server.IsLodgeBlockForTest(c), $"the boat's {c} is protected"));
        Assert.All(cells, c => Assert.False(w.Server.World.GetBlock(c).IsAir));
    }
}
