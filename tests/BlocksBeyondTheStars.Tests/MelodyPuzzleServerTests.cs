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
/// #3346 — melody puzzles on the server: a started puzzle opens the melody bar and plays its prompt, the bar's notes
/// (<see cref="MelodyNoteIntent"/>, also while a scene holds a singleplayer world) and the pillars of its melody site (the
/// use key) feed the checker, a solution raises <c>melody:solved</c> and calls the registered handlers — for everyone who
/// played the same puzzle nearby. Wrong notes only restart; walking away, the stop key (-1) and other code end a puzzle
/// quietly. Also the Sounder instruments of the melody block (#3345), which need the <c>sounder_voice</c> blueprint.
/// </summary>
public sealed class MelodyPuzzleServerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Player, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((actor.State.Name, arg));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static MelodyPuzzleSpec Spec(MelodyPuzzleMode mode, params int[] target)
        => new() { Id = "test_tune", Mode = mode, Target = target, Rounds = 1 };

    private static void Note(NpcLifeWorld w, PlayerSession who, int tone, string puzzleId = "")
        => w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new MelodyNoteIntent { Tone = tone, PuzzleId = puzzleId }));

    private static void Use(NpcLifeWorld w, PlayerSession who, Vector3i cell)
        => w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));

    private static MelodyPuzzleState LastState(NpcLifeWorld w, PlayerSession who) => w.Transport.To<MelodyPuzzleState>(who).Last();

    private static PlayerSession Friend(NpcLifeWorld w, float dx, float dz)
    {
        var friend = w.Server.AddLocalPlayer("Friend");
        friend.State.AboardShip = false;
        friend.State.Position = new Vector3f(w.Player.State.Position.X + dx, w.Feet, w.Player.State.Position.Z + dz);
        return friend;
    }

    [Fact]
    public void AStartedPuzzle_OpensTheBar_PlaysItsPrompt_AndTheSolutionRaisesMelodySolved()
    {
        using var w = World("melody_solve");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.MelodySolved, events);
        var solvedBy = new List<string>();
        w.Server.RegisterMelodySolvedHandler("test_tune", s => solvedBy.Add(s.State.Name));

        Assert.True(w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Repeat, 0, 2, 4)));

        var open = LastState(w, w.Player);
        Assert.True(open.Open);
        Assert.Equal("test_tune", open.PuzzleId);
        Assert.Equal(new[] { 0, 2, 4 }, open.Highlight);
        Assert.Equal(3, open.Expected);
        var prompt = Assert.Single(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "test_tune");
        Assert.Equal(new[] { 0, 2, 4 }, prompt.Notes);
        Assert.Equal(SoundersIds.InstrumentVoice, prompt.Instrument);
        Assert.Equal("test_tune", w.Server.RunningMelodyPuzzle(w.Player));

        Note(w, w.Player, 0, "test_tune");
        Assert.Equal((int)MelodyPuzzleResult.Correct, LastState(w, w.Player).Result);
        Note(w, w.Player, 2);
        Note(w, w.Player, 4);

        var solved = LastState(w, w.Player);
        Assert.False(solved.Open);
        Assert.Equal((int)MelodyPuzzleResult.Solved, solved.Result);
        Assert.Equal(new[] { (NpcLifeWorld.Owner, "test_tune") }, events.Events);
        Assert.Equal(new[] { NpcLifeWorld.Owner }, solvedBy);
        Assert.Null(w.Server.RunningMelodyPuzzle(w.Player));
        Assert.Contains(w.Transport.To<NoteSequence>(w.Player), n => n.Instrument == SoundersIds.InstrumentChoir && n.WithFifth); // the chorus
    }

    [Fact]
    public void AWrongNote_SaysTryAgain_AndThePromptPlaysAgainAfterAShortPause()
    {
        using var w = World("melody_retry");
        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Repeat, 5, 3));

        Note(w, w.Player, 5);
        Note(w, w.Player, 7);
        Assert.Equal((int)MelodyPuzzleResult.TryAgain, LastState(w, w.Player).Result);
        Assert.True(LastState(w, w.Player).Open);

        w.Server.TickMelodyPuzzlesForTest(SvGameServer.MelodyRetryPause + 0.1);
        var again = LastState(w, w.Player);
        Assert.Equal(new[] { 5, 3 }, again.Highlight);
        Assert.Equal(0, again.Entered);
        Assert.Equal(2, w.Transport.To<NoteSequence>(w.Player).Count(n => n.Source == "test_tune"));

        Note(w, w.Player, 5);
        Note(w, w.Player, 3);
        Assert.Equal((int)MelodyPuzzleResult.Solved, LastState(w, w.Player).Result);
    }

    [Fact]
    public void ARightNote_RightAfterAWrongOne_CarriesOn_WithoutTheReplayWipingIt()
    {
        using var w = World("melody_carry_on");
        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Harmony, 0, 1, 2)); // the counter-voice: 4, 5, 6

        Note(w, w.Player, 4);
        Note(w, w.Player, 0); // wrong: the melody itself
        Note(w, w.Player, 4); // the player starts again at once
        w.Server.TickMelodyPuzzlesForTest(SvGameServer.MelodyRetryPause + 0.1);

        Assert.Single(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "test_tune"); // only the first prompt played
        Note(w, w.Player, 5);
        Note(w, w.Player, 6);
        Assert.Equal((int)MelodyPuzzleResult.Solved, LastState(w, w.Player).Result);
    }

    [Fact]
    public void ALongPause_RemindsWithThePrompt_NeverFails()
    {
        using var w = World("melody_reminder");
        var spec = Spec(MelodyPuzzleMode.Repeat, 1, 2);
        spec.IdleSeconds = 5f;
        w.Server.StartMelodyPuzzle(w.Player, spec);

        for (int i = 0; i < 8; i++)
        {
            w.Server.TickMelodyPuzzlesForTest(1.0);
        }

        var reminder = LastState(w, w.Player);
        Assert.Equal((int)MelodyPuzzleResult.Reminder, reminder.Result);
        Assert.True(reminder.Open);
        Assert.Equal("test_tune", w.Server.RunningMelodyPuzzle(w.Player));
    }

    [Fact]
    public void WalkingAway_TheStopKey_OrOtherCode_EndThePuzzleQuietly()
    {
        using var w = World("melody_leave");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.MelodySolved, events);

        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, 0, 1));
        var start = w.Player.State.Position;
        w.Player.State.Position = new Vector3f(start.X + 30f, start.Y, start.Z);
        w.Server.TickMelodyPuzzlesForTest(0.1);
        Assert.Equal((int)MelodyPuzzleResult.Cancelled, LastState(w, w.Player).Result);
        Assert.False(LastState(w, w.Player).Open);
        Assert.Null(w.Server.RunningMelodyPuzzle(w.Player));

        w.Player.State.Position = start;
        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, 0, 1));
        Note(w, w.Player, -1, "another_tune"); // a stale stop for a puzzle that is over: ignored
        Assert.Equal("test_tune", w.Server.RunningMelodyPuzzle(w.Player));
        Note(w, w.Player, -1, "test_tune");
        Assert.Null(w.Server.RunningMelodyPuzzle(w.Player));

        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, 0, 1));
        Assert.False(w.Server.CancelMelodyPuzzle(w.Player, "another_tune"));
        Assert.True(w.Server.CancelMelodyPuzzle(w.Player));
        Assert.Equal((int)MelodyPuzzleResult.Cancelled, LastState(w, w.Player).Result);

        var timed = Spec(MelodyPuzzleMode.Compose, 0, 1);
        timed.TimeLimitSeconds = 3f;
        w.Server.StartMelodyPuzzle(w.Player, timed);
        w.Server.TickMelodyPuzzlesForTest(3.5);
        Assert.Equal((int)MelodyPuzzleResult.TimedOut, LastState(w, w.Player).Result);
        Assert.Empty(events.Events); // nothing of that is a solution
    }

    [Fact]
    public void ASpecWithoutATone_DoesNotStart()
    {
        using var w = World("melody_invalid");

        Assert.False(w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, SounderAlphabet.Rest)));
        Assert.Empty(w.Transport.To<MelodyPuzzleState>(w.Player));
    }

    [Fact]
    public void AMelodyNote_ReachesThePuzzle_EvenWhileAHeldScene_PausesTheSingleplayerWorld()
    {
        using var w = World("melody_paused");
        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, 6, 7));

        w.Server.PauseForTest(w.Player, true);
        Assert.True(w.Server.IsPaused);
        Note(w, w.Player, 6);

        Assert.Equal((int)MelodyPuzzleResult.Correct, LastState(w, w.Player).Result); // went through the dispatch gate
        Assert.Equal(1, LastState(w, w.Player).Entered);
    }

    [Fact]
    public void FreePlay_WithoutAPuzzle_IsHeardByThePlayersNearby_NotEchoedToThePlayer()
    {
        using var w = World("melody_free");
        var friend = Friend(w, 4f, 0f);

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MelodyNoteIntent { Tone = 3, Instrument = 5 }));

        var heard = Assert.Single(w.Transport.To<NoteSequence>(friend), n => n.Source == "melody_bar");
        Assert.Equal(new[] { 3 }, heard.Notes);
        Assert.Equal(5, heard.Instrument);
        Assert.DoesNotContain(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "melody_bar");
    }

    [Fact]
    public void PlayersRunningTheSamePuzzleNearby_SolveItTogether()
    {
        using var w = World("melody_together");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.MelodySolved, events);
        var friend = Friend(w, 3f, 0f);
        var faraway = w.Server.AddLocalPlayer("Faraway");
        faraway.State.AboardShip = false;
        faraway.State.Position = new Vector3f(w.Player.State.Position.X + 40f, w.Feet, w.Player.State.Position.Z);

        foreach (var p in new[] { w.Player, friend, faraway })
        {
            Assert.True(w.Server.StartMelodyPuzzle(p, Spec(MelodyPuzzleMode.Compose, 2, 4)));
        }

        Note(w, friend, 2);
        Note(w, w.Player, 2);
        Note(w, w.Player, 4); // the player finishes — the friend beside them is done as well

        Assert.Equal((int)MelodyPuzzleResult.Solved, LastState(w, friend).Result);
        Assert.Equal(new[] { "Friend", NpcLifeWorld.Owner }, events.Events.Select(e => e.Player).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("test_tune", w.Server.RunningMelodyPuzzle(faraway)); // 40 blocks off: still playing
    }

    [Fact]
    public void TheShrinesPillars_StrikeTheirTones_WhileAPuzzleRunsThere_AndSingTheirWordOtherwise()
    {
        using var w = World("melody_shrine");
        var shrine = w.Server.RegisterShrineMelodySite(NpcLifeWorld.Cx, w.PadY, NpcLifeWorld.Cz)!;
        foreach (var p in shrine.Pillars)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                w.Server.World.SetBlock(new Vector3i(p.X, p.Y + dy, p.Z), w.Block("rune_stone"));
            }
        }

        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 0.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        var spec = Spec(MelodyPuzzleMode.Compose, 0, 2, 7);
        spec.SiteId = SvGameServer.ShrineSiteKey;
        w.Server.StartMelodyPuzzle(w.Player, spec);

        var north = shrine.Pillars[0];
        Assert.Equal(new Vector3i(NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz + 5), north); // tone 0 stands in the north
        Use(w, w.Player, new Vector3i(north.X, north.Y + 2, north.Z)); // the top stone of the pillar
        var struck = Assert.Single(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "melody_pillar");
        Assert.Equal(new[] { 0 }, struck.Notes);
        Assert.Equal(1, LastState(w, w.Player).Entered);

        Use(w, w.Player, shrine.Pillars[2]);
        Use(w, w.Player, shrine.Pillars[7]);
        Assert.Equal((int)MelodyPuzzleResult.Solved, LastState(w, w.Player).Result);
        Assert.Empty(w.Transport.To<NoteSequence>(w.Player).Where(n => n.Source == "rune_stone")); // no words meanwhile

        Use(w, w.Player, north); // no puzzle now: the pillar is a rune stone again
        Assert.Single(w.Transport.To<NoteSequence>(w.Player), n => n.Source == "rune_stone");
    }

    [Fact]
    public void ASiteStarter_StartsItsPuzzle_WhenAPillarIsStruck()
    {
        using var w = World("melody_starter");
        var shrine = w.Server.RegisterShrineMelodySite(NpcLifeWorld.Cx, w.PadY, NpcLifeWorld.Cz)!;
        w.Server.World.SetBlock(shrine.Pillars[4], w.Block("rune_stone"));
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 0.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        w.Server.RegisterMelodySiteStarter((s, site) => null); // passes
        w.Server.RegisterMelodySiteStarter((s, site) => site.Kind == SvGameServer.MelodySiteKindShrine
            ? new MelodyPuzzleSpec { Id = SoundersIds.PuzzleShrineEcho, Mode = MelodyPuzzleMode.Repeat, Target = new[] { 0, 4, 7 } }
            : null);

        Use(w, w.Player, shrine.Pillars[4]);

        Assert.Equal(SoundersIds.PuzzleShrineEcho, w.Server.RunningMelodyPuzzle(w.Player));
        var open = LastState(w, w.Player);
        Assert.True(open.Open);
        Assert.Equal(new[] { 0 }, open.Highlight); // Simon says: round one asks for the first tone
        Assert.Empty(w.Transport.To<NoteSequence>(w.Player).Where(n => n.Source == "rune_stone"));
    }

    [Fact]
    public void ARingOfEightRuneStones_AroundAnAncientBrick_IsAMelodySite_UntilAStoneIsMined()
    {
        using var w = World("melody_ring");
        int cx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz, y = w.Feet;
        w.Set(cx, y, cz, "ancient_brick");
        var offsets = new[] { (0, 3), (2, 2), (3, 0), (2, -2), (0, -3), (-2, -2), (-3, 0), (-2, 2) };
        foreach (var (dx, dz) in offsets)
        {
            w.Set(cx + dx, y, cz + dz, "rune_stone");
        }

        w.Player.State.Position = new Vector3f(cx + 0.5f, y + 1, cz + 0.5f); // standing on the centre brick
        w.Server.StartMelodyPuzzle(w.Player, Spec(MelodyPuzzleMode.Compose, 0, 4)); // no site of its own: any pillar plays

        Use(w, w.Player, new Vector3i(cx, y, cz + 3));  // north = tone 0
        Use(w, w.Player, new Vector3i(cx, y, cz - 3));  // south = tone 4
        Assert.Equal((int)MelodyPuzzleResult.Solved, LastState(w, w.Player).Result);

        var site = w.Server.MelodySiteAt(new Vector3i(cx + 2, y, cz + 2), out int pillar);
        Assert.NotNull(site);
        Assert.Equal(SvGameServer.MelodySiteKindRing, site!.Kind);
        Assert.Equal($"ring:{cx},{y},{cz}", site.Id);
        Assert.Equal(1, pillar);

        w.Server.World.SetBlock(new Vector3i(cx - 3, y, cz), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air); // mined
        Assert.Null(w.Server.MelodySiteAt(new Vector3i(cx + 2, y, cz + 2), out _));
    }

    [Fact]
    public void TheSounderInstruments_OnAMelodyBlock_NeedTheBlueprint_AndStayWhenAFriendChangesTheNote()
    {
        using var w = World("melody_block");
        var cell = new Vector3i(NpcLifeWorld.Cx + 1, w.Feet, NpcLifeWorld.Cz + 1);
        Assert.Equal(0, w.Player.State.Inventory.Add("melody_block", 1, 1024));
        w.Server.PlaceBlock(NpcLifeWorld.Owner, cell.X, cell.Y, cell.Z, "melody_block");

        w.Server.SetCrystalDeviceForTest(w.Player, cell, action: 2, mode: 3, config: "inst=4");
        Assert.Equal(0, Inst(w, cell)); // no blueprint: the Sounder voice is not there yet

        w.Player.State.UnlockedBlueprints.Add(SoundersIds.BlueprintVoice);
        w.Server.SetCrystalDeviceForTest(w.Player, cell, action: 2, mode: 3, config: "inst=5");
        Assert.Equal(5, Inst(w, cell));

        w.Player.State.UnlockedBlueprints.Remove(SoundersIds.BlueprintVoice);
        w.Server.SetCrystalDeviceForTest(w.Player, cell, action: 2, mode: 6, config: "inst=5"); // only the note changes
        Assert.Equal(5, Inst(w, cell));
        w.Server.SetCrystalDeviceForTest(w.Player, cell, action: 2, mode: 6, config: "inst=4"); // a new Sounder choice: no
        Assert.Equal(5, Inst(w, cell));
    }

    private static int Inst(NpcLifeWorld w, Vector3i cell)
    {
        string config = w.Server.CrystalDeviceConfigForTest(cell) ?? string.Empty;
        foreach (string part in config.Split(';'))
        {
            if (part.StartsWith("inst=", StringComparison.Ordinal))
            {
                return int.Parse(part.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return 0;
    }
}
