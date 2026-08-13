// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Im Einzelspieler sollte das Spiel pausiert werden, wenn man in das Menü geht." The Esc dialog was already
/// titled "Pause" with a "Resume" button while the world kept simulating behind it (#612/#908). The hold is
/// server-side (singleplayer runs the bundled server in its own process).
/// <para>
/// #973 turns it into a group decision: the world holds once EVERY joined player is in their pause menu, and
/// runs again the moment one of them resumes. Before that a second joined player had the request refused
/// outright, so two friends taking a break both watched hunger drain behind their menus.
/// </para>
/// </summary>
public sealed class WorldPauseTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorldPauseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pause_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every server send so a test can assert the tally the pause dialogs are shown.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void DisconnectClient(int connectionId) { }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pause"));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "pause",
            Seed = 3,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void ALonePlayer_CanHoldAndReleaseTheWorld()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");

            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.PauseForTest(p, false);
            Assert.False(server.IsPaused);
        }
    }

    [Fact]
    public void WhileHeld_TheClockDoesNotAdvance()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            server.TickForTest(0.1);
            long before = server.Metadata.CumulativePlaytimeSeconds;

            server.PauseForTest(p, true);
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.5); // 20 seconds of wall clock
            }

            // Playtime is accumulated by a simulation system that must not run while the world is held.
            Assert.Equal(before, server.Metadata.CumulativePlaytimeSeconds);
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void AfterResuming_TheWorldSimulatesAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            server.PauseForTest(p, true);
            server.TickForTest(1.0);
            server.PauseForTest(p, false);

            long before = server.Metadata.CumulativePlaytimeSeconds;
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(server.Metadata.CumulativePlaytimeSeconds > before);
        }
    }

    [Fact]
    public void OnePlayersMenu_DoesNotFreezeTheOther_ButBothTogetherDo()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");

            // One menu is just an intent — the other player is still playing.
            server.PauseForTest(a, true);
            server.TickForTest(0.1);
            Assert.False(server.IsPaused);

            // The moment everybody agrees, the world stops for all of them (#973).
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void WhenOnePlayerResumes_TheWorldRunsAgainForEverybody()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");
            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);

            server.PauseForTest(b, false);
            Assert.False(server.IsPaused); // one player leaving the menu is enough
        }
    }

    [Fact]
    public void AWatchingAdmin_NeitherBlocksAHoldNorCountsTowardsIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var admin = server.AddLocalPlayer("Marcel");
            admin.Spectating = true; // observer mode (#487): invisible, no footprint, ignored by creatures

            // Nobody's game is interrupted by holding the world for its only actual player (#908) — and an
            // observer never opens a pause menu, so counting them would now block the hold forever.
            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.TickForTest(0.1);
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void WhileHeld_GameplayIntentsAreDropped_ButTheResumePathStaysLive()
    {
        // #995: the hold froze the SIMULATION but the dispatcher kept serving gameplay intents — a stock
        // client sends nothing from its pause menu, but a modified one could mine/build/move for the whole
        // hold with every threat (and everyone else's clock) suspended.
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var start = p.State.Position;
            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode( // clear the join adopt gate (#865)
                new MoveIntent { X = start.X, Y = start.Y, Z = start.Z }));

            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(
                new MoveIntent { X = start.X + 1f, Y = start.Y, Z = start.Z }));
            Assert.Equal(start.X, p.State.Position.X); // the frozen world ignored the movement

            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new PauseIntent { Paused = false }));
            Assert.False(server.IsPaused); // the resume path must stay live through the gate

            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(
                new MoveIntent { X = start.X + 1f, Y = start.Y, Z = start.Z }));
            Assert.Equal(start.X + 1f, p.State.Position.X); // …and movement is trusted again
        }
    }

    [Fact]
    public void WhileHeld_NoChunksStreamToThePausedPlayers()
    {
        // The paused tick skips the simulation loop entirely — holders sit in their menus and have no use
        // for chunks. (The observer case below is the exception, #996.)
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            server.PauseForTest(p, true);

            transport.Sent.Clear();
            server.TickForTest(0.1);
            Assert.True(server.IsPaused);
            Assert.DoesNotContain(transport.Sent, m => m is ChunkDataMessage);
        }
    }

    [Fact]
    public void WhileHeld_AnObserverStillReceivesChunks()
    {
        // #996: a spectator neither holds nor counts toward the pause and keeps flying — but chunk
        // streaming lived below the paused early-return, so an admin moving through a held world ran off
        // the already-streamed radius into void for up to the whole hold.
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var admin = server.AddLocalPlayer("Marcel");
            admin.Spectating = true; // observer mode (#487)

            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            transport.Sent.Clear();
            server.TickForTest(0.1);
            Assert.True(server.IsPaused);
            Assert.Contains(transport.Sent, m => m is ChunkDataMessage); // streamed to the observer
        }
    }

    [Fact]
    public void APauseIsLifted_WhenSomeoneElseJoins()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            server.PauseForTest(a, true);
            Assert.True(server.IsPaused);

            server.AddLocalPlayer("Severin");
            server.TickForTest(0.1);

            // The newcomer is not in a pause menu, so the world is theirs to play.
            Assert.False(server.IsPaused);
        }
    }

    [Fact]
    public void AHoldSurvives_WhenOneOfSeveralHoldersLeaves()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");
            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);

            server.DisconnectLocalPlayerForTest("Severin");
            server.TickForTest(0.1);

            // Everyone still in the world is still in their menu — the hold has no reason to end.
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void APauseIsLifted_WhenTheLastHolderLeaves()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            server.PauseForTest(a, true);
            Assert.True(server.IsPaused);

            server.DisconnectLocalPlayerForTest("Justus");
            server.TickForTest(0.1);

            // Nobody left to hold it for — a dedicated world must not sit frozen because someone quit
            // with the menu open, and an empty world would never save or idle out either.
            Assert.False(server.IsPaused);
        }
    }

    [Fact]
    public void AClientThatDiesWhilePaused_ReleasesItsNameOnTheUsualBudget()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");
            MarkAsWireClients(a, b);

            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);

            // Severin's game dies behind the dialog and stops sending keep-alives. Justus keeps sending his.
            for (int i = 0; i < 40; i++)
            {
                server.PauseForTest(a, true); // the client's repeat, every few seconds
                server.TickForTest(5.0);      // 200 s of held world
            }

            // The normal heartbeat sweep is blind here — it ages sessions against a clock a held world does
            // not advance — so without this pass the crashed player's name and slot would stay taken for the
            // whole hold: the same rejoin lockout #964 removed for a running world.
            Assert.Equal(1, server.JoinedPlayerCountForTest);

            // Justus is alone now and still in his menu, which is simply the singleplayer hold.
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void WhenEveryPausedClientDies_TheWorldDoesNotStayFrozen()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");
            MarkAsWireClients(a, b);

            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);

            // The whole machine goes to sleep behind the dialogs: nobody is left to press Resume. A held world
            // saves nothing and simulates nothing, so it must not sit there until the ceiling runs out.
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(5.0);
            }

            Assert.False(server.IsPaused);
            Assert.Equal(0, server.JoinedPlayerCountForTest);
        }
    }

    [Fact]
    public void AnExpiredHold_IsNotRestartedByTheNextKeepAlive()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");
            MarkAsWireClients(a, b);

            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);

            // Both menus stay open for longer than a group hold may last (10 min), both clients healthy.
            for (int i = 0; i < 180; i++)
            {
                server.PauseForTest(a, true); // the keep-alives keep coming — the menus are still up
                server.PauseForTest(b, true);
                server.TickForTest(5.0);
            }

            // The ceiling would be meaningless if the very next keep-alive put the world back to sleep.
            Assert.False(server.IsPaused);
            Assert.Equal(2, server.JoinedPlayerCountForTest); // and nobody was dropped: both are alive

            // Closing and reopening the menu asks again, and is honoured.
            server.PauseForTest(a, false);
            server.PauseForTest(b, false);
            server.PauseForTest(a, true);
            server.PauseForTest(b, true);
            Assert.True(server.IsPaused);
        }
    }

    /// <summary>Makes local test sessions look like clients that joined over the wire and speak the pause
    /// keep-alive — the only sessions the paused-silence sweep is allowed to touch.</summary>
    private static void MarkAsWireClients(params BlocksBeyondTheStars.GameServer.PlayerSession[] sessions)
    {
        foreach (var s in sessions)
        {
            s.HeartbeatTracked = true;
            s.SendsPauseKeepAlive = true;
        }
    }

    [Fact]
    public void ThePauseStateTally_TellsEveryClientWhoIsStillMissing()
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            var b = server.AddLocalPlayer("Severin");

            transport.Sent.Clear();
            server.PauseForTest(a, true);

            var waiting = transport.Sent.OfType<PauseState>().Last();
            Assert.False(waiting.Paused);
            Assert.Equal(1, waiting.HoldingPlayers);
            Assert.Equal(2, waiting.JoinedPlayers);
            Assert.Equal("Severin", waiting.WaitingFor); // what the pause dialog names

            transport.Sent.Clear();
            server.PauseForTest(b, true);

            var held = transport.Sent.OfType<PauseState>().Last();
            Assert.True(held.Paused);
            Assert.Equal(2, held.HoldingPlayers);
            Assert.Equal(string.Empty, held.WaitingFor);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
