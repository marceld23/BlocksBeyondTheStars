// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Sounders' Song after the Bridge Song (#3910): chapters 9 to 11 take the player to the other side. A player who
/// finished chapter 8 before gets chapter 9 by itself and the Journal reads "running", not "done and running"; every new
/// step completes on its event; the steps bound to Soaniri have stand-ins; a skipped step hands over what playing it would
/// have left (the blueprint, one crossing disc, the great crack) and a lost crossing disc comes again; Elora and Kamo
/// stand at the gate for their dialog steps; the choir gathers for the Song from Over There, whose solution closes the
/// great crack and finishes the story.
/// </summary>
public sealed class SoundersOtherSideStoryTests
{
    private const string Pack = SoundersIds.PackId;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 14);

    private static void Deliver(NpcLifeWorld w, object message)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(message));

    private static void SkipChapter(NpcLifeWorld w, string chapter)
        => Deliver(w, new StoryPackIntent { PackId = Pack, Action = StoryPackActions.SkipChapter, ChapterId = chapter });

    private static string? Chip(NpcLifeWorld w) => w.Server.ChipStepForTest(w.Player, Pack);

    private static int Count(NpcLifeWorld w, string item) => w.Player.State.Inventory.CountOf(item);

    private static NetStoryPack Journal(NpcLifeWorld w) => w.Server.StoryPackStatesForTest(w.Player).Packs.Single(p => p.Id == Pack);

    /// <summary>A save from before the new chapters: the pack started, the given chapters played, the pack stamped done
    /// (it was done once chapter 8 was through).</summary>
    private static void PlayedThrough(NpcLifeWorld w, int lastChapter)
    {
        var m = w.Player.State.Milestones;
        m.Add(StoryPackKeys.Started(Pack));
        for (int i = 1; i <= lastChapter; i++)
        {
            m.Add(StoryPackKeys.ChapterDone(Pack, "s" + i));
        }

        m.Add(StoryPackKeys.Done(Pack));
        foreach (string played in new[]
                 {
                     SvGameServer.SoundersShrineRewardMilestone, SoundersIds.MilestoneSongLearned, SvGameServer.SoundersRollConvincedMilestone,
                     SvGameServer.SoundersBridgeDoneMilestone, SvGameServer.SoundersSoundiWonMilestone,
                 })
        {
            m.Add(played); // what playing the first eight chapters left behind
        }

        if (lastChapter >= 9)
        {
            m.Add(SoundersIds.MilestoneThreshold);
        }

        w.Server.TickStoryPacksForTest(w.Player);
    }

    /// <summary>Three rune stones the player placed, beside them: a rune gate. Returns its middle stone.</summary>
    private static Vector3i BuildGate(NpcLifeWorld w)
    {
        int x = NpcLifeWorld.Cx + 4, z = NpcLifeWorld.Cz + 3, y = w.Feet;
        w.Set(x - 2, y, z, "rune_stone");
        w.Set(x, y, z, "rune_stone");
        w.Set(x + 2, y, z, "rune_stone");
        w.Player.State.Position = new Vector3f(x + 0.5f, y, z - 2.5f);
        return new Vector3i(x, y, z);
    }

    /// <summary>The obsidian chest worn, the crossing disc in its socket.</summary>
    private static void Dress(NpcLifeWorld w)
    {
        w.Player.State.Equipment.SetSlot((int)EquipSlot.Chest, new ItemStack(SoundersIds.ObsidianChest, 1));
        w.Player.State.Equipment.SetSlot((int)EquipSlot.Disc, new ItemStack(SoundersIds.DiscCrossing, 1));
    }

    private static void CrossAt(NpcLifeWorld w, Vector3i stone)
    {
        Dress(w);
        w.Server.ResetRuneCooldownForTest(w.Player);
        w.Server.UseBlockForTest(w.Player, stone);
        Assert.True(w.Player.OtherSide);
    }

    /// <summary>Talks a dialog through with the first reply of every node, standing next to the NPC.</summary>
    private static void TalkThrough(NpcLifeWorld w, int npc, int replies)
    {
        var at = w.Server.NetNpcForTest(npc);
        w.Player.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);
        Deliver(w, new TalkToNpcIntent { NpcId = npc });
        for (int i = 0; i < replies; i++)
        {
            Deliver(w, new NpcDialogChoiceIntent { NpcId = npc, ChoiceIndex = 0 });
        }
    }

    // ---- the pack ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheGluesChapterList_IsThePacks_AndThePartCardCountsEleven()
    {
        var pack = Content.Value.Stories[Pack];

        Assert.Equal(pack.Chapters.Select(c => c.Id), SvGameServer.SoundersChapterIdsForTest);
        Assert.Equal(11, pack.Chapters.Count);
        var nine = pack.Chapters.Single(c => c.Id == "s9");
        Assert.Equal((9, 11), CutsceneArgs.PlaceOf(pack, nine));
        Assert.Equal("snd.part", nine.OpenCutscene);
        Assert.Equal("snd.epilogue2", pack.Chapters.Last().CloseCutscene);
        foreach (string id in new[] { "snd.threshold", "snd.other_song", "snd.epilogue2" })
        {
            Assert.NotNull(pack.FindCutscene(id));
        }

        foreach (string key in new[] { "snd_eldest_threshold", "snd_elora_over", "snd_kamo_over" })
        {
            Assert.Contains(pack.Dialogs, d => d.Key == key);
        }

        Assert.Equal("story:sounders", pack.Graduation!.Counter);
        Assert.Contains(Content.Value.Achievements, a => a.Key == "sounders_song" && a.Counter == pack.Graduation.Counter);
    }

    // ---- old saves, the Journal ------------------------------------------------------------------------------------

    [Fact]
    public void APlayerWhoFinishedChapterEight_GetsChapterNineByItself_AndTheJournalReadsRunning()
    {
        using var w = World("snd9_old_save");
        PlayedThrough(w, 8);

        Assert.Equal("s9.1a", Chip(w)); // not in Ornemis: a rune stone stands in for the Eldest
        var journal = Journal(w);
        Assert.False(journal.Done);
        Assert.False(journal.Badge);
        Assert.True(journal.MandatoryRunning);
        Assert.Equal(StoryChapterStates.Active, journal.Chapters.Single(c => c.Id == "s9").State);
        Assert.Contains(StoryPackKeys.Done(Pack), w.Player.State.Milestones); // the stamp itself stays
        Assert.True(w.Server.EvaluateStoryConditionForTest("pack:sounders:done", w.Player));

        foreach (string chapter in new[] { "s9", "s10", "s11" })
        {
            SkipChapter(w, chapter);
        }

        journal = Journal(w);
        Assert.True(journal.Done); // through again — skipped, so without the badge
        Assert.False(journal.Badge);
        Assert.False(journal.MandatoryRunning);
    }

    [Fact]
    public void APlayerWhoSkippedTheWholeStory_KeepsTheNewChaptersSkipped_AndGetsNothing()
    {
        using var w = World("snd9_skipped_all");
        var m = w.Player.State.Milestones;
        m.Add(StoryPackKeys.Started(Pack));
        for (int i = 1; i <= 8; i++)
        {
            m.Add(StoryPackKeys.ChapterSkipped(Pack, "s" + i)); // "Skip all", before chapters 9–11 existed
        }

        m.Add(StoryPackKeys.Done(Pack));
        w.Server.TickStoryPacksForTest(w.Player, 2);

        foreach (string chapter in new[] { "s9", "s10", "s11" })
        {
            Assert.Contains(StoryPackKeys.ChapterSkipped(Pack, chapter), m);
        }

        Assert.Null(Chip(w));
        Assert.Empty(w.Player.State.StepProgress.Keys.Where(k => k.Contains(Pack, StringComparison.Ordinal)));
        Assert.Equal(0, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(0, Count(w, SoundersIds.DiscCrossing));
        Assert.DoesNotContain(w.Transport.TextsTo(w.Player), t => t.StartsWith("@srv.sounders.", StringComparison.Ordinal));
        Assert.Empty(w.Transport.PackLines(w.Player, Pack));
        var journal = Journal(w);
        Assert.True(journal.Done);
        Assert.False(journal.MandatoryRunning);
    }

    // ---- chapter 9 -------------------------------------------------------------------------------------------------

    [Fact]
    public void ChapterNine_RunsFromTheThinStones_ThroughTheGate_ToEloraOnTheOtherSide()
    {
        using var w = World("snd9_play");
        PlayedThrough(w, 8);
        var p = w.Player;
        Assert.False(w.Server.EvaluateStoryConditionForTest("world:in_ornemis", p));
        Assert.False(w.Server.EvaluateStoryConditionForTest("milestone:snd:threshold", p)); // the blueprint is still hidden

        w.Server.RaiseGameEventForTest(p, GameEventKeys.RunePlayed, "placed"); // VEGA translates the stone
        Assert.Contains(SoundersIds.MilestoneThreshold, p.State.Milestones);
        Assert.Equal("s9.2", Chip(w));
        w.Server.RaiseGameEventForTest(p, GameEventKeys.ResearchAny, SoundersIds.BlueprintCrossing);
        Assert.Equal("s9.3", Chip(w));
        w.Server.RaiseGameEventForTest(p, GameEventKeys.CraftAny, SoundersIds.DiscCrossing);
        Assert.Equal("s9.4", Chip(w));
        w.Server.RaiseGameEventForTest(p, GameEventKeys.DiscInserted, SoundersIds.DiscCrossing);
        Assert.Equal("s9.5", Chip(w));

        CrossAt(w, BuildGate(w)); // the real crossing, at three placed stones
        Assert.Equal("s9.6", Chip(w));
        Assert.Contains(p.State.Milestones, k => k == StoryPackKeys.CutsceneSent(Pack, "snd.threshold") || k == StoryPackKeys.CutsceneSeen(Pack, "snd.threshold"));

        w.Server.TickSoundersStoryForTest();
        Assert.Equal(SoundersIds.EloraOver, w.Server.SoundersOverEchoForTest(p.State.PlayerId));
        int elora = w.Server.SoundersOverEchoNpcForTest(p.State.PlayerId);
        Assert.Contains(elora, w.Server.OtherSideNpcIdsForTest()); // only players over there see her
        Assert.Equal("Elora", w.Server.NetNpcForTest(elora).Name);

        TalkThrough(w, elora, replies: 2); // the pack's dialog answers for her character
        Assert.Contains(StoryPackKeys.ChapterDone(Pack, "s9"), p.State.Milestones);
        Assert.Equal("s10.1", Chip(w));
    }

    [Fact]
    public void TheEldest_HasOneTalkPerPartOfTheStory_AndHerSecondOneShowsTheBlueprint()
    {
        using var before = World("snd9_eldest_before");
        before.Server.SoundersAdminForTest(before.Player, "chapter 7");
        var heardBefore = new RecordingGameEventListener();
        before.Server.AddGameEventListener(GameEventKeys.DialogDone, heardBefore);
        var spot = before.Player.State.Position;
        int eldest = before.Server.SpawnSounderNpcForTest(SoundersIds.Sounder, new Vector3f(spot.X + 1.2f, spot.Y, spot.Z), "Eldest", "eldest");
        TalkThrough(before, eldest, replies: 2);
        Assert.Equal(new[] { "snd_eldest" }, heardBefore.Events.Select(e => e.Arg));
        Assert.DoesNotContain(SoundersIds.MilestoneThreshold, before.Player.State.Milestones);

        using var after = World("snd9_eldest_after");
        PlayedThrough(after, 8);
        var heardAfter = new RecordingGameEventListener();
        after.Server.AddGameEventListener(GameEventKeys.DialogDone, heardAfter);
        spot = after.Player.State.Position;
        eldest = after.Server.SpawnSounderNpcForTest(SoundersIds.Sounder, new Vector3f(spot.X + 1.2f, spot.Y, spot.Z), "Eldest", "eldest");
        TalkThrough(after, eldest, replies: 2);
        Assert.Equal(new[] { "snd_eldest_threshold" }, heardAfter.Events.Select(e => e.Arg)); // never the plea of chapter 7
        Assert.Contains(SoundersIds.MilestoneThreshold, after.Player.State.Milestones);

        // The stand-in (a rune stone) was running: who heard it from the Eldest after all is through it.
        Assert.Equal("s9.1a", Chip(after));
        after.Server.TickSoundersStoryForTest();
        Assert.Equal("s9.2", Chip(after));
    }

    // ---- skips and needs -------------------------------------------------------------------------------------------

    [Fact]
    public void SkippingChapterNine_ShowsAndUnlocksTheBlueprint_AndHandsOverOneCrossingDisc()
    {
        using var w = World("snd9_skip");
        PlayedThrough(w, 8);
        SkipChapter(w, "s9");
        w.Server.TickStoryPacksForTest(w.Player);

        Assert.Contains(SoundersIds.MilestoneThreshold, w.Player.State.Milestones);
        Assert.Contains(SoundersIds.BlueprintCrossing, w.Player.State.UnlockedBlueprints);
        Assert.Equal(1, Count(w, SoundersIds.DiscCrossing));
        Assert.Single(w.Transport.TextsTo(w.Player), t => t.StartsWith("@srv.sounders.catch_up_items:", StringComparison.Ordinal));

        w.Server.TickStoryPacksForTest(w.Player, 3); // once
        Assert.Equal(1, Count(w, SoundersIds.DiscCrossing));
        Assert.DoesNotContain(w.Transport.TextsTo(w.Player), t => t.StartsWith("@srv.sounders.need_item:", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutACrossingDisc_VegaPacksOne_WhileAStepNeedsIt()
    {
        using var w = World("snd10_need");
        PlayedThrough(w, 9); // chapter 10 runs; the disc of chapter 9 lies in a crate somewhere
        w.Server.TickStoryPacksForTest(w.Player);

        Assert.Equal(1, Count(w, SoundersIds.DiscCrossing));
        Assert.Contains("@srv.sounders.need_item:Crossing disc", w.Transport.TextsTo(w.Player));

        w.Server.TickStoryPacksForTest(w.Player, 3);
        Assert.Equal(1, Count(w, SoundersIds.DiscCrossing)); // a disc at hand: no second one
        Dress(w);
        w.Player.State.Inventory.Remove(SoundersIds.DiscCrossing, 1);
        w.Server.TickStoryPacksForTest(w.Player, 2);
        Assert.Equal(0, Count(w, SoundersIds.DiscCrossing)); // worn in the socket counts as at hand
    }

    [Fact]
    public void SkippingTheLastChapter_ClosesTheGreatCrackForTheStory_AndHandsOutNothing()
    {
        using var w = World("snd11_skip");
        PlayedThrough(w, 10);
        Dress(w); // the disc is at hand: the needs rule has nothing to pack
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.DoesNotContain(SoundersIds.MilestoneGreatCrack, w.Player.State.Milestones);
        int lines = w.Transport.TextsTo(w.Player).Count;

        SkipChapter(w, "s11");
        w.Server.TickStoryPacksForTest(w.Player, 2);

        Assert.Contains(SoundersIds.MilestoneGreatCrack, w.Player.State.Milestones);
        Assert.DoesNotContain(w.Transport.TextsTo(w.Player).Skip(lines), t => t.StartsWith("@srv.sounders.catch_up", StringComparison.Ordinal));
        Assert.True(Journal(w).Done);
    }

    // ---- chapter 10 ------------------------------------------------------------------------------------------------

    [Fact]
    public void ChapterTen_KamoStandsAtTheGate_ThenABridgeOfSound_ThenThreeDifferentGates()
    {
        using var w = World("snd10_play");
        PlayedThrough(w, 9);
        var p = w.Player;
        Assert.Equal("s10.1", Chip(w));

        w.Server.TickSoundersStoryForTest();
        Assert.Null(w.Server.SoundersOverEchoForTest(p.State.PlayerId)); // Kamo stands only on the other side

        CrossAt(w, BuildGate(w));
        w.Server.TickSoundersStoryForTest();
        Assert.Equal(SoundersIds.KamoOver, w.Server.SoundersOverEchoForTest(p.State.PlayerId));
        int kamo = w.Server.SoundersOverEchoNpcForTest(p.State.PlayerId);
        TalkThrough(w, kamo, replies: 3);
        Assert.Equal("s10.2", Chip(w));

        w.Server.RaiseGameEventForTest(p, GameEventKeys.SoundShaped, "3", 11);
        Assert.Equal("s10.2", Chip(w));
        w.Server.RaiseGameEventForTest(p, GameEventKeys.SoundShaped, "5");
        Assert.Equal("s10.3", Chip(w)); // twelve sound blocks: a first bridge

        var here = p.State.Position;
        Assert.True(w.Server.MendCrackForTest(p, here));
        Assert.False(w.Server.MendCrackForTest(p, here)); // the same gate does not count twice
        Assert.True(w.Server.MendCrackForTest(p, new Vector3f(here.X + 40f, here.Y, here.Z)));
        Assert.Equal("s10.3", Chip(w));
        Assert.True(w.Server.MendCrackForTest(p, new Vector3f(here.X, here.Y, here.Z + 40f)));

        Assert.Contains(StoryPackKeys.ChapterDone(Pack, "s10"), p.State.Milestones);
        Assert.Equal("s11.1a", Chip(w)); // "cross at a gate" — but the player is over there already …
        w.Server.TickSoundersStoryForTest();
        Assert.Equal("s11.2", Chip(w)); // … so nobody has to go back and cross again
    }

    [Fact]
    public void AFigureOfTheOtherSide_LeavesWithItsPlayer()
    {
        using var w = World("snd10_leave");
        PlayedThrough(w, 9);
        var stone = BuildGate(w);
        CrossAt(w, stone);
        w.Server.TickSoundersStoryForTest();
        int kamo = w.Server.SoundersOverEchoNpcForTest(w.Player.State.PlayerId);
        Assert.Contains(kamo, w.Server.OtherSideNpcIdsForTest());

        w.Server.ForceReturnForTest(w.Player);
        w.Server.TickSoundersStoryForTest();

        Assert.Null(w.Server.SoundersOverEchoForTest(w.Player.State.PlayerId));
        Assert.DoesNotContain(kamo, w.Server.OtherSideNpcIdsForTest());
    }

    /// <summary>The per-second pass that lets a figure go only looks at joined players the story reaches — so a figure
    /// stood at its gate for good when its player quit, watched, flew off or switched the pack off.</summary>
    [Fact]
    public void AFigureOfTheOtherSide_DoesNotStayAtTheGate_WhenItsPlayerQuitsOrIsNoLongerReached()
    {
        using var w = World("snd10_quit");
        PlayedThrough(w, 9);
        var keeper = w.Server.AddLocalPlayer("Keeper"); // somebody else keeps the world resident
        keeper.State.AboardShip = false;
        keeper.State.Position = new Vector3f(NpcLifeWorld.Cx - 8.5f, w.Feet, NpcLifeWorld.Cz - 8.5f);
        string id = w.Player.State.PlayerId;
        CrossAt(w, BuildGate(w));
        w.Server.TickSoundersStoryForTest();
        int kamo = w.Server.SoundersOverEchoNpcForTest(id);
        Assert.Contains(kamo, w.Server.OtherSideNpcIdsForTest());

        // The pass does not reach the player any more (an observer): the next pass lets the figure go.
        w.Player.Spectating = true;
        w.Server.TickSoundersStoryForTest();
        Assert.Null(w.Server.SoundersOverEchoForTest(id));
        Assert.DoesNotContain(kamo, w.Server.OtherSideNpcIdsForTest());

        w.Player.Spectating = false;
        w.Server.AdvanceUptimeForTest(3.0); // past the pause between two looks for a gate
        w.Server.TickSoundersStoryForTest();
        kamo = w.Server.SoundersOverEchoNpcForTest(id);
        Assert.Contains(kamo, w.Server.OtherSideNpcIdsForTest()); // he is back for the player

        // The player quits: the leave step lets the figure go at once — no pass will ever look at them again.
        w.Server.OtherSideOnLeaveForTest(w.Player);
        Assert.Null(w.Server.SoundersOverEchoForTest(id));
        Assert.DoesNotContain(kamo, w.Server.OtherSideNpcIdsForTest());
    }

    // ---- chapter 11 ------------------------------------------------------------------------------------------------

    [Fact]
    public void ChapterEleven_TheChoirGathers_TheSongClosesTheGreatCrack_AndTheStoryIsDone()
    {
        using var w = World("snd11_play");
        PlayedThrough(w, 10);
        var p = w.Player;
        Assert.False(w.Server.EvaluateStoryConditionForTest("world:soaniri_reachable", p));
        Assert.Equal("s11.1a", Chip(w)); // Soaniri is out of reach here: any gate stands in for the Choir Tower

        CrossAt(w, BuildGate(w));
        Assert.Equal("s11.2", Chip(w));
        w.Server.TickSoundersStoryForTest();
        var choir = w.Server.OverChoirNamesForTest();
        Assert.InRange(choir.Count, SvGameServer.OverChoirMin, SvGameServer.OverChoirMax);
        foreach (string name in new[] { "Elora", "Kamo", "Feya", "Tini" })
        {
            Assert.Contains(name, choir);
        }

        Deliver(w, new MelodyNoteIntent { Tone = 0 }); // any tone calls the song
        Assert.Equal(SoundersIds.PuzzleSongFromOverThere, w.Server.RunningMelodyPuzzle(p));
        var open = w.Transport.To<MelodyPuzzleState>(p).Last();
        Assert.Equal(SoundersIds.SongFromOverThereWord, open.Word);
        Assert.Equal(7, open.RoundCount); // one letter a round

        foreach (int[] letter in Letters(SoundersIds.SongFromOverThereWord))
        {
            w.Server.TickMelodyPuzzlesForTest(2.0); // the choir sings the letter
            foreach (int tone in MelodyPuzzle.Counter(letter, 4)) // … and the player answers with the counter-voice
            {
                Deliver(w, new MelodyNoteIntent { Tone = tone });
            }
        }

        Assert.Null(w.Server.RunningMelodyPuzzle(p));
        Assert.Contains(SoundersIds.MilestoneGreatCrack, p.State.Milestones);
        Assert.Contains(w.Server.Metadata.MendedCracks, k => k.EndsWith("|64", StringComparison.Ordinal));
        Assert.True(w.Server.OtherSideQualiIdsForTest().Count >= SvGameServer.GreatCrackQualis); // the Lights gather
        Assert.Contains(StoryPackKeys.ChapterDone(Pack, "s11"), p.State.Milestones);
        var journal = Journal(w);
        Assert.True(journal.Done && journal.Badge);
        Assert.False(journal.MandatoryRunning);
        Assert.Equal(1, p.State.AchievementCounters.GetValueOrDefault("story:sounders"));
        Assert.True(p.OtherSide); // the crossing stays possible for good: nothing takes the player back

        // Afterwards the Echoes at the gates have a new line for this player.
        Assert.True(w.Server.EvaluateStoryConditionForTest("milestone:snd:great_crack", p));
        var folk = Content.Value.Dialogs.Single(d => d.Key == "snd_over_folk");
        Assert.Contains(folk.Nodes[0].Choices, c => c.TextKey == "dlg.snd_over_folk.c_crack" && c.VisibleIf.Contains("milestone:snd:great_crack"));
    }

    /// <summary>The Choir Tower was built with crystal pipes and no rune stones, so by the blocks rule it is no gate. While
    /// a player of chapter 11 is near, three rune stones stand at its foot — real blocks, so every rule that reads the
    /// world (the crossing, the gate list, the client's gate finder) sees a gate there.</summary>
    [Fact]
    public void TheChoirTower_GetsARuneGateAtItsFoot_WhileChapterElevenRuns_AndTheChoirSingsThere()
    {
        using var w = new NpcLifeWorld(Content.Value, "snd11_tower", padHalf: 4, configure: c =>
        {
            c.StartPlanet = SoundersIds.PlanetType;
            c.World.TerrainGeneration = WorldDescription.CurrentTerrainGeneration;
        });
        var tower = w.Server.SounderSitesForTest().Single(s => s.Key == SvGameServer.ChoirTowerSiteKey);
        var foot = new Vector3f(tower.X + 0.5f, tower.FloorY + 1f, tower.Z + 0.5f);
        int RuneStones()
        {
            ushort rune = Content.Value.GetBlock("rune_stone")!.NumericId.Value;
            int count = 0;
            for (int dx = -8; dx <= 8; dx++)
            {
                for (int dz = -8; dz <= 8; dz++)
                {
                    count += w.Server.World.GetBlock(new Vector3i(tower.X + dx, tower.FloorY + 1, tower.Z + dz)).Value == rune ? 1 : 0;
                }
            }

            return count;
        }

        Assert.Equal(0, RuneStones()); // crystal pipes only: no gate by itself
        Assert.False(w.Server.IsGateForTest(foot, out _));

        PlayedThrough(w, 10);
        w.Player.State.Position = new Vector3f(tower.X + 5.5f, tower.FloorY + 1f, tower.Z + 0.5f);
        w.Server.TickSoundersStoryForTest();

        Assert.Equal(3, RuneStones());
        Assert.True(w.Server.IsGateForTest(foot, out _));
        for (int i = 0; i < 30; i++)
        {
            w.Server.TickForTest(0.2); // past the pause between two looks
        }

        w.Server.TickSoundersStoryForTest();
        Assert.Equal(3, RuneStones()); // a gate stands there: nothing is written twice

        var stone = Enumerable.Range(-8, 17).SelectMany(dx => Enumerable.Range(-8, 17).Select(dz => new Vector3i(tower.X + dx, tower.FloorY + 1, tower.Z + dz)))
            .First(c => w.Server.World.GetBlock(c).Value == Content.Value.GetBlock("rune_stone")!.NumericId.Value);
        w.Player.State.Position = new Vector3f(stone.X - 1.5f, stone.Y, stone.Z + 0.5f);
        CrossAt(w, stone); // the real crossing, at the tower
        Assert.Equal("s11.2", Chip(w));

        w.Server.TickSoundersStoryForTest();
        Assert.InRange(w.Server.OverChoirNamesForTest().Count, SvGameServer.OverChoirMin, SvGameServer.OverChoirMax);
        Deliver(w, new MelodyNoteIntent { Tone = 0 });
        Assert.Equal(SoundersIds.PuzzleSongFromOverThere, w.Server.RunningMelodyPuzzle(w.Player)); // the song, not the gate's own crack
    }

    /// <summary>The tower's three stones are written once per world. A look that takes an unloaded chunk for air saw "no
    /// gate" after a restart and raised a second set on the next side; and stones that are gone must not come back as a
    /// second set either — the world remembers that it wrote them.</summary>
    [Fact]
    public void TheChoirTowersGate_IsWrittenOncePerWorld_AlsoAfterARestartWithTheTowerNotLoaded()
    {
        using var w = new NpcLifeWorld(Content.Value, "snd11_tower_once", padHalf: 4, configure: c =>
        {
            c.StartPlanet = SoundersIds.PlanetType;
            c.World.TerrainGeneration = WorldDescription.CurrentTerrainGeneration;
        });
        var tower = w.Server.SounderSitesForTest().Single(s => s.Key == SvGameServer.ChoirTowerSiteKey);
        ushort rune = Content.Value.GetBlock("rune_stone")!.NumericId.Value;
        List<Vector3i> RuneStones() => Enumerable.Range(-8, 17)
            .SelectMany(dx => Enumerable.Range(-8, 17).Select(dz => new Vector3i(tower.X + dx, tower.FloorY + 1, tower.Z + dz)))
            .Where(c => w.Server.World.GetBlock(c).Value == rune).ToList();
        void Look()
        {
            PlayedThrough(w, 10);
            w.Player.State.Position = new Vector3f(tower.X + 5.5f, tower.FloorY + 1f, tower.Z + 0.5f);
            w.Server.AdvanceUptimeForTest(6.0); // past the pause between two looks
            w.Server.TickSoundersStoryForTest();
        }

        Look();
        var first = RuneStones();
        Assert.Equal(3, first.Count);
        Assert.Contains(w.Server.Metadata.StampedFeatures, f => f.EndsWith("|" + SvGameServer.ChoirGateFeature, StringComparison.Ordinal));

        // A restart: nothing around the tower is loaded when the first look comes.
        w.Restart();
        Look();
        Assert.Equal(first, RuneStones());

        // And should the stones ever be gone, the world does not raise a second set.
        foreach (var cell in first)
        {
            w.Server.World.SetBlock(cell, default);
        }

        Look();
        Assert.Empty(RuneStones());
    }

    /// <summary>The tones of each letter of <paramref name="word"/> in the note alphabet.</summary>
    private static List<int[]> Letters(string word)
    {
        var letters = new List<int[]>();
        var letter = new List<int>();
        foreach (int note in SounderAlphabet.Encode(word).Append(SounderAlphabet.Rest))
        {
            if (note != SounderAlphabet.Rest)
            {
                letter.Add(note);
            }
            else if (letter.Count > 0)
            {
                letters.Add(letter.ToArray());
                letter.Clear();
            }
        }

        return letters;
    }

    // ---- the admin verb, the data ------------------------------------------------------------------------------------

    [Fact]
    public void TheAdminVerb_KnowsElevenChapters_AndTheNewParts()
    {
        using var w = World("snd_admin_over");
        Assert.StartsWith("Chapter 11", w.Server.SoundersAdminForTest(w.Player, "chapter 11"), StringComparison.Ordinal);
        Assert.StartsWith("Usage", w.Server.SoundersAdminForTest(w.Player, "chapter 12"), StringComparison.Ordinal);
        Assert.Contains("chapter=s11", w.Server.SoundersAdminForTest(w.Player, "status"), StringComparison.Ordinal);

        Assert.StartsWith("No rune gate here", w.Server.SoundersAdminForTest(w.Player, "crack"), StringComparison.Ordinal);
        BuildGate(w);
        Assert.Equal("The crack of this gate is mended.", w.Server.SoundersAdminForTest(w.Player, "crack"));
        Assert.Equal("This gate carries no crack any more.", w.Server.SoundersAdminForTest(w.Player, "crack"));

        Assert.Contains("you are over there", w.Server.SoundersAdminForTest(w.Player, "cross"), StringComparison.Ordinal);
        Assert.True(w.Player.OtherSide);
        Assert.True(SvGameServer.HasActiveSocketDisc(w.Player.State, SoundersIds.DiscCrossing));

        Assert.StartsWith("Chapter 11's song runs", w.Server.SoundersAdminForTest(w.Player, "choir"), StringComparison.Ordinal);
        Assert.Equal("s11.2", Chip(w));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheHelpLine_NamesTheNewVerbs_AndStaysShort(string language)
    {
        string help = TestLocales.Load(language)["ui.admin.help_sounders"];

        Assert.True(help.Length <= 160, $"{language}: {help.Length} characters");
        Assert.DoesNotContain('<', help);
        foreach (string word in new[] { "chapter 1-11", "cross", "crack", "choir" })
        {
            Assert.Contains(word, help, StringComparison.Ordinal);
        }
    }
}
