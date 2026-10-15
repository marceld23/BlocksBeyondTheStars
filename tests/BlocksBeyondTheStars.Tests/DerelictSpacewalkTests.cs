// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Derelict stations, phase 2 (#2636): falling out of a generated derelict through a hole is a real spacewalk — the
/// flight opens on an EVA at the spot matching where the boarder left the hull, the suit tank drains, and E at an opening
/// boards onto the deck behind it. Nothing aboard ever triggers it. And every derelict, module station or classic
/// template, sets a boarder down in a sealed pocket with air (the follow-up fix).
/// </summary>
public sealed class DerelictSpacewalkTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DerelictSpacewalkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_derelict_eva_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Flies a fresh player up to the start system's derelict and boards it from the ship; returns its id.</summary>
    private static string BoardGuaranteed(SvGameServer server, PlayerSession p)
    {
        string id = DerelictPlacer.BodyId(server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId, 0);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid));
        return id;
    }

    private static (Vector3i Min, Vector3i Max) Box(SvGameServer server, string id)
    {
        var cells = server.StationCellsForTest(id).Keys.ToList();
        return (new Vector3i(cells.Min(c => c.X), cells.Min(c => c.Y), cells.Min(c => c.Z)),
            new Vector3i(cells.Max(c => c.X), cells.Max(c => c.Y), cells.Max(c => c.Z)));
    }

    /// <summary>Runs ticks (the void check is 1 Hz) until the player is out in a flight instance, at most two seconds.</summary>
    private static void TickUntilInSpace(SvGameServer server, string pid)
    {
        for (int i = 0; i < 5 && !server.InSpace(pid); i++)
        {
            server.TickForTest(0.5);
        }
    }

    // ---------------- falling out ----------------

    [Fact]
    public void FallingOutThroughTheHull_IsASpacewalk_AtTheMatchingSpot_AndTheTankDrains()
    {
        var server = NewServer("eva_fall", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var (min, _) = Box(server, id);
            var arrival = p.State.Position;

            // Fell through a hole in the floor: three blocks under the hull's lowest cell, below the arrival.
            float bottom = server.DerelictCellToWorldForTest(id, min).Y;
            var fell = new Vector3f(arrival.X + 0.25f, bottom - 3f, arrival.Z - 0.25f);
            p.State.Position = fell;
            p.State.Yaw = 123f;
            var expected = server.DerelictInteriorToFlightForTest(id, fell);
            TickUntilInSpace(server, "Justus");

            Assert.False(server.InStation("Justus"), "leaving the hull leaves the derelict");
            Assert.True(server.InSpace("Justus"), "the flight opened");
            Assert.True(p.State.InEva, "…on a spacewalk");
            var suit = server.SuitPoseForTest("Justus");
            Assert.NotNull(suit);
            Assert.True(Math.Abs(suit!.Value.X - expected.X) < 0.01f && Math.Abs(suit.Value.Y - (expected.Y + 1.5f)) < 0.01f
                && Math.Abs(suit.Value.Z - expected.Z) < 0.01f, $"the suit floats where it left the hull: {suit} vs {expected} (+1.5 eye)");

            // The derelict hangs right there in this flight, and the suit is just outside its hull.
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            Assert.True(contact.Position.DistanceSquared(suit.Value) < 60f * 60f, "the suit floats next to the derelict");

            // The suit tank drains, as on every spacewalk — no instant death.
            p.State.Oxygen = 50f;
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(p.State.Oxygen < 50f, $"the suit tank drains on the spacewalk (O2 {p.State.Oxygen})");
            Assert.True(p.State.Health > 0f);
            server.Stop();
        }
    }

    [Fact]
    public void FallingFarBelow_StartsJustOutsideTheHull()
    {
        var server = NewServer("eva_far", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var (min, _) = Box(server, id);
            var arrival = p.State.Position;
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 150f, arrival.Z); // the 1 Hz check caught a long fall late
            TickUntilInSpace(server, "Justus");

            Assert.True(p.State.InEva);
            var bottomFlight = server.DerelictCellToFlightForTest(id, min).Y;
            var suit = server.SuitPoseForTest("Justus")!.Value;
            Assert.InRange(suit.Y, bottomFlight - 6f + 1.5f - 0.01f, bottomFlight); // held to the reach under the hull, at eye height
            server.Stop();
        }
    }

    [Fact]
    public void NothingAboard_StepsOut_DecksRoofOrAJumpOnIt()
    {
        var server = NewServer("eva_aboard", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var layout = server.DerelictLayoutForTest(id)!;
            var (min, max) = Box(server, id);

            var decks = new List<Vector3f>();
            for (int x = 0; x < layout.Width; x += 2)
                for (int z = 0; z < layout.Length; z += 2)
                    for (int y = 1; y < layout.Height - 1; y++)
                    {
                        if (layout.Get(x, y, z) == 0 && layout.Get(x, y + 1, z) == 0 && layout.Get(x, y - 1, z) != 0)
                        {
                            var w = server.DerelictCellToWorldForTest(id, new Vector3i(x, y, z));
                            decks.Add(new Vector3f(w.X + 0.5f, w.Y, w.Z + 0.5f));
                        }
                    }

            // Twenty decks spread over the whole hull (the outermost ones included).
            var spots = Enumerable.Range(0, 20).Select(i => decks[i * (decks.Count - 1) / 19]).Distinct().ToList();

            // The roof, a jump off it, and the very edge of the hull's box on every side.
            var top = server.DerelictCellToWorldForTest(id, max);
            var low = server.DerelictCellToWorldForTest(id, min);
            spots.Add(new Vector3f(top.X + 0.5f, top.Y + 1f, top.Z + 0.5f));
            spots.Add(new Vector3f(top.X + 0.5f, top.Y + 2.3f, top.Z + 0.5f));
            spots.Add(new Vector3f(top.X + 2.9f, top.Y - 1f, top.Z + 0.5f));
            spots.Add(new Vector3f(low.X - 1.9f, low.Y + 1f, low.Z + 0.5f));
            spots.Add(new Vector3f(low.X + 0.5f, low.Y - 1.9f, low.Z + 0.5f));
            Assert.True(decks.Count > 20, "the layout has decks to walk");

            foreach (var spot in spots)
            {
                p.State.Position = spot;
                server.RunVoidRescueForTest(); // the void check, right now
                Assert.False(server.DerelictSpacewalkQueuedForTest("Justus"), $"standing at {spot} aboard must never step out");
            }

            server.TickForTest(0.5); // and a whole tick from the last spot: still aboard
            Assert.True(server.InStation("Justus") && !server.InSpace("Justus"));
            server.Stop();
        }
    }

    // ---------------- coming back ----------------

    [Fact]
    public void BackFromTheSpacewalk_ThroughABreach_BoardsOnTheDeckBehindIt()
    {
        var server = NewServer("eva_breach", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var arrival = p.State.Position;
            var deck = BreachedDeckCell(server, id);
            var deckFlight = server.DerelictInteriorToFlightForTest(id, new Vector3f(deck.X + 0.5f, deck.Y, deck.Z + 0.5f));

            // Out through a hole…
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 150f, arrival.Z);
            TickUntilInSpace(server, "Justus");
            Assert.True(p.State.InEva);

            // …and floating back in at the breached room: E boards onto that deck, not in the command room.
            server.ShipMove("Justus", deckFlight.X, deckFlight.Y + 1.5f, deckFlight.Z);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"), "a derelict boards from the spacewalk");
            Assert.False(p.State.InEva);
            var at = p.State.Position;
            Assert.True(Math.Abs(at.X - (deck.X + 0.5f)) <= 2.01f && Math.Abs(at.Z - (deck.Z + 0.5f)) <= 2.01f && Math.Abs(at.Y - deck.Y) <= 1.01f,
                $"expected the boarder on the deck behind the hole ({deck}), at {at}");
            Assert.False(server.StationCellSealedForTest(id, at.ToBlock()), "the deck behind a breach holds no air");

            // Out again: the boarding remembered the suit's spot inside the hull — the ship never waits there.
            p.State.Position = new Vector3f(at.X, at.Y - 150f, at.Z);
            TickUntilInSpace(server, "Justus");
            Assert.True(p.State.InEva);
            var ship = server.ShipFloatForTest("Justus")!.Value;
            var centre = server.SpaceEntitiesFor("Justus").First(e => e.Id == id).Position;
            var layout = server.DerelictLayoutForTest(id)!;
            bool outside = Math.Abs(ship.X - centre.X) > layout.Width / 2f + 10f || Math.Abs(ship.Y - centre.Y) > layout.Height / 2f + 10f
                || Math.Abs(ship.Z - centre.Z) > layout.Length / 2f + 10f;
            Assert.True(outside, $"the ship waits clear of the hull, at {ship} (derelict at {centre})");
            server.Stop();
        }
    }

    [Fact]
    public void BackFromTheSpacewalk_AwayFromAnyOpening_ArrivesInTheSealedRoom()
    {
        var server = NewServer("eva_far_board", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string id = BoardGuaranteed(server, p);
            var arrival = p.State.Position;
            p.State.Position = new Vector3f(arrival.X, arrival.Y - 150f, arrival.Z);
            TickUntilInSpace(server, "Justus");

            // E from twenty blocks off the hull: the usual arrival in the command room, with air.
            var contact = server.SpaceEntitiesFor("Justus").First(e => e.Id == id);
            var layout = server.DerelictLayoutForTest(id)!;
            server.ShipMove("Justus", contact.Position.X, contact.Position.Y, contact.Position.Z + layout.Length / 2f + 20f);
            server.BoardStation("Justus", id);
            Assert.True(server.InStation("Justus"));
            Assert.True(server.StationCellSealedForTest(id, p.State.Position.ToBlock()), "the usual arrival holds air");
            server.Stop();
        }
    }

    // ---------------- every arrival holds air ----------------

    [Fact]
    public void EveryDerelict_ModuleOrTemplate_HasASealedArrivalWithAir()
    {
        int templates = 0, modules = 0;
        var seenTemplates = new HashSet<string>(StringComparer.Ordinal);
        for (long seed = 1; seed <= 400; seed++)
        {
            var l = DerelictGenerator.Generate(seed * 104729, _content);
            bool template = l.Source.StartsWith("template:", StringComparison.Ordinal);
            if (template)
            {
                templates++;
                seenTemplates.Add(l.Source);
            }
            else
            {
                modules++;
            }

            var a = l.Arrival;
            Assert.True(l.Get(a.X, a.Y, a.Z) == 0 && l.Get(a.X, a.Y + 1, a.Z) == 0 && l.Get(a.X, a.Y - 1, a.Z) != 0,
                $"seed {seed} ({l.Source}): the arrival {a} is no deck to stand on");
            Assert.True(l.ArrivalSealed, $"seed {seed} ({l.Source}): the generator's verdict says the arrival leaks");
            Assert.True(SealedByTheServersRule(l), $"seed {seed} ({l.Source}): the arrival pocket leaks by the server's air rule");
        }

        Assert.True(modules > 0 && templates > 0, $"both sources sampled ({modules} module stations, {templates} templates)");
        Assert.True(seenTemplates.Count >= 3, $"most classic templates sampled: {string.Join(", ", seenTemplates)}");
    }

    /// <summary>A world seed whose galaxy (Frequent derelicts) holds classic-template derelicts — the pocket waystation
    /// (the smallest) and the hub outpost; every template is covered at layout level above. Found by sampling; a change
    /// to the placement or the source pick moves it — the test then says so.</summary>
    [Fact]
    public void TemplateDerelicts_SetTheBoarderDownWithAir()
    {
        const long seed = -4427142430354310808L;
        var server = NewServer("eva_tpl_" + (seed & 0xFFFF), out var repo, seed: seed, derelicts: Frequency.Frequent);
        using (repo)
        {
            var templates = server.DerelictIdsForTest
                .Where(d => server.DerelictLayoutForTest(d)!.Source.StartsWith("template:", StringComparison.Ordinal)).ToList();
            Assert.True(templates.Count > 0, $"seed {seed} no longer holds a template derelict — pick another (see the summary)");

            var p = server.AddLocalPlayer("Justus");
            foreach (var id in templates)
            {
                server.LeaveStation("Justus");
                server.Travel("Justus", id); // boards straight from the travel screen
                Assert.True(server.InStation("Justus"), $"boarding the template derelict {id}");
                string source = server.DerelictLayoutForTest(id)!.Source;
                Assert.True(server.StationCellSealedForTest(id, p.State.Position.ToBlock()),
                    $"the template derelict {id} ({source}) set the boarder down in vacuum");

                p.State.Oxygen = 40f;
                for (int i = 0; i < 4; i++)
                {
                    server.TickForTest(0.5);
                }

                Assert.True(p.State.Oxygen > 40f, $"{source}: the arrival refills the suit tank");
            }

            server.Stop();
        }
    }

    // ---------------- helpers ----------------

    /// <summary>The server's station air fill, rebuilt from the layout alone: it moves face to face through every cell
    /// that is not an airtight full cube (the content flag, plus the door blocks and the station core) and not a door
    /// opening, and leaks when it leaves the bounding box.</summary>
    private bool SealedByTheServersRule(DerelictLayout l)
    {
        var extras = new HashSet<string>(StringComparer.Ordinal) { "door_slide", "door_hinge", "door_energy", "station_core" };
        var doors = new HashSet<Vector3i>();
        foreach (var d in l.Doors)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                doors.Add(new Vector3i(d.Cell.X, d.Cell.Y + dy, d.Cell.Z));
            }
        }

        bool Wall(Vector3i c)
        {
            if (doors.Contains(c))
            {
                return true;
            }

            ushort b = l.Get(c.X, c.Y, c.Z);
            if (b == 0 || _content.BlockById(new BlockId(b)) is not { } def)
            {
                return false;
            }

            return (def.Airtight || extras.Contains(def.Key)) && ShapeCode.IsCube(l.GetShape(c.X, c.Y, c.Z));
        }

        var start = l.Arrival;
        if (Wall(start))
        {
            return false;
        }

        var seen = new HashSet<Vector3i> { start };
        var q = new Queue<Vector3i>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var n in new[]
            {
                new Vector3i(c.X + 1, c.Y, c.Z), new Vector3i(c.X - 1, c.Y, c.Z), new Vector3i(c.X, c.Y + 1, c.Z),
                new Vector3i(c.X, c.Y - 1, c.Z), new Vector3i(c.X, c.Y, c.Z + 1), new Vector3i(c.X, c.Y, c.Z - 1),
            })
            {
                if (!l.InBounds(n.X, n.Y, n.Z))
                {
                    return false;
                }

                if (!Wall(n) && seen.Add(n))
                {
                    q.Enqueue(n);
                }
            }
        }

        return true;
    }

    /// <summary>A deck cell of a boarded derelict under its roof that holds no air — a room the decay opened to space — and
    /// out of every doorway (a door's opening is three cells high and a step to either side).</summary>
    private static Vector3i BreachedDeckCell(SvGameServer server, string id)
    {
        var layout = server.DerelictLayoutForTest(id)!;
        var doors = server.StationDoorsForTest(id).Select(d => server.DerelictCellToWorldForTest(id, d.Cell)).ToList();
        bool NearDoor(Vector3i w) => doors.Any(d => Math.Abs(d.X - w.X) <= 1 && Math.Abs(d.Z - w.Z) <= 1 && w.Y >= d.Y - 1 && w.Y <= d.Y + 2);
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
                    if (!NearDoor(w) && server.World.GetBlock(w).IsAir && !server.StationCellSealedForTest(id, w))
                    {
                        return w;
                    }
                }

        throw new InvalidOperationException("the derelict has no breached room");
    }
}
