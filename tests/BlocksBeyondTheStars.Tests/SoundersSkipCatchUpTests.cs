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
/// #3425 — a skip never strands a player in the Sounders' Song. A kid skipped chapters 1 and 2 and stood at "decode the first
/// Song Disc" without disc 1 (chapter 2's reward comes only from the shrine's echo game). Now a skipped step or chapter hands
/// over, once, what playing it would have left with the player — the spindle and disc 1, the decoded discs (their letters
/// fill the note table), the blueprints, the crafted pieces, the learned song, Roll's and the Bridge Song's milestones — while
/// a player who played it gets nothing twice and "Skip all" hands out nothing. A disc that is no story disc, sung while a
/// decode step runs, names the disc the story waits for.
/// </summary>
public sealed class SoundersSkipCatchUpTests
{
    private const string Pack = SoundersIds.PackId;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static void Deliver(NpcLifeWorld w, object message)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(message));

    private static void SkipChapter(NpcLifeWorld w, string chapter)
        => Deliver(w, new StoryPackIntent { PackId = Pack, Action = StoryPackActions.SkipChapter, ChapterId = chapter });

    private static void SkipStep(NpcLifeWorld w, string step)
        => Deliver(w, new StoryPackIntent { PackId = Pack, Action = StoryPackActions.SkipStep, StepId = step });

    private static List<string> CatchUpLines(NpcLifeWorld w)
        => w.Transport.TextsTo(w.Player).Where(t => t.StartsWith("@srv.sounders.catch_up", StringComparison.Ordinal)).ToList();

    private static int Count(NpcLifeWorld w, string item) => w.Player.State.Inventory.CountOf(item);

    /// <summary>A Song Spindle in the world next to the player.</summary>
    private static Vector3i Spindle(NpcLifeWorld w)
    {
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, SoundersIds.SongSpindle);
        return cell;
    }

    /// <summary>Moves one <paramref name="item"/> of the backpack (or a new one) into the hand.</summary>
    private static void Hold(NpcLifeWorld w, string item)
    {
        var inv = w.Player.State.Inventory;
        inv.Remove(item, 1);
        inv.SetSlot(0, new ItemStack(item, 1));
        w.Player.State.SelectedHotbarSlot = 0;
    }

    /// <summary>Chapter 3 runs (1 and 2 skipped by the QA verb), the spindle is placed: "decode the first disc" (s3.2) runs.</summary>
    private static void AtTheFirstDecode(NpcLifeWorld w)
    {
        w.Server.SoundersAdminForTest(w.Player, "chapter 3");
        w.Server.RaiseGameEventForTest(w.Player, GameEventKeys.BuildAny, SoundersIds.SongSpindle); // s3.1: the spindle placed
        Assert.Equal("s3.2", w.Server.ChipStepForTest(w.Player, Pack));
    }

    [Fact]
    public void SkippingChaptersOneAndTwo_HandsOverTheSpindleAndTheFirstDisc_Once_AndTheDiscDecodes()
    {
        using var w = World("snd_skip_12");
        w.Server.SoundersAdminForTest(w.Player, "start");
        SkipChapter(w, "s1");
        SkipChapter(w, "s2");
        Assert.Contains(StoryPackKeys.ChapterSkipped(Pack, "s2"), w.Player.State.Milestones);

        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(1, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning));
        Assert.Contains(SvGameServer.SoundersShrineRewardMilestone, w.Player.State.Milestones);
        var line = Assert.Single(CatchUpLines(w));
        Assert.Equal("@srv.sounders.catch_up_items:Song Spindle, Song Disc: The Beginning", line);

        w.Server.TickStoryPacksForTest(w.Player, 3); // once: nothing twice, nothing said twice
        Assert.Equal(1, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning));
        Assert.Single(CatchUpLines(w));

        // The rest of chapter 3 plays as for everyone: place the spindle, decode disc 1 at it.
        w.Server.RaiseGameEventForTest(w.Player, GameEventKeys.BuildAny, SoundersIds.SongSpindle);
        Assert.Equal("s3.2", w.Server.ChipStepForTest(w.Player, Pack));
        var cell = Spindle(w);
        Hold(w, SoundersIds.DiscBeginning);
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Single(w.Transport.To<StoryMinigameStart>(w.Player), m => m.Context == SoundersIds.DiscBeginning);
        w.Server.StoryMinigameResultForTest(w.Player, new StoryMinigameResultIntent
        {
            Game = SongDiscs.NoteKeyGame,
            Context = SoundersIds.DiscBeginning,
            Completed = true,
            Answers = new[] { "MIRA", "SOA", "ESA" },
        });
        Assert.Contains(StoryPackKeys.StepDone(Pack, "s3.2"), w.Player.State.Milestones);
    }

    [Fact]
    public void AStuckSaveLikeTheReport_GetsTheFirstDisc_ButNoSpindleWhenItCarriesSome()
    {
        using var w = World("snd_skip_report");
        AtTheFirstDecode(w);
        w.Player.State.Inventory.Add(SoundersIds.SongSpindle, 3, 64);
        w.Player.State.Inventory.Add(SoundersIds.DiscLight, 2, 1);

        w.Server.TickStoryPacksForTest(w.Player);

        Assert.Equal(3, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning));
        Assert.Equal("@srv.sounders.catch_up_items:Song Disc: The Beginning", Assert.Single(CatchUpLines(w)));
    }

    [Fact]
    public void APlayerWhoAnsweredTheShrine_GetsNothingTwice()
    {
        using var w = World("snd_skip_none");
        w.Server.SoundersAdminForTest(w.Player, "chapter 2");
        w.Server.StartMelodyPuzzle(w.Player, new MelodyPuzzleSpec
        {
            Id = SoundersIds.PuzzleShrineEcho,
            Mode = MelodyPuzzleMode.Repeat,
            Target = new[] { 0 },
            Rounds = 1,
        });
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MelodyNoteIntent { Tone = 0, PuzzleId = SoundersIds.PuzzleShrineEcho }));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning)); // the shrine's own reward

        w.Server.TickStoryPacksForTest(w.Player, 3);

        Assert.Contains(StoryPackKeys.ChapterDone(Pack, "s2"), w.Player.State.Milestones);
        Assert.Equal(1, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning));
        Assert.Empty(CatchUpLines(w));
    }

    [Fact]
    public void ASkippedChapterThree_CountsItsDiscsAsDecoded_SoTheNoteTableFillsIn()
    {
        using var w = World("snd_skip_3");
        w.Server.SoundersAdminForTest(w.Player, "chapter 4");
        w.Server.TickStoryPacksForTest(w.Player);

        var p = w.Player.State;
        foreach (string disc in new[] { SoundersIds.DiscBeginning, SoundersIds.DiscBuilders, SoundersIds.DiscHearing })
        {
            Assert.Contains(SoundersIds.DiscMilestone(disc), p.Milestones);
        }

        string expected = SongDiscs.LettersOf(SongDiscs.WordsOf(SoundersIds.DiscBeginning)
            .Concat(SongDiscs.WordsOf(SoundersIds.DiscBuilders)).Concat(SongDiscs.WordsOf(SoundersIds.DiscHearing)));
        Assert.Equal(expected, SvGameServer.KnownSounderLetters(p));
        Assert.Equal(1, Count(w, SoundersIds.DiscHearing)); // the chest socket's disc chapters 4 and 5 need
        Assert.Equal(1, Count(w, SoundersIds.DiscBuilders));
        Assert.DoesNotContain(SoundersIds.BlueprintResonance, p.UnlockedBlueprints); // chapter 4 is still to play
    }

    [Fact]
    public void LaterSkips_HandOverBlueprintsPiecesAndTheSong_RollAndTheBridge_ButNeverTheSoundi()
    {
        using var w = World("snd_skip_late");
        w.Server.SoundersAdminForTest(w.Player, "chapter 8");
        w.Server.TickStoryPacksForTest(w.Player);

        var p = w.Player.State;
        Assert.Contains(SoundersIds.BlueprintResonance, p.UnlockedBlueprints);
        Assert.Contains(SoundersIds.BlueprintObsidian, p.UnlockedBlueprints);
        Assert.Equal(1, Count(w, SoundersIds.ResonanceBlaster));
        Assert.Equal(1, Count(w, SoundersIds.ObsidianChest));
        Assert.Equal(1, Count(w, SoundersIds.DiscGreatSong));
        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscGreatSong), p.Milestones);
        Assert.Contains(SoundersIds.MilestoneSongLearned, p.Milestones);
        Assert.DoesNotContain(SvGameServer.SoundersRollConvincedMilestone, p.Milestones); // s8.1 runs: Roll is still to talk to

        SkipStep(w, "s8.1");
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Contains(SvGameServer.SoundersRollConvincedMilestone, p.Milestones);
        SkipStep(w, "s8.2");
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Contains(SvGameServer.SoundersBridgeDoneMilestone, p.Milestones); // the sing duel can start

        SkipChapter(w, "s8");
        w.Server.TickStoryPacksForTest(w.Player, 2);
        Assert.DoesNotContain(p.TamedCreatures, t => t.Kind == TamedCreature.SoundiKind);
        Assert.DoesNotContain(SvGameServer.SoundersSoundiWonMilestone, p.Milestones);
        Assert.Equal(1, Count(w, SoundersIds.ResonanceBlaster)); // the crafted pieces came once
    }

    [Fact]
    public void ASkippedChamber_HandsTheGreatSongDiscOver_Once_EvenWhileItSitsInTheSpindle()
    {
        using var w = World("snd_skip_chamber");
        w.Server.SoundersAdminForTest(w.Player, "chapter 4");
        foreach (string step in new[] { "s4.2", "s4.3", "s4.4" })
        {
            SkipStep(w, step);
        }

        Assert.True(w.Player.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s4.5"))); // decode it: still to play
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(1, Count(w, SoundersIds.DiscGreatSong));
        Assert.DoesNotContain(SoundersIds.DiscMilestone(SoundersIds.DiscGreatSong), w.Player.State.Milestones);

        var cell = Spindle(w);
        Hold(w, SoundersIds.DiscGreatSong);
        w.Server.UseSongSpindle(w.Player, cell); // in the spindle now: not "at hand", yet it never comes twice
        Assert.Equal(SoundersIds.DiscGreatSong, w.Server.SpindleDiscAt(cell));
        w.Server.TickStoryPacksForTest(w.Player, 3);
        Assert.Equal(0, Count(w, SoundersIds.DiscGreatSong));
    }

    [Fact]
    public void SkippingTheWholeStory_HandsOutNothing()
    {
        using var w = World("snd_skip_all");
        w.Server.SoundersAdminForTest(w.Player, "start");
        Deliver(w, new StoryPackIntent { PackId = Pack, Action = StoryPackActions.SkipPack });
        w.Server.TickStoryPacksForTest(w.Player, 2);

        Assert.Equal(0, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(0, Count(w, SoundersIds.DiscBeginning));
        Assert.DoesNotContain(SoundersIds.MilestoneSongLearned, w.Player.State.Milestones);
        Assert.Empty(CatchUpLines(w));
    }

    [Fact]
    public void ANonStoryDisc_DuringTheFirstDecode_SaysWhichDiscTheStoryWants()
    {
        using var w = World("snd_wrong_disc");
        AtTheFirstDecode(w);
        w.Server.TickStoryPacksForTest(w.Player); // the catch-up hands disc 1 over
        var cell = Spindle(w);
        Hold(w, SoundersIds.DiscLight);

        w.Server.UseSongSpindle(w.Player, cell);

        Assert.Contains("@srv.sounders.wrong_disc:Song Disc: The Beginning", w.Transport.TextsTo(w.Player));
        Assert.Empty(w.Transport.To<StoryMinigameStart>(w.Player)); // a tune, no note-key game
    }

    [Fact]
    public void ANonStoryDisc_WhileTheNextDiscsAreStillToFind_SaysWhereToLook_AndOutsideTheStoryNothing()
    {
        using var w = World("snd_wrong_disc_find");
        AtTheFirstDecode(w);
        SkipStep(w, "s3.2"); // the read step after it never blocks: "decode two more discs" (s3.4) runs
        Assert.True(w.Player.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s3.4")));
        var cell = Spindle(w);
        Hold(w, SoundersIds.DiscCalm);
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Contains("@srv.sounders.wrong_disc_find:Song Disc: The Builders", w.Transport.TextsTo(w.Player));

        using var plain = World("snd_wrong_disc_plain");
        var other = Spindle(plain);
        Hold(plain, SoundersIds.DiscCalm);
        plain.Server.UseSongSpindle(plain.Player, other);
        Assert.DoesNotContain(plain.Transport.TextsTo(plain.Player), t => t.StartsWith("@srv.sounders.wrong_disc", StringComparison.Ordinal));
    }
}
