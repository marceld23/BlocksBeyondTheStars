// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Moderation;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Every player-written NAME and every line the AI writes now goes through the same content screen as chat
/// (#1221). Before this, only the join name was screened: a base, a station, a beacon, a beam pad and a
/// companion could be called anything at all, and it stayed there for everyone to read.
///
/// The interesting cases are the two ends. A German compound must survive (the chat screen matches whole
/// tokens for exactly this reason — the join-name screen, which substring-matches, would not), and a name
/// that chat would merely MASK is refused outright, because a name is permanent and a masked one is worse
/// than being asked for another.
/// </summary>
public sealed class NameAndAiScreeningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public NameAndAiScreeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_namescreen_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

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

    /// <summary>An AI backend that always answers with the same text — the point is what the SERVER does
    /// with it, not what a model would produce.</summary>
    private sealed class FixedAiProvider : IAiMissionProvider
    {
        private readonly string _text;

        public FixedAiProvider(string text) => _text = text;

        public MissionPlan? Generate(string context) => new() { Title = _text, Description = _text };

        public string? GenerateNpcLine(NpcLineRequest request) => _text;

        public MissionTextResult? GenerateMissionText(MissionTextRequest request)
            => new() { Title = "A delivery", Description = _text };
    }

    private SvGameServer NewServer(string name, RecordingTransport transport,
        Action<ServerConfig>? configure = null, IAiMissionProvider? ai = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 5,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo, logger: null, aiProvider: ai);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    /// <summary>A player up in the air (so the target cell is empty and within reach) holding the two blocks
    /// these tests place, plus a radio so their chat is relayed.</summary>
    private static PlayerSession Builder(SvGameServer server, string name = "Builder")
    {
        var session = server.AddLocalPlayer(name);
        session.State.Position = new Vector3f(0, 200, 0);
        session.State.Inventory.Add("base_core", 4, 16);
        session.State.Inventory.Add("radio_beacon", 4, 16);
        session.State.Inventory.Add("comm_radio", 1, 1);
        return session;
    }

    private static int NoticesTo(RecordingTransport t, PlayerSession who, string reason)
        => t.Sent.Count(s => s.Conn == who.ConnectionId && s.Msg is ActionRejected r && r.Reason == reason);

    // ---------------- Player-written names ----------------

    [Fact]
    public void BaseName_WithAHateTerm_IsRefused_AndTheOldNameStands()
    {
        var transport = new RecordingTransport();
        var server = NewServer("base_hate", transport);
        var owner = Builder(server);
        string body = server.ActiveLocationId;
        server.PlaceBlock("Builder", 1, 200, 0, "base_core");

        server.SetBaseNameForTest(owner, body, "Fort Iron");
        Assert.Equal("Fort Iron", server.BaseSnapshots.Single().Name);

        transport.Sent.Clear();
        server.SetBaseNameForTest(owner, body, "h.i.t.l.e.r base");

        Assert.Equal("Fort Iron", server.BaseSnapshots.Single().Name);
        Assert.Equal(1, NoticesTo(transport, owner, "@srv.name.blocked"));
    }

    [Fact]
    public void BaseName_WithAGermanCompound_IsAccepted()
    {
        // The join-name screen substring-matches, which is right for a 24-character handle and wrong here:
        // "Dickichtlager" would lose to a three-letter entry. The chat screen matches whole tokens, which is
        // why names are routed through THAT one.
        var transport = new RecordingTransport();
        var server = NewServer("base_compound", transport);
        var owner = Builder(server);
        string body = server.ActiveLocationId;
        server.PlaceBlock("Builder", 1, 200, 0, "base_core");

        server.SetBaseNameForTest(owner, body, "Dickichtlager");

        Assert.Equal("Dickichtlager", server.BaseSnapshots.Single().Name);
        Assert.Equal(0, NoticesTo(transport, owner, "@srv.name.blocked"));
    }

    [Fact]
    public void AName_IsRefusedWhereAChatLineWouldOnlyBeMasked()
    {
        // The deliberate difference from chat: a line is gone in a minute, a name is painted on the world.
        var transport = new RecordingTransport();
        var server = NewServer("base_mask", transport);
        var owner = Builder(server);
        var reader = server.AddLocalPlayer("Reader");
        string body = server.ActiveLocationId;
        server.PlaceBlock("Builder", 1, 200, 0, "base_core");
        server.SetBaseNameForTest(owner, body, "Fort Iron");
        transport.Sent.Clear();

        server.SetBaseNameForTest(owner, body, "asshole camp");
        Assert.Equal("Fort Iron", server.BaseSnapshots.Single().Name);
        Assert.Equal(1, NoticesTo(transport, owner, "@srv.name.blocked"));

        // …while the very same word in chat is relayed with asterisks, as it always was.
        owner.LastChatTick = Environment.TickCount - 10_000;
        server.Chat("Builder", "you are an asshole");
        var heard = transport.Sent.Where(s => s.Conn == reader.ConnectionId)
            .Select(s => s.Msg).OfType<ChatMessage>().Select(c => c.Text).ToList();
        Assert.Equal(new[] { "you are an *******" }, heard);
    }

    [Fact]
    public void CompanionName_IsScreened_AndTheCompanionKeepsItsOldName()
    {
        var transport = new RecordingTransport();
        var server = NewServer("companion_name", transport);
        var owner = Builder(server);

        owner.State.TamedCreatures.Add(new BlocksBeyondTheStars.Shared.State.TamedCreature
        {
            Id = "c1",
            SpeciesId = "grazer",
            Name = "Flauschi",
        });

        transport.Sent.Clear();
        server.RenameCompanionForTest("Builder", "c1", "h.i.t.l.e.r");

        Assert.Equal("Flauschi", server.TamedCreaturesForTest("Builder").Single().Name);
        Assert.Equal(1, NoticesTo(transport, owner, "@srv.name.blocked"));
    }

    [Fact]
    public void ARefusedBeaconLabel_LeavesTheBeaconStanding_WithNoLabel()
    {
        // The block is already in the world by the time the label is screened, so refusing the PLACEMENT
        // would leave a beacon the player cannot get rid of. An empty label is a state the client already
        // renders a localized default for.
        var transport = new RecordingTransport();
        var server = NewServer("beacon_label", transport);
        var owner = Builder(server);
        server.PlaceBlock("Builder", 1, 200, 0, "radio_beacon");
        Assert.Equal(1, server.BeaconCount);

        int id = server.BeaconSnapshots.Single().Id;
        transport.Sent.Clear();
        server.SetBeaconLabelForTest(owner, id, "h.i.t.l.e.r");

        Assert.Equal(1, server.BeaconCount);
        Assert.Equal(string.Empty, server.BeaconSnapshots.Single().Label);
        Assert.Equal(1, NoticesTo(transport, owner, "@srv.name.blocked"));
    }

    [Fact]
    public void OperatorOff_LeavesEveryNameAlone()
    {
        // A private family LAN: the operator switched the filter off, and nothing is screened anywhere.
        var transport = new RecordingTransport();
        var server = NewServer("names_off", transport, c => c.ChatFilter = ChatFilterLevel.Off);
        var owner = Builder(server);
        string body = server.ActiveLocationId;
        server.PlaceBlock("Builder", 1, 200, 0, "base_core");

        server.SetBaseNameForTest(owner, body, "asshole camp");

        Assert.Equal("asshole camp", server.BaseSnapshots.Single().Name);
        Assert.Equal(0, NoticesTo(transport, owner, "@srv.name.blocked"));
    }

    // ---------------- AI-written text ----------------

    [Fact]
    public void TheAiProvider_IsAlwaysWrappedInTheScreen()
    {
        // The wrapping happens once, in the constructor. Nothing else in the game would notice if a refactor
        // dropped it — which is exactly why it is pinned here.
        var transport = new RecordingTransport();
        var server = NewServer("ai_wrapped", transport, ai: new FixedAiProvider("hello there"));

        Assert.True(server.AiProviderIsScreenedForTest);
    }

    [Fact]
    public void AiText_ThatWouldBeBlocked_IsDropped_SoTheAuthoredLineIsUsed()
    {
        var transport = new RecordingTransport();
        var server = NewServer("ai_block", transport);

        Assert.Null(server.ScreenAiTextForTest("h.i.t.l.e.r was right"));

        // Masked is dropped too: our own backend wrote this, so a starred-out sentence in an NPC's mouth is
        // a worse answer than the line we authored ourselves.
        Assert.Null(server.ScreenAiTextForTest("what an asshole"));

        Assert.Equal("Welcome to Karth Town, pilot.", server.ScreenAiTextForTest("Welcome to Karth Town, pilot."));
        Assert.Null(server.ScreenAiTextForTest(null));
    }

    [Fact]
    public void AiText_IsUntouched_WhenTheOperatorTurnedTheFilterOff()
    {
        var transport = new RecordingTransport();
        var server = NewServer("ai_off", transport, c => c.ChatFilter = ChatFilterLevel.Off);

        Assert.Equal("what an asshole", server.ScreenAiTextForTest("what an asshole"));
    }

    [Fact]
    public void AMissionPosting_IsDroppedWhole_WhenEitherHalfIsRefused()
    {
        // Title and description are one posting. Keeping a clean title next to a refused description would
        // publish half a screened text and look like a bug in the filter.
        var refused = new ScreenedAiTextProvider(new FixedAiProvider("h.i.t.l.e.r"), _ => null);
        var kept = new ScreenedAiTextProvider(new FixedAiProvider("all fine"), t => t);

        Assert.Null(refused.GenerateMissionText(new MissionTextRequest()));
        Assert.Null(refused.GenerateNpcLine(new NpcLineRequest()));
        Assert.Null(refused.Generate("context"));

        Assert.NotNull(kept.GenerateMissionText(new MissionTextRequest()));
        Assert.Equal("all fine", kept.GenerateNpcLine(new NpcLineRequest()));
        Assert.NotNull(kept.Generate("context"));
    }

    [Fact]
    public void TheDecorator_LeavesAnEmptyFieldAlone_AndPassesTheProvidersOwnNull()
    {
        // Several plan fields are optional; an empty one is not a refusal, and "the backend gave us nothing"
        // must stay distinguishable from "the backend gave us something we dropped".
        int calls = 0;
        var provider = new ScreenedAiTextProvider(new NullAiMissionProvider(), t => { calls++; return t; });

        Assert.Null(provider.GenerateNpcLine(new NpcLineRequest()));
        Assert.Null(provider.GenerateMissionText(new MissionTextRequest()));
        Assert.Null(provider.Generate("context"));
        Assert.Equal(1, calls); // only the npc line reaches the screen; the other two are null before it
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
