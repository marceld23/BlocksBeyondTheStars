// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The cracks of the other side (#3909): every rune gate carries one over there until its mend game is solved. The game
/// starts from the melody bar — only over there, only in a cracked gate — and never takes the way home; a solved one saves
/// the mend for the world, tells everybody over there near the gate, and lets the real side answer once: singing plants,
/// and a gift of the Lights that waits for room. Three stones a player placed mend too, but give nothing.
/// </summary>
public sealed class OtherSideCracksTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] BloomPlants = { FloraCatalog.ChimeReedKey, FloraCatalog.BellBloomKey, FloraCatalog.EchoFlowerKey };

    private static Vector3f GateCentre(OtherSideStage w, Vector3f at)
    {
        Assert.True(w.Server.IsGateForTest(at, out var centre));
        return centre;
    }

    private static void Note(OtherSideStage w, PlayerSession s, int tone) => w.Send(s, new MelodyNoteIntent { Tone = tone });

    /// <summary>Starts the mend game with one tone and plays both rounds' answers.</summary>
    private static void PlayTheMendGame(OtherSideStage w, PlayerSession s, Vector3f gate)
    {
        Note(w, s, 0); // the tone that calls the puzzle
        Assert.Equal(SoundersIds.PuzzleCrackMend, w.Server.RunningMelodyPuzzle(s));
        foreach (int[] answer in SvGameServer.CrackMendAnswersForTest(s.CurrentLocationId, gate))
        {
            w.Server.TickMelodyPuzzlesForTest(2.0); // the round's prompt
            foreach (int tone in answer)
            {
                Note(w, s, tone);
            }
        }
    }

    private static int PlantsNear(OtherSideStage w, Vector3f gate)
    {
        var ids = BloomPlants.Select(k => Content.GetBlock(k)!.NumericId.Value).ToHashSet();
        int count = 0;
        for (int dx = -14; dx <= 14; dx++)
        {
            for (int dz = -14; dz <= 14; dz++)
            {
                var cell = new Vector3i((int)MathF.Floor(gate.X) + dx, w.PadY + 1, (int)MathF.Floor(gate.Z) + dz);
                count += ids.Contains(w.Server.World.GetBlock(cell).Value) ? 1 : 0;
            }
        }

        return count;
    }

    [Fact]
    public void AGate_CarriesACrack_UntilItIsMended_AndTheGateListSaysSo()
    {
        using var w = new OtherSideStage(Content, "crack_list");
        var s = w.Crossed("Kid");
        var gate = GateCentre(w, w.AtGateA);

        Assert.False(w.Server.IsCrackMendedForTest(gate));
        Assert.All(w.Server.OtherSideGatesForTest(s), g => Assert.True(g.Cracked));

        Assert.True(w.Server.MendCrackForTest(s, gate));

        Assert.True(w.Server.IsCrackMendedForTest(gate));
        var gates = w.Server.OtherSideGatesForTest(s);
        Assert.False(gates.OrderBy(g => new Vector3f(g.X, g.Y, g.Z).DistanceSquared(gate)).First().Cracked);
        Assert.False(w.Server.IsCrackMendedForTest(GateCentre(w, w.AtGateB))); // every gate has its own crack
        Assert.False(w.Server.MendCrackForTest(s, gate)); // once per gate and world
    }

    [Fact]
    public void TheMendGame_StartsOnlyOverThere_AndOnlyInACrackedGate()
    {
        using var w = new OtherSideStage(Content, "crack_start");
        var s = w.Player("Kid", w.AtGateA);
        OtherSideStage.Dress(s);

        Note(w, s, 3); // on our side the bar is only music
        Assert.Null(w.Server.RunningMelodyPuzzle(s));

        w.Use(s, w.GateA[1]);
        Assert.True(s.OtherSide);
        s.State.Position = w.AtLoneStone; // a lone stone is no gate
        Note(w, s, 3);
        Assert.Null(w.Server.RunningMelodyPuzzle(s));

        s.State.Position = w.AtGateA;
        w.Tick(2.0); // the "no gate here" pause of the scan
        Note(w, s, 3);
        Assert.Equal(SoundersIds.PuzzleCrackMend, w.Server.RunningMelodyPuzzle(s));
        var open = w.Transport.To<MelodyPuzzleState>(s).Last();
        Assert.True(open.Open);
        Assert.Equal(2, open.RoundCount);
        Assert.Equal(3, open.Expected);

        w.Send(s, new MelodyNoteIntent { Tone = -1 }); // leave it
        var gate = GateCentre(w, w.AtGateA);
        Assert.True(w.Server.MendCrackForTest(s, gate));
        w.Tick(2.0);
        Note(w, s, 3); // a whole gate asks for nothing
        Assert.Null(w.Server.RunningMelodyPuzzle(s));
    }

    [Fact]
    public void SolvingTheMendGame_SavesTheMend_TellsThePlayer_AndResendsTheGates()
    {
        using var w = new OtherSideStage(Content, "crack_solve");
        var s = w.Crossed("Kid");
        var gate = GateCentre(w, w.AtGateA);
        var heard = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.CrackMended, heard);
        int listsBefore = w.Transport.To<OtherSideGates>(s).Count;

        Note(w, s, 0);
        int[][] answers = SvGameServer.CrackMendAnswersForTest(s.CurrentLocationId, gate);
        w.Server.TickMelodyPuzzlesForTest(2.0);
        Note(w, s, (answers[0][0] + 1) % SounderAlphabet.ToneCount); // a wrong tone simply does not count
        Assert.Equal(SoundersIds.PuzzleCrackMend, w.Server.RunningMelodyPuzzle(s));
        foreach (int tone in answers[0])
        {
            Note(w, s, tone);
        }

        w.Server.TickMelodyPuzzlesForTest(2.0);
        Assert.True(w.Transport.To<MelodyPuzzleState>(s).Last().NextTone >= 0); // the key to press glows
        foreach (int tone in answers[1])
        {
            Note(w, s, tone);
        }

        Assert.Null(w.Server.RunningMelodyPuzzle(s));
        string key = Assert.Single(w.Server.Metadata.MendedCracks);
        Assert.StartsWith(s.CurrentLocationId + "|", key, StringComparison.Ordinal);
        Assert.True(w.Server.IsCrackMendedForTest(gate));
        Assert.Equal((s.State.PlayerId, GameEventKeys.CrackMended, key, 1), Assert.Single(heard.Events));
        Assert.Contains(SoundersIds.CrackMilestone(s.CurrentLocationId, 1), s.State.Milestones);
        Assert.Contains(w.Transport.To<SoundFx>(s), fx => fx.SoundId == SvGameServer.CrackMendSound);
        Assert.Contains("@srv.sounders.crack_mended", w.Transport.TextsTo(s));
        var lists = w.Transport.To<OtherSideGates>(s);
        Assert.True(lists.Count > listsBefore);
        Assert.Contains(lists.Last().Gates, g => !g.Cracked);
    }

    [Fact]
    public void ACrackedGate_StillTakesThePlayerHome()
    {
        using var w = new OtherSideStage(Content, "crack_home");
        var s = w.Crossed("Kid");
        Assert.False(w.Server.IsCrackMendedForTest(GateCentre(w, w.AtGateA)));

        Note(w, s, 2); // even with the mend game open
        w.Use(s, w.GateA[1]);

        Assert.False(s.OtherSide);
    }

    [Fact]
    public void TheRealSide_BloomsOnce_AndTheLightsLeaveAGift()
    {
        using var w = new OtherSideStage(Content, "crack_bloom");
        var s = w.Crossed("Kid");
        var gate = GateCentre(w, w.AtGateA);
        Assert.Equal(0, PlantsNear(w, gate));

        PlayTheMendGame(w, s, gate);

        int plants = PlantsNear(w, gate);
        Assert.InRange(plants, SvGameServer.CrackBloomMin, SvGameServer.CrackBloomMax);
        Assert.Contains("@srv.sounders.crack_bloom", w.Transport.TextsTo(s));
        Assert.True(w.Server.CrackGiftWaitsForTest(s));

        w.Server.TickStoryPacksForTest(s);
        Assert.Equal(2, s.State.Inventory.CountOf("crystal"));
        Assert.Equal(1, s.State.Inventory.CountOf("obsidian"));
        Assert.False(w.Server.CrackGiftWaitsForTest(s));
        Assert.Contains("@srv.sounders.crack_gift", w.Transport.TextsTo(s));

        w.Server.TickStoryPacksForTest(s, 3); // once
        Assert.Equal(2, s.State.Inventory.CountOf("crystal"));
        Assert.False(w.Server.MendCrackForTest(s, gate));
        Assert.Equal(plants, PlantsNear(w, gate));
    }

    [Fact]
    public void AFullBackpack_KeepsTheGift_UntilThereIsRoom()
    {
        using var w = new OtherSideStage(Content, "crack_gift_room");
        var s = w.Crossed("Kid");
        var inv = s.State.Inventory;
        int max = Content.MaxStackOf("stone");
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is not { IsEmpty: false })
            {
                inv.SetSlot(i, new ItemStack("stone", max));
            }
        }

        Assert.True(w.Server.MendCrackForTest(s, GateCentre(w, w.AtGateA)));
        w.Server.TickStoryPacksForTest(s, 2);

        Assert.Equal(0, inv.CountOf("crystal"));
        Assert.True(w.Server.CrackGiftWaitsForTest(s));
        Assert.Single(w.Transport.TextsTo(s), t => t.StartsWith("@srv.sounders.catch_up_room:", StringComparison.Ordinal));

        inv.SetSlot(0, null);
        inv.SetSlot(1, null);
        w.Server.TickStoryPacksForTest(s);
        Assert.Equal(2, inv.CountOf("crystal"));
        Assert.Equal(1, inv.CountOf("obsidian"));
        Assert.False(w.Server.CrackGiftWaitsForTest(s));
    }

    [Fact]
    public void AGateAPlayerBuilt_MendsToo_ButTheRealSideGivesNothing()
    {
        using var w = new OtherSideStage(Content, "crack_built");
        var s = w.Crossed("Kid");
        var rune = Content.GetBlock("rune_stone")!.NumericId;
        var built = new[] { new Vector3i(OtherSideStage.Cx + 14, w.PadY + 1, OtherSideStage.Cz + 14), new Vector3i(OtherSideStage.Cx + 16, w.PadY + 1, OtherSideStage.Cz + 14), new Vector3i(OtherSideStage.Cx + 15, w.PadY + 1, OtherSideStage.Cz + 16) };
        foreach (var cell in built)
        {
            w.Server.World.SetBlock(cell, rune, owner: s.State.PlayerId);
        }

        s.State.Position = new Vector3f(OtherSideStage.Cx + 15.5f, w.PadY + 1, OtherSideStage.Cz + 15.5f);
        var gate = GateCentre(w, s.State.Position);
        var heard = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.CrackMended, heard);

        PlayTheMendGame(w, s, gate);

        Assert.True(w.Server.IsCrackMendedForTest(gate));
        Assert.Single(heard.Events); // it counts for the story …
        Assert.Equal(0, PlantsNear(w, gate)); // … and nothing else
        Assert.False(w.Server.CrackGiftWaitsForTest(s));
        w.Server.TickStoryPacksForTest(s);
        Assert.Equal(0, s.State.Inventory.CountOf("crystal"));
    }

    /// <summary>The middle of a gate is the mean of the rune stones around whoever looks. Stones a player places beside a
    /// world gate — and another place to stand — move it further than a mend reaches while the world's stones are still
    /// part of the gate; keyed by the middle alone, the same site was mended, counted and paid again.</summary>
    [Fact]
    public void AWorldSite_IsMendedOnce_WhateverStonesAPlayerAddsAndWhereverTheyStand()
    {
        using var w = new OtherSideStage(Content, "crack_site");
        var s = w.Crossed("Kid");
        var gate = GateCentre(w, w.AtGateA);
        string? site = w.Server.WorldGateSiteForTest(gate);
        Assert.NotNull(site);
        var heard = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.CrackMended, heard);

        Assert.True(w.Server.MendCrackForTest(s, gate));
        int plants = PlantsNear(w, gate);
        w.Server.TickStoryPacksForTest(s);
        Assert.Equal(2, s.State.Inventory.CountOf("crystal"));
        Assert.EndsWith("|" + site, Assert.Single(w.Server.Metadata.MendedCracks), StringComparison.Ordinal);

        // Thirty stones of the player's, up and away from the gate, and a place to stand from which both groups are seen.
        var rune = Content.GetBlock("rune_stone")!.NumericId;
        int y0 = w.PadY + 1;
        for (int x = OtherSideStage.Cx + 6; x <= OtherSideStage.Cx + 7; x++)
        {
            for (int z = OtherSideStage.Cz + 11; z <= OtherSideStage.Cz + 13; z++)
            {
                for (int y = y0 + 7; y <= y0 + 11; y++)
                {
                    w.Server.World.SetBlock(new Vector3i(x, y, z), rune, owner: s.State.PlayerId);
                }
            }
        }

        s.State.Position = new Vector3f(OtherSideStage.Cx + 6.5f, y0 + 4, OtherSideStage.Cz + 11.5f);
        var moved = GateCentre(w, s.State.Position);
        Assert.True(moved.DistanceSquared(gate) > SvGameServer.CrackGateReach * SvGameServer.CrackGateReach, "the middle moved out of the mend's reach");
        Assert.Equal(site, w.Server.WorldGateSiteForTest(moved)); // … and it is still the same site

        Assert.True(w.Server.IsCrackMendedForTest(moved)); // what the gate list's crack flag asks
        w.Tick(2.0);
        Note(w, s, 0); // so the mend game does not start again …
        Assert.Null(w.Server.RunningMelodyPuzzle(s));
        Assert.False(w.Server.MendCrackForTest(s, moved)); // … and a mend from anywhere else counts for nothing

        Assert.Single(heard.Events); // not a second "different gate" for the story
        Assert.Single(w.Server.Metadata.MendedCracks);
        Assert.Equal(plants, PlantsNear(w, gate)); // no second bloom
        Assert.False(w.Server.CrackGiftWaitsForTest(s)); // no second gift
        w.Server.TickStoryPacksForTest(s, 2);
        Assert.Equal(2, s.State.Inventory.CountOf("crystal"));
    }

    [Fact]
    public void TheSite_IsTheWorldsOwnStones_FromWhereverTheGateIsLookedAt_AndPlacedStonesHaveNone()
    {
        using var w = new OtherSideStage(Content, "crack_site_id");
        var s = w.Crossed("Kid");
        var rune = Content.GetBlock("rune_stone")!.NumericId;
        int y0 = w.PadY + 1;
        w.Server.World.SetBlock(new Vector3i(OtherSideStage.Cx - 8, y0, OtherSideStage.Cz + 3), rune); // a fourth stone of the world, six blocks west

        // From the gate's middle all four are seen; from further east only three — the group is the same group.
        string? whole = w.Server.WorldGateSiteForTest(new Vector3f(OtherSideStage.Cx + 0.5f, y0, OtherSideStage.Cz + 3.5f));
        string? part = w.Server.WorldGateSiteForTest(new Vector3f(OtherSideStage.Cx + 4.5f, y0, OtherSideStage.Cz + 3.5f));
        Assert.NotNull(whole);
        Assert.Equal(whole, part);
        Assert.DoesNotContain("|", whole, StringComparison.Ordinal); // it rides in the crack's key
        Assert.NotEqual(whole, w.Server.WorldGateSiteForTest(GateCentre(w, w.AtGateB))); // every gate of the world is its own site

        var built = new[] { new Vector3i(OtherSideStage.Cx + 14, y0, OtherSideStage.Cz + 14), new Vector3i(OtherSideStage.Cx + 16, y0, OtherSideStage.Cz + 14), new Vector3i(OtherSideStage.Cx + 15, y0, OtherSideStage.Cz + 16) };
        foreach (var cell in built)
        {
            w.Server.World.SetBlock(cell, rune, owner: s.State.PlayerId);
        }

        Assert.Null(w.Server.WorldGateSiteForTest(GateCentre(w, new Vector3f(OtherSideStage.Cx + 15.5f, y0, OtherSideStage.Cz + 15.5f))));
    }

    /// <summary>"Mending never blocks the way home" also where a gate's stones are the pillars of a melody site (the
    /// Singing Shrine, a ring): over there the use key on a stone is the return — the running mend game takes its answers
    /// from the melody bar only.</summary>
    [Fact]
    public void AtAGateOfMelodyPillars_TheUseKeyStillTakesThePlayerHome_WhileTheMendGameRuns()
    {
        using var w = new OtherSideStage(Content, "crack_pillars");
        var rune = Content.GetBlock("rune_stone")!.NumericId;
        int cx = OtherSideStage.Cx + 12, cz = OtherSideStage.Cz + 12, y = w.PadY + 1;
        var pillars = new[] { (0, -3), (2, -2), (3, 0), (2, 2), (0, 3), (-2, 2), (-3, 0), (-2, -2) } // clockwise from north
            .Select(o => new Vector3i(cx + o.Item1, y, cz + o.Item2)).ToArray();
        foreach (var cell in pillars)
        {
            w.Server.World.SetBlock(cell, rune);
        }

        Assert.NotNull(w.Server.RegisterMelodySite("test_ring", pillars));
        var s = w.Player("Kid", new Vector3f(cx + 0.5f, y, cz + 0.5f));
        OtherSideStage.Dress(s);
        w.Use(s, pillars[0]);
        Assert.True(s.OtherSide);

        Note(w, s, 0);
        Assert.Equal(SoundersIds.PuzzleCrackMend, w.Server.RunningMelodyPuzzle(s));

        w.Use(s, pillars[2]); // a pillar strike would feed the game and never reach the return

        Assert.False(s.OtherSide);
        Assert.Contains("@srv.otherside.returned", w.Transport.TextsTo(s));
    }

    [Fact]
    public void EverybodyOverThereAtTheGate_TakesPart_AndNobodyOnOurSide()
    {
        using var w = new OtherSideStage(Content, "crack_together");
        var a = w.Crossed("Anna");
        var b = w.Crossed("Ben");
        var far = w.Crossed("Far");
        far.State.Position = w.AtGateB; // over there, but at another gate
        var here = w.Player("Here", w.AtGateA); // at the gate, on our side
        var heard = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.CrackMended, heard);

        PlayTheMendGame(w, a, GateCentre(w, w.AtGateA));

        Assert.Equal(new[] { a.State.PlayerId, b.State.PlayerId }.OrderBy(x => x), heard.Events.Select(e => e.Player).OrderBy(x => x));
        Assert.True(w.Server.CrackGiftWaitsForTest(b));
        Assert.False(w.Server.CrackGiftWaitsForTest(far));
        Assert.False(w.Server.CrackGiftWaitsForTest(here));
        Assert.DoesNotContain(w.Transport.To<SoundFx>(here), fx => fx.SoundId == SvGameServer.CrackMendSound);
    }

    [Fact]
    public void AReload_KeepsTheCrackMended()
    {
        using var w = new OtherSideStage(Content, "crack_reload");
        var s = w.Crossed("Kid");
        var gate = GateCentre(w, w.AtGateA);
        var other = GateCentre(w, w.AtGateB);
        Assert.True(w.Server.MendCrackForTest(s, gate));

        var again = w.Restart(); // the mend was saved when it happened

        Assert.Single(again.Metadata.MendedCracks);
        Assert.True(again.IsCrackMendedForTest(gate));
        Assert.False(again.IsCrackMendedForTest(other));
    }

    [Fact]
    public void TheMotif_IsTheGatesOwn_AndItsAnswerIsTheCounterVoice()
    {
        var gate = new Vector3f(120.4f, 64f, -33.2f);
        int[] motif = SvGameServer.CrackMotif("world-a", gate);

        Assert.Equal(3, motif.Length);
        Assert.All(motif, t => Assert.InRange(t, 0, SounderAlphabet.ToneCount - 1));
        Assert.NotEqual(motif[0], motif[1]);
        Assert.NotEqual(motif[1], motif[2]);
        Assert.Equal(motif, SvGameServer.CrackMotif("world-a", new Vector3f(120.1f, 64.2f, -33.4f))); // the same gate, measured again
        var spec = SvGameServer.CrackMendSpec("world-a", gate);
        Assert.True(spec.ShowNextTone);
        Assert.Equal(new[] { motif[2], motif[1], motif[0] }, spec.Calls[1]);
        Assert.Equal(MelodyPuzzle.Counter(motif, 4), spec.Answers[0]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryLine_ExistsInBothLanguages(string language)
    {
        var table = TestLocales.Load(language);
        foreach (string key in new[] { "srv.sounders.crack_mended", "srv.sounders.crack_bloom", "srv.sounders.crack_gift" })
        {
            Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{language}.json is missing '{key}'");
        }
    }
}
