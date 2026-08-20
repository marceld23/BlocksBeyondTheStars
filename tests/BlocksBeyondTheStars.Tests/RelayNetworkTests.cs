// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The SPS relay network (#1125, Track F — F-1). A commissioned player station converts into a relay by
/// pouring in the data-driven bill of materials — co-op contributable, delivered in person. Two completed
/// relays in adjacent systems (star-map distance ≤ the definition's link range) form a jump lane: travel
/// between those systems needs no jump generator. Lanes are never persisted — they re-derive from the
/// completed relays on every start.
/// </summary>
public sealed class RelayNetworkTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RelayNetworkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_relay_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Deploys + builds out a commissioned player station at the pilot's CURRENT location's space
    /// (core + 11 walls + airlock door — the minimum commissioning hull). Leaves the pilot on an EVA in the
    /// station's space instance with InstantBuild OFF, so contribution tests run in survival terms.</summary>
    private static string BuildCommissionedStation(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        if (!server.InSpace(playerId))
        {
            server.EnterSpace(playerId);
        }

        pilot.State.InEva = true;
        pilot.State.InstantBuild = true; // free build for the hull — the meter is what these tests cost out

        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest(playerId,
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }

        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });

        pilot.State.InstantBuild = false;
        return id;
    }

    /// <summary>Completes a station's relay meter for free (creative fill), restoring the InstantBuild flag.</summary>
    private static void CompleteRelayFree(SvGameServer server, PlayerSession pilot, string stationId, GameContent content)
    {
        bool instant = pilot.State.InstantBuild;
        pilot.State.InstantBuild = true;
        foreach (var line in content.Relay!.Costs)
        {
            server.ContributeRelayForTest(pilot.State.PlayerId, stationId, line.Item, 1);
        }

        pilot.State.InstantBuild = instant;
    }

    /// <summary>The two closest procedural systems of the galaxy — the natural spot for the first lane.</summary>
    private static (StarSystem A, StarSystem B) NearestSystemPair(SvGameServer server)
    {
        var systems = server.Galaxy.Systems.Where(s => s.Id.StartsWith("sys", StringComparison.Ordinal)).ToList();
        (StarSystem A, StarSystem B) best = (systems[0], systems[1]);
        float bestSq = float.MaxValue;
        for (int i = 0; i < systems.Count; i++)
        {
            for (int j = i + 1; j < systems.Count; j++)
            {
                float dx = systems[i].MapX - systems[j].MapX;
                float dy = systems[i].MapY - systems[j].MapY;
                float sq = dx * dx + dy * dy;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = (systems[i], systems[j]);
                }
            }
        }

        return best;
    }

    private static CelestialBody LandableBodyOf(StarSystem system)
        => system.Bodies.First(b => !string.IsNullOrEmpty(b.PlanetType));

    /// <summary>Flies a pilot to a body the regular way (their ship gets a jump generator for the trip, so
    /// cross-system hops work) — the real travel flow keys the ship + session to the target body, which the
    /// relay host/system derivation relies on. No-op when already there.</summary>
    private static void TravelTo(SvGameServer server, PlayerSession pilot, string bodyId)
    {
        if (pilot.CurrentLocationId == bodyId)
        {
            return;
        }

        if (!server.Ship.Modules.Contains("jump_generator"))
        {
            server.Ship.Modules.Add("jump_generator");
        }

        server.Travel(pilot.State.PlayerId, bodyId);
        Assert.Equal(bodyId, pilot.CurrentLocationId); // the trip must actually have happened
    }

    // ---------------- Content consistency ----------------

    [Fact]
    public void RelayDefinition_Loads_AndEveryCostItemResolves()
    {
        var def = _content.Relay;
        Assert.NotNull(def);
        Assert.True(def!.LinkRange > 0f);
        Assert.NotEmpty(def.Costs);
        foreach (var line in def.Costs)
        {
            Assert.True(line.Count > 0);
            Assert.NotNull(_content.GetItem(line.Item)); // a typo'd item key must fail loudly here
        }
    }

    // ---------------- Contributions ----------------

    [Fact]
    public void Contributions_ConsumeItems_AndCompleteTheRelay()
    {
        var server = NewServer("relay_contrib", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Builder");
            string id = BuildCommissionedStation(server, pilot);
            Assert.True(server.StationIsBoardableForTest(id));

            // A partial delivery of the first cost line consumes exactly what was offered.
            var def = _content.Relay!;
            var first = def.Costs[0];
            int half = first.Count / 2;
            pilot.State.Inventory.Add(first.Item, first.Count, _content.MaxStackOf(first.Item));
            server.ContributeRelayForTest("Builder", id, first.Item, half);
            Assert.False(server.RelayCompletedForTest(id));
            Assert.Equal(first.Count - half, pilot.State.Inventory.CountOf(first.Item));

            // "Give everything I have" per line — the server clamps to missing AND held.
            foreach (var line in def.Costs)
            {
                pilot.State.Inventory.Add(line.Item, line.Count, _content.MaxStackOf(line.Item));
                server.ContributeRelayForTest("Builder", id, line.Item, int.MaxValue);
            }

            Assert.True(server.RelayCompletedForTest(id));
            Assert.True(pilot.State.AchievementCounters.TryGetValue("relay:commissioned", out int n) && n == 1);

            var net = server.RelayNetworkForTest();
            Assert.True(net.Enabled);
            Assert.Contains(net.Relays, r => r.StationId == id && r.Completed);

            // Completed means done — further deliveries are refused, nothing is consumed.
            pilot.State.Inventory.Add(first.Item, 5, _content.MaxStackOf(first.Item));
            server.ContributeRelayForTest("Builder", id, first.Item, 5);
            Assert.True(pilot.State.Inventory.CountOf(first.Item) >= 5);
        }
    }

    [Fact]
    public void Contributions_AreDeliveredInPerson_AndAreCoop()
    {
        var server = NewServer("relay_person", out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            string id = BuildCommissionedStation(server, owner);
            var def = _content.Relay!;
            var first = def.Costs[0];

            // A player in ANOTHER system contributes nothing — materials travel with people.
            var far = server.AddLocalPlayer("Faraway");
            var elsewhere = server.Galaxy.Systems
                .Where(s => s.Bodies.All(b => b.Id != far.CurrentLocationId))
                .SelectMany(s => s.Bodies)
                .First(b => !string.IsNullOrEmpty(b.PlanetType));
            far.CurrentLocationId = elsewhere.Id;
            far.State.Inventory.Add(first.Item, first.Count, _content.MaxStackOf(first.Item));
            server.ContributeRelayForTest("Faraway", id, first.Item, int.MaxValue);
            Assert.Equal(first.Count, far.State.Inventory.CountOf(first.Item)); // untouched
            Assert.DoesNotContain(server.RelayNetworkForTest().Relays,
                r => r.StationId == id && r.Contributed.Any(c => c > 0));

            // A DIFFERENT player at the station's host body contributes fine — the meter is co-op.
            var friend = server.AddLocalPlayer("Friend");
            friend.State.Inventory.Add(first.Item, first.Count, _content.MaxStackOf(first.Item));
            server.ContributeRelayForTest("Friend", id, first.Item, int.MaxValue);
            Assert.Equal(0, friend.State.Inventory.CountOf(first.Item));
            var relay = server.RelayNetworkForTest().Relays.First(r => r.StationId == id);
            Assert.Equal(first.Count, relay.Contributed[System.Array.IndexOf(relay.Items, first.Item)]);

            // Empty-handed in survival: nothing happens (no free progress).
            var broke = server.AddLocalPlayer("Broke");
            server.ContributeRelayForTest("Broke", id, def.Costs[1].Item, int.MaxValue);
            relay = server.RelayNetworkForTest().Relays.First(r => r.StationId == id);
            Assert.Equal(0, relay.Contributed[System.Array.IndexOf(relay.Items, def.Costs[1].Item)]);
        }
    }

    // ---------------- Lanes ----------------

    [Fact]
    public void AdjacentCompletedRelays_FormAJumpLane_ThatCarriesJumpsWithoutAGenerator()
    {
        var server = NewServer("relay_lanes", out var repo);
        using (repo)
        {
            var (sysA, sysB) = NearestSystemPair(server);
            float dx = sysA.MapX - sysB.MapX, dy = sysA.MapY - sysB.MapY;
            Assert.True(dx * dx + dy * dy <= _content.Relay!.LinkRange * _content.Relay.LinkRange,
                "seed 1's closest system pair should sit within the data link range");

            // One relay per system, each raised by its own builder on the spot.
            var one = server.AddLocalPlayer("One");
            TravelTo(server, one, LandableBodyOf(sysA).Id);
            string stationA = BuildCommissionedStation(server, one);
            CompleteRelayFree(server, one, stationA, _content);
            Assert.True(server.RelayCompletedForTest(stationA));
            Assert.Equal(0, server.RelayLaneCountForTest); // one relay alone links nothing

            var two = server.AddLocalPlayer("Two");
            TravelTo(server, two, LandableBodyOf(sysB).Id);
            string stationB = BuildCommissionedStation(server, two);
            CompleteRelayFree(server, two, stationB, _content);

            Assert.True(server.HasJumpLaneForTest(sysA.Id, sysB.Id));
            Assert.True(server.HasJumpLaneForTest(sysB.Id, sysA.Id)); // unordered

            // A generator-less pilot rides the lane… (the generator only ferries them INTO position).
            var jumper = server.AddLocalPlayer("Jumper");
            TravelTo(server, jumper, LandableBodyOf(sysA).Id);
            server.Ship.Modules.Remove("jump_generator"); // Jumper's ship was served last by the travel
            Assert.DoesNotContain(sysB.Id, jumper.State.KnownSystems);
            server.HyperjumpToSystem("Jumper", sysB.Id);
            Assert.Contains(sysB.Id, jumper.State.KnownSystems); // arrived via the lane

            // …but a lane-less target still needs the generator.
            var sysC = server.Galaxy.Systems.First(s =>
                s.Id.StartsWith("sys", StringComparison.Ordinal) && s.Id != sysA.Id && s.Id != sysB.Id);
            server.HyperjumpToSystem("Jumper", sysC.Id);
            Assert.DoesNotContain(sysC.Id, jumper.State.KnownSystems);
        }
    }

    // ---------------- Persistence across restart ----------------

    [Fact]
    public void RelayMeters_AndLanes_SurviveARestart()
    {
        string stationA, stationB, sysAId, sysBId;
        string partialItem;
        int partialGiven;
        {
            var s1 = NewServer("relay_persist", out var repo1);
            using (repo1)
            {
                var (sysA, sysB) = NearestSystemPair(s1);
                sysAId = sysA.Id;
                sysBId = sysB.Id;

                var one = s1.AddLocalPlayer("One");
                TravelTo(s1, one, LandableBodyOf(sysA).Id);
                stationA = BuildCommissionedStation(s1, one);
                CompleteRelayFree(s1, one, stationA, _content);

                // Station B only gets a PARTIAL survival delivery before the restart.
                var two = s1.AddLocalPlayer("Two");
                TravelTo(s1, two, LandableBodyOf(sysB).Id);
                stationB = BuildCommissionedStation(s1, two);
                var line = _content.Relay!.Costs[0];
                partialItem = line.Item;
                partialGiven = Math.Max(1, line.Count / 3);
                two.State.Inventory.Add(line.Item, partialGiven, _content.MaxStackOf(line.Item));
                s1.ContributeRelayForTest("Two", stationB, line.Item, partialGiven);

                Assert.True(s1.RelayCompletedForTest(stationA));
                Assert.False(s1.RelayCompletedForTest(stationB));
                Assert.Equal(0, s1.RelayLaneCountForTest); // only one relay so far
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("relay_persist", out var repo2);
        using (repo2)
        {
            // The completed relay and the partial meter both came back from the metadata blob.
            Assert.True(s2.RelayCompletedForTest(stationA));
            var relayB = s2.RelayNetworkForTest().Relays.First(r => r.StationId == stationB);
            Assert.Equal(partialGiven, relayB.Contributed[System.Array.IndexOf(relayB.Items, partialItem)]);

            // Finishing relay B AFTER the restart forms the lane — proof the whole chain re-derives.
            var two = s2.AddLocalPlayer("Two");
            TravelTo(s2, two, LandableBodyOf(s2.Galaxy.Systems.First(s => s.Id == sysBId)).Id);
            CompleteRelayFree(s2, two, stationB, _content);
            Assert.True(s2.RelayCompletedForTest(stationB));
            Assert.True(s2.HasJumpLaneForTest(sysAId, sysBId));
        }
    }

    // ---------------- F-2: world effect, growth hook, epilogue insights ----------------

    private SvGameServer NewServer(string name, long seed, bool galaxyGrowth, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.World.GalaxyGrowth = galaxyGrowth;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int TrafficRank(string level) => level switch { "None" => 0, "Rare" => 1, _ => 2 };

    [Fact]
    public void CompletedRelay_LiftsAmbientTraderTraffic_OneLevel()
    {
        var server = NewServer("relay_traffic", out var repo);
        using (repo)
        {
            var (sysA, _) = NearestSystemPair(server);
            string before = server.TrafficLevelForTest(sysA.Id);

            var one = server.AddLocalPlayer("One");
            TravelTo(server, one, LandableBodyOf(sysA).Id);
            string station = BuildCommissionedStation(server, one);
            CompleteRelayFree(server, one, station, _content);

            string after = server.TrafficLevelForTest(sysA.Id);
            Assert.NotEqual("None", after); // a relay system always sees at least the odd freighter
            Assert.Equal(before == "Often" ? 2 : TrafficRank(before) + 1, TrafficRank(after));
        }
    }

    [Fact]
    public void LaneIntoAnEdgeSystem_GrowsTheGalaxy_AndSpeaksTheGrowthInsight()
    {
        // Find a seed whose galaxy has an EDGE system with a neighbour inside the lane link range — the
        // scan is deterministic, so whichever seed qualifies first always qualifies.
        float range = _content.Relay!.LinkRange;
        for (long seed = 1; seed <= 10; seed++)
        {
            var server = NewServer("relay_grow_" + seed, seed, galaxyGrowth: true, out var repo);
            using (repo)
            {
                // Join BOTH pilots before scanning for an edge pair: joining marks the start system known,
                // which can itself grow the galaxy (the start body's system needn't be "sys0"-home) — and
                // every growth moves the rim. Scanning afterwards sees the settled edge.
                var one = server.AddLocalPlayer("One");
                var two = server.AddLocalPlayer("Two");
                var systems = server.Galaxy.Systems.Where(s => s.Id.StartsWith("sys", StringComparison.Ordinal)).ToList();
                StarSystem? edge = null, partner = null;
                foreach (var e in systems.Where(s => server.IsEdgeSystemForTest(s.Id)))
                {
                    foreach (var p in systems.Where(s => s.Id != e.Id))
                    {
                        float dx = e.MapX - p.MapX, dy = e.MapY - p.MapY;
                        if (dx * dx + dy * dy <= range * range)
                        {
                            edge = e;
                            partner = p;
                            break;
                        }
                    }

                    if (edge != null) break;
                }

                if (edge == null)
                {
                    continue; // this seed's rim is lonely — try the next one
                }

                // Pre-mark both destination systems known: the travel funnels then skip their own growth
                // trigger ("newly known"), so any growth below is attributable to the LANE hook alone.
                one.State.KnownSystems.Add(partner!.Id);
                TravelTo(server, one, LandableBodyOf(partner).Id);
                CompleteRelayFree(server, one, BuildCommissionedStation(server, one), _content);

                two.State.KnownSystems.Add(edge.Id);
                TravelTo(server, two, LandableBodyOf(edge).Id);
                string stationB = BuildCommissionedStation(server, two);

                int before = server.Galaxy.Systems.Count;
                CompleteRelayFree(server, two, stationB, _content);

                Assert.True(server.HasJumpLaneForTest(edge.Id, partner.Id));
                Assert.True(server.Galaxy.Systems.Count > before, "a lane into the rim must grow the galaxy");
                Assert.True(server.Metadata.GalaxyGrownSystems >= 1);
                Assert.Contains("growth", server.Metadata.RelayInsights);
                return;
            }
        }

        Assert.Fail("no seed in 1..10 offers an edge system within lane range — loosen the scan");
    }

    [Fact]
    public void RelayInsights_SpeakOncePerSave_AndTheirKeysResolve()
    {
        var server = NewServer("relay_insights", out var repo);
        using (repo)
        {
            var (sysA, sysB) = NearestSystemPair(server);

            var one = server.AddLocalPlayer("One");
            TravelTo(server, one, LandableBodyOf(sysA).Id);
            CompleteRelayFree(server, one, BuildCommissionedStation(server, one), _content);
            Assert.Equal(new[] { "relay" }, server.Metadata.RelayInsights);

            // The second relay forms the first lane: ONE lane insight, and the lane achievement lands on
            // the completing contributor. The relay insight is not spoken a second time.
            var two = server.AddLocalPlayer("Two");
            TravelTo(server, two, LandableBodyOf(sysB).Id);
            CompleteRelayFree(server, two, BuildCommissionedStation(server, two), _content);
            Assert.Equal(1, server.Metadata.RelayInsights.Count(s => s == "relay"));
            Assert.Equal(1, server.Metadata.RelayInsights.Count(s => s == "lane"));
            Assert.True(two.State.AchievementCounters.TryGetValue("lane:linked", out int n) && n == 1);

            // The insight lines actually localize — EN and DE both carry the keys.
            foreach (var locale in new[] { GameLocale.English, GameLocale.German })
            {
                var loc = _content.CreateLocalizer(locale);
                foreach (var key in new[] { "vega.relay.first", "vega.relay.lane", "vega.relay.growth" })
                {
                    Assert.False(loc.Get(key).StartsWith("[", StringComparison.Ordinal), key + " must resolve in " + locale);
                }
            }
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
