// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Peaceful NPC trader traffic — ambient ships that warp in, fly, dock and depart. They are
/// invulnerable, non-targetable scenery rendered through the remote-ship path.</summary>
public sealed class SpaceTraderTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceTraderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_trader_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, bool ship = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = ship };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off; // traders are independent of combat
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TrafficLevel_IsDeterministicPerSystem()
    {
        var server = NewServer("traffic", out var repo);
        using (repo)
        {
            var sys = server.Galaxy.Systems.First().Id;
            var a = server.TrafficLevelForTest(sys);
            var b = server.TrafficLevelForTest(sys);
            Assert.Equal(a, b); // stable from seed + system id, so no persistence is needed
            Assert.Contains(a, new[] { "None", "Rare", "Often" });
        }
    }

    [Fact]
    public void EmptySystemId_HasNoTraffic()
    {
        var server = NewServer("notraffic", out var repo);
        using (repo)
        {
            Assert.Equal("None", server.TrafficLevelForTest(string.Empty));
        }
    }

    [Fact]
    public void FreshInstance_IsNotInstantlyBusy()
    {
        var server = NewServer("schedule", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));
            Assert.Equal(0, server.TraderCountForTest("Pilot")); // first arrival is scheduled out, not on entry
        }
    }

    [Fact]
    public void SpawnTrader_AddsAFlyingTrader_ThatIsNeverATargetableEntity()
    {
        var server = NewServer("spawn", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            int entitiesBefore = server.SpaceEntitiesFor("Pilot").Count();
            Assert.True(server.SpawnTraderForTest("Pilot"));
            Assert.True(server.TraderCountForTest("Pilot") >= 1);

            // Peaceful + invulnerable by design: a trader is NEVER a combat entity (can't be locked or shot)
            // and never hostile — it only ever rides the remote-ship pose path.
            var entities = server.SpaceEntitiesFor("Pilot");
            Assert.Equal(entitiesBefore, entities.Count());
            Assert.DoesNotContain(entities, e => e.Hostile);
        }
    }

    [Fact]
    public void LandingTrader_ReservesAFreePad_AndOnlyOnePerBody()
    {
        var server = NewServer("land", out var repo);
        using (repo)
        {
            var body = server.Galaxy.Systems
                .SelectMany(s => s.Bodies)
                .First(b => b.Kind == CelestialKind.Planet || b.Kind == CelestialKind.Moon);

            int freeBefore = server.FreePadsForTest(body.Id);
            Assert.True(freeBefore > 0); // the body has pads to land on

            // It lands only when a pad is free, and then that pad is reserved (one fewer free) so a player
            // can never be assigned it — exactly the requirement.
            Assert.True(server.LandTraderForTest(body.Id));
            Assert.Equal(1, server.LandedTraderCountForTest());
            Assert.Equal(freeBefore - 1, server.FreePadsForTest(body.Id));

            // Only one visiting trader may be parked on a body at a time.
            Assert.False(server.LandTraderForTest(body.Id));
        }
    }

    /// <summary>
    /// #1678: a trader never stamps its hull onto ground another hull already holds. A player report had a
    /// trader ship and the player's own ship cell-for-cell inside each other on landing pad 0, with the player
    /// walled in and no way out. The pad reservation is derived state and can disagree with what is standing
    /// there; the stamp itself now has the last word, and the trader gives the pad back instead.
    /// </summary>
    [Fact]
    public void ATrader_DoesNotSetDown_WhereAnotherHullAlreadyStands()
    {
        var server = NewServer("traderoverlap", out var repo, ship: true);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot"); // parks the player's own ship on their pad
            string body = pilot.CurrentLocationId;
            Assert.Contains("Pilot", server.PlacedHullOwnersForTest());

            // Force the trader onto the pad the player's hull is standing on — the desync the report showed.
            Assert.True(server.LandTraderForTest(body));
            server.ForceTraderPadForTest(body, server.AssignedPadForTest("Pilot"));

            Assert.False(server.MaterializeLandedTraderForTest());          // no second hull on that ground…
            Assert.Equal(0, server.LandedTraderCountForTest());             // …and the pad is released again
            Assert.Equal(new[] { "Pilot" }, server.PlacedHullOwnersForTest());
        }
    }

    /// <summary>
    /// #1680: a departing trader takes its parked hull off the pad it is freeing. The cleanup reached for the
    /// ACTIVE world's id rather than the trader's own body, which is the same thing only while the caller
    /// happens to be that body's tick — every other path left a hull standing on a pad reported as free.
    /// </summary>
    [Fact]
    public void AnExpiredTrader_TakesItsParkedHullWithIt()
    {
        var server = NewServer("traderexpiry", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string body = pilot.CurrentLocationId;

            Assert.True(server.LandTraderForTest(body));
            Assert.True(server.MaterializeLandedTraderForTest());
            Assert.Contains(server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));

            Assert.True(server.ExpireLandedTraderForTest(body));
            server.Tick(0.1); // the body's own tick lifts it off

            Assert.Equal(0, server.LandedTraderCountForTest());
            Assert.DoesNotContain(server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// #1680: a trader registered but never materialised has no pilot, and its id field still holds the
    /// default 0 — the departure removed "the NPC with id 0", taking an unrelated one out of the world.
    /// </summary>
    [Fact]
    public void ATraderThatNeverSetDown_TakesNoOtherNpcWithIt()
    {
        var server = NewServer("traderpilot", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string body = pilot.CurrentLocationId;
            int npcsBefore = server.NpcCount;

            Assert.True(server.LandTraderForTest(body)); // registered, never materialised → PilotNpcId stays 0
            Assert.True(server.ExpireLandedTraderForTest(body));
            server.Tick(0.1);

            Assert.Equal(0, server.LandedTraderCountForTest());
            Assert.Equal(npcsBefore, server.NpcCount);
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
