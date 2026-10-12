// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2472 — step 13 of the story packs (#2459): the world rules behind world creation's "Stories" page. The creation flags
/// (<c>--story</c>, <c>--story-density</c>, <c>--tutorial</c>) parse together and are baked into the save (a later launch
/// cannot change them; an older save reads Normal / Auto); the world admin changes the story pace live
/// (<c>SetWorldRulesIntent.StoryDensity</c>, new) next to the tutorial rule — persisted, re-sent in <c>ServerRules</c>, a
/// faster pace revealing at once what its score covers and a slower one never taking a heard beat back; numbers and
/// non-admins change nothing; no preset switches the tutorial off (family = Auto); a server started without story flags
/// (a hosted or glitch world) runs the default story with the tutorial on Auto.
/// </summary>
public sealed class WorldStoriesRulesTests : IDisposable
{
    /// <summary>The shipped content, loaded once for the class (every test boots its own server and save on it).</summary>
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public WorldStoriesRulesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stories_" + Guid.NewGuid().ToString("N"));
    }

    private static GameContent Content => Shipped.Value;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>Records every server send so a test can assert who was told what.</summary>
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

        public List<T> To<T>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<T>().ToList();
    }

    private static ServerConfig Config(string world) => new()
    {
        WorldName = world,
        Seed = 4242,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 6,
        PlaceStarterShip = false,
    };

    private (SvGameServer Server, ServerConfig Config, RecordingTransport Transport) Boot(string world, Action<GameRules>? rules = null)
    {
        var config = Config(world);
        rules?.Invoke(config.Rules);
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        return (server, config, transport);
    }

    private static PlayerSession Admin(SvGameServer server, string name = "Admin")
    {
        var p = server.AddLocalPlayer(name);
        p.State.Role = PlayerRole.WorldAdmin;
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    // ---- Creation flags ------------------------------------------------------------------------

    [Fact]
    public void TheCreationFlags_ParseTogether()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--story", "none", "--story-density", "Dense", "--tutorial", "off" });

        Assert.Contains("story", applied);
        Assert.Contains("story-density", applied);
        Assert.Contains("tutorial", applied);
        Assert.Equal("none", config.Rules.StoryId);
        Assert.Equal(StoryDensity.Dense, config.Rules.StoryDensity);
        Assert.Equal(TutorialPolicy.Off, config.Rules.TutorialPolicy);
    }

    [Fact]
    public void AServerWithoutStoryFlags_RunsTheDefaultStory_WithTheTutorialOnAuto()
    {
        // A hosted or glitch world (#2472: no portal option in v1) starts the game server without any story flag.
        var config = new ServerConfig();
        Assert.Equal(string.Empty, config.Rules.StoryId); // empty = the default story (VEGA)
        Assert.Equal(StoryDensity.Normal, config.Rules.StoryDensity);
        Assert.Equal(TutorialPolicy.Auto, config.Rules.TutorialPolicy);

        var (server, _, _) = Boot("hosted_defaults");
        Assert.Equal("vega_protocol", server.StorySnapshot.StoryId);
        Assert.Equal(TutorialPolicy.Auto, server.Metadata.RulesOverride!.TutorialPolicy);
        Assert.Equal(StoryDensity.Normal, server.Metadata.RulesOverride.StoryDensity);
    }

    [Fact]
    public void TheStoryChoices_AreBakedIntoTheSave_AndALaterLaunchCannotChangeThem()
    {
        var paths = new SaveGamePaths(_root, "baked");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var config = Config("baked");
            config.ApplyCommandLine(new[] { "--story", "none", "--story-density", "Sparse", "--tutorial", "optin" });
            var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
            server.Start();
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var relaunch = Config("baked");
            relaunch.ApplyCommandLine(new[] { "--story-density", "Dense", "--tutorial", "off" }); // a creation flag on a later launch…
            var server2 = new SvGameServer(relaunch, Content, new RecordingTransport(), repo2);
            server2.Start();

            Assert.Equal("none", relaunch.Rules.StoryId);                    // …is overruled by the save's own rules
            Assert.Equal(StoryDensity.Sparse, relaunch.Rules.StoryDensity);
            Assert.Equal(TutorialPolicy.OptIn, relaunch.Rules.TutorialPolicy);
            Assert.Equal("none", server2.StorySnapshot.StoryId);
            server2.Stop();
        }
    }

    [Fact]
    public void AnOlderSave_WithoutTheStoryRules_ReadsNormalPaceAndTheTutorialOnAuto()
    {
        var meta = JsonSerializer.Deserialize<WorldMetadata>("{\"WorldName\":\"old\",\"RulesOverride\":{\"StoryId\":\"vega_protocol\"}}")!;
        Assert.NotNull(meta.RulesOverride);
        Assert.Equal(StoryDensity.Normal, meta.RulesOverride!.StoryDensity);
        Assert.Equal(TutorialPolicy.Auto, meta.RulesOverride.TutorialPolicy);
    }

    // ---- Live edit -----------------------------------------------------------------------------

    [Fact]
    public void TheAdmin_ChangesTheStoryPaceLive_ItPersists_AndEveryoneIsTold()
    {
        var (server, config, transport) = Boot("live_pace");
        var admin = Admin(server);
        var other = server.AddLocalPlayer("Kid");

        Deliver(server, admin, new SetWorldRulesIntent { StoryDensity = "Dense" });

        Assert.Equal(StoryDensity.Dense, config.Rules.StoryDensity);
        Assert.Equal(StoryDensity.Dense, server.Metadata.RulesOverride!.StoryDensity);
        Assert.Equal("Dense", transport.To<ServerRules>(admin).Last().StoryDensity);
        Assert.Equal("Dense", transport.To<ServerRules>(other).Last().StoryDensity);
        Assert.Equal(TutorialPolicy.Auto, config.Rules.TutorialPolicy); // an empty tutorial field leaves the rule alone
    }

    [Fact]
    public void BothStoryRules_RideOneIntent()
    {
        var (server, config, transport) = Boot("live_both");
        var admin = Admin(server);

        Deliver(server, admin, new SetWorldRulesIntent { TutorialPolicy = "OptIn", StoryDensity = "Sparse" });

        Assert.Equal(TutorialPolicy.OptIn, config.Rules.TutorialPolicy);
        Assert.Equal(StoryDensity.Sparse, config.Rules.StoryDensity);
        var rules = transport.To<ServerRules>(admin).Last();
        Assert.Equal("OptIn", rules.TutorialPolicy);
        Assert.Equal("Sparse", rules.StoryDensity);
        Assert.Equal(TutorialPolicy.OptIn, server.Metadata.RulesOverride!.TutorialPolicy);
    }

    [Fact]
    public void APlayer_CannotChangeTheStoryRules()
    {
        var (server, config, transport) = Boot("live_refused");
        var kid = server.AddLocalPlayer("Kid");
        kid.State.Role = PlayerRole.Player;

        Deliver(server, kid, new SetWorldRulesIntent { StoryDensity = "Dense", TutorialPolicy = "Off" });

        Assert.Equal(StoryDensity.Normal, config.Rules.StoryDensity);
        Assert.Equal(TutorialPolicy.Auto, config.Rules.TutorialPolicy);
        Assert.Contains(transport.To<ActionRejected>(kid), m => m.Reason.Contains("rules_admin_only", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("Fast")]
    [InlineData("")]
    public void ANumberOrAnUnknownWord_LeavesThePaceUnchanged(string word)
    {
        var (server, config, _) = Boot("live_word_" + (word.Length == 0 ? "empty" : word.Replace("-", "m")));
        var admin = Admin(server);

        Deliver(server, admin, new SetWorldRulesIntent { StoryDensity = word });

        Assert.Equal(StoryDensity.Normal, config.Rules.StoryDensity);
    }

    [Theory]
    [InlineData("Sparse", StoryDensity.Sparse)]
    [InlineData("normal", StoryDensity.Normal)]
    [InlineData(" DENSE ", StoryDensity.Dense)]
    public void ThePaceWord_Parses(string word, StoryDensity expected)
    {
        Assert.True(ServerConfig.TryParseStoryDensity(word, out var density));
        Assert.Equal(expected, density);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("+2")]
    [InlineData("Slow")]
    [InlineData(null)]
    public void ThePaceParser_RefusesNumbersAndOtherWords(string? word)
        => Assert.False(ServerConfig.TryParseStoryDensity(word, out _));

    [Fact]
    public void AFasterPace_RevealsWhatItsScoreCovers_AtOnce_AndASlowerOneTakesNothingBack()
    {
        var (server, _, _) = Boot("live_reveal", r => r.StoryDensity = StoryDensity.Sparse);
        var admin = Admin(server);
        for (int i = 0; i < 12; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        int sparseBeats = server.StorySnapshot.BeatsRevealed;
        Deliver(server, admin, new SetWorldRulesIntent { StoryDensity = "Dense" });
        int denseBeats = server.StorySnapshot.BeatsRevealed;
        Assert.True(denseBeats > sparseBeats, $"switching to Dense should reveal the beats 12 milestones cover at once ({sparseBeats} → {denseBeats})");

        Deliver(server, admin, new SetWorldRulesIntent { StoryDensity = "Sparse" });
        Assert.Equal(denseBeats, server.StorySnapshot.BeatsRevealed); // heard beats stay
    }

    [Fact]
    public void ThePace_OnAWorldWithoutAStory_IsStoredAndChangesNothingElse()
    {
        var (server, config, _) = Boot("live_none", r => r.StoryId = "none");
        var admin = Admin(server);

        Deliver(server, admin, new SetWorldRulesIntent { StoryDensity = "Dense" });

        Assert.Equal(StoryDensity.Dense, config.Rules.StoryDensity);
        Assert.Equal("none", server.StorySnapshot.StoryId);
        Assert.Equal(0, server.StorySnapshot.BeatsRevealed);
    }

    [Fact]
    public void TheCodec_CarriesTheNewFields_AndAnOlderServerSendsNone()
    {
        var intent = Assert.IsType<SetWorldRulesIntent>(NetCodec.Decode(NetCodec.Encode(new SetWorldRulesIntent { StoryDensity = "Dense" })));
        Assert.Equal("Dense", intent.StoryDensity);
        var rules = Assert.IsType<ServerRules>(NetCodec.Decode(NetCodec.Encode(new ServerRules { StoryDensity = "Sparse" })));
        Assert.Equal("Sparse", rules.StoryDensity);
        Assert.Equal(string.Empty, new ServerRules().StoryDensity); // the client hides the row for an older server
        Assert.Equal(string.Empty, new SetWorldRulesIntent().StoryDensity); // empty = unchanged
    }

    // ---- Presets -------------------------------------------------------------------------------

    [Fact]
    public void NoPreset_SwitchesTheTutorialOff_AndTheFamilyPresetIsAuto()
    {
        foreach (var name in ServerPresets.Names)
        {
            var rules = ServerPresets.Get(name)!;
            Assert.Equal(TutorialPolicy.Auto, rules.TutorialPolicy);
            Assert.Equal(StoryDensity.Normal, rules.StoryDensity);
            Assert.Equal(string.Empty, rules.StoryId); // every preset tells the default story
        }

        Assert.Equal(TutorialPolicy.Auto, ServerPresets.Get("family")!.TutorialPolicy);
    }
}
