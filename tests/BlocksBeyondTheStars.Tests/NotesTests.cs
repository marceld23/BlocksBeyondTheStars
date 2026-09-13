// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Player notes (#1844): the per-player cap, title/body clamping, newline survival, control-char
/// stripping, the content screen (title refused, body masked), persistence across a reload, and the list
/// being PUSHED on join and after every set/remove.</summary>
public sealed class NotesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NotesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_notes_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every decoded message the server sends, per connection (copied from MarkerTests).</summary>
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

    private static ServerConfig Config(string tag) => new() { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999 };

    /// <summary>Alice's session on the server <see cref="NewServer"/> last built (the push assertions need her connection id).</summary>
    private PlayerSession _alice = null!;

    private SvGameServer NewServer(out SqliteWorldRepository repo, string tag = "n", IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(Config(tag), _content, st, repo);
        server.Start();
        _alice = server.AddLocalPlayer("Alice");
        return server;
    }

    private static List<NoteList> ListsPushedTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<NoteList>().ToList();

    private static int RejectsTo(RecordingTransport t, PlayerSession who, string reason)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Count(r => r.Reason == reason);

    [Fact]
    public void TwentyNotes_TheTwentyFirstIsRefused()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "cap", transport);
        using (repo)
        {
            var alice = _alice;
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(i + 1, server.SetNoteForTest("Alice", "note " + i, "body"));
            }

            Assert.Equal(20, server.SetNoteForTest("Alice", "one too many", "body"));
            Assert.Equal(1, RejectsTo(transport, alice, "@srv.note.full"));
        }
    }

    [Fact]
    public void UpdateById_EditsInPlace_InsteadOfCountingAgainstTheCap()
    {
        var server = NewServer(out var repo, "update");
        using (repo)
        {
            server.SetNoteForTest("Alice", "plan", "dig down");
            var mine = server.NotesForTest("Alice").Single();

            int count = server.SetNoteForTest("Alice", "plan v2", "dig down\nthen up", id: mine.Id);

            Assert.Equal(1, count);
            var updated = server.NotesForTest("Alice").Single();
            Assert.Equal(mine.Id, updated.Id);
            Assert.Equal("plan v2", updated.Title);
            Assert.Equal("dig down\nthen up", updated.Body);
        }
    }

    [Fact]
    public void TitleAndBody_AreClamped()
    {
        var server = NewServer(out var repo, "clamp");
        using (repo)
        {
            server.SetNoteForTest("Alice", new string('t', 80), new string('b', 3000));
            var n = server.NotesForTest("Alice").Single();
            Assert.Equal(40, n.Title.Length);
            Assert.Equal(2000, n.Body.Length);
        }
    }

    [Fact]
    public void Body_KeepsNewlines_ButLosesOtherControlChars()
    {
        var server = NewServer(out var repo, "ctrl");
        using (repo)
        {
            server.SetNoteForTest("Alice", "line\none", "first\r\nsecond\tthirdfourth\rfifth");
            var n = server.NotesForTest("Alice").Single();
            Assert.Equal("line one", n.Title); // a title is one line
            Assert.Equal("first\nsecond third fourth\nfifth", n.Body);
        }
    }

    [Fact]
    public void ScreenedTitle_IsRefused_AndNothingIsStored()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "title_hate", transport);
        using (repo)
        {
            var alice = _alice;
            Assert.Equal(0, server.SetNoteForTest("Alice", "h.i.t.l.e.r fan page", "harmless body"));
            Assert.Empty(server.NotesForTest("Alice"));
            Assert.Equal(1, RejectsTo(transport, alice, "@srv.note.blocked"));
        }
    }

    [Fact]
    public void ProfaneBody_IsMasked_LikeChat_NotRefused()
    {
        var server = NewServer(out var repo, "body_mask");
        using (repo)
        {
            Assert.Equal(1, server.SetNoteForTest("Alice", "rant", "you are an asshole\nbut a nice one"));
            var n = server.NotesForTest("Alice").Single();
            Assert.Equal("you are an *******\nbut a nice one", n.Body);
        }
    }

    [Fact]
    public void Remove_DropsTheNote()
    {
        var server = NewServer(out var repo, "remove");
        using (repo)
        {
            server.SetNoteForTest("Alice", "a", "1");
            server.SetNoteForTest("Alice", "b", "2");
            var first = server.NotesForTest("Alice").Single(n => n.Title == "a");

            server.RemoveNoteForTest("Alice", first.Id);

            var left = Assert.Single(server.NotesForTest("Alice"));
            Assert.Equal("b", left.Title);
        }
    }

    [Fact]
    public void Notes_SurviveAReload()
    {
        var paths = new SaveGamePaths(_root, "persist");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(Config("persist"), _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.SetNoteForTest("Alice", "still here", "§6gold§r\nline two");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var server2 = new SvGameServer(Config("persist"), _content, st2, repo2);
            server2.Start();
            var p = server2.AddLocalPlayer("Alice");

            var n = p.State.Notes.Single();
            Assert.Equal("still here", n.Title);
            Assert.Equal("§6gold§r\nline two", n.Body);
            Assert.True(n.CreatedUtc > 0);
            Assert.True(n.UpdatedUtc >= n.CreatedUtc);
            Assert.Single(server2.NotesForTest("Alice"));
        }
    }

    [Fact]
    public void List_IsPushedOnARealJoin_AndTheIntentIsDispatched()
    {
        // AddLocalPlayer skips the network join block, so this one goes through the payload seam: a real
        // JoinRequest, then a real NoteActionIntent — the dispatch case and the join push in one.
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "join", transport);
        using (repo)
        {
            const int conn = 7;
            server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest { PlayerName = "Bob", Token = "tok-bob" }));
            Assert.Contains(transport.Sent, x => x.Conn == conn && x.Msg is JoinAccepted);
            var onJoin = transport.Sent.Where(s => s.Conn == conn).Select(s => s.Msg).OfType<NoteList>().ToList();
            Assert.NotEmpty(onJoin);
            Assert.Empty(onJoin.Last().Notes);

            transport.Sent.Clear();
            server.HandlePayloadForTest(conn, NetCodec.Encode(new NoteActionIntent { Kind = "set", Title = "via the wire", Body = "§lhi§r\nthere" }));
            var pushed = transport.Sent.Where(s => s.Conn == conn).Select(s => s.Msg).OfType<NoteList>().ToList();
            var note = Assert.Single(Assert.Single(pushed).Notes);
            Assert.Equal("via the wire", note.Title);
            Assert.Equal("§lhi§r\nthere", note.Body);
            Assert.StartsWith("nt", note.Id);
        }
    }

    [Fact]
    public void List_IsPushedAfterSetAndRemove_NewestFirst()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "push", transport);
        using (repo)
        {
            var alice = _alice;
            transport.Sent.Clear();
            server.SetNoteForTest("Alice", "first", "1");
            server.SetNoteForTest("Alice", "second", "2");
            var pushed = ListsPushedTo(transport, alice);
            Assert.Equal(2, pushed.Count);
            Assert.Equal(new[] { "second", "first" }, pushed.Last().Notes.Select(n => n.Title));

            transport.Sent.Clear();
            server.RemoveNoteForTest("Alice", pushed.Last().Notes[0].Id);
            pushed = ListsPushedTo(transport, alice);
            var left = Assert.Single(Assert.Single(pushed).Notes);
            Assert.Equal("first", left.Title);

            // Removing what is not there pushes nothing (no phantom rebuild on the client).
            transport.Sent.Clear();
            server.RemoveNoteForTest("Alice", "nt-does-not-exist");
            Assert.Empty(ListsPushedTo(transport, alice));
        }
    }

    [Fact]
    public void NoteMessages_RoundTripThroughTheCodec()
    {
        var intent = new NoteActionIntent { Kind = "set", Id = "nt1", Title = "t", Body = "a\nb §4c" };
        var back = Assert.IsType<NoteActionIntent>(NetCodec.Decode(NetCodec.Encode(intent)));
        Assert.Equal("a\nb §4c", back.Body);

        var list = new NoteList { Notes = new[] { new NetNote { Id = "nt1", Title = "t", Body = "b", CreatedUtc = 5, UpdatedUtc = 6 } } };
        var listBack = Assert.IsType<NoteList>(NetCodec.Decode(NetCodec.Encode(list)));
        Assert.Equal(6, Assert.Single(listBack.Notes).UpdatedUtc);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
