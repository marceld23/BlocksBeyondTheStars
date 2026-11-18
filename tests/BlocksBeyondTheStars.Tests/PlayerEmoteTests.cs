// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The feel pass's emotes: a player's emote reaches the players nearby (never the sender, never someone far away or
/// on the other side of a ship's hull), only known ids pass, a held key cannot flood the others — and the server
/// adds the flinch of a hit and the fall of a defeat by itself.
/// </summary>
public sealed class PlayerEmoteTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public PlayerEmoteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_emotes_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static List<PlayerEmote> EmotesTo(RecordingTransport transport, int connection)
        => transport.Sent.Where(x => x.Conn == connection).Select(x => x.Msg).OfType<PlayerEmote>().ToList();

    [Fact]
    public void AnEmote_ReachesThePlayersNearby_NotTheSender_NotTheFarOnes()
    {
        var transport = new RecordingTransport();
        var server = Started("emotes_near", transport);
        var waver = server.AddLocalPlayer("Waver");
        var near = server.AddLocalPlayer("Near");
        var far = server.AddLocalPlayer("Far");
        foreach (var s in new[] { waver, near, far })
        {
            s.State.AboardShip = false;
        }

        var at = waver.State.Position;
        near.State.Position = new Vector3f(at.X + 6f, at.Y, at.Z);
        far.State.Position = new Vector3f(at.X + SvGameServer.EmoteRange + 30f, at.Y, at.Z);
        transport.Sent.Clear();

        server.EmoteForTest("Waver", PlayerEmotes.Dance);

        var seen = Assert.Single(EmotesTo(transport, near.ConnectionId));
        Assert.Equal("Waver", seen.PlayerId);
        Assert.Equal(PlayerEmotes.Dance, seen.Emote);
        Assert.Empty(EmotesTo(transport, waver.ConnectionId));
        Assert.Empty(EmotesTo(transport, far.ConnectionId));
    }

    [Fact]
    public void TheHull_IsInBetween_AboardAndOutsideDoNotSeeEachOther()
    {
        var transport = new RecordingTransport();
        var server = Started("emotes_hull", transport);
        var inside = server.AddLocalPlayer("Inside");
        var outside = server.AddLocalPlayer("Outside");
        inside.State.AboardShip = true;
        outside.State.AboardShip = false;
        outside.State.Position = inside.State.Position;
        transport.Sent.Clear();

        server.EmoteForTest("Inside", PlayerEmotes.Wave);

        Assert.Empty(EmotesTo(transport, outside.ConnectionId));
    }

    [Fact]
    public void OnlyAPlayersOwnEmotesPass_AndNotFasterThanThePace()
    {
        var transport = new RecordingTransport();
        var server = Started("emotes_pace", transport);
        var player = server.AddLocalPlayer("Player");
        var watcher = server.AddLocalPlayer("Watcher");
        player.State.AboardShip = false;
        watcher.State.AboardShip = false;
        watcher.State.Position = player.State.Position;
        transport.Sent.Clear();

        // A body state is the server's to say, an unknown id is nothing.
        server.EmoteForTest("Player", PlayerEmotes.Down);
        server.EmoteForTest("Player", PlayerEmotes.Hurt);
        server.EmoteForTest("Player", 77);
        server.EmoteForTest("Player", PlayerEmotes.None);
        Assert.Empty(EmotesTo(transport, watcher.ConnectionId));

        // A held key: the first one passes, the rest wait for the pace.
        server.EmoteForTest("Player", PlayerEmotes.Clap);
        server.EmoteForTest("Player", PlayerEmotes.Clap);
        server.EmoteForTest("Player", PlayerEmotes.Cheer);
        Assert.Equal(new[] { PlayerEmotes.Clap }, EmotesTo(transport, watcher.ConnectionId).Select(e => e.Emote));

        server.TickForTest(SvGameServer.EmoteCooldownSeconds + 0.2);
        server.EmoteForTest("Player", PlayerEmotes.Cheer);
        Assert.Equal(new[] { PlayerEmotes.Clap, PlayerEmotes.Cheer }, EmotesTo(transport, watcher.ConnectionId).Select(e => e.Emote));
    }

    [Fact]
    public void AHit_ShowsAsAFlinch_TheLastOneAsTheFall()
    {
        var transport = new RecordingTransport();
        var server = Started("emotes_hurt", transport);
        var victim = server.AddLocalPlayer("Victim");
        var watcher = server.AddLocalPlayer("Watcher");
        victim.State.AboardShip = false;
        watcher.State.AboardShip = false;
        watcher.State.Position = victim.State.Position;
        victim.State.Health = 100f;
        transport.Sent.Clear();

        // A fall a little over the safe speed: hurt, not defeated.
        server.FallDamageForTest("Victim", 18f);
        Assert.True(victim.State.Health is > 0f and < 100f, $"expected a survivable fall, health is {victim.State.Health}");
        Assert.Equal(new[] { PlayerEmotes.Hurt }, EmotesTo(transport, watcher.ConnectionId).Select(e => e.Emote));
        Assert.Empty(EmotesTo(transport, victim.ConnectionId)); // the victim feels its own hit, it is not told

        // The fall that takes the rest.
        transport.Sent.Clear();
        victim.State.Health = 1f;
        server.FallDamageForTest("Victim", 60f);
        Assert.Contains(PlayerEmotes.Down, EmotesTo(transport, watcher.ConnectionId).Select(e => e.Emote));
    }
    [Fact]
    public void AHitAndAFall_AreHeardInTheFiguresOwnVoice()
    {
        // #3767: the voice travels with the colours; the server keeps it (sanitized) and hands it on with the flinch and
        // the fall, so the others play the pain grunt and the fall in THAT player's voice. A wave carries none.
        var transport = new RecordingTransport();
        var server = Started("emotes_voice", transport);
        var victim = server.AddLocalPlayer("Victim");
        var watcher = server.AddLocalPlayer("Watcher");
        victim.State.AboardShip = false;
        watcher.State.AboardShip = false;
        watcher.State.Position = victim.State.Position;
        victim.State.Health = 100f;

        int voice = BlocksBeyondTheStars.Shared.Definitions.PlayerVoice.Pack(80, 20);
        server.AppearanceForTest(victim, new SetAppearanceIntent { Skin = 1, Torso = 2, Arms = 3, Legs = 4, Voice = voice });
        Assert.Equal(voice, victim.Voice);
        transport.Sent.Clear();

        server.FallDamageForTest("Victim", 18f);
        var hurt = Assert.Single(EmotesTo(transport, watcher.ConnectionId));
        Assert.Equal((PlayerEmotes.Hurt, voice), (hurt.Emote, hurt.Voice));

        transport.Sent.Clear();
        server.EmoteForTest("Victim", PlayerEmotes.Wave);
        Assert.All(EmotesTo(transport, watcher.ConnectionId), e => Assert.Equal(0, e.Voice));

        // A client that sends none (an older one) keeps the middle; a foreign value is clamped, never trusted.
        server.AppearanceForTest(victim, new SetAppearanceIntent { Skin = 1, Torso = 2, Arms = 3, Legs = 4 });
        Assert.Equal(0, victim.Voice);
        server.AppearanceForTest(victim, new SetAppearanceIntent { Voice = 0x7FFF_FFFF });
        Assert.Equal(BlocksBeyondTheStars.Shared.Definitions.PlayerVoice.Pack(100, 100), victim.Voice);
    }
}
