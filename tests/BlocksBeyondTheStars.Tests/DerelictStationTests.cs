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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Derelict stations (#2632, #2634): broken old boxy stations — one guaranteed in every world's start system, otherwise
/// rare; seeded layouts with sealed and breached rooms, doors, broken trading posts and the spots the later phases use;
/// nobody lives there and no trading rule counts them; breached rooms hold no air, falling out is caught; mined blocks
/// stay mined across a restart and the hull seen from space follows.
/// </summary>
public sealed class DerelictStationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DerelictStationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_derelict_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, long seed = 7, Frequency derelicts = Frequency.Normal)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.World.Derelicts = derelicts;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static string StartSystemOf(SvGameServer server) => server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;

    /// <summary>The guaranteed derelict of the start system.</summary>
    private static string GuaranteedDerelict(SvGameServer server) => DerelictPlacer.BodyId(StartSystemOf(server), 0);

    /// <summary>Flies a fresh player up to the start system's derelict and boards it; returns its id.</summary>
    private static string BoardGuaranteed(SvGameServer server, PlayerSession p)
    {
        string id = GuaranteedDerelict(server);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid), "the derelict should be boardable by anyone");
        return id;
    }

    private static void Equip(PlayerSession p, string tool)
    {
        p.State.Inventory.SetSlot(0, new ItemStack(tool, 1));
        p.State.SelectedHotbarSlot = 0;
    }

    private List<StarSystem> Galaxy(long seed, int systems = 12)
    {
        var desc = new WorldDescription { StarSystemCount = systems };
        return new UniverseGenerator(seed, desc, _content).Generate(systems).Systems;
    }

    private static int SystemIndex(string bodyId)
    {
        string rest = bodyId.Substring(DerelictPlacer.IdPrefix.Length + 3); // "derelict:sys" stripped
        return int.Parse(rest.Substring(0, rest.IndexOf('-')), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- placement ----------------

    [Fact]
    public void Placer_GuaranteesTheStartSystem_IsRareElsewhere_AndOffPlacesNone()
    {
        int systemsWithOne = 0, candidates = 0;
        for (long seed = 1; seed <= 40; seed++) // small typed seeds on purpose: they must roll extras too
        {
            var systems = Galaxy(seed);
            DerelictPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0");
            var sys0 = systems.First(s => s.Id == "sys0");
            Assert.Contains(sys0.Bodies, b => b.Id == DerelictPlacer.BodyId("sys0", 0) && b.Kind == CelestialKind.SpaceStation);

            foreach (var sys in systems.Where(s => s.Id != "sys0"))
            {
                int n = sys.Bodies.Count(b => DerelictPlacer.IsDerelictId(b.Id));
                Assert.InRange(n, 0, DerelictPlacer.MaxPerSystem);
                candidates++;
                systemsWithOne += n > 0 ? 1 : 0;
            }

            // Idempotent: a second pass over the same galaxy places nothing.
            Assert.Equal(0, DerelictPlacer.Place(systems, seed, Frequency.Normal, systems.Count, "sys0"));

            var off = Galaxy(seed);
            Assert.Equal(0, DerelictPlacer.Place(off, seed, Frequency.Off, off.Count, "sys0"));
            Assert.DoesNotContain(off.SelectMany(s => s.Bodies), b => DerelictPlacer.IsDerelictId(b.Id));
        }

        double share = (double)systemsWithOne / candidates;
        Assert.True(share < 0.3, $"derelicts should be rare outside the start system, found them in {share:P0} of the systems");
        Assert.True(systemsWithOne > 0, "rare is not never: some system beyond the start must hold a derelict");
    }

    [Fact]
    public void Placer_IsSeedStable_AndNeverTouchesAGrownSystem()
    {
        var a = Galaxy(42, systems: 14);
        var b = Galaxy(42, systems: 14);
        DerelictPlacer.Place(a, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        DerelictPlacer.Place(b, 42, Frequency.Frequent, fixedCount: 10, "sys2");
        var ida = a.SelectMany(s => s.Bodies).Where(x => DerelictPlacer.IsDerelictId(x.Id)).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ)).ToList();
        var idb = b.SelectMany(s => s.Bodies).Where(x => DerelictPlacer.IsDerelictId(x.Id)).Select(x => (x.Id, x.Name, x.SystemX, x.SystemZ)).ToList();
        Assert.Equal(ida, idb);
        Assert.Contains(ida, d => d.Id == DerelictPlacer.BodyId("sys2", 0));
        Assert.All(ida, d => Assert.True(SystemIndex(d.Id) < 10, $"{d.Id} sits in a system beyond the fixed ten"));
        Assert.All(ida, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public void AnOldSave_GainsItsDerelicts_WithoutMovingAnyOtherBody_OrTheStartStation()
    {
        var off = NewServer("drl_off", out var repoOff, seed: 11, derelicts: Frequency.Off);
        List<(string Id, string Name, float X, float Z)> before;
        using (repoOff)
        {
            Assert.Empty(off.DerelictIdsForTest);
            before = off.Galaxy.AllBodies().Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            off.Stop();
        }

        var on = NewServer("drl_on", out var repoOn, seed: 11, derelicts: Frequency.Normal);
        using (repoOn)
        {
            var after = on.Galaxy.AllBodies().Where(b => !DerelictPlacer.IsDerelictId(b.Id) || b.Id == SvGameServer.DerelictStationId)
                .Select(b => (b.Id, b.Name, b.SystemX, b.SystemZ)).ToList();
            Assert.Equal(before, after);
            Assert.Contains(GuaranteedDerelict(on), on.DerelictIdsForTest);

            // The start system still has its own trading station (a derelict never stands in for it).
            string sys = StartSystemOf(on);
            Assert.Contains(on.Galaxy.Systems.First(s => s.Id == sys).Bodies,
                b => b.Kind == CelestialKind.SpaceStation && !DerelictPlacer.IsDerelictId(b.Id));
            on.Stop();
        }
    }

    // ---------------- layout ----------------

    [Fact]
    public void Layout_IsSeedStable_AndCarriesTheHooksOfTheLaterPhases()
    {
        var a = DerelictGenerator.Generate(1234, _content, proceduralOnly: true);
        var b = DerelictGenerator.Generate(1234, _content, proceduralOnly: true);
        Assert.Equal((a.Width, a.Height, a.Length, a.Source), (b.Width, b.Height, b.Length, b.Source));
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                }

        Assert.Equal(a.CrateSpots, b.CrateSpots);
        Assert.Equal(a.SpawnerSpot, b.SpawnerSpot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Layouts_AreBrokenButBoardable_WithSealedAndBreachedRooms_PostsAndSpots(bool proceduralOnly)
    {
        ushort vendor = _content.GetBlock("station_vendor")!.NumericId.Value;
        var forbidden = new HashSet<ushort>(_content.Blocks.Values
            .Where(d => d.Category == "flora" || d.Key is "force_field" or "data_cache" or "heal_tank")
            .Select(d => d.NumericId.Value));
        int templates = 0;
        for (long seed = 1; seed <= 16; seed++)
        {
            var l = DerelictGenerator.Generate(seed * 7919, _content, proceduralOnly);
            bool template = l.Source.StartsWith("template:", StringComparison.Ordinal);
            templates += template ? 1 : 0;

            Assert.NotEmpty(l.TraderPosts);
            Assert.All(l.TraderPosts, p => Assert.Equal(vendor, l.Get(p.X, p.Y, p.Z)));
            Assert.True(l.CrateSpots.Count >= 1, $"seed {seed}: no crate spot");
            Assert.All(l.CrateSpots, c => Assert.True(l.Get(c.X, c.Y, c.Z) == 0 && l.Get(c.X, c.Y - 1, c.Z) != 0, $"seed {seed}: crate spot {c} is no empty deck cell"));
            Assert.NotNull(l.SpawnerSpot);

            for (int x = 0; x < l.Width; x++)
                for (int y = 0; y < l.Height; y++)
                    for (int z = 0; z < l.Length; z++)
                    {
                        Assert.DoesNotContain(l.Get(x, y, z), forbidden);
                    }

            if (!template)
            {
                Assert.True(l.ArrivalSealed, $"seed {seed} ({l.Source}): the command room must keep its air");
                Assert.True(l.BreachedRoomCount >= 1, $"seed {seed} ({l.Source}): no room is open to space");
                Assert.True(l.SealedRoomCount >= 1);
                Assert.NotEmpty(l.Doors);
            }
        }

        if (proceduralOnly)
        {
            Assert.Equal(0, templates);
        }
    }

    // ---------------- the server ----------------

    [Fact]
    public void Boarding_ADerelict_FindsNobody_NoTrade_AndArrivesInASealedRoom()
    {
        var server = NewServer("drl_board", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);

            Assert.Equal(0, server.NpcCount); // no crew, no visiting trader
            Assert.Empty(server.SpaceStationMarkers.Where(m => m.Type is "vendor" or "mission_board"));
            Assert.True(server.StationCellSealedForTest(id, p.State.Position.ToBlock()), "the boarder arrives in a room with air");

            // The broken trading post: a station_vendor block, but nothing trades there.
            var layout = server.DerelictLayoutForTest(id)!;
            var post = server.DerelictCellToWorldForTest(id, layout.TraderPosts[0]);
            Assert.Equal("station_vendor", _content.BlockById(server.World.GetBlock(post))!.Key);
            p.State.Position = new Vector3f(post.X + 0.5f, post.Y, post.Z + 1.5f);
            Assert.False(server.NearSpaceStationVendor(p.State));

            // The hooks of the later phases resolve into the interior world once it is stamped.
            var crates = server.DerelictCrateSpotsForTest(id);
            Assert.Equal(layout.CrateSpots.Count, crates.Count);
            Assert.NotNull(server.DerelictSpawnerSpotForTest(id));
            Assert.All(crates, c => Assert.Equal("crate", _content.BlockById(server.World.GetBlock(c))!.Key)); // #2633: the old supply crates

            // Nobody's home: a heal tank set up aboard (on top of a crate) does not take a home spawn.
            p.State.InstantBuild = true;
            var spot = new Vector3i(crates[0].X, crates[0].Y + 1, crates[0].Z);
            p.State.Position = new Vector3f(spot.X + 0.5f, spot.Y, spot.Z + 2.4f); // beside it, not in the cell
            server.PlaceBlock("Justus", spot.X, spot.Y, spot.Z, "heal_tank");
            Assert.Equal("heal_tank", _content.BlockById(server.World.GetBlock(spot))!.Key);
            server.SetSpawnPoint("Justus", spot.X, spot.Y, spot.Z);
            Assert.True(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "a derelict is never a home spawn");
            server.Stop();
        }
    }

    [Fact]
    public void TradingRules_NeverCountADerelict()
    {
        var server = NewServer("drl_trade", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string id = GuaranteedDerelict(server);
            server.EnterSpace("Pilot");
            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Id == id && e.Kind == CombatEntityKind.SpaceStation);

            // The dock lesson never points at it, even from right beside its hull.
            var contact = server.SpaceEntitiesFor("Pilot").First(e => e.Id == id);
            server.ShipMove("Pilot", contact.Position.X, contact.Position.Y, contact.Position.Z - 30f);
            Assert.NotEqual(id, server.NearestStationForTest(p));
            server.Stop();
        }
    }

    [Fact]
    public void BreachedRooms_DrainTheTank_SealedRoomsBreathe_AndFallingOutIsASpacewalk()
    {
        var server = NewServer("drl_air", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var arrival = p.State.Position;

            p.State.Oxygen = 40f;
            for (int i = 0; i < 4; i++)
            {
                p.State.Position = arrival;
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen > 40f, "a sealed room refills the tank");
            Assert.Equal(2, p.State.LifeSupportSource);

            var breached = BreachedDeckCell(server, id);
            float o2 = p.State.Oxygen;
            for (int i = 0; i < 4; i++)
            {
                p.State.Position = new Vector3f(breached.X + 0.5f, breached.Y, breached.Z + 0.5f);
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < o2, $"a breached room holds no air (O2 {o2} → {p.State.Oxygen})");

            // Out through a hole and away: no fall for ever — since #2636 a real spacewalk beside the hull
            // (DerelictSpacewalkTests has the details).
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 120f, arrival.Z);
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.False(server.InStation("Justus"));
            Assert.True(server.InSpace("Justus") && p.State.InEva, "falling out of the hull is a spacewalk");
            server.Stop();
        }
    }

    [Fact]
    public void MinedBlocks_StayMined_AcrossARestart_TheHullFollows_AndTheBrokenPostDropsAVendor()
    {
        string id;
        Vector3i post, postCell;
        {
            var s1 = NewServer("drl_mine", out var repo1);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = BoardGuaranteed(s1, p);
                Equip(p, "diamond_drill");
                var layout = s1.DerelictLayoutForTest(id)!;
                postCell = layout.TraderPosts[0];
                post = s1.DerelictCellToWorldForTest(id, postCell);
                p.State.Position = new Vector3f(post.X + 0.5f, post.Y, post.Z + 1.5f);
                s1.MineBlock("Justus", post.X, post.Y, post.Z);

                Assert.True(s1.World.GetBlock(post).IsAir);
                Assert.Equal(1, p.State.Inventory.CountOf("station_vendor")); // the old post still works once rebuilt
                Assert.False(s1.StationCellsForTest(id).ContainsKey(postCell)); // the hull seen from space lost it too
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("drl_mine", out var repo2);
        using (repo2)
        {
            Assert.NotNull(s2.DerelictLayoutForTest(id)); // builds it (a derelict is built on first need)
            Assert.NotEmpty(s2.StationCellsForTest(id));
            Assert.False(s2.StationCellsForTest(id).ContainsKey(postCell), "the derelict's build keeps the mined cell after a restart");
            var p = s2.AddLocalPlayer("Justus");
            s2.LeaveStation("Justus"); // the rejoin may have put the player back aboard
            Assert.False(s2.InStation("Justus"));
            BoardGuaranteed(s2, p);
            Assert.True(s2.World.GetBlock(post).IsAir, "the interior keeps the mined post after a restart");
            s2.Stop();
        }
    }

    [Fact]
    public void Spacewalk_MinesTheHull_AndTheInteriorFollows()
    {
        var server = NewServer("drl_eva", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = GuaranteedDerelict(server);
            server.EnterSpace("Justus");
            Equip(p, "diamond_drill");
            p.State.InEva = true;
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y + 12f, contact.Position.Z);

            // An outer cell: the highest solid cell of the build.
            var cells = server.StationCellsForTest(id);
            var target = cells.Keys.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).First();
            for (int i = 0; i < 12 && cells.ContainsKey(target); i++)
            {
                server.HandleStructureEditForTest("Justus", new StructureEditIntent { StructureId = id, X = target.X, Y = target.Y, Z = target.Z, Mine = true });
            }

            Assert.False(server.StationCellsForTest(id).ContainsKey(target), "anyone may salvage a derelict's hull on a spacewalk");

            // Boarding later stamps the build as it is now.
            p.State.InEva = false;
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z - 30f);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"));
            Assert.True(server.World.GetBlock(server.DerelictCellToWorldForTest(id, target)).IsAir);
            server.Stop();
        }
    }

    [Fact]
    public void TheLongQuiet_CatchesABoarderWhoFallsOffItsOpenCorner()
    {
        var server = NewServer("drl_quiet", out var repo, seed: 5);
        using (repo)
        {
            string host = server.UniqueSiteBodyForTest("derelict_host")!;
            var p = server.AddLocalPlayer("Salvager");
            if (p.CurrentLocationId != host)
            {
                if (!server.Ship.Modules.Contains("jump_generator"))
                {
                    server.Ship.Modules.Add("jump_generator");
                }

                server.Travel("Salvager", host);
                Assert.Equal(host, p.CurrentLocationId);
            }

            server.EnterSpace("Salvager");
            var contact = server.SpaceEntitiesFor("Salvager").First(e => e.Id == SvGameServer.DerelictStationId);
            server.ShipMove("Salvager", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            server.BoardStation("Salvager", SvGameServer.DerelictStationId);
            Assert.True(server.InStation("Salvager"));
            var deck = p.State.Position;

            // Walked off the torn stern corner and fell: before #2632 nothing ever caught this.
            p.State.Position = new Vector3f(deck.X + 10f, deck.Y - 150f, deck.Z + 6f);
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(Math.Abs(p.State.Position.Y - deck.Y) < 4f, $"expected the boarder back on the deck, at {p.State.Position}");
            server.Stop();
        }
    }

    [Fact]
    public void WorldOption_ReadsFromTheCommandLine()
    {
        var config = new ServerConfig();
        Assert.Equal(Frequency.Normal, config.World.Derelicts);
        var applied = config.ApplyCommandLine(new[] { "--derelicts", "off" });
        Assert.Contains("derelicts", applied);
        Assert.Equal(Frequency.Off, config.World.Derelicts);
    }

    /// <summary>A deck cell of the guaranteed derelict that sits under the station's roof but holds no air — a room the
    /// decay opened to space (the hangar at the least).</summary>
    private static Vector3i BreachedDeckCell(SvGameServer server, string id)
    {
        var layout = server.DerelictLayoutForTest(id)!;
        for (int x = 1; x < layout.Width - 1; x++)
            for (int z = 1; z < layout.Length - 1; z++)
                for (int y = 1; y < layout.Height - 2; y++)
                {
                    bool deck = layout.Get(x, y, z) == 0 && layout.Get(x, y + 1, z) == 0 && layout.Get(x, y - 1, z) != 0;
                    bool roofed = Enumerable.Range(2, 6).Any(k => layout.Get(x, y + k, z) != 0);
                    if (!deck || !roofed)
                    {
                        continue;
                    }

                    var w = server.DerelictCellToWorldForTest(id, new Vector3i(x, y, z));
                    if (server.World.GetBlock(w).IsAir && !server.StationCellSealedForTest(id, w))
                    {
                        return w;
                    }
                }

        throw new InvalidOperationException("the derelict has no breached room");
    }
}
