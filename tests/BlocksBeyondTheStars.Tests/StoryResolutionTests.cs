// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The ending as a shown thing (#1124): winning the finale broadcasts <see cref="StoryResolved"/>
/// (the client's resolution cinematic) and speaks the epilogue into the story log; whoever missed the
/// moment catches up exactly once on join, and the Story tab can replay it any time — but only once the
/// story actually IS resolved.</summary>
public sealed class StoryResolutionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StoryResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_resolution_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static ServerConfig Config() => new()
    {
        WorldName = "rocky",
        Seed = 4242,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        PlaceStarterShip = false,
    };

    /// <summary>Reveals the finale, lands the named player's session on the core, hacks it open and wins
    /// the argument duel (the authored correct rebuttals, in order: 0, 1, 2, 0).</summary>
    private static void WinTheFinale(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession pilot, string name)
    {
        for (int i = 0; i < 300 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        pilot.CurrentLocationId = SvGameServer.GuardianCoreBodyId;
        for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
        {
            server.CoreHackTickForTest(name);
        }

        server.CoreDialogueChoiceForTest(name, 0);
        server.CoreDialogueChoiceForTest(name, 1);
        server.CoreDialogueChoiceForTest(name, 2);
        server.CoreDialogueChoiceForTest(name, 0);
    }

    [Fact]
    public void Winning_the_duel_broadcasts_the_resolution_and_speaks_the_epilogue()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        StoryResolved? resolved = null;
        var vegaKeys = new List<string>();
        client.PayloadReceived += p =>
        {
            switch (NetCodec.Decode(p))
            {
                case StoryResolved m: resolved = m; break;
                case ShipAiLine l when !string.IsNullOrEmpty(l.LineKey): vegaKeys.Add(l.LineKey); break;
            }
        };

        var server = new SvGameServer(Config(), _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        WinTheFinale(server, server.Sessions[1], "Pilot");
        client.Poll();

        Assert.NotNull(resolved);
        Assert.Equal("story.vega_protocol.name", resolved!.StoryNameKey);
        Assert.Equal("The relay network", resolved.EpilogueTitle);
        Assert.Equal("story.vega.beat13", resolved.EpilogueTextKey);
        // The epilogue also entered the story log (a kind-2 VEGA line), right after the resolution line.
        Assert.Contains("story.vega.finale_resolved", vegaKeys);
        Assert.Contains("story.vega.beat13", vegaKeys);
        // And the "seen" milestone is remembered, so a rejoin never replays it unasked.
        Assert.Contains("story:vega_protocol:resolved", server.Sessions[1].State.Milestones);
    }

    [Fact]
    public void A_latecomer_catches_up_exactly_once()
    {
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(Config(), _content, st, repo);
            server.Start();
            var pilot = server.AddLocalPlayer("Winner");
            WinTheFinale(server, pilot, "Winner");
            Assert.True(server.StorySnapshot.Defeated);
            repo.Flush();
        }

        int received = JoinAndCountResolved("Late");
        Assert.Equal(1, received); // missed the moment → the ending plays once on join

        received = JoinAndCountResolved("Late");
        Assert.Equal(0, received); // seen it → never again unasked (the Story tab can still replay)
    }

    private int JoinAndCountResolved(string name)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        int received = 0;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is StoryResolved) { received++; } };

        var server = new SvGameServer(Config(), _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        repo.Flush();
        return received;
    }

    [Fact]
    public void The_replay_request_answers_only_once_resolved()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        int received = 0;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is StoryResolved) { received++; } };

        var server = new SvGameServer(Config(), _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // Unresolved story: the request must stay silent (the button cannot spoil anything).
        client.Send(NetCodec.Encode(new RequestStoryResolutionIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.Equal(0, received);

        WinTheFinale(server, server.Sessions[1], "Pilot");
        client.Poll();
        Assert.Equal(1, received); // the win itself played it

        client.Send(NetCodec.Encode(new RequestStoryResolutionIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.Equal(2, received); // and the Story tab can watch it again
    }

    [Fact]
    public void Epilogue_text_resolves_in_both_languages()
    {
        var story = _content.DefaultStory;
        Assert.False(string.IsNullOrEmpty(story.EpilogueTextKey));
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        Assert.False(en.Get(story.EpilogueTextKey).StartsWith("["), "EN missing the epilogue text");
        Assert.False(de.Get(story.EpilogueTextKey).StartsWith("["), "DE missing the epilogue text");
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
