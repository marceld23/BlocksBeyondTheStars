// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The singing plants of Soaniri as puzzle helpers (#3367): the echo flower sings back what a player played once they are
/// quiet, a phrase of the Song of Return opens the bell blooms around the player (the wild ones close again), Ornasa's
/// bell-bloom trail opens one bloom per phrase in order and finally its sound chamber (<c>melody:solved</c> with
/// <c>bloom_trail</c>), wrong notes do nothing, and a resonance crystal is an instrument — a Choir Tower pipe sings its
/// pillar's tone.
/// </summary>
public sealed class SingingPlantsTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_singing_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string world, out RecordingTransport transport)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return SoaniriSitesTests.StartOn(repo, world, 3367, transport);
    }

    private static string KeyAt(SvGameServer server, Vector3i cell) => Content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private static (string Key, int X, int FloorY, int Z) Site(SvGameServer server, string key)
        => server.SounderSitesForTest().Single(s => s.Key == key);

    private static PlayerSession OnFoot(SvGameServer server, string name, Vector3i at)
    {
        var s = server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        MoveTo(s, at);
        return s;
    }

    private static void MoveTo(PlayerSession s, Vector3i at) => s.State.Position = new Vector3f(at.X + 0.5f, at.Y + 0.2f, at.Z + 0.5f);

    private static void Play(SvGameServer server, PlayerSession who, params int[] tones)
    {
        foreach (int tone in tones)
        {
            server.HearSingingPlants(who, tone, 0);
        }
    }

    private static int[] Phrase(string part) => SounderAlphabet.TonesOnly(SounderAlphabet.Encode(part));

    [Fact]
    public void TheSongOfReturnPhrases_AreRecognisedAtTheEndOfWhatWasPlayed()
    {
        Assert.True(SvGameServer.EndsWithSongPhrase(Phrase("OR")));
        Assert.True(SvGameServer.EndsWithSongPhrase(new[] { 7, 7 }.Concat(Phrase("NE")).ToList()));
        Assert.True(SvGameServer.EndsWithSongPhrase(Phrase("ORNEMIS")));
        Assert.False(SvGameServer.EndsWithSongPhrase(new[] { 0, 1, 2, 3 }));
        Assert.False(SvGameServer.EndsWithSongPhrase(Array.Empty<int>()));
    }

    [Fact]
    public void TheEchoFlower_SingsBackTheLastEightTones_OnceThePlayerIsQuiet()
    {
        var server = Started("echo", out var transport);
        var mirun = Site(server, SvGameServer.MirunSiteKey);
        var flower = new Vector3i(mirun.X + 2, mirun.FloorY + 1, mirun.Z + 1); // the echo flower by the bell post
        Assert.Equal(FloraCatalog.EchoFlowerKey, KeyAt(server, flower));
        var player = OnFoot(server, "Singer", new Vector3i(mirun.X - 2, mirun.FloorY + 1, mirun.Z - 4));

        Play(server, player, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6);
        server.TickSingingPlantsForTest(0.6);
        Assert.Empty(Echoes(transport, player)); // still playing (not quiet long enough)
        server.TickSingingPlantsForTest(0.8);
        var echo = Assert.Single(Echoes(transport, player));
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 7, 6 }, echo.Notes); // the last eight
        Assert.Equal(SoundersIds.InstrumentVoice, echo.Instrument);
        Assert.InRange(echo.X, flower.X, flower.X + 1);

        server.TickSingingPlantsForTest(5.0);
        Assert.Single(Echoes(transport, player)); // one answer per phrase

        MoveTo(player, new Vector3i(mirun.X + 40, mirun.FloorY + 1, mirun.Z + 40)); // far from every echo flower
        Play(server, player, 1, 2);
        server.TickSingingPlantsForTest(3.0);
        Assert.Single(Echoes(transport, player));
    }

    [Fact]
    public void ASongPhrase_OpensTheWildBellBlooms_ForAWhile_AndWrongNotesDoNothing()
    {
        var server = Started("blooms", out _);
        var mirun = Site(server, SvGameServer.MirunSiteKey);
        var bloom = new Vector3i(mirun.X + 2, mirun.FloorY + 1, mirun.Z - 2); // a bell bloom by the bell post
        Assert.Equal(FloraCatalog.BellBloomKey, KeyAt(server, bloom));
        var player = OnFoot(server, "Singer", new Vector3i(mirun.X - 3, mirun.FloorY + 1, mirun.Z - 3));

        Play(server, player, 0, 1, 2, 3, 0, 1); // not a phrase of the Song of Return
        Assert.Equal(FloraCatalog.BellBloomKey, KeyAt(server, bloom));

        Play(server, player, Phrase("MI"));
        Assert.Equal(FloraCatalog.BellBloomOpenKey, KeyAt(server, bloom));
        server.TickSingingPlantsForTest(SvGameServer.WildBloomOpenSeconds - 5);
        Assert.Equal(FloraCatalog.BellBloomOpenKey, KeyAt(server, bloom));
        server.TickSingingPlantsForTest(6);
        Assert.Equal(FloraCatalog.BellBloomKey, KeyAt(server, bloom));
    }

    [Fact]
    public void TheBloomTrail_OpensInOrder_ThenItsChamber_AndSolvesBloomTrail()
    {
        var server = Started("trail", out var transport);
        var events = new Recorder();
        server.AddGameEventListener(GameEventKeys.MelodySolved, events);
        var trail = server.BloomTrailForTest;
        int chamber = SounderSiteLayout.BloomTrailChamber;
        Assert.False(server.IsOrnasaChamberOpen(chamber));
        var player = OnFoot(server, "Singer", trail[0]);

        // Far from the next bloom a phrase does not move the trail.
        MoveTo(player, new Vector3i(trail[0].X + 30, trail[0].Y, trail[0].Z + 30));
        Play(server, player, Phrase("OR"));
        Assert.All(trail, c => Assert.Equal(FloraCatalog.BellBloomKey, KeyAt(server, c)));

        for (int k = 0; k < trail.Count; k++)
        {
            MoveTo(player, trail[k]);
            Assert.False(server.IsOrnasaChamberOpen(chamber));
            Play(server, player, Phrase(SoundersIds.SongOfReturnParts[k % SoundersIds.SongOfReturnParts.Length]));
            for (int j = 0; j < trail.Count; j++)
            {
                Assert.Equal(j <= k ? FloraCatalog.BellBloomOpenKey : FloraCatalog.BellBloomKey, KeyAt(server, trail[j]));
            }
        }

        Assert.True(server.IsOrnasaChamberOpen(chamber));
        Assert.Contains((GameEventKeys.MelodySolved, SoundersIds.PuzzleBloomTrail), events.Events);
        Assert.Contains("vega.sys.bloom_trail", transport.LineKeys(player));
        Assert.False(server.IsOrnasaChamberOpen(0)); // the blaster's chambers stay sealed

        server.TickSingingPlantsForTest(SvGameServer.WildBloomOpenSeconds + 10);
        Assert.All(trail, c => Assert.Equal(FloraCatalog.BellBloomOpenKey, KeyAt(server, c))); // the trail stays open
    }

    [Fact]
    public void TheBlaster_OpensItsChamberOnce()
    {
        var server = Started("blaster", out _);
        var events = new Recorder();
        server.AddGameEventListener(GameEventKeys.ChamberOpened, events);
        var player = OnFoot(server, "Blaster", server.OrnasaChambers[0].Seal[0]);
        Assert.True(server.OpenOrnasaChamber(player, 0, byBlaster: true));
        Assert.True(server.IsOrnasaChamberOpen(0));
        Assert.False(server.OpenOrnasaChamber(player, 0, byBlaster: true));
        Assert.Single(events.Events);
    }

    [Fact]
    public void AResonanceCrystal_IsAnInstrument_AndAPipeSingsItsTone()
    {
        var server = Started("crystal", out var transport);
        var tower = Site(server, SvGameServer.ChoirTowerSiteKey);
        var pipes = SvGameServer.ChoirPipes(tower.X, tower.FloorY, tower.Z);
        var player = OnFoot(server, "Player", new Vector3i(tower.X + 1, SvGameServer.ChoirPlatformY(tower.FloorY) + 1, tower.Z + 3));
        for (int i = 0; i < 8; i++)
        {
            var crystal = new Vector3i(pipes[i].X, pipes[i].Y + SounderSiteLayout.ChoirPipeStem(i), pipes[i].Z);
            MoveTo(player, new Vector3i(pipes[i].X, SvGameServer.ChoirPlatformY(tower.FloorY) + 1, pipes[i].Z + (pipes[i].Z > tower.Z ? -2 : 2)));
            int before = Crystals(transport, player).Count;
            server.HandlePayloadForTest(player.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = crystal.X, Y = crystal.Y, Z = crystal.Z }));
            var played = Crystals(transport, player);
            Assert.Equal(before + 1, played.Count);
            Assert.Equal(new[] { i }, played[^1].Notes);
        }

        var mirun = Site(server, SvGameServer.MirunSiteKey);
        var post = new Vector3i(mirun.X, mirun.FloorY + 6, mirun.Z); // the crystal on Mirun's bell post
        Assert.Equal("resonance_crystal", KeyAt(server, post));
        MoveTo(player, new Vector3i(mirun.X + 2, mirun.FloorY + 4, mirun.Z + 2));
        server.HandlePayloadForTest(player.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = post.X, Y = post.Y, Z = post.Z }));
        Assert.Equal(new[] { SvGameServer.CrystalTone(WorldConstants.CanonicalBlock(post, server.World.Circumference)) }, Crystals(transport, player)[^1].Notes);
    }

    private static List<NoteSequence> Echoes(RecordingTransport t, PlayerSession who)
        => t.To<NoteSequence>(who).Where(n => n.Source == "echo_flower").ToList();

    private static List<NoteSequence> Crystals(RecordingTransport t, PlayerSession who)
        => t.To<NoteSequence>(who).Where(n => n.Source == "resonance_crystal").ToList();
}
