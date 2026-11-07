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
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3345 — playable rune stones: the use key on a <c>rune_stone</c> sings the stone's word in the note alphabet with the
/// Sounder voice, for everyone nearby — always, with or without the Sounders story pack. Every use writes the player's
/// <c>snd:first_rune</c> (the pack starts on it) and the word for the Song Archive (<c>snd:word:&lt;WORD&gt;</c>, so the
/// stone that starts the story is archived too); only with the pack running does <c>rune:played</c> fire with the site kind.
/// </summary>
public sealed class RuneStoneTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((key, arg));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static void Use(NpcLifeWorld w, PlayerSession who, Vector3i cell)
        => w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

    private static List<NoteSequence> Runes(NpcLifeWorld w, PlayerSession who)
        => w.Transport.To<NoteSequence>(who).Where(n => n.Source == "rune_stone").ToList();

    [Fact]
    public void ARuneStone_SingsItsWord_WithTheSounderVoice_WithoutTheStoryPack()
    {
        using var w = World("rune_sings");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.RunePlayed, events);
        var friend = w.Server.AddLocalPlayer("Friend");
        friend.State.AboardShip = false;
        friend.State.Position = new Vector3f(NpcLifeWorld.Cx + 6f, w.Feet, NpcLifeWorld.Cz + 6f);
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, "rune_stone");

        Use(w, w.Player, cell);

        string word = w.Server.RuneWordAt(cell);
        Assert.True(SounderLexicon.IsWord(word));
        var sung = Assert.Single(Runes(w, w.Player));
        Assert.Equal(SounderAlphabet.Encode(word), sung.Notes);
        Assert.Equal(SoundersIds.InstrumentVoice, sung.Instrument);
        Assert.Equal(word, SounderAlphabet.Decode(sung.Notes));
        Assert.Single(Runes(w, friend)); // the friend standing by hears it too

        Assert.Contains(SvGameServer.FirstRuneMilestone, w.Player.State.Milestones); // the pack's start rule waits for this
        Assert.Contains(SoundersIds.WordMilestone(word), w.Player.State.Milestones); // kept for the Song Archive
        Assert.Empty(events.Events); // no story event without the pack
    }

    [Fact]
    public void TheSameStone_AlwaysSingsTheSameWord_OnBothSidesOfTheSeam()
    {
        using var w = World("rune_word");
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        int circ = w.Server.World.Circumference;

        Assert.Equal(w.Server.RuneWordAt(cell), w.Server.RuneWordAt(new Vector3i(cell.X + circ, cell.Y, cell.Z)));
        Assert.Equal(SounderLexicon.WordAt(w.Server.Metadata.Seed, cell.X, cell.Y, cell.Z), w.Server.RuneWordAt(cell));
    }

    [Fact]
    public void AHeldKey_DoesNotStackWords_ButTheNextWordComesAfterTheCooldown()
    {
        using var w = World("rune_cooldown");
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, "rune_stone");

        Use(w, w.Player, cell);
        Use(w, w.Player, cell);
        Assert.Single(Runes(w, w.Player));

        w.Server.TickMelodyPuzzlesForTest(SvGameServer.RuneCooldownSeconds + 0.1);
        Use(w, w.Player, cell);
        Assert.Equal(2, Runes(w, w.Player).Count);
    }

    [Fact]
    public void WithThePackRunning_AUseCollectsTheWord_AndRaisesRunePlayed_WithWhereTheStoneStands()
    {
        using var w = World("rune_kinds");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.RunePlayed, events);
        w.Player.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));
        Assert.True(w.Server.IsStoryPackActive(w.Player, SoundersIds.PackId));

        var placed = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(placed.X, placed.Y, placed.Z, "rune_stone"); // the player built it
        var stamped = new Vector3i(NpcLifeWorld.Cx + 1, w.Feet, NpcLifeWorld.Cz - 2);
        w.Server.World.SetBlock(stamped, w.Block("rune_stone")); // a structure stamp wrote it (no player owner)

        Use(w, w.Player, placed);
        Assert.Equal((GameEventKeys.RunePlayed, SvGameServer.RuneSitePlaced), events.Events.Last());
        Assert.Contains(SoundersIds.WordMilestone(w.Server.RuneWordAt(placed)), w.Player.State.Milestones);

        Again(w);
        Use(w, w.Player, stamped);
        Assert.Equal(SvGameServer.RuneSiteRuin, events.Events.Last().Arg);

        w.Server.SpawnMonumentForTest(new Vector3f(stamped.X, stamped.Y, stamped.Z), "obelisk");
        Again(w);
        Use(w, w.Player, stamped);
        Assert.Equal(SvGameServer.RuneSiteMonument, events.Events.Last().Arg);

        w.Server.RegisterShrineMelodySite(NpcLifeWorld.Cx, w.PadY, NpcLifeWorld.Cz); // the stone stands inside the shrine
        Again(w);
        Use(w, w.Player, stamped);
        Assert.Equal(SvGameServer.RuneSiteShrine, events.Events.Last().Arg);
        Assert.Equal(4, events.Events.Count);
    }

    [Fact]
    public void ASwitchedOffPack_GetsNoWordAndNoEvent_ButTheStoneStillSings()
    {
        using var w = World("rune_optout");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.RunePlayed, events);
        w.Player.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));
        w.Player.State.Milestones.Add(StoryPackKeys.OptOut(SoundersIds.PackId));
        var cell = new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz);
        w.Set(cell.X, cell.Y, cell.Z, "rune_stone");

        Use(w, w.Player, cell);

        Assert.False(w.Server.IsStoryPackActive(w.Player, SoundersIds.PackId));
        Assert.Single(Runes(w, w.Player));
        Assert.Empty(events.Events);
        Assert.Contains(SvGameServer.FirstRuneMilestone, w.Player.State.Milestones);
    }

    private static void Again(NpcLifeWorld w) => w.Server.ResetRuneCooldownForTest(w.Player);
}
