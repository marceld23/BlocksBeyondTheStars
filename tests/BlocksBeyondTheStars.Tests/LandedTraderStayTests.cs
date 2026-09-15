// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1904: trader ships were hard to use — a landed trader left after 3–6 minutes counted from touchdown, even with
/// a player walking over or mid-trade, no map showed it, and its landing and lift-off never re-published the pad
/// list. Now it stays 10–15 minutes (docked: 7–12), waits while anyone is near, is a live planet-map marker, and
/// both ends of its stay re-send pads and POIs to the body's players.
/// </summary>
public sealed class LandedTraderStayTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public LandedTraderStayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_traderstay_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Transport recording every server send, so a test can assert what each player received.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, IServerTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off; // traders are independent of combat
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static T? LastTo<T>(RecordingTransport transport, BlocksBeyondTheStars.GameServer.PlayerSession player)
        where T : class
        => transport.Sent.Where(x => x.Conn == player.ConnectionId).Select(x => x.Msg).OfType<T>().LastOrDefault();

    /// <summary>Far outside the stay radius, up in the open air so no rescue moves the player back: a test
    /// player without a ship spawns on the very pad the trader takes, so "their pad" is no way away.</summary>
    private static Vector3f FarFrom(Vector3f pilotPos) => new(pilotPos.X + 300f, pilotPos.Y + 80f, pilotPos.Z);

    [Fact]
    public void ALandedTrader_StaysTenToFifteenMinutes()
    {
        var server = NewServer("landdwell");
        var pilot = server.AddLocalPlayer("Pilot");
        string body = pilot.CurrentLocationId;
        server.EnterSpace("Pilot"); // the in-flight landing decision runs in the pilot's space instance

        for (int i = 0; i < 16; i++)
        {
            Assert.True(server.LandTraderFromFlightForTest("Pilot", body));
            double left = server.LandedTraderSecondsLeftForTest(body) ?? double.NaN;
            Assert.InRange(left, 600.0, 900.0);
            Assert.True(server.ReleaseLandedTraderRecordForTest(body)); // make room for the next roll
        }
    }

    [Fact]
    public void ADockedTrader_StaysSevenToTwelveMinutes()
    {
        var server = NewServer("dockdwell");
        string station = server.Galaxy.Systems.SelectMany(s => s.Bodies)
            .FirstOrDefault(b => b.Kind == CelestialKind.SpaceStation)?.Id ?? "station-under-test";

        for (int i = 0; i < 16; i++)
        {
            server.DockTraderForTest(station);
            double left = server.VisitingTraderSecondsLeftForTest(station) ?? double.NaN;
            Assert.InRange(left, 420.0, 720.0);
        }
    }

    [Fact]
    public void AnExpiredTrader_WaitsWhileAPlayerIsNear_AndLeavesAfterTheGracePeriod()
    {
        var server = NewServer("traderhold");
        var pilot = server.AddLocalPlayer("Pilot");
        string body = pilot.CurrentLocationId;

        Assert.True(server.LandTraderForTest(body));
        server.Tick(0.1); // the body's own tick sets it down
        var at = server.LandedTraderPilotPosForTest(body);
        Assert.NotNull(at);

        // The player walks over and browses the wares — and the trader's time runs out meanwhile.
        pilot.State.Position = new Vector3f(at!.Value.X, at.Value.Y, at.Value.Z + 1f);
        Assert.True(server.ExpireLandedTraderForTest(body));
        server.Tick(0.1);
        server.AdvanceUptimeForTest(600.0);
        server.Tick(0.1);
        Assert.Equal(1, server.LandedTraderCountForTest()); // never lifts off from under a customer
        Assert.Contains(server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));

        // The player heads off: the trader waits out the grace period first…
        pilot.State.Position = FarFrom(at.Value);
        server.Tick(0.1);
        server.AdvanceUptimeForTest(SvGameServer.TraderLeaveGraceSeconds - 5.0);
        server.Tick(0.1);
        Assert.Equal(1, server.LandedTraderCountForTest());

        // …then lifts off, taking its hull with it (#1680).
        server.AdvanceUptimeForTest(10.0);
        server.Tick(0.1);
        Assert.Equal(0, server.LandedTraderCountForTest());
        Assert.DoesNotContain(server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
    }

    [Fact]
    public void APlayerBeyondTheStayRadius_DoesNotHoldIt()
    {
        var server = NewServer("traderbeyond");
        var pilot = server.AddLocalPlayer("Pilot");
        string body = pilot.CurrentLocationId;

        Assert.True(server.LandTraderForTest(body));
        server.Tick(0.1);
        var at = server.LandedTraderPilotPosForTest(body)!.Value;

        // Hovering well above the reach radius (clear of the hull's own reach, too): no hold, it leaves at once.
        pilot.State.Position = new Vector3f(at.X, at.Y + SvGameServer.TraderStayNearRadius + 16f, at.Z);
        Assert.True(server.ExpireLandedTraderForTest(body));
        server.Tick(0.1);
        Assert.Equal(0, server.LandedTraderCountForTest());
    }

    [Fact]
    public void APlayerComingBackWithinTheGracePeriod_HoldsTheTraderAgain()
    {
        var server = NewServer("traderreturn");
        var pilot = server.AddLocalPlayer("Pilot");
        string body = pilot.CurrentLocationId;

        Assert.True(server.LandTraderForTest(body));
        server.Tick(0.1);
        var at = server.LandedTraderPilotPosForTest(body)!.Value;

        // Near while the time runs out, away for most of the grace period, then back before it ends.
        pilot.State.Position = new Vector3f(at.X, at.Y, at.Z + 1f);
        Assert.True(server.ExpireLandedTraderForTest(body));
        server.Tick(0.1);
        pilot.State.Position = FarFrom(at);
        server.AdvanceUptimeForTest(SvGameServer.TraderLeaveGraceSeconds - 5.0);
        server.Tick(0.1);
        pilot.State.Position = new Vector3f(at.X, at.Y, at.Z + 1f);
        server.Tick(0.1);

        // The grace period restarts from the last moment someone was near.
        server.AdvanceUptimeForTest(SvGameServer.TraderLeaveGraceSeconds - 5.0);
        server.Tick(0.1);
        Assert.Equal(1, server.LandedTraderCountForTest());
    }

    [Fact]
    public void ALandedTrader_IsOnThePlanetMap_WhileParked_AndPadsAndPoisAreRepublished()
    {
        var transport = new RecordingTransport();
        var server = NewServer("tradermap", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        var gast = server.AddLocalPlayer("Gast", "de");
        string body = pilot.CurrentLocationId;
        Assert.DoesNotContain(server.PlanetPoisForTest("Pilot"), p => p.Type == SvGameServer.TraderShipPoiType);

        // Registered but not yet set down: no pilot position, so no marker yet.
        Assert.True(server.LandTraderForTest(body));
        Assert.DoesNotContain(server.PlanetPoisForTest("Pilot"), p => p.Type == SvGameServer.TraderShipPoiType);

        transport.Sent.Clear();
        server.Tick(0.1); // sets down

        var at = server.LandedTraderPilotPosForTest(body)!.Value;
        var marker = Assert.Single(server.PlanetPoisForTest("Pilot"), p => p.Type == SvGameServer.TraderShipPoiType);
        Assert.Equal("Trader ship Test Trader", marker.Name);
        Assert.Equal(at.X, marker.X);
        Assert.Equal(at.Z, marker.Z);
        Assert.Contains(server.PlanetPoisForTest("Gast"),
            p => p.Type == SvGameServer.TraderShipPoiType && p.Name == "Händlerschiff Test Trader");

        // Everyone already on the body is told: the marker appears and the trader's pad shows as taken.
        foreach (var player in new[] { pilot, gast })
        {
            var pois = LastTo<PlanetPoiList>(transport, player);
            Assert.NotNull(pois);
            Assert.Contains(pois!.Pois, p => p.Type == SvGameServer.TraderShipPoiType);

            var pads = LastTo<LandingPadList>(transport, player);
            Assert.NotNull(pads);
            Assert.Contains(pads!.Pads, p => p.Occupied && p.Occupant == "Test Trader");
        }

        // Lift-off (nobody near): the marker is gone and the pad is free again — on the server and on both maps.
        pilot.State.Position = FarFrom(at);
        gast.State.Position = FarFrom(at);
        Assert.True(server.ExpireLandedTraderForTest(body));
        transport.Sent.Clear();
        server.Tick(0.1);
        Assert.Equal(0, server.LandedTraderCountForTest());
        Assert.DoesNotContain(server.PlanetPoisForTest("Pilot"), p => p.Type == SvGameServer.TraderShipPoiType);
        foreach (var player in new[] { pilot, gast })
        {
            var pois = LastTo<PlanetPoiList>(transport, player);
            Assert.NotNull(pois);
            Assert.DoesNotContain(pois!.Pois, p => p.Type == SvGameServer.TraderShipPoiType);

            var pads = LastTo<LandingPadList>(transport, player);
            Assert.NotNull(pads);
            Assert.DoesNotContain(pads!.Pads, p => p.Occupant == "Test Trader");
        }
    }

    [Fact]
    public void APlayerWhoLeavesTheBodyBesideTheTrader_DoesNotHoldItThere()
    {
        var server = NewServer("tradersweep");
        var pilot = server.AddLocalPlayer("Pilot");
        string body = pilot.CurrentLocationId;

        Assert.True(server.LandTraderForTest(body));
        server.Tick(0.1);
        var at = server.LandedTraderPilotPosForTest(body)!.Value;
        pilot.State.Position = new Vector3f(at.X, at.Y, at.Z + 1f);
        server.Tick(0.1); // seen near

        // The player logs off right beside it: nobody is on the body any more, so the sweep of unwatched bodies
        // frees the pad as soon as the time is up — a last known position never pins a trader.
        server.DisconnectLocalPlayerForTest(pilot.State.PlayerId);
        Assert.Equal(1, server.LandedTraderCountForTest());
        Assert.True(server.ExpireLandedTraderForTest(body));
        server.SweepExpiredLandedTradersForTest();
        Assert.Equal(0, server.LandedTraderCountForTest());
    }

    [Fact]
    public void TheTraderMarkerLabel_ExistsInEveryLanguage()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            var table = TestLocales.Load(Path.GetFileNameWithoutExtension(file));
            Assert.True(table.TryGetValue("poi.trader_ship", out var label), $"{Path.GetFileName(file)} lacks poi.trader_ship");
            Assert.Contains("{0}", label, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
