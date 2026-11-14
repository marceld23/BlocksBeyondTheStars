// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Giant ship wrecks in space on the server (#3614): one in every world's start system (Off = none, an older galaxy gains it
/// without moving a body), built on first need as a derived station from its pinned ship; boarding sets you down at the
/// entrance on a floor, no air, nobody home; falling out is a spacewalk and the way in lets the suit back; the chests fill once
/// across re-boarding and a restart; a mined wall reaches the pilots as one cell and stays mined across a restart as a delta;
/// a changed ship never rebuilds a wreck the save holds changes of; the guardian bay wakes; VEGA and the scanner measure to the
/// hull and name the ship; in flight its box touches no other hull. The real template is the pin-only sample
/// (<c>sample_hulk</c>); where rooms matter, the synthetic liner (<see cref="GiantDerelictTestShips"/>).
/// </summary>
public sealed class GiantDerelictTests : IDisposable
{
    private const string Sample = "sample_hulk";

    private readonly string _root;
    private readonly GameContent _content;

    public GiantDerelictTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_giant_derelict_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        GiantDerelictTestShips.AddTo(_content, GiantDerelictTestShips.Liner());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, string? design = Sample, Frequency giants = Frequency.Normal,
        IServerTransport? transport = null, GameContent? content = null, IGameLogger? log = null, int workers = 2,
        Action<ServerConfig>? tweak = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, ChunkGenWorkers = workers };
        config.Rules.FreeSpaceFlight = true;
        config.World.GiantWrecks = giants;
        tweak?.Invoke(config);
        var server = new SvGameServer(config, content ?? _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo, log)
        {
            GiantWreckDesignForTest = design,
        };
        server.Start();
        return server;
    }

    private static string StartSystemOf(SvGameServer server) => server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;

    private static string Guaranteed(SvGameServer server) => GiantWreckPlacer.BodyId(StartSystemOf(server), 0);

    /// <summary>Flies the player up to the start system's giant wreck (in front of it, toward the launch point) and boards it.</summary>
    private static string Board(SvGameServer server, PlayerSession p)
    {
        string id = Guaranteed(server);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var half = server.GiantWreckForTest(id)!.Value.Half;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z + half.Z + 8f);
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid), "a giant wreck is boardable by anyone");
        return id;
    }

    private static void TickUntilInSpace(SvGameServer server, string pid)
    {
        for (int i = 0; i < 5 && !server.InSpace(pid); i++)
        {
            server.TickForTest(0.5);
        }
    }

    private static Vector3i Up(Vector3i c, int dy = 1) => new(c.X, c.Y + dy, c.Z);

    private static bool Near(Vector3i a, Vector3i b, int reach) => Math.Abs(a.X - b.X) <= reach && Math.Abs(a.Y - b.Y) <= 1 && Math.Abs(a.Z - b.Z) <= reach;

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    // ---------------- placement ----------------

    [Fact]
    public void AWorld_HoldsOneInItsStartSystem_OffHoldsNone_AndAGalaxyWithoutThemGainsThemWithoutMovingABody()
    {
        List<(string Id, string Name, float X, float Z)> before;
        var off = NewServer("gw_off", out var repoOff, giants: Frequency.Off);
        using (repoOff)
        {
            Assert.Empty(off.GiantDerelictIdsForTest);
            Assert.DoesNotContain(off.Galaxy.AllBodies(), b => GiantShipRules.IsGiantWreckId(b.Id));
            before = off.Galaxy.AllBodies().Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            off.Stop();
        }

        var on = NewServer("gw_on", out var repoOn);
        using (repoOn)
        {
            string id = Guaranteed(on);
            var start = on.Galaxy.Systems.First(s => s.Id == StartSystemOf(on));
            Assert.Equal(id, Assert.Single(start.Bodies, b => GiantShipRules.IsGiantWreckId(b.Id)).Id);
            Assert.Equal(CelestialKind.SpaceStation, start.Bodies.First(b => b.Id == id).Kind);
            Assert.Contains(id, on.GiantDerelictIdsForTest);
            Assert.DoesNotContain(on.DerelictIdsForTest, GiantShipRules.IsGiantWreckId); // never rebuilt as a beta station
            var after = on.Galaxy.AllBodies().Where(b => !GiantShipRules.IsGiantWreckId(b.Id)).Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            Assert.Equal(before, after);
            on.Stop();
        }
    }

    /// <summary>The start system's wreck is built at boot on a background thread where the server may run threads, so the first
    /// flight only registers it; a server without threads (the in-browser singleplayer) builds it at the first flight — the
    /// very same wreck either way.</summary>
    [Fact]
    public void TheStartSystemsWreck_IsBuiltAheadOnAWorker_OrInlineWithoutThreads_AndIsTheSameWreck()
    {
        var builds = new List<(Dictionary<Vector3i, BlockId> Cells, Vector3f Position)>();
        foreach (int workers in new[] { 2, 0 })
        {
            var log = new CapturingLogger();
            var server = NewServer("gw_ahead_" + workers, out var repo, design: GiantDerelictTestShips.Key, log: log, workers: workers);
            using (repo)
            {
                string id = Guaranteed(server);
                // With threads it is started at boot on the builder; without, it is built during the boot itself.
                Assert.Equal(workers > 0, server.GiantBuildPendingForTest(id));
                Assert.Equal(workers == 0, log.Infos.Any(l => l.StartsWith("Giant wreck '", StringComparison.Ordinal)));
                server.AddLocalPlayer("Pilot");
                server.EnterSpace("Pilot");
                Assert.False(server.GiantBuildPendingForTest(id));
                var contact = Assert.Single(server.SpaceEntitiesFor("Pilot"), e => e.Id == id);
                string line = Assert.Single(log.Infos, l => l.StartsWith("Giant wreck '", StringComparison.Ordinal));
                Assert.Contains(workers > 0 ? "background" : "built inline", line, StringComparison.Ordinal);
                builds.Add((server.StationCellsForTest(id).ToDictionary(kv => kv.Key, kv => kv.Value), contact.Position));
                server.Stop();
            }
        }

        Assert.Equal(builds[0].Position, builds[1].Position);
        Assert.Equal(builds[0].Cells.Count, builds[1].Cells.Count);
        Assert.All(builds[0].Cells, kv => Assert.Equal(kv.Value, builds[1].Cells[kv.Key]));
    }

    // ---------------- boarding ----------------

    [Fact]
    public void Boarding_ArrivesAtTheEntranceOnAFloor_NoAirNobodyHome_AndStoresOneOverlayAndThePin()
    {
        var transport = new RecordingTransport();
        var server = NewServer("gw_board", out var repo, transport: transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = Board(server, p);
            var wreck = server.GiantWreckForTest(id)!.Value;
            Assert.Equal(Sample, wreck.Ship);

            var entrance = server.DerelictCellToWorldForTest(id, wreck.Arrival);
            var feet = p.State.Position.ToBlock();
            Assert.True(Near(feet, entrance, 2), $"the boarder stands at the entrance: {feet} vs {entrance}");
            Assert.True(server.World.GetBlock(feet).IsAir && server.World.GetBlock(Up(feet)).IsAir, "free feet and head");
            Assert.False(server.World.GetBlock(Up(feet, -1)).IsAir, "a floor under the feet");

            // No air aboard: the suit tank drains, told once with the wreck's own line; VEGA's first-boarding hint.
            var arrival = p.State.Position;
            p.State.Oxygen = 40f;
            for (int i = 0; i < 10; i++)
            {
                p.State.Position = arrival;
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < 40f, $"no air aboard a giant wreck (O2 {p.State.Oxygen})");
            Assert.Single(transport.Texts(), t => t == "@srv.giantwreck.no_air");
            Assert.Contains("vega.hint." + SvGameServer.DerelictNoAirHint, transport.LineKeys(p));
            Assert.Equal(0, server.NpcCount); // nobody home

            // Nobody's home either: a heal tank set up beside the way in takes no home spawn — the wreck's own words say why.
            var tankAt = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dz => new Vector3i(feet.X + dx, feet.Y, feet.Z + dz)))
                .Where(c => c != feet && server.World.GetBlock(c).IsAir && !server.World.GetBlock(Up(c, -1)).IsAir)
                .OrderBy(c => Math.Abs(c.X - feet.X) + Math.Abs(c.Z - feet.Z)).First();
            p.State.InstantBuild = true;
            p.State.Position = arrival;
            server.PlaceBlock("Justus", tankAt.X, tankAt.Y, tankAt.Z, "heal_tank");
            Assert.Equal("heal_tank", KeyAt(server, tankAt));
            server.SetSpawnPoint("Justus", tankAt.X, tankAt.Y, tankAt.Z);
            Assert.Contains(transport.To<ActionRejected>(p), r => r.Reason == "@srv.giantwreck.no_home");
            Assert.True(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "a giant wreck is never a home spawn");

            // Stored: the interior as ONE overlay, never a cell row; the ship pinned with its fingerprint; the shape marker.
            Assert.Contains(repo.ListStructureOverlays("station:" + id), o => o.Key == "derived:" + id);
            Assert.DoesNotContain(repo.ListSpaceStructures(), r => r.Id == id);
            Assert.StartsWith(Pin(Sample, _content), server.Metadata.GiantWreckDesigns[id], StringComparison.Ordinal);
            Assert.True(server.Metadata.GiantWreckSpots.ContainsKey(id), "its flight spot is pinned");
            Assert.Contains(GiantShipRules.ShapeMarkerPrefix + id, server.Metadata.GeneratedLoot);
            server.Stop();
        }
    }

    [Fact]
    public void FallingOut_IsASpacewalk_AndTheSuitComesBackInThroughTheWayIn()
    {
        var server = NewServer("gw_eva", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = Board(server, p);
            var arrival = p.State.Position;
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 60f, arrival.Z);
            TickUntilInSpace(server, "Justus");
            Assert.False(server.InStation("Justus"));
            Assert.True(server.InSpace("Justus") && p.State.InEva, "falling out of a giant wreck is a spacewalk");

            // Back in through the way in: float at the entrance and press E.
            var wreck = server.GiantWreckForTest(id)!.Value;
            var at = server.DerelictCellToFlightForTest(id, wreck.Arrival);
            server.ShipMove("Justus", at.X, at.Y + 1.5f, at.Z);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"), "the suit boards through the entrance");
            Assert.False(p.State.InEva);
            Assert.True(Near(p.State.Position.ToBlock(), server.DerelictCellToWorldForTest(id, wreck.Arrival), 2),
                $"set down at the way in it floated at: {p.State.Position}");
            server.Stop();
        }
    }

    // ---------------- chests ----------------

    [Fact]
    public void Chests_FillOnce_AsCratesInTheInteriorAndTheBuild_AcrossReboardingAndARestart()
    {
        string id;
        Vector3i keptAt;
        List<(string Item, int Count)> keptItems;
        {
            var s1 = NewServer("gw_chests", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = Board(s1, p);
                var wreck = s1.GiantWreckForTest(id)!.Value;
                var chests = s1.GiantWreckChestsForTest(id);
                Assert.Equal(3, wreck.Chests.Count);
                Assert.Equal(3, chests.Count);
                var at = wreck.Chests.Select(c => s1.DerelictCellToWorldForTest(id, c)).ToList();
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal("crate", KeyAt(s1, at[i]));
                    Assert.Equal(_content.GetBlock("crate")!.NumericId, s1.StationCellsForTest(id)[wreck.Chests[i]]); // the build keeps it
                    Assert.Contains(chests, c => c.Position.Equals(at[i]));
                }

                Assert.Equal("scrap_pile", KeyAt(s1, Up(at[2]))); // hidden=1: some scrap fell over it
                var plates = chests.Single(c => c.Position.Equals(at[1]));
                Assert.InRange(plates.Items.Where(s => s.Item == "iron_plate").Sum(s => s.Count), 2, 4); // items=iron_plate:2-4

                // Loot the first; leave and board again: nothing refills.
                p.State.Position = new Vector3f(at[0].X + 0.5f, at[0].Y, at[0].Z + 1.5f);
                s1.LootContainer("Justus", chests.Single(c => c.Position.Equals(at[0])).Id);
                Assert.Equal(2, s1.GiantWreckChestsForTest(id).Count);
                keptAt = at[1];
                keptItems = plates.Items.Select(s => (s.Item, s.Count)).ToList();
                s1.LeaveStation("Justus");
                Board(s1, p);
                Assert.Equal(2, s1.GiantWreckChestsForTest(id).Count);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("gw_chests", out var repo2, design: GiantDerelictTestShips.Key);
        using (repo2)
        {
            var p = s2.AddLocalPlayer("Justus");
            s2.LeaveStation("Justus"); // the rejoin may have put the player back aboard
            Board(s2, p);
            var chests = s2.GiantWreckChestsForTest(id);
            Assert.Equal(2, chests.Count); // filled once ever — a restart refills nothing
            Assert.Equal(keptItems, chests.Single(c => c.Position.Equals(keptAt)).Items.Select(s => (s.Item, s.Count)).ToList());
            Assert.Equal("crate", KeyAt(s2, keptAt));
            s2.Stop();
        }
    }

    // ---------------- mining ----------------

    [Fact]
    public void AMinedHullCell_ReachesThePilotsAsOneCell_AndStaysMinedAcrossARestart_AsADelta()
    {
        string id;
        Vector3i mined;
        var transport = new RecordingTransport();
        {
            var s1 = NewServer("gw_mine", out var repo1, transport: transport);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                s1.EnterSpace("Justus");
                id = Guaranteed(s1);
                Assert.NotNull(s1.GiantWreckForTest(id));
                Assert.Contains(transport.To<SpaceShipDesign>(p), d => d.Id == id); // the hull came with the flight
                mined = ExposedTopCell(s1.StationCellsForTest(id));
                p.State.Inventory.SetSlot(0, new ItemStack("diamond_drill", 1));
                p.State.SelectedHotbarSlot = 0;
                p.State.InEva = true;
                transport.Sent.Clear();

                var at = s1.DerelictCellToFlightForTest(id, mined);
                s1.ShipMove("Justus", at.X, at.Y + 3f, at.Z);
                for (int i = 0; i < 16 && s1.StationCellsForTest(id).ContainsKey(mined); i++)
                {
                    s1.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = id, X = mined.X, Y = mined.Y, Z = mined.Z, Mine = true });
                }

                Assert.False(s1.StationCellsForTest(id).ContainsKey(mined), "the spacewalk mined the hull cell");
                var change = Assert.Single(transport.To<StructureBlockChanged>(p), c => c.StructureId == id);
                Assert.Equal((mined.X, mined.Y, mined.Z, (ushort)0), (change.X, change.Y, change.Z, change.Block));
                Assert.Empty(transport.To<SpaceShipDesign>(p).Where(d => d.Id == id));
                Assert.DoesNotContain(repo1.ListSpaceStructures(), r => r.Id == id); // never a cell row
                Assert.Contains(repo1.ListStructureOverlays(SvGameServer.DerivedDeltaLocation), o => o.Key == "delta:" + id);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("gw_mine", out var repo2);
        using (repo2)
        {
            s2.AddLocalPlayer("Justus");
            Assert.NotNull(s2.GiantWreckForTest(id)); // rebuilt: template + seed + the stored changes
            Assert.False(s2.StationCellsForTest(id).ContainsKey(mined), "the mined cell stays mined after a restart");
            Assert.Equal(1, s2.DerivedDeltaCountForTest(id));
            s2.Stop();
        }
    }

    /// <summary>A hull cell on the very top whose removal lays nothing new open: open above and on at least one side, and the
    /// cell under it (if any) already open to the sky beside it.</summary>
    private static Vector3i ExposedTopCell(IReadOnlyDictionary<Vector3i, BlockId> cells)
    {
        int top = cells.Keys.Max(c => c.Y);
        bool Sky(Vector3i c) => !cells.Keys.Any(o => o.X == c.X && o.Z == c.Z && o.Y >= c.Y);
        foreach (var c in cells.Keys.Where(c => c.Y == top).OrderBy(c => c.X).ThenBy(c => c.Z))
        {
            var below = Up(c, -1);
            var sides = new[] { new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1) };
            bool openSide = sides.Any(d => !cells.ContainsKey(new Vector3i(c.X + d.X, c.Y, c.Z + d.Z)));
            bool belowShown = !cells.ContainsKey(below)
                || sides.Any(d => Sky(new Vector3i(below.X + d.X, below.Y, below.Z + d.Z)));
            if (openSide && belowShown)
            {
                return c;
            }
        }

        throw new InvalidOperationException("no exposed top cell");
    }

    // ---------------- the pin ----------------

    [Fact]
    public void AChangedShip_NeverRebuildsAWreckTheSaveHoldsChangesOf_ButAnUntouchedOneIsBuiltAnew()
    {
        string id;
        string pin;
        var changed = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        GiantDerelictTestShips.AddTo(changed, GiantDerelictTestShips.Liner(w: 90)); // the same ship, a new version
        {
            var s1 = NewServer("gw_pin", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                id = Board(s1, s1.AddLocalPlayer("Justus")); // boarded: its interior and its changes are stored
                pin = s1.Metadata.GiantWreckDesigns[id];
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var s2 = NewServer("gw_pin", out var repo2, design: GiantDerelictTestShips.Key, content: changed);
        using (repo2)
        {
            Assert.DoesNotContain(id, s2.GiantDerelictIdsForTest);
            Assert.Null(s2.Galaxy.FindBody(id)); // off the star map while its ship is not as it was
            Assert.Equal(pin, s2.Metadata.GiantWreckDesigns[id]); // the pin and the stored changes are kept
            Assert.Contains(repo2.ListStructureOverlays("station:" + id), o => o.Key == "derived:" + id);
            s2.Stop();
        }

        // Only seen in flight (pinned, nothing stored): the changed ship is simply built anew.
        {
            var s3 = NewServer("gw_seen", out var repo3, design: GiantDerelictTestShips.Key);
            using (repo3)
            {
                s3.AddLocalPlayer("Justus");
                s3.EnterSpace("Justus");
                Assert.Equal(pin, s3.Metadata.GiantWreckDesigns[id]);
                s3.Stop();
                repo3.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var s4 = NewServer("gw_seen", out var repo4, design: GiantDerelictTestShips.Key, content: changed);
        using (repo4)
        {
            Assert.Contains(id, s4.GiantDerelictIdsForTest);
            Assert.Equal(90f, s4.GiantWreckForTest(id)!.Value.Half.X * 2f); // the new version, 90 long
            Assert.StartsWith(Pin(GiantDerelictTestShips.Key, changed), s4.Metadata.GiantWreckDesigns[id], StringComparison.Ordinal);
            s4.Stop();
        }
    }

    /// <summary>The pin's start: <c>key|template fingerprint|r&lt;ruin&gt;|d&lt;decay&gt;|b</c> (the base hash follows).</summary>
    private static string Pin(string key, GameContent content)
        => key + "|" + GiantShipGenerator.Fingerprint(content.GetGiantShip(key)!) + "|r" + GiantShipGenerator.RuinRevision
           + "|d" + DerelictGenerator.ShipDecayRevision + "|b";

    /// <summary>Rewrites one value of a stopped world's metadata (the save as the next start reads it).</summary>
    private void EditSave(string world, Action<WorldMetadata> edit)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        var meta = repo.LoadMetadata()!;
        edit(meta);
        repo.SaveMetadata(meta);
        repo.Flush();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>Rewrites a stopped world's stored record of a derived station (<c>derived-station:&lt;id&gt;</c>).</summary>
    private void EditRecord(string world, string id, Func<string, string> edit)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        string key = SvGameServer.DerivedRecordKey(id);
        string json = repo.LoadNamedBlob(key)!;
        string edited = edit(json);
        Assert.NotEqual(json, edited);
        repo.SaveNamedBlob(key, edited);
        repo.Flush();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>The base hash in a pin is replaced, as if a block property or the generator had changed what the wreck becomes.</summary>
    private static string WithBase(string pin, string hash) => pin.Substring(0, pin.LastIndexOf("|b", StringComparison.Ordinal)) + "|b" + hash;

    /// <summary>#3614 review: the pin holds the revisions and a hash of the base. A wreck whose base no longer comes out as
    /// stored stays away while the save holds its changes — the data kept — and is simply rebuilt (and pinned anew) when
    /// nothing was stored; an older ruin revision in the pin keeps a stored wreck away at the registration already.</summary>
    [Fact]
    public void AChangedBase_KeepsAStoredWreckAway_ButAnUntouchedOneIsRebuilt()
    {
        string id;
        string pin;
        {
            var s1 = NewServer("gw_base", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                id = Board(s1, s1.AddLocalPlayer("Justus"));
                pin = s1.Metadata.GiantWreckDesigns[id];
                s1.Stop();
                repo1.Flush();
            }
        }

        // The derived station's record names the base its changes were made on: as if a block property or the generator had
        // changed what the wreck becomes, the stored base is no longer the one built now.
        string realBase = pin.Substring(pin.LastIndexOf("|b", StringComparison.Ordinal) + 2);
        EditRecord("gw_base", id, json => json.Replace(realBase, "0123456789abcdef", StringComparison.Ordinal));
        var s2 = NewServer("gw_base", out var repo2, design: GiantDerelictTestShips.Key);
        using (repo2)
        {
            s2.AddLocalPlayer("Pilot");
            s2.LeaveStation("Pilot");
            s2.EnterSpace("Pilot");
            Assert.DoesNotContain(s2.SpaceEntitiesFor("Pilot"), e => e.Id == id);
            Assert.Null(s2.Galaxy.FindBody(id));
            Assert.Null(s2.GiantWreckForTest(id));
            Assert.Equal(pin, s2.Metadata.GiantWreckDesigns[id]); // kept as it was
            Assert.Contains(repo2.ListStructureOverlays("station:" + id), o => o.Key == "derived:" + id);
            s2.Stop();
            repo2.Flush();
        }

        EditSave("gw_base", m => m.GiantWreckDesigns[id] = pin.Replace("|r" + GiantShipGenerator.RuinRevision + "|", "|r2|", StringComparison.Ordinal));
        var s3 = NewServer("gw_base", out var repo3, design: GiantDerelictTestShips.Key);
        using (repo3)
        {
            Assert.DoesNotContain(id, s3.GiantDerelictIdsForTest); // away at the registration
            s3.Stop();
        }

        // Only seen (nothing stored): a changed base is simply built anew and pinned with its real hash.
        string seenId;
        {
            var s4 = NewServer("gw_base_seen", out var repo4, design: GiantDerelictTestShips.Key);
            using (repo4)
            {
                s4.AddLocalPlayer("Pilot");
                s4.EnterSpace("Pilot");
                seenId = Guaranteed(s4);
                s4.Stop();
                repo4.Flush();
            }
        }

        EditSave("gw_base_seen", m => m.GiantWreckDesigns[seenId] = WithBase(m.GiantWreckDesigns[seenId], "0123456789abcdef"));
        var s5 = NewServer("gw_base_seen", out var repo5, design: GiantDerelictTestShips.Key);
        using (repo5)
        {
            Assert.NotNull(s5.GiantWreckForTest(seenId));
            Assert.Equal(pin, s5.Metadata.GiantWreckDesigns[seenId]); // the same world seed, ship and id: the very same pin
            s5.Stop();
        }
    }

    /// <summary>#3614 review: after a restart the wreck carries its shell from the build, with the stored changes (the chest
    /// crates of the first boarding) replayed over it — nothing a pilot must see is missing, and no shell is built at the
    /// first send.</summary>
    [Fact]
    public void AfterARestart_TheShellComesWithTheBuild_WithTheStoredChangesReplayed()
    {
        string id;
        {
            var s1 = NewServer("gw_shell", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                id = Board(s1, s1.AddLocalPlayer("Justus"));
                Assert.NotEmpty(s1.GiantWreckChestsForTest(id)); // its crates are changes of the build now
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (int workers in new[] { 2, 0 })
        {
            var s2 = NewServer("gw_shell", out var repo2, design: GiantDerelictTestShips.Key, workers: workers);
            using (repo2)
            {
                Assert.NotNull(s2.GiantWreckForTest(id));
                Assert.True(s2.DerivedDeltaCountForTest(id) > 0, "the stored changes were laid over the base");
                var (carried, missing) = s2.GiantShellCheckForTest(id);
                Assert.True(carried, "the shell came with the build");
                Assert.Equal(0, missing);
                s2.Stop();
                repo2.Flush();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>#3614 review: the host's switch keeps the space wrecks out of an EXISTING world too — no body, no contact; a
    /// player who was aboard comes back on a planet — and everything stored for the wreck stays; switched on again, it is back
    /// with its changes.</summary>
    [Fact]
    public void TheHostsSwitch_KeepsTheSpaceWrecksOut_OfAnExistingWorld_AndTheirDataStays()
    {
        string id;
        int changes;
        {
            var s1 = NewServer("gw_host", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                id = Board(s1, s1.AddLocalPlayer("Justus")); // and stays aboard
                changes = s1.DerivedDeltaCountForTest(id);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var s2 = NewServer("gw_host", out var repo2, design: GiantDerelictTestShips.Key, tweak: c => c.PlaceGiantWrecks = false);
        using (repo2)
        {
            Assert.Null(s2.Galaxy.FindBody(id));
            Assert.Empty(s2.GiantDerelictIdsForTest);
            s2.AddLocalPlayer("Justus");
            Assert.False(s2.InStation("Justus"), "a player who was aboard comes back on a planet");
            Assert.True(s2.Metadata.GiantWreckDesigns.ContainsKey(id));
            Assert.Contains(repo2.ListStructureOverlays(SvGameServer.DerivedDeltaLocation), o => o.Key == "delta:" + id);
            s2.Stop();
            repo2.Flush();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var s3 = NewServer("gw_host", out var repo3, design: GiantDerelictTestShips.Key);
        using (repo3)
        {
            Assert.NotNull(s3.GiantWreckForTest(id));
            Assert.Equal(changes, s3.DerivedDeltaCountForTest(id));
            s3.Stop();
        }
    }

    /// <summary>#3614 review: the flight spot is chosen once and pinned — the next start puts the wreck exactly there, whatever
    /// stands in the system by then (a player station built in its way never moves it).</summary>
    [Fact]
    public void TheFlightSpot_IsPinnedTheFirstTime_AndKeptOnTheNextStart()
    {
        string id;
        var moved = new Vector3f(12.5f, 333.25f, -777.75f);
        {
            var s1 = NewServer("gw_spot", out var repo1, design: GiantDerelictTestShips.Key);
            using (repo1)
            {
                s1.AddLocalPlayer("Pilot");
                s1.EnterSpace("Pilot");
                id = Guaranteed(s1);
                var contact = s1.SpaceEntitiesFor("Pilot").First(e => e.Id == id);
                Assert.Equal(contact.Position.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    s1.Metadata.GiantWreckSpots[id].Split(';')[2]);
                s1.Stop();
                repo1.Flush();
            }
        }

        EditSave("gw_spot", m => m.GiantWreckSpots[id] = "12.5;333.25;-777.75"); // as if it had been placed among other hulls
        var s2 = NewServer("gw_spot", out var repo2, design: GiantDerelictTestShips.Key);
        using (repo2)
        {
            s2.AddLocalPlayer("Pilot");
            s2.LeaveStation("Pilot");
            s2.EnterSpace("Pilot");
            Assert.Equal(moved, s2.SpaceEntitiesFor("Pilot").First(e => e.Id == id).Position);
            s2.Stop();
        }
    }

    /// <summary>#3614 review: one builder thread at a time; a finished build nobody took is dropped after its time; without
    /// threads the process cache is not used.</summary>
    [Fact]
    public void OneBuilderAtATime_AnUntakenBuildIsDropped_AndNoCacheWithoutThreads()
    {
        var server = NewServer("gw_builder", out var repo, design: GiantDerelictTestShips.Key);
        using (repo)
        {
            string id = Guaranteed(server);
            Assert.True(server.GiantBuildPendingForTest(id));
            Assert.InRange(server.GiantBuildQueueForTest().Running, 0, 1);
            server.AgeGiantBuildsForTest();
            Assert.False(server.GiantBuildPendingForTest(id), "nobody took it in time: dropped");
            Assert.NotNull(server.GiantWreckForTest(id)); // built again when needed
            server.Stop();
        }

        var lone = GiantDerelictTestShips.Liner(w: 89); // a ship of its own: no other test shares its cache key
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        GiantDerelictTestShips.AddTo(content, lone);
        var inline = NewServer("gw_nocache", out var repo2, design: GiantDerelictTestShips.Key, content: content, workers: 0);
        using (repo2)
        {
            string id = Guaranteed(inline);
            Assert.NotNull(inline.GiantWreckForTest(id)); // built during the boot
            long seed = GiantWreckPlacer.BuildSeed(inline.Metadata.Seed, id);
            Assert.False(BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.Holds(
                BlocksBeyondTheStars.GameServer.GiantWreckBuildCache.KeyFor(content.GetGiantShip(GiantDerelictTestShips.Key)!, seed, content)));
            inline.Stop();
        }
    }

    // ---------------- the guardian bay ----------------

    [Fact]
    public void TheGuardianBay_StandsAboard_AndWakesWhileSomeoneIsThere()
    {
        var server = NewServer("gw_bay", out var repo, design: GiantDerelictTestShips.Key);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = Board(server, p);
            p.State.GodMode = true; // counted, not fought
            var bay = server.DerelictSpawnerSpotForTest(id)!.Value;
            Assert.Equal(SvGameServer.GuardianBayBlockKey, KeyAt(server, bay));
            for (double t = 0; t <= SvGameServer.GuardianFirstReleaseSeconds + 1; t += 1.0)
            {
                server.TickEnemiesForTest(1.0);
            }

            Assert.True(server.GuardiansReleasedForTest(id) >= 1, "the bay of a giant wreck releases its guardians in survival");
            Assert.NotEmpty(server.PlanetEnemies);
            server.Stop();
        }
    }

    // ---------------- VEGA, the scanner, the flight ----------------

    [Fact]
    public void VegaAndTheScanner_MeasureToTheHull_SpeakOnce_AndNameTheShip()
    {
        var transport = new RecordingTransport();
        var server = NewServer("gw_scan", out var repo, design: GiantDerelictTestShips.Key, transport: transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            string id = Guaranteed(server);
            var half = server.GiantWreckForTest(id)!.Value.Half;
            var contact = server.SpaceEntitiesFor("Pilot").First(e => e.Id == id);

            // 130 units off the hull's end: more than the cockpit scanner's 150 from the centre, within reach of the hull.
            server.ShipMove("Pilot", contact.Position.X + half.X + 130f, contact.Position.Y, contact.Position.Z);
            server.ShipMove("Pilot", contact.Position.X + half.X + 120f, contact.Position.Y, contact.Position.Z);
            Assert.Single(transport.LineKeys(p), k => k == "vega.giantwreck.approach");
            Assert.DoesNotContain("vega.derelict.approach", transport.LineKeys(p));

            var read = server.ScanSpaceEntity("Pilot", id);
            Assert.Equal("giant_wreck", read.SubjectKey);
            Assert.Equal("station", read.Kind);
            Assert.Equal("ui.scan.giant_wreck_space", read.InfoKey);
            Assert.Contains(GiantShipRules.NameKey(GiantDerelictTestShips.Key), read.TraitKeys);
            Assert.Contains("ui.scan.trait.giant_size_large", read.TraitKeys);
            Assert.True(read.FirstTime);
            Assert.True(read.KnowledgeGained > 0);
            Assert.Contains(SvGameServer.GiantWreckScanKey(id, GiantDerelictTestShips.Key), p.State.Scanned);

            server.ResetShipScanCooldownForTest();
            var again = server.ScanSpaceEntity("Pilot", id);
            Assert.False(again.FirstTime);
            Assert.Equal(0, again.KnowledgeGained);
            server.Stop();
        }
    }

    [Fact]
    public void InFlight_TheGiantFloatsBehindTheLaunchPoint_ClearOfEveryOtherHull()
    {
        var server = NewServer("gw_flight", out var repo, design: GiantDerelictTestShips.Key);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            string id = Guaranteed(server);
            var boxes = server.StationHullBoxesForTest("Pilot");
            var (centre, half) = boxes[id];
            Assert.True(centre.Y - half.Y >= 44f, "its floor clears every planet sphere");
            Assert.True(centre.Z + half.Z <= -110f + 0.01f, "it floats behind the launch point");
            Assert.Contains(boxes.Keys, k => DerelictPlacer.IsDerelictId(k) && !GiantShipRules.IsGiantWreckId(k)); // the derelict is here too
            foreach (var (other, box) in boxes.Where(kv => kv.Key != id))
            {
                bool overlap = Math.Abs(centre.X - box.Centre.X) < half.X + box.Half.X && Math.Abs(centre.Y - box.Centre.Y) < half.Y + box.Half.Y
                    && Math.Abs(centre.Z - box.Centre.Z) < half.Z + box.Half.Z;
                Assert.False(overlap, $"the giant's box touches {other}'s");
            }

            server.Stop();
        }
    }
}
