// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3612 / #3613: a DERIVED station structure the size of a giant wreck — 200 000 cells, a hollow box with decks and rooms —
/// travels to the pilots as its visible shell in parts under the 1 MB cap; a spacewalk edit sends single cells (the cell
/// itself, and the walls of a hall it lays open), never the design, and writes the players' changes as one small delta
/// blob, never a cell row; a restart rebuilds base + changes; the first boarding lays the interior down as ONE structure
/// overlay (no block edit per cell) and sets the boarder down at the entrance; the edit range reaches along the hull.
/// </summary>
public sealed class DerivedStationTests : IDisposable
{
    private const string Id = "derelict:giant-test-0";
    private static readonly Vector3i Entrance = new(54, 1, 2);
    private static readonly Vector3f Spot = new(0f, 300f, -400f);

    private readonly string _root;
    private readonly GameContent _content;

    public DerivedStationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_derived_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, RecordingTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport ?? (IServerTransport)new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The base build: the synthetic giant of the client suite (a hollow 120 × 40 × 120 box, decks every five,
    /// room walls every twelve, an opening at (54…55, 1…3, 0), a fin on the roof, a mast at a corner), in iron.</summary>
    private Dictionary<Vector3i, BlockId> Base()
    {
        var wall = _content.GetBlock("iron_wall")!.NumericId;
        var cells = new Dictionary<Vector3i, BlockId>();
        const int w = 120, h = 40, l = 120;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    bool shell = x == 0 || x == w - 1 || y == 0 || y == h - 1 || z == 0 || z == l - 1;
                    bool inner = y % 5 == 0 || x % 12 == 0 || z % 12 == 0;
                    bool entrance = z == 0 && (x == 54 || x == 55) && y >= 1 && y <= 3;
                    if ((shell || inner) && !entrance)
                    {
                        cells[new Vector3i(x, y, z)] = wall;
                    }
                }

        for (int y = h; y <= h + 2; y++)
        {
            cells[new Vector3i(30, y, 30)] = wall; // the fin
        }

        for (int y = h; y <= h + 5; y++)
        {
            cells[new Vector3i(100, y, 100)] = wall; // the mast (the box's top)
        }

        return cells;
    }

    /// <summary>Where the flight view draws a build cell: the box (0,0,0)…(119,45,119) centred on the structure's spot.</summary>
    private static Vector3f Flight(Vector3i cell) => new(Spot.X + cell.X + 0.5f - 60f, Spot.Y + cell.Y + 0.5f - 23f, Spot.Z + cell.Z + 0.5f - 60f);

    /// <summary>A pilot out on a spacewalk beside the registered giant (its design already received).</summary>
    private static PlayerSession OnASpacewalk(SvGameServer server, IReadOnlyDictionary<Vector3i, BlockId> cells, string baseHash = "")
    {
        var p = server.AddLocalPlayer("Justus");
        server.RegisterDerivedStationForTest(Id, "Test Giant", cells, Entrance, Spot, baseHash);
        server.EnterSpace("Justus");
        server.ShowDerivedStationForTest("Justus", Id);
        p.State.Inventory.SetSlot(0, new ItemStack("diamond_drill", 1));
        p.State.SelectedHotbarSlot = 0;
        p.State.InEva = true;
        return p;
    }

    /// <summary>Mines a hull cell on a spacewalk, from three blocks above it, until it breaks.</summary>
    private static void MineOutside(SvGameServer server, Vector3i cell)
    {
        var at = Flight(cell);
        server.ShipMove("Justus", at.X, at.Y + 3f, at.Z);
        for (int i = 0; i < 16 && server.StationCellsForTest(Id).ContainsKey(cell); i++)
        {
            server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = Id, X = cell.X, Y = cell.Y, Z = cell.Z, Mine = true });
        }
    }

    [Fact]
    public void TheDesign_TravelsAsItsShell_InPartsUnderTheCap()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_design", out var repo, transport);
        using (repo)
        {
            var cells = Base();
            Assert.True(cells.Count >= 200_000, $"the giant holds {cells.Count} cells");
            var p = OnASpacewalk(server, cells);

            var parts = transport.To<SpaceShipDesign>(p).Where(d => d.Id == Id).ToList();
            Assert.True(parts.Count >= 2, $"{parts.Count} parts");
            Assert.All(parts, d => Assert.Equal(parts[0].Serial, d.Serial));
            Assert.All(parts, d => Assert.Equal(parts.Count, d.PartCount));
            Assert.Equal(Enumerable.Range(0, parts.Count), parts.Select(d => d.PartIndex));
            foreach (var part in parts)
            {
                Assert.True(NetCodec.Encode(part).Length < 800_000, $"part {part.PartIndex}: MessagePack");
                Assert.True(NetCodec.EncodeJson(part).Length < 800_000, $"part {part.PartIndex}: JSON envelope");
            }

            var sent = parts.SelectMany(d => Enumerable.Range(0, d.Block.Length).Select(i => new Vector3i(d.X[i], d.Y[i], d.Z[i]))).ToHashSet();
            Assert.True(sent.Count < cells.Count / 2, $"the shell is {sent.Count} of {cells.Count} cells");
            Assert.Contains(new Vector3i(0, 20, 30), sent); // an outer wall
            Assert.Contains(new Vector3i(54, 0, 3), sent); // the floor seen through the open entrance
            Assert.DoesNotContain(new Vector3i(30, 20, 30), sent); // a deck deep inside
            server.Stop();
        }
    }

    [Fact]
    public void ASpacewalkEdit_SendsOneCell_NoDesign_AndWritesTheDeltaNotARow()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_edit", out var repo, transport);
        using (repo)
        {
            var p = OnASpacewalk(server, Base());
            transport.Sent.Clear();

            var tip = new Vector3i(30, 42, 30); // the fin's tip: nothing lies behind it
            MineOutside(server, tip);

            Assert.False(server.StationCellsForTest(Id).ContainsKey(tip), "the fin tip was mined");
            var changes = transport.To<StructureBlockChanged>(p).Where(c => c.StructureId == Id).ToList();
            var change = Assert.Single(changes);
            Assert.Equal((tip.X, tip.Y, tip.Z, (ushort)0), (change.X, change.Y, change.Z, change.Block));
            Assert.Empty(transport.To<SpaceShipDesign>(p).Where(d => d.Id == Id));

            Assert.DoesNotContain(repo.ListSpaceStructures(), r => r.Id == Id); // never the cell list
            Assert.Equal(1, server.DerivedDeltaWritesForTest(Id)); // the very first change goes at once, with the record
            var delta = repo.ListStructureOverlays(SvGameServer.DerivedDeltaLocation).Single(o => o.Key == "delta:" + Id);
            Assert.True(StructureOverlay.TryDecode(delta.Data, out var stored));
            var cell = Assert.Single(stored.Cells());
            Assert.Equal((tip, (ushort)0), (cell.Cell, cell.Block));
            Assert.True(delta.Data.Length < 200, $"the delta blob is {delta.Data.Length} bytes");
            Assert.Equal(1, server.DerivedDeltaCountForTest(Id));
            Assert.Equal(string.Empty, server.DerivedStoredBaseHashForTest(Id)); // the record (registered without a hash)

            // Every later change waits for the coalesced flush, a second later.
            MineOutside(server, new Vector3i(30, 41, 30));
            Assert.Equal(1, server.DerivedDeltaWritesForTest(Id));
            server.TickForTest(0.6);
            server.TickForTest(0.6);
            Assert.Equal(2, server.DerivedDeltaWritesForTest(Id));
            Assert.Equal(2, server.DerivedDeltaCountForTest(Id));
            server.Stop();
        }
    }

    [Fact]
    public void AHoleIntoAHall_SendsTheHallsWalls_CellByCell_AndStillNoDesign()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_hole", out var repo, transport);
        using (repo)
        {
            var p = OnASpacewalk(server, Base());
            transport.Sent.Clear();

            var roof = new Vector3i(66, 39, 66); // over the room (61…71, 36…38, 61…71)
            MineOutside(server, roof);

            var changes = transport.To<StructureBlockChanged>(p).Where(c => c.StructureId == Id).ToList();
            Assert.Contains(changes, c => c.X == roof.X && c.Y == roof.Y && c.Z == roof.Z && c.Block == 0);
            var floor = changes.FirstOrDefault(c => c.X == 66 && c.Y == 35 && c.Z == 66);
            Assert.NotNull(floor); // the room's floor, seen through the hole now
            Assert.Equal(_content.GetBlock("iron_wall")!.NumericId.Value, floor!.Block);
            Assert.Contains(changes, c => c.X == 60 && c.Y == 37 && c.Z == 66); // a side wall
            Assert.DoesNotContain(changes, c => c.X == 30 && c.Y == 20 && c.Z == 30); // nothing deeper in
            Assert.Empty(transport.To<SpaceShipDesign>(p).Where(d => d.Id == Id));
            Assert.True(server.DerivedShellShowsForTest(Id, new Vector3i(66, 35, 66)));
            server.Stop();
        }
    }

    /// <summary>The edit range of a hull this big (half its box diagonal is past the range) is measured to the edited cell:
    /// beside a corner ~80 blocks from the hull's centre the suit mines it (as many hits as the block takes); from 30 blocks
    /// above the centre the same corner is refused as too far — every hit — while a roof cell right under the suit there still
    /// breaks. (A small structure keeps its centre measure; PlayerStationReportsTests extends a small station from its centre.)</summary>
    [Fact]
    public void TheEditRange_IsMeasuredToTheEditedCell_NotToTheCentre()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_range", out var repo, transport);
        using (repo)
        {
            var p = OnASpacewalk(server, Base());
            var far = new Vector3i(115, 39, 5); // a roof cell ~80 blocks from the centre
            MineOutside(server, far);
            Assert.False(server.StationCellsForTest(Id).ContainsKey(far), "the suit beside the cell may work it");

            // 30 blocks above the centre: the corner lies ~78 blocks off — out of reach for every hit.
            var corner = new Vector3i(114, 39, 5);
            server.ShipMove("Justus", Spot.X, Spot.Y + 30f, Spot.Z);
            transport.Sent.Clear();
            for (int i = 0; i < 16; i++)
            {
                server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = Id, X = corner.X, Y = corner.Y, Z = corner.Z, Mine = true });
            }

            Assert.True(server.StationCellsForTest(Id).ContainsKey(corner), "the far corner stays");
            var refusals = transport.To<ActionRejected>(p).Select(r => r.Reason).ToList();
            Assert.Equal(16, refusals.Count(r => r == "@srv.structure.too_far"));
            Assert.Empty(transport.To<StructureMiningProgress>(p)); // not one hit landed

            // …while the roof cell under the suit (≈ 13 blocks below it) breaks from the same spot.
            var below = new Vector3i(59, 39, 59);
            for (int i = 0; i < 16 && server.StationCellsForTest(Id).ContainsKey(below); i++)
            {
                server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = Id, X = below.X, Y = below.Y, Z = below.Z, Mine = true });
            }

            Assert.False(server.StationCellsForTest(Id).ContainsKey(below), "a cell within reach of the suit breaks");
            server.Stop();
        }
    }

    [Fact]
    public void ARestart_RebuildsBasePlusChanges_AndTheFirstBoarding_LaysOneOverlayAtTheEntrance()
    {
        var baseCells = Base();
        Dictionary<Vector3i, BlockId> before;
        var mined = new Vector3i(60, 2, 5); // a room wall, mined from inside
        {
            var s1 = NewServer("derived_restart", out var repo1);
            using (repo1)
            {
                var p = OnASpacewalk(s1, baseCells);
                MineOutside(s1, new Vector3i(30, 42, 30));

                // Board through the entrance (by ship, from in front of it).
                p.State.InEva = false;
                var door = Flight(new Vector3i(55, 2, 0));
                s1.ShipMove("Justus", door.X, door.Y, door.Z - 4f);
                s1.BoardStation("Justus", Id);
                Assert.True(s1.InStation("Justus"));

                string loc = "station:" + Id;
                var hint = new Vector3i(8 + Entrance.X, 64 + Entrance.Y, 8 + Entrance.Z);
                var feet = p.State.Position.ToBlock();
                Assert.True(Math.Abs(feet.X - hint.X) + Math.Abs(feet.Y - hint.Y) + Math.Abs(feet.Z - hint.Z) <= 2,
                    $"the boarder stands at the entrance: {feet} vs {hint}");
                Assert.Equal(0, EditRows(repo1, loc)); // no block edit per cell — the interior is one overlay
                Assert.Contains(repo1.ListStructureOverlays(loc), o => o.Key == "derived:" + Id);
                var wall = _content.GetBlock("iron_wall")!.NumericId;
                Assert.Equal(wall, s1.World.GetBlock(new Vector3i(8 + 30, 64 + 20, 8 + 30))); // a deck deep inside is there
                Assert.True(s1.World.GetBlock(new Vector3i(8 + 30, 64 + 42, 8 + 30)).IsAir, "the fin tip mined before boarding");

                // Mined aboard: the build and the delta follow, the interior keeps it as an ordinary edit.
                var target = new Vector3i(8 + mined.X, 64 + mined.Y, 8 + mined.Z);
                p.State.Position = new Vector3f(target.X - 1.5f, target.Y - 1f, target.Z + 0.5f);
                s1.MineBlock("Justus", target.X, target.Y, target.Z);
                Assert.True(s1.World.GetBlock(target).IsAir);
                Assert.False(s1.StationCellsForTest(Id).ContainsKey(mined), "the hull seen from space lost it too");
                Assert.Equal(2, s1.DerivedDeltaCountForTest(Id));
                Assert.DoesNotContain(repo1.ListSpaceStructures(), r => r.Id == Id);
                before = s1.StationCellsForTest(Id).ToDictionary(kv => kv.Key, kv => kv.Value);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("derived_restart", out var repo2);
        using (repo2)
        {
            s2.RegisterDerivedStationForTest(Id, "Test Giant", baseCells, Entrance, Spot);
            var after = s2.StationCellsForTest(Id);
            Assert.Equal(before.Count, after.Count);
            Assert.All(before, kv => Assert.Equal(kv.Value, after[kv.Key]));
            Assert.Equal(2, s2.DerivedDeltaCountForTest(Id));
            s2.Stop();
        }
    }

    /// <summary>R1 (small hardware): a hundred blocks built on the hull within a second write the delta blob once — not a
    /// hundred times — and edits still waiting when the server stops cleanly are written by the stop, so a restart has every
    /// one of them.</summary>
    [Fact]
    public void AHundredEditsInASecond_WriteTheBlobOnce_AndACleanStopKeepsEveryOne()
    {
        var baseCells = Base();
        var built = new List<Vector3i>();
        {
            var s1 = NewServer("derived_coalesce", out var repo1);
            using (repo1)
            {
                var p = OnASpacewalk(s1, baseCells);
                p.State.InstantBuild = true;
                for (int i = 0; i < 100; i++)
                {
                    built.Add(PlaceOutside(s1, new Vector3i(2 + i, 40, 2)));
                }

                Assert.Equal(100, s1.DerivedDeltaCountForTest(Id));
                Assert.Equal(1, s1.DerivedDeltaWritesForTest(Id)); // the first change at once, the other 99 wait …
                for (int i = 0; i < 3; i++)
                {
                    s1.TickForTest(0.5);
                }

                int writes = s1.DerivedDeltaWritesForTest(Id); // … for ONE coalesced write
                Assert.True(writes == 2, $"100 edits within a second wrote the blob {writes} times");

                for (int i = 0; i < 20; i++)
                {
                    built.Add(PlaceOutside(s1, new Vector3i(2 + i, 40, 4))); // still waiting when the server stops
                }

                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("derived_coalesce", out var repo2);
        using (repo2)
        {
            s2.RegisterDerivedStationForTest(Id, "Test Giant", baseCells, Entrance, Spot);
            Assert.Equal(120, s2.DerivedDeltaCountForTest(Id));
            var cells = s2.StationCellsForTest(Id);
            Assert.All(built, c => Assert.True(cells.ContainsKey(c), $"{c} survived the restart"));
            s2.Stop();
        }
    }

    /// <summary>R1 (latent corruption): the interior's anchor is stored with the first boarding, so a base build that comes
    /// back different after a restart (a later generator or content change — here a new mast west of the hull moves the build's
    /// minimum) maps every cell where it was mapped before; and the base hash the stored changes were made on stays readable
    /// — before the station is registered too — so the caller can keep a changed wreck away.</summary>
    [Fact]
    public void TheInteriorAnchor_IsStored_AndAChangedBaseNeitherMovesIt_NorHidesWhichBaseTheChangesBelongTo()
    {
        var baseCells = Base();
        var mined = new Vector3i(60, 2, 5);
        {
            var s1 = NewServer("derived_anchor", out var repo1);
            using (repo1)
            {
                var p = OnASpacewalk(s1, baseCells, baseHash: "base-A");
                Assert.Null(s1.DerivedStoredBaseHashForTest(Id)); // nothing stored yet
                BoardAtTheEntrance(s1, p);
                Assert.Equal("base-A", s1.DerivedStoredBaseHashForTest(Id)); // the boarding stored the record
                Assert.Equal(new Vector3i(8, 64, 8), s1.DerivedInteriorOffsetForTest(Id));

                var target = new Vector3i(8 + mined.X, 64 + mined.Y, 8 + mined.Z);
                p.State.Position = new Vector3f(target.X - 1.5f, target.Y - 1f, target.Z + 0.5f);
                s1.MineBlock("Justus", target.X, target.Y, target.Z);
                Assert.False(s1.StationCellsForTest(Id).ContainsKey(mined));
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("derived_anchor", out var repo2);
        using (repo2)
        {
            Assert.Equal("base-A", s2.DerivedStoredBaseHashForTest(Id)); // readable before the wreck is registered

            var changed = new Dictionary<Vector3i, BlockId>(baseCells) { [new Vector3i(-6, 0, 0)] = baseCells[new Vector3i(0, 0, 0)] };
            s2.RegisterDerivedStationForTest(Id, "Test Giant", changed, Entrance, Spot, baseHash: "base-B");

            Assert.Equal(new Vector3i(8, 64, 8), s2.DerivedInteriorOffsetForTest(Id)); // not (14, 64, 8): the stored anchor
            Assert.Equal("base-A", s2.DerivedStoredBaseHashForTest(Id)); // the changes still name the build they were made on
            Assert.False(s2.StationCellsForTest(Id).ContainsKey(mined), "the change made aboard stays on its cell");
            s2.Stop();
        }
    }

    /// <summary>R1: when the whole design must go again (a block built past the box moves its centre), it is built and encoded
    /// ONCE for every pilot in the orbit — both pilots get the very same design (one serial), not one build each.</summary>
    [Fact]
    public void AWholeDesignAgain_IsBuiltOnceForEveryPilot()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_once", out var repo, transport);
        using (repo)
        {
            var p = OnASpacewalk(server, Base());
            var other = server.AddLocalPlayer("Layex");
            server.EnterSpace("Layex");
            server.ShowDerivedStationForTest("Layex", Id);
            p.State.InstantBuild = true;
            transport.Sent.Clear();

            PlaceOutside(server, new Vector3i(100, 46, 100)); // on top of the mast: the box grows

            var mine = transport.To<SpaceShipDesign>(p).Where(d => d.Id == Id).ToList();
            var theirs = transport.To<SpaceShipDesign>(other).Where(d => d.Id == Id).ToList();
            Assert.True(mine.Count >= 2, $"the design went again in {mine.Count} parts");
            Assert.Single(mine.Select(d => d.Serial).Distinct());
            Assert.Equal(mine.Select(d => (d.Serial, d.PartIndex, d.Block.Length)), theirs.Select(d => (d.Serial, d.PartIndex, d.Block.Length)));
            Assert.Contains(theirs, d => Enumerable.Range(0, d.Block.Length).Any(i => d.X[i] == 100 && d.Y[i] == 46 && d.Z[i] == 100));
            server.Stop();
        }
    }

    /// <summary>Boards the giant by ship, from in front of its entrance.</summary>
    private static void BoardAtTheEntrance(SvGameServer server, PlayerSession p)
    {
        p.State.InEva = false;
        var door = Flight(new Vector3i(55, 2, 0));
        server.ShipMove("Justus", door.X, door.Y, door.Z - 4f);
        server.BoardStation("Justus", Id);
        Assert.True(server.InStation("Justus"));
    }

    /// <summary>Builds an iron block onto the hull on a spacewalk, from three blocks above the cell.</summary>
    private static Vector3i PlaceOutside(SvGameServer server, Vector3i cell)
    {
        var at = Flight(cell);
        server.ShipMove("Justus", at.X, at.Y + 3f, at.Z);
        server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = Id, X = cell.X, Y = cell.Y, Z = cell.Z, Mine = false, ItemKey = "iron_wall" });
        Assert.True(server.StationCellsForTest(Id).ContainsKey(cell), $"{cell} was built");
        return cell;
    }

    /// <summary>The single-cell rule holds for every station structure: a block mined ABOARD a plain derelict reaches the
    /// pilot floating outside as that one cell — the whole design used to go out again for every block.</summary>
    [Fact]
    public void AnInteriorEdit_OnAPlainDerelict_ReachesThePilotOutside_AsOneCell()
    {
        var transport = new RecordingTransport();
        var server = NewServer("derived_plain", out var repo, transport);
        using (repo)
        {
            var aboard = server.AddLocalPlayer("Justus");
            var outside = server.AddLocalPlayer("Layex");
            string id = WorldGeneration.DerelictPlacer.BodyId(server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId, 0);
            server.EnterSpace("Layex");
            server.EnterSpace("Justus");
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            var layout = server.DerelictLayoutForTest(id)!;
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"));
            Assert.Contains(transport.To<SpaceShipDesign>(outside), d => d.Id == id); // the pilot outside holds the hull

            // A deck cell beside the boarder's feet.
            var feet = aboard.State.Position.ToBlock();
            var target = new Vector3i(feet.X + 1, feet.Y - 1, feet.Z);
            var cell = server.StationCellsForTest(id).Keys.First(c => server.DerelictCellToWorldForTest(id, c) == target);
            aboard.State.Inventory.SetSlot(0, new ItemStack("diamond_drill", 1));
            aboard.State.SelectedHotbarSlot = 0;
            transport.Sent.Clear();
            server.MineBlock("Justus", target.X, target.Y, target.Z);

            Assert.False(server.StationCellsForTest(id).ContainsKey(cell), "the build lost the cell");
            var change = Assert.Single(transport.To<StructureBlockChanged>(outside), c => c.StructureId == id);
            Assert.Equal((cell.X, cell.Y, cell.Z, (ushort)0), (change.X, change.Y, change.Z, change.Block));
            Assert.Empty(transport.To<SpaceShipDesign>(outside).Where(d => d.Id == id));
            server.Stop();
        }
    }

    /// <summary>The block-edit rows of the interior world over the giant's box.</summary>
    private static int EditRows(IWorldRepository repo, string loc)
    {
        int rows = 0;
        for (int cx = 0; cx <= 8; cx++)
            for (int cy = 3; cy <= 7; cy++)
                for (int cz = 0; cz <= 8; cz++)
                {
                    rows += repo.LoadChunkEdits(loc, new ChunkCoord(cx, cy, cz)).Count;
                }

        return rows;
    }
}
