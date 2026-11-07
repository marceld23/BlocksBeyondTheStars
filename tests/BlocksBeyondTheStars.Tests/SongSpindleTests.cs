// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3352 — the Song Spindle and the note-key minigame on the server: a disc in the hand goes in and sings (the disc's phrase
/// in the note alphabet with the Sounder voice), an empty hand plays it again, crouch + use takes it out, the record survives
/// a reload and a blasted spindle drops its disc. A story disc the player has not decoded opens the minigame — only with the
/// Sounders pack running — and the server checks the answers: wrong words or a run it did not open count as a failed try, a
/// skip is accepted only after two of them, and decoding writes <c>snd:disc:&lt;disc&gt;</c> and raises
/// <c>disc:decoded</c>. The answer passes a singleplayer pause.
/// </summary>
public sealed class SongSpindleTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static Vector3i Spindle(NpcLifeWorld w)
    {
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, SoundersIds.SongSpindle);
        return cell;
    }

    private static void Hold(PlayerSession p, string item)
    {
        p.State.Inventory.SetSlot(0, new ItemStack(item, 1));
        p.State.SelectedHotbarSlot = 0;
    }

    private static void StartPack(PlayerSession p) => p.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));

    private static List<NoteSequence> Songs(NpcLifeWorld w)
        => w.Transport.To<NoteSequence>(w.Player).Where(n => n.Source == SoundersIds.SongSpindle).ToList();

    [Fact]
    public void TheSpindle_IsAUsableBlock_ThatSingsTheDiscPutIntoIt()
    {
        using var w = World("spindle_sings");
        var cell = Spindle(w);
        Assert.True(w.Server.BlockHasUse(SoundersIds.SongSpindle));
        Hold(w.Player, SoundersIds.DiscBuilders);

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

        Assert.Equal(SoundersIds.DiscBuilders, w.Server.SpindleDiscAt(cell));
        Assert.False(w.Player.State.Inventory.Has(SoundersIds.DiscBuilders, 1)); // it left the hand
        var song = Assert.Single(Songs(w));
        Assert.Equal(SongDiscs.NotesOf(SoundersIds.DiscBuilders), song.Notes);
        Assert.Equal(SoundersIds.InstrumentVoice, song.Instrument);
        Assert.Equal("OLU TAR SOA", SounderAlphabet.Decode(song.Notes));
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "spindle_spin");
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "disc_insert");
        Assert.DoesNotContain(w.Server.Containers, c => c.Kind == SvGameServer.SpindleContainerKind); // never sent to clients
        Assert.Empty(w.Transport.To<StoryMinigameStart>(w.Player)); // no story without the pack
    }

    [Fact]
    public void AnEmptyHand_PlaysAgain_CrouchTakesItOut_AndAnEmptySpindleSaysSo()
    {
        using var w = World("spindle_hand");
        var cell = Spindle(w);
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Contains("@srv.spindle.empty", w.Transport.TextsTo(w.Player));

        Hold(w.Player, SoundersIds.DiscCalm);
        w.Server.UseSongSpindle(w.Player, cell);
        w.Player.State.Inventory.SetSlot(0, null);
        w.Server.UseSongSpindle(w.Player, cell); // still singing: a held key does not stack songs
        Assert.Single(Songs(w));

        w.Server.AdvanceUptimeForTest(30);
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Equal(2, Songs(w).Count);

        w.Player.State.Crouching = true;
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Equal(string.Empty, w.Server.SpindleDiscAt(cell));
        Assert.True(w.Player.State.Inventory.Has(SoundersIds.DiscCalm, 1));
    }

    [Fact]
    public void ASecondDisc_TakesThePlaceOfTheFirst_WhichComesBack()
    {
        using var w = World("spindle_swap");
        var cell = Spindle(w);
        Hold(w.Player, SoundersIds.DiscCalm);
        w.Server.UseSongSpindle(w.Player, cell);
        Hold(w.Player, SoundersIds.DiscLight);
        w.Server.UseSongSpindle(w.Player, cell);

        Assert.Equal(SoundersIds.DiscLight, w.Server.SpindleDiscAt(cell));
        Assert.True(w.Player.State.Inventory.Has(SoundersIds.DiscCalm, 1));
        Assert.False(w.Player.State.Inventory.Has(SoundersIds.DiscLight, 1));
    }

    [Fact]
    public void TheCollectibleDisc_SingsTheSeededSongOfItsSpindle()
    {
        using var w = World("spindle_song");
        var cell = Spindle(w);
        Hold(w.Player, SoundersIds.DiscSong);
        w.Server.UseSongSpindle(w.Player, cell);

        var song = Assert.Single(Songs(w));
        string[] words = SongDiscs.CollectibleWords(w.Server.Metadata.Seed, cell.X, cell.Y, cell.Z);
        Assert.Equal(string.Join(" ", words), SounderAlphabet.Decode(song.Notes));
        Assert.Equal(song.Notes, w.Server.SpindleNotes(cell, SoundersIds.DiscSong));
    }

    [Fact]
    public void TheDiscInTheSpindle_SurvivesAReload()
    {
        using var w = World("spindle_reload");
        var cell = Spindle(w);
        Hold(w.Player, SoundersIds.DiscHunger);
        w.Server.UseSongSpindle(w.Player, cell);

        w.Restart();

        Assert.Equal(SoundersIds.DiscHunger, w.Server.SpindleDiscAt(cell));
    }

    [Fact]
    public void ABlastedSpindle_DropsItsDisc_OnTheGround()
    {
        using var w = World("spindle_blast");
        var cell = Spindle(w);
        Hold(w.Player, SoundersIds.DiscVoices);
        w.Server.UseSongSpindle(w.Player, cell);

        w.Player.State.Inventory.SetSlot(1, new ItemStack("terrain_blaster", 1));
        w.Player.State.SuitEnergy = 100f;
        w.Server.UseGadgetForTest(w.Player.State.PlayerId, "terrain_blaster", new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));

        Assert.True(w.Server.World.GetBlock(cell).IsAir);
        Assert.Equal(string.Empty, w.Server.SpindleDiscAt(cell));
        Assert.Contains(w.Server.DropPackets, d => d.Items.Any(s => s.Item == SoundersIds.DiscVoices));
    }

    [Fact]
    public void AnUndecodedStoryDisc_OpensTheNoteKey_OnlyWithThePackRunning()
    {
        using var w = World("spindle_offer");
        var cell = Spindle(w);
        StartPack(w.Player);
        Assert.True(w.Server.SoundersActive(w.Player));
        w.Player.State.Milestones.Add(SoundersIds.WordMilestone("LUM"));
        Hold(w.Player, SoundersIds.DiscBuilders);

        w.Server.UseSongSpindle(w.Player, cell);

        var start = Assert.Single(w.Transport.To<StoryMinigameStart>(w.Player));
        Assert.Equal(SongDiscs.NoteKeyGame, start.Game);
        Assert.Equal(SoundersIds.DiscBuilders, start.Context);
        Assert.Equal(new[] { "OLU", "TAR", "SOA" }, start.Words);
        Assert.Equal("LMU", start.KnownLetters); // the heard rune word's letters
        Assert.Equal(0, start.Failures);

        // A disc that is no story disc, and a decoded one, never open it.
        w.Player.State.Milestones.Add(SoundersIds.DiscMilestone(SoundersIds.DiscBuilders));
        w.Server.AdvanceUptimeForTest(30);
        w.Server.UseSongSpindle(w.Player, cell);
        Hold(w.Player, SoundersIds.DiscCalm);
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Single(w.Transport.To<StoryMinigameStart>(w.Player));
    }

    [Fact]
    public void RightAnswers_DecodeTheDisc_AndRaiseDiscDecoded()
    {
        using var w = World("spindle_decode");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.DiscDecoded, events);
        var cell = Spindle(w);
        StartPack(w.Player);
        Hold(w.Player, SoundersIds.DiscHearing);
        w.Server.UseSongSpindle(w.Player, cell);

        Answer(w, SoundersIds.DiscHearing, completed: true, "MIRA", "VEL", "RUSO");

        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscHearing), w.Player.State.Milestones);
        Assert.Equal((GameEventKeys.DiscDecoded, SoundersIds.DiscHearing), Assert.Single(events.Events));
        Assert.Contains("@srv.sounders.disc_decoded", w.Transport.TextsTo(w.Player));
    }

    [Fact]
    public void WrongAnswers_AndRunsTheServerDidNotOpen_DecodeNothing_AndTheSkipWaitsForTwoFailures()
    {
        using var w = World("spindle_validate");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.DiscDecoded, events);
        var cell = Spindle(w);
        StartPack(w.Player);

        Answer(w, SoundersIds.DiscGreatSong, completed: true, "SOA", "ESA", "NIRI"); // never offered: ignored
        Assert.DoesNotContain(SoundersIds.DiscMilestone(SoundersIds.DiscGreatSong), w.Player.State.Milestones);

        Hold(w.Player, SoundersIds.DiscGreatSong);
        w.Server.UseSongSpindle(w.Player, cell);
        Skip(w, SoundersIds.DiscGreatSong); // no failed try yet: refused
        Answer(w, SoundersIds.DiscGreatSong, completed: true, "SOA", "ESA", "NIRA"); // a wrong word: failure 1
        Answer(w, SoundersIds.DiscGreatSong, completed: false); // a failed run: failure 2
        Assert.DoesNotContain(SoundersIds.DiscMilestone(SoundersIds.DiscGreatSong), w.Player.State.Milestones);
        Assert.Equal(new[] { 0, 1, 2 }, w.Transport.To<StoryMinigameStart>(w.Player).Select(s => s.Failures));

        Skip(w, SoundersIds.DiscGreatSong);

        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscGreatSong), w.Player.State.Milestones);
        Assert.Single(events.Events);
        Assert.Contains("@srv.sounders.disc_skipped", w.Transport.TextsTo(w.Player));
    }

    [Fact]
    public void TheAnswer_PassesASingleplayerPause()
    {
        using var w = World("spindle_pause");
        var cell = Spindle(w);
        StartPack(w.Player);
        Hold(w.Player, SoundersIds.DiscBeginning);
        w.Server.UseSongSpindle(w.Player, cell);
        w.Server.PauseForTest(w.Player, true);
        Assert.True(w.Server.IsPaused);

        var answer = new StoryMinigameResultIntent
        {
            Game = SongDiscs.NoteKeyGame,
            Context = SoundersIds.DiscBeginning,
            Completed = true,
            Answers = new[] { "MIRA", "SOA", "ESA" },
        };
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(answer));

        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning), w.Player.State.Milestones);
        Assert.True(w.Server.IsPaused); // it moved nothing in the frozen world
    }

    [Fact]
    public void TheKnownLetters_AreTheHeardWordsAndTheDecodedDiscs()
    {
        var p = new PlayerState();
        p.Milestones.Add(SoundersIds.WordMilestone("ORN"));
        p.Milestones.Add(SoundersIds.DiscMilestone(SoundersIds.DiscBuilders));
        p.Milestones.Add("snd:got:" + SoundersIds.DiscHunger);
        Assert.Equal("ALNORSTU", SvGameServer.KnownSounderLetters(p));
    }

    private static void Answer(NpcLifeWorld w, string disc, bool completed, params string[] words)
        => w.Server.StoryMinigameResultForTest(w.Player, new StoryMinigameResultIntent
        {
            Game = SongDiscs.NoteKeyGame,
            Context = disc,
            Completed = completed,
            Answers = words,
        });

    private static void Skip(NpcLifeWorld w, string disc)
        => w.Server.StoryMinigameResultForTest(w.Player, new StoryMinigameResultIntent { Game = SongDiscs.NoteKeyGame, Context = disc, Skip = true });
}
