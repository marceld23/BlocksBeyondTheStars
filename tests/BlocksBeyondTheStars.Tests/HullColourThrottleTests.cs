// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3196: a hull repaint re-announces the whole parked ship (every structure cell) to everyone on the world, and each
/// client rebuilds that ship's mesh for it. It now runs through a bucket of its own: a few repaints show at once, a flood
/// collapses into one rebroadcast per refill — and the LAST colour still reaches everyone.
/// </summary>
public sealed class HullColourThrottleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_hullrelay_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Alice, PlayerSession Bob) TwoPlayersWithParkedShips(string name)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true, // the ship stands on the world — a repaint re-announces it
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        transport.Sent.Clear();
        return (server, transport, alice, bob);
    }

    private static List<LandedShipState> ShipsOf(RecordingTransport t, PlayerSession viewer, PlayerSession owner)
        => t.Sent.Where(x => x.Conn == viewer.ConnectionId).Select(x => x.Msg).OfType<LandedShipState>()
            .Where(m => m.PlayerId == owner.State.PlayerId && !m.Removed).ToList();

    private static void Repaint(SvGameServer server, PlayerSession who, int hull)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new SetAppearanceIntent { Hull = hull }));

    [Fact]
    public void ASingleRepaint_ReachesTheOtherPlayerAtOnce()
    {
        var (server, t, alice, bob) = TwoPlayersWithParkedShips("hull_single");

        Repaint(server, alice, 0x123456);

        var seen = Assert.Single(ShipsOf(t, bob, alice));
        Assert.Equal(0x123456, seen.Hull);
        Assert.NotEmpty(seen.X); // the full parked ship, as before — only how often it goes out changed
    }

    [Fact]
    public void FiftyRepaintsInOneTick_ReachTheOtherPlayerAtMostBurstTimes_AndTheLastColourStillArrives()
    {
        var (server, t, alice, bob) = TwoPlayersWithParkedShips("hull_flood");

        const int last = 0x0000FF + 50;
        for (int i = 1; i <= 50; i++)
        {
            Repaint(server, alice, 0x0000FF + i); // a new colour every time — each one a real change
        }

        int burst = (int)SvGameServer.HullRelayBurst;
        Assert.Equal(burst, ShipsOf(t, bob, alice).Count);
        Assert.Equal(burst, ShipsOf(t, alice, alice).Count); // the owner's own view is bounded the same way

        server.Tick(SvGameServer.HullRelayRefillSeconds + 0.1);

        var afterRefill = ShipsOf(t, bob, alice);
        Assert.Equal(burst + 1, afterRefill.Count); // the coalesced rest: ONE rebroadcast, not 47
        Assert.Equal(last, afterRefill[^1].Hull);   // …carrying the colour the flood ended on

        for (int i = 0; i < 30; i++)
        {
            server.Tick(0.1); // nothing is pending any more — no further rebroadcast
        }

        Assert.Equal(burst + 1, ShipsOf(t, bob, alice).Count);
    }

    /// <summary>A sustained flood is held to one rebroadcast per refill period, and the colour on screen is always the
    /// latest one the server knows.</summary>
    [Fact]
    public void ASustainedFlood_IsHeldToOneRebroadcastPerRefill()
    {
        var (server, t, alice, bob) = TwoPlayersWithParkedShips("hull_sustained");

        int hull = 0x010101;
        const int seconds = 5;
        for (int tick = 0; tick < seconds * 10; tick++)
        {
            for (int i = 0; i < 20; i++)
            {
                Repaint(server, alice, ++hull); // 200 changes per simulated second (the message bucket lets through what it lets through)
            }

            server.Tick(0.1);
        }

        var seen = ShipsOf(t, bob, alice);
        int ceiling = (int)SvGameServer.HullRelayBurst + (int)(seconds / SvGameServer.HullRelayRefillSeconds) + 1;
        Assert.InRange(seen.Count, 1, ceiling);

        server.Tick(SvGameServer.HullRelayRefillSeconds + 0.1);
        Assert.Equal(alice.HullColor, ShipsOf(t, bob, alice)[^1].Hull);
    }

    /// <summary>The other appearance edits (#2672) keep their own bucket: hull repaints do not use up the face's tokens.</summary>
    [Fact]
    public void HullRepaints_DoNotSpendTheFaceBucket()
    {
        var (server, t, alice, bob) = TwoPlayersWithParkedShips("hull_face");
        for (int i = 1; i <= 10; i++)
        {
            Repaint(server, alice, 0x220000 + i);
        }

        string face = new string('a', 32 * 32);
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new SetFaceIntent { Pixels = face }));

        Assert.Contains(t.Sent.Where(x => x.Conn == bob.ConnectionId).Select(x => x.Msg).OfType<PlayerFace>(),
            f => f.PlayerId == alice.State.PlayerId && f.Pixels == face);
    }
}
