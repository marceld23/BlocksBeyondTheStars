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
/// A full backpack never costs a story item (Justus 2026-11-15, v2026.11.18: "I skipped a story step but had no room in my
/// inventory for what I get afterwards, and now I don't have the item"). What a skipped Sounders step hands over goes into the
/// backpack only and waits for room — never the ground behind the player's back, and a disc still to come is not counted as
/// decoded meanwhile (a decoded disc is never handed out again); the spindle and disc 1 of chapter 3 and the hearing disc the
/// chest's socket and the Echoes need come again while those steps run and the player has none at hand; a relic cache tried with a full backpack keeps one disc, not
/// one more per try; a once-only dialog present lands at the player's feet instead of nowhere; and a hand-over aboard goes into
/// the player's own hold, not the ship the server's cursor points at.
/// </summary>
public sealed class StoryItemFullBackpackTests
{
    private const string Pack = SoundersIds.PackId;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static void SkipStep(NpcLifeWorld w, string step)
        => w.Server.HandlePayloadForTest(w.Player.ConnectionId,
            NetCodec.Encode(new StoryPackIntent { PackId = Pack, Action = StoryPackActions.SkipStep, StepId = step }));

    private static int Count(NpcLifeWorld w, string item) => w.Player.State.Inventory.CountOf(item);

    /// <summary>The server lines to the player starting with <paramref name="prefix"/>, from the <paramref name="from"/>-th on.</summary>
    private static List<string> Lines(NpcLifeWorld w, string prefix, int from = 0)
        => w.Transport.TextsTo(w.Player).Skip(from).Where(t => t.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    private static int LineCount(NpcLifeWorld w) => w.Transport.TextsTo(w.Player).Count;

    /// <summary>Every free backpack slot gets a full stack of stone — no room left for anything but stone.</summary>
    private static void FillBackpack(PlayerSession p)
    {
        var inv = p.State.Inventory;
        int max = Content.Value.MaxStackOf("stone");
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is not { IsEmpty: false })
            {
                inv.SetSlot(i, new ItemStack("stone", max));
            }
        }
    }

    private static void FreeSlots(PlayerSession p, params int[] slots)
    {
        foreach (int slot in slots)
        {
            p.State.Inventory.SetSlot(slot, null);
        }
    }

    /// <summary>The report's state: chapters 1 and 2 behind, the spindle placed, disc 1 decoded — "decode two more discs"
    /// (s3.4) runs.</summary>
    private static void AtDecodeTwoMore(NpcLifeWorld w)
    {
        w.Server.SoundersAdminForTest(w.Player, "chapter 3");
        w.Server.RaiseGameEventForTest(w.Player, GameEventKeys.BuildAny, SoundersIds.SongSpindle); // s3.1
        w.Server.TickStoryPacksForTest(w.Player); // chapter 2's reward for the skipper: the spindle and disc 1
        SkipStep(w, "s3.2");
        w.Server.TickStoryPacksForTest(w.Player); // disc 1 counts as decoded
        Assert.True(w.Player.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s3.4")));
    }

    [Fact]
    public void SkippingDecodeTwoMore_WithAFullBackpack_KeepsBothDiscsWaiting_ThenPacksThem()
    {
        using var w = World("story_item_skip_full");
        AtDecodeTwoMore(w);
        FillBackpack(w.Player);
        var p = w.Player.State;
        int from = LineCount(w);

        SkipStep(w, "s3.4");
        w.Server.TickStoryPacksForTest(w.Player, 3);

        Assert.Equal(0, Count(w, SoundersIds.DiscBuilders));
        Assert.Equal(0, Count(w, SoundersIds.DiscHearing));
        Assert.Empty(w.Server.DropPackets); // nothing left lying behind the player's back
        Assert.DoesNotContain(SoundersIds.DiscMilestone(SoundersIds.DiscHearing), p.Milestones); // decoded = never handed again
        Assert.DoesNotContain(SvGameServer.SoundersCatchUpPrefix + SoundersIds.DiscHearing, p.Milestones);
        Assert.Equal("@srv.sounders.catch_up_room:Song Disc: The Builders, Song Disc: Hearing",
            Assert.Single(Lines(w, "@srv.sounders.catch_up_room", from))); // asked once, not every second
        Assert.Empty(Lines(w, "@srv.sounders.catch_up_items", from)); // "VEGA packed …" only once it is true
        Assert.DoesNotContain("@srv.sounders.catch_up", w.Transport.TextsTo(w.Player).Skip(from));

        FreeSlots(w.Player, 20, 21);
        w.Server.TickStoryPacksForTest(w.Player);

        Assert.Equal(1, Count(w, SoundersIds.DiscBuilders));
        Assert.Equal(1, Count(w, SoundersIds.DiscHearing));
        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscBuilders), p.Milestones);
        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscHearing), p.Milestones);
        Assert.Equal("@srv.sounders.catch_up_items:Song Disc: The Builders, Song Disc: Hearing",
            Assert.Single(Lines(w, "@srv.sounders.catch_up_items", from)));

        w.Server.TickStoryPacksForTest(w.Player, 3);
        Assert.Equal(1, Count(w, SoundersIds.DiscHearing)); // once
    }

    [Fact]
    public void TheShrinesRewardForASkipper_ComesWhole_AndDiscOneIsNotDecodedBehindItsBack()
    {
        using var w = World("story_item_shrine_full");
        w.Server.SoundersAdminForTest(w.Player, "chapter 3");
        FillBackpack(w.Player);
        SkipStep(w, "s3.1");
        SkipStep(w, "s3.2");
        var p = w.Player.State;

        w.Server.TickStoryPacksForTest(w.Player);
        Assert.DoesNotContain(SvGameServer.SoundersShrineRewardMilestone, p.Milestones);
        Assert.DoesNotContain(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning), p.Milestones);
        Assert.Equal("@srv.sounders.catch_up_room:Song Spindle, Song Disc: The Beginning",
            Assert.Single(Lines(w, "@srv.sounders.catch_up_room")));

        FreeSlots(w.Player, 30); // room for one of the two: both still wait
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(0, Count(w, SoundersIds.SongSpindle));

        FreeSlots(w.Player, 31);
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(1, Count(w, SoundersIds.SongSpindle));
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning));
        Assert.Contains(SvGameServer.SoundersShrineRewardMilestone, p.Milestones);
        Assert.Contains(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning), p.Milestones);
    }

    [Fact]
    public void TheSocketStep_HandsTheHearingDiscAgain_WhenTheSkippedOneIsGone_ButNotWhileItSitsInASpindle()
    {
        using var w = World("story_item_socket");
        w.Server.SoundersAdminForTest(w.Player, "chapter 4");
        w.Server.TickStoryPacksForTest(w.Player); // the skipped chapters' catch-up: the discs decoded, the hearing disc handed
        var p = w.Player.State;
        Assert.Contains(SvGameServer.SoundersCatchUpPrefix + SoundersIds.DiscHearing, p.Milestones);
        p.Inventory.Remove(SoundersIds.DiscHearing, 1); // the report: it went to the ground once and was never found

        foreach (string step in new[] { "s4.2", "s4.3", "s4.4", "s4.5", "s4.6" })
        {
            SkipStep(w, step);
        }

        Assert.True(p.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s4.7"))); // "put the hearing disc in the socket"
        w.Server.TickStoryPacksForTest(w.Player, 3);

        Assert.Equal(1, Count(w, SoundersIds.DiscHearing)); // no dead end — and only one
        Assert.Equal("@srv.sounders.need_item:Song Disc: Hearing", Assert.Single(Lines(w, "@srv.sounders.need_item")));

        // In a spindle it is at hand (that is where a disc is decoded): no spare.
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, SoundersIds.SongSpindle);
        p.Inventory.Remove(SoundersIds.DiscHearing, 1);
        p.Inventory.SetSlot(0, new ItemStack(SoundersIds.DiscHearing, 1));
        p.SelectedHotbarSlot = 0;
        w.Server.UseSongSpindle(w.Player, cell);
        Assert.Equal(SoundersIds.DiscHearing, w.Server.SpindleDiscAt(cell));
        w.Server.TickStoryPacksForTest(w.Player, 2);
        Assert.Equal(0, Count(w, SoundersIds.DiscHearing));
    }

    [Fact]
    public void TheShrinesRewardLeftOnTheGround_ComesAgain_WhenChapterThreeNeedsIt()
    {
        using var w = World("story_item_shrine_ground");
        w.Server.SoundersAdminForTest(w.Player, "chapter 2");
        FillBackpack(w.Player);
        w.Server.StartMelodyPuzzle(w.Player, new MelodyPuzzleSpec
        {
            Id = SoundersIds.PuzzleShrineEcho,
            Mode = MelodyPuzzleMode.Repeat,
            Target = new[] { 0 },
            Rounds = 1,
        });
        w.Server.HandlePayloadForTest(w.Player.ConnectionId,
            NetCodec.Encode(new MelodyNoteIntent { Tone = 0, PuzzleId = SoundersIds.PuzzleShrineEcho }));
        Assert.Single(w.Server.DropPackets); // the reward lies at the shrine (an event in the world: at the feet, as before)
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 60f, w.Feet, NpcLifeWorld.Cz); // and the child walks on

        w.Server.TickStoryPacksForTest(w.Player);
        Assert.True(w.Player.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s3.1"))); // "place the spindle"
        Assert.Equal("@srv.sounders.catch_up_room:Song Spindle", Assert.Single(Lines(w, "@srv.sounders.catch_up_room")));

        FreeSlots(w.Player, 30);
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(1, Count(w, SoundersIds.SongSpindle));

        w.Server.RaiseGameEventForTest(w.Player, GameEventKeys.BuildAny, SoundersIds.SongSpindle); // s3.1: placed
        FreeSlots(w.Player, 31);
        w.Server.TickStoryPacksForTest(w.Player, 2);
        Assert.Equal(1, Count(w, SoundersIds.DiscBeginning)); // "decode disc 1" needs it — the relic caches never bring it
        Assert.Equal(new[] { "@srv.sounders.need_item:Song Spindle", "@srv.sounders.need_item:Song Disc: The Beginning" },
            Lines(w, "@srv.sounders.need_item"));
    }

    [Fact]
    public void TheEchoes_GetTheHearingDiscBack_WhileChapterFiveRuns()
    {
        using var w = World("story_item_echoes");
        w.Server.SoundersAdminForTest(w.Player, "chapter 5");
        w.Server.TickStoryPacksForTest(w.Player);
        Assert.Equal(1, Count(w, SoundersIds.DiscHearing)); // the s3.4 catch-up
        Assert.Empty(Lines(w, "@srv.sounders.need_item"));

        w.Player.State.Inventory.Remove(SoundersIds.DiscHearing, 1); // lost: the Echoes would never come
        w.Server.TickStoryPacksForTest(w.Player);

        Assert.Equal(1, Count(w, SoundersIds.DiscHearing));
        Assert.Single(Lines(w, "@srv.sounders.need_item"));
    }

    [Fact]
    public void ARelicCache_TriedWithAFullBackpack_KeepsOneDisc_NotOneMorePerTry()
    {
        using var w = World("story_item_cache_full");
        w.Player.State.Milestones.Add(StoryPackKeys.Started(Pack));
        w.Player.State.Milestones.Add(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning));
        FillBackpack(w.Player);
        string id = w.Server.AddRelicCacheForTest(new Vector3i(NpcLifeWorld.Cx + 1, w.Feet, NpcLifeWorld.Cz));

        for (int i = 0; i < 3; i++)
        {
            w.Server.LootContainer(w.Player.State.PlayerId, id);
        }

        var cache = w.Server.Containers.Single(c => c.Id == id);
        Assert.Equal(1, cache.Items.Where(s => s.Item == SoundersIds.DiscBuilders).Sum(s => s.Count));

        FreeSlots(w.Player, Enumerable.Range(20, 16).ToArray());
        w.Server.LootContainer(w.Player.State.PlayerId, id);
        Assert.Equal(1, Count(w, SoundersIds.DiscBuilders));
    }

    [Fact]
    public void AOnceOnlyDialogPresent_WithAFullBackpack_LandsAtTheFeet()
    {
        using var w = new NpcLifeWorld(Content.Value, "story_item_gift_full");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int npcId = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        var spot = new Vector3f(NpcLifeWorld.Cx + 5.5f, w.Feet, NpcLifeWorld.Cz + 5.5f);
        w.Server.ConfigureNpcForTest(npcId, spot);
        w.Player.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
        string id = w.Player.State.PlayerId;
        w.Server.TalkToNpcForTest(id, npcId);
        Assert.Equal(("settler_crystal_gift", 0), w.Server.ActiveDialogForTest(id));
        FillBackpack(w.Player);

        w.Server.ChooseDialogForTest(id, 0); // "gift:crystal_conduit:8" — the dialog never comes again

        Assert.Equal(0, Count(w, "crystal_conduit"));
        var packet = Assert.Single(w.Server.DropPackets);
        Assert.Equal(8, packet.Items.Where(s => s.Item == "crystal_conduit").Sum(s => s.Count));
        Assert.Contains("@dropped_on_ground", w.Transport.TextsTo(w.Player));
    }

    [Fact]
    public void AHandOverAboard_GoesIntoThePlayersOwnHold_NotTheShipTheCursorPointsAt()
    {
        using var w = World("story_item_own_hold");
        var friend = w.Server.AddLocalPlayer("Friend");
        friend.State.AboardShip = false;
        friend.State.Position = w.Player.State.Position;
        w.Player.State.AboardShip = true;
        FillBackpack(w.Player);
        _ = w.Server.FindAirCellForTest(friend.State.PlayerId); // serves the friend: the ship cursor points at their ship

        w.Server.GiveSoundersItem(w.Player, SoundersIds.DiscHunger, 1); // as the 1 Hz story pass would, unserved

        Assert.Equal(1, w.Player.Ships[w.Player.ActiveShipId].Cargo.CountOf(SoundersIds.DiscHunger));
        Assert.Equal(0, friend.Ships[friend.ActiveShipId].Cargo.CountOf(SoundersIds.DiscHunger));
    }
}
