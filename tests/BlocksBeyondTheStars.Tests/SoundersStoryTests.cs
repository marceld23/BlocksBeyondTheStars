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

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Sounders' Song story glue (#3368): the melody site starter hands out the right puzzle per chapter (the shrine's
/// echo game in chapter 2, the Song of Return in chapter 6) on a rune ring — the fallback of every world — and their
/// solutions give the Song Spindle and the first disc, then learn the song (Ornemis on the star map). In chapter 5 an Echo
/// appears at three rune stones for a player who wears the hearing disc, and the end of its dialog raises
/// <c>echo:questioned</c>. The sing duel's solution makes the story Soundi a travelling companion. The admin verb
/// <c>/sounders</c> drives the story for QA.
/// </summary>
public sealed class SoundersStoryTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<string> Args = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Args.Add(arg);
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static void Note(NpcLifeWorld w, int tone)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MelodyNoteIntent { Tone = tone }));

    private static void Use(NpcLifeWorld w, Vector3i cell)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

    /// <summary>A ring of eight rune stones (radius 3) around an ancient brick, the player standing inside it next to the
    /// brick; returns the north pillar (tone 0).</summary>
    private static Vector3i BuildRing(NpcLifeWorld w)
    {
        int cx = NpcLifeWorld.Cx + 6, cz = NpcLifeWorld.Cz, y = w.Feet;
        w.Player.State.Position = new Vector3f(cx + 1.5f, y, cz + 0.5f);
        w.Set(cx, y, cz, "ancient_brick");
        var offsets = new[] { (0, 3), (2, 2), (3, 0), (2, -2), (0, -3), (-2, -2), (-3, 0), (-2, 2) };
        foreach (var (dx, dz) in offsets)
        {
            w.Set(cx + dx, y, cz + dz, "rune_stone");
        }

        return new Vector3i(cx, y, cz + 3);
    }

    private static void PlayTones(NpcLifeWorld w, IEnumerable<int> tones)
    {
        foreach (int tone in tones)
        {
            Note(w, tone);
        }
    }

    [Fact]
    public void ChapterTwo_TheRingPlaysTheEchoGame_AndItsSolutionGivesTheSpindleAndTheFirstDisc()
    {
        using var w = World("snd_ch2");
        Assert.StartsWith("Chapter 2", w.Server.SoundersAdminForTest(w.Player, "chapter 2"), StringComparison.Ordinal);
        Use(w, BuildRing(w));
        Assert.Equal(SoundersIds.PuzzleShrineEcho, w.Server.RunningMelodyPuzzle(w.Player));

        // Repeat, three rounds of C E G E C': 2, then 4, then all 5 tones.
        int[] tune = { 0, 2, 4, 2, 7 };
        foreach (int count in new[] { 2, 4, 5 })
        {
            w.Server.TickMelodyPuzzlesForTest(2.0);
            PlayTones(w, tune.Take(count));
        }

        Assert.Null(w.Server.RunningMelodyPuzzle(w.Player));
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SoundersIds.SongSpindle));
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SoundersIds.DiscBeginning));
        Assert.Contains(SvShrineReward, w.Player.State.Milestones);
    }

    private const string SvShrineReward = "snd:shrine_reward";

    [Fact]
    public void ChapterSix_TheRingPlaysTheSongOfReturn_AndItsSolutionLearnsIt()
    {
        using var w = World("snd_ch6");
        w.Server.SoundersAdminForTest(w.Player, "chapter 6");
        Use(w, BuildRing(w));
        Assert.Equal(SoundersIds.PuzzleSongOfReturn, w.Server.RunningMelodyPuzzle(w.Player));

        PlayTones(w, SounderAlphabet.TonesOnly(SounderAlphabet.Encode(SoundersIds.SongOfReturnWord)));

        Assert.Contains(SoundersIds.MilestoneSongLearned, w.Player.State.Milestones);
    }

    /// <summary>#3426: the Song of Return's bar carries ORNEMIS and the two notes of every letter, so the bar can spell it;
    /// the letter the player is at follows the right notes played.</summary>
    [Fact]
    public void TheSongOfReturn_SendsOrnemisAndEveryLettersTwoNotes_ToTheMelodyBar()
    {
        using var w = World("snd_ch6_spell");
        w.Server.SoundersAdminForTest(w.Player, "chapter 6");
        Use(w, BuildRing(w));

        var open = w.Transport.To<MelodyPuzzleState>(w.Player).Last();
        Assert.Equal(SoundersIds.PuzzleSongOfReturn, open.PuzzleId);
        Assert.Equal(SoundersIds.SongOfReturnWord, open.Word);
        Assert.Equal(SounderAlphabet.TonesOnly(SounderAlphabet.Encode("ORNEMIS")), open.WordTones);
        Assert.Equal(open.Expected, open.WordTones.Length);

        PlayTones(w, open.WordTones.Take(2)); // the O
        var after = w.Transport.To<MelodyPuzzleState>(w.Player).Last();
        Assert.Equal(1, MelodySpelling.CurrentLetter(after.Entered, SoundersIds.SongOfReturnWord.Length)); // now at the R
    }

    [Fact]
    public void OutsideTheChapters_TheRingJustSingsItsRuneWord()
    {
        using var w = World("snd_no_chapter");
        Use(w, BuildRing(w));
        Assert.Null(w.Server.RunningMelodyPuzzle(w.Player));
        Assert.Contains(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "rune_stone");
    }

    [Fact]
    public void ChapterFive_AnEchoAppearsAtRuneStones_ForAPlayerWithTheHearingDisc_AndItsDialogEndCounts()
    {
        using var w = World("snd_ch5");
        w.Server.SoundersAdminForTest(w.Player, "chapter 5");
        BuildRing(w);
        w.Server.TickSoundersStoryForTest();
        Assert.Null(w.Server.SoundersEchoIdForTest(w.Player.State.PlayerId)); // no hearing disc yet

        w.Server.SoundersAdminForTest(w.Player, "items");
        Wear(w, SoundersIds.ObsidianChest);
        Wear(w, SoundersIds.DiscHearing);
        w.Server.TickSoundersStoryForTest();
        Assert.Equal("elora", w.Server.SoundersEchoIdForTest(w.Player.State.PlayerId));
        Assert.True(w.Server.SoundersEchoNpcForTest(w.Player.State.PlayerId) >= 0);

        var questioned = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.EchoQuestioned, questioned);
        w.Player.State.Milestones.Add(SoundersIds.EchoMilestone("elora")); // the dialog's last answer writes it
        w.Server.RaiseGameEventForTest(w.Player, GameEventKeys.DialogDone, "snd_echo_elora");
        Assert.Equal(new[] { "elora" }, questioned.Args);
    }

    [Fact]
    public void TheNextEcho_NeedsAPlaceFarFromTheOthers_AndTiniAnotherWorld()
    {
        var milestones = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal("elora", SvNextEcho(milestones));
        milestones.Add(SoundersIds.EchoMilestone("elora"));
        milestones.Add(SoundersIds.EchoMilestone("kamo"));
        Assert.Equal("feya", SvNextEcho(milestones));
        milestones.Add(SoundersIds.EchoMilestone("feya"));
        Assert.Equal("tini", SvNextEcho(milestones));
        milestones.Add(SoundersIds.EchoMilestone("tini"));
        Assert.Null(SvNextEcho(milestones));
    }

    private static string? SvNextEcho(HashSet<string> milestones) => BlocksBeyondTheStars.GameServer.GameServer.NextSoundersEcho(milestones);

    [Fact]
    public void TheSingDuel_Won_MakesTheStorySoundiATravellingCompanion_Once()
    {
        using var w = World("snd_duel");
        w.Server.SoundersAdminForTest(w.Player, "chapter 8");
        Assert.True(w.Server.StartMelodyPuzzle(w.Player, new MelodyPuzzleSpec
        {
            Id = SoundersIds.PuzzleSingDuel,
            Mode = MelodyPuzzleMode.Repeat,
            Target = new[] { 3 },
            Rounds = 1,
        }));
        Note(w, 3);

        var soundi = Assert.Single(w.Player.State.TamedCreatures, t => t.Kind == TamedCreature.SoundiKind);
        Assert.True(soundi.Roams);
        Assert.Contains("snd:soundi", w.Player.State.Milestones);
        Assert.Equal("You already have a Soundi.", w.Server.SoundersAdminForTest(w.Player, "soundi"));
    }

    [Fact]
    public void TheAdminVerb_StartsThePack_GivesTheItems_AndLearnsTheSong()
    {
        using var w = World("snd_admin");
        Assert.Contains("active=False", w.Server.SoundersAdminForTest(w.Player, "status"), StringComparison.Ordinal);
        w.Server.SoundersAdminForTest(w.Player, "start");
        Assert.Contains("active=True", w.Server.SoundersAdminForTest(w.Player, "status"), StringComparison.Ordinal);

        w.Server.SoundersAdminForTest(w.Player, "items");
        Assert.Contains(SoundersIds.BlueprintResonance, w.Player.State.UnlockedBlueprints);
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SoundersIds.ResonanceBlaster));

        w.Server.SoundersAdminForTest(w.Player, "song");
        Assert.Contains(SoundersIds.MilestoneSongLearned, w.Player.State.Milestones);
        Assert.StartsWith("Usage", w.Server.SoundersAdminForTest(w.Player, "dance"), StringComparison.Ordinal);
    }

    private static void Wear(NpcLifeWorld w, string item)
    {
        var inv = w.Player.State.Inventory;
        if (inv.CountOf(item) == 0)
        {
            inv.Add(item, 1, 1);
        }

        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                w.Server.EquipItemForTest(w.Player.State.PlayerId, i);
                return;
            }
        }
    }
}
