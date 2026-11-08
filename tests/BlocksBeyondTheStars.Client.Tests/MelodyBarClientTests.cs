// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3345/#3346 on the client, Unity-free: the melody bar's keys (number keys, pad d-pad + shoulder, the eight touch tones),
/// its model (open/close, the prompt lighting along, flashes, result lines, colours AND symbols per tone), the beat of a
/// server <see cref="NoteSequence"/> (rests are silence, the client keeps the time) and the use key's pick of a usable block.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class MelodyBarClientTests
{
    // ---------------- Keys ----------------

    [Fact]
    public void TheNumberKeys1To8_AreTheTonesCToC_AndNothingElse()
    {
        Assert.Equal(Enumerable.Range(0, 8), Enumerable.Range(1, 8).Select(MelodyBarInput.ToneForDigit));
        Assert.Equal(MelodyBarInput.NoTone, MelodyBarInput.ToneForDigit(0));
        Assert.Equal(MelodyBarInput.NoTone, MelodyBarInput.ToneForDigit(9));
        Assert.All(Enumerable.Range(0, 8), t => Assert.Equal(t, MelodyBarInput.ToneForDigit(MelodyBarInput.DigitForTone(t))));
    }

    [Fact]
    public void ThePad_DPadPlaysOneToFour_WithAShoulderFiveToEight()
    {
        Assert.Equal(0, MelodyBarInput.ToneForPad(PadDirection.Up, false));
        Assert.Equal(1, MelodyBarInput.ToneForPad(PadDirection.Right, false));
        Assert.Equal(2, MelodyBarInput.ToneForPad(PadDirection.Down, false));
        Assert.Equal(3, MelodyBarInput.ToneForPad(PadDirection.Left, false));
        Assert.Equal(4, MelodyBarInput.ToneForPad(PadDirection.Up, true));
        Assert.Equal(7, MelodyBarInput.ToneForPad(PadDirection.Left, true));
        Assert.Equal(MelodyBarInput.NoTone, MelodyBarInput.ToneForPad(PadDirection.None, true));

        // Every tone has exactly one pad chord, and the caption the bar shows for it plays it.
        var chords = Enumerable.Range(0, 8).Select(MelodyBarInput.PadForTone).ToList();
        Assert.Equal(8, chords.Distinct().Count());
        Assert.All(Enumerable.Range(0, 8), t => Assert.Equal(t, MelodyBarInput.ToneForPad(chords[t].Direction, chords[t].Shoulder)));
    }

    [Fact]
    public void TheDPad_PlaysOncePerPress_NotEveryFrameItIsHeld()
    {
        var reader = new MelodyPadReader();

        Assert.Equal(0, reader.Update(0f, 1f, false));                     // up pressed
        Assert.Equal(MelodyBarInput.NoTone, reader.Update(0f, 1f, false)); // …held
        Assert.Equal(MelodyBarInput.NoTone, reader.Update(0f, 0f, false)); // released
        Assert.Equal(0, reader.Update(0.1f, 0.9f, false));                 // pressed again (a near-diagonal reads as up)
        Assert.Equal(5, reader.Update(1f, 0f, true));                      // rolled to the right with a shoulder: tone 6
        Assert.Equal(MelodyBarInput.NoTone, reader.Update(0.3f, 0.2f, false)); // under the threshold: a release
        reader.Reset();
        Assert.Equal(2, reader.Update(0f, -1f, false));
    }

    // ---------------- The bar's model ----------------

    [Fact]
    public void EveryTone_HasItsOwnColourAndItsOwnSymbol()
    {
        Assert.Equal(8, MelodyBarModel.Symbols.Distinct().Count());
        Assert.Equal(8, MelodyBarModel.Colors.Distinct().Count());
        Assert.All(MelodyBarModel.Symbols, s => Assert.False(string.IsNullOrWhiteSpace(s)));
    }

    [Fact]
    public void AState_OpensTheBar_LightsThePromptAlong_AndAClosingStateShowsItsResultAMoment()
    {
        var bar = new MelodyBarModel();
        bar.Apply(new MelodyPuzzleState
        {
            PuzzleId = SoundersIds.PuzzleShrineEcho,
            Open = true,
            Mode = (int)MelodyPuzzleMode.Repeat,
            RoundCount = 3,
            Expected = 2,
            Highlight = new[] { 4, SounderAlphabet.Rest, 6 },
            NoteSeconds = 0.5f,
            Instrument = SoundersIds.InstrumentVoice,
        }, now: 10.0);

        Assert.True(bar.Open);
        Assert.False(bar.FreePlay);
        Assert.Equal("ui.melody.mode.repeat", bar.TitleKey);
        Assert.Equal(4, bar.LitTone(10.1));
        Assert.Equal(-1, bar.LitTone(10.6)); // the rest lights nothing
        Assert.Equal(6, bar.LitTone(11.2));
        Assert.Equal(-1, bar.LitTone(11.6)); // the prompt is over
        Assert.True(bar.PromptPlaying(11.4));
        Assert.False(bar.PromptPlaying(11.6));

        bar.Apply(new MelodyPuzzleState { PuzzleId = SoundersIds.PuzzleShrineEcho, Open = true, Result = (int)MelodyPuzzleResult.TryAgain, LastTone = 3, NoteSeconds = 0.5f }, now: 12.0);
        Assert.True(bar.Flashing(3, 12.1));
        Assert.False(bar.Flashing(3, 12.5));
        Assert.Equal("ui.melody.result.again", bar.ResultKey(12.2));
        Assert.Null(bar.ResultKey(14.0));

        bar.Apply(new MelodyPuzzleState { PuzzleId = SoundersIds.PuzzleShrineEcho, Open = false, Result = (int)MelodyPuzzleResult.Solved }, now: 20.0);
        Assert.False(bar.Open);
        Assert.True(bar.Visible(21.0));  // "Well done!" stays a moment
        Assert.False(bar.Visible(22.0));
        Assert.Equal("ui.melody.result.solved", bar.ResultKey(20.5));
    }

    [Fact]
    public void FreePlay_OpensWithoutAPuzzle_AndNeverTakesTheBarFromOne()
    {
        var bar = new MelodyBarModel();
        bar.OpenFreePlay(SoundersIds.InstrumentChoir);
        Assert.True(bar.FreePlay);
        Assert.Equal("ui.melody.title", bar.TitleKey);
        Assert.Equal(SoundersIds.InstrumentChoir, bar.Instrument);
        bar.CloseFreePlay();
        Assert.False(bar.Open);

        bar.Apply(new MelodyPuzzleState { PuzzleId = SoundersIds.PuzzleSingDuel, Open = true, Mode = (int)MelodyPuzzleMode.CallAndResponse }, 1.0);
        bar.OpenFreePlay(0);
        bar.CloseFreePlay();
        Assert.True(bar.Open);
        Assert.Equal(SoundersIds.PuzzleSingDuel, bar.PuzzleId);
        Assert.Equal("ui.melody.mode.call", bar.TitleKey);
    }

    /// <summary>#3426: a melody that spells a word shows every letter's two notes, in the player's note names, the letter the
    /// player is at marked as the right notes come in; a closed bar, free play and a reset spell nothing.</summary>
    [Fact]
    public void AWordOnTheBar_IsSpelledLetterByLetter_TheCurrentLetterMarked()
    {
        var bar = new MelodyBarModel();
        string[] german = "C D E F G A H C'".Split(' ');
        int[] tones = MelodySpelling.PlayedTones(SoundersIds.SongOfReturnWord, MelodyPuzzleMode.Compose, 4);
        var state = new MelodyPuzzleState
        {
            PuzzleId = SoundersIds.PuzzleSongOfReturn,
            Open = true,
            Mode = (int)MelodyPuzzleMode.Compose,
            Expected = tones.Length,
            Word = SoundersIds.SongOfReturnWord,
            WordTones = tones,
        };
        bar.Apply(state, 1.0);
        Assert.Equal("[O = F E] · R = G D · N = F D · E = D C · M = F C · I = E C · S = G E", bar.SpellingLine(german, "[", "]"));

        state.Entered = 3; // the O and the first note of the R
        bar.Apply(state, 2.0);
        Assert.Equal(1, bar.CurrentLetter);
        Assert.Contains("[R = G D]", bar.SpellingLine(german, "[", "]"), System.StringComparison.Ordinal);

        bar.Apply(new MelodyPuzzleState { PuzzleId = SoundersIds.PuzzleSongOfReturn, Open = false, Result = (int)MelodyPuzzleResult.Solved }, 3.0);
        Assert.Equal(string.Empty, bar.SpellingLine(german));
        bar.Apply(state, 4.0);
        bar.Reset();
        Assert.Equal(string.Empty, bar.SpellingLine(german));
        bar.OpenFreePlay(0);
        Assert.Equal(string.Empty, bar.SpellingLine(german));
    }

    // ---------------- Note sequences ----------------

    [Fact]
    public void ANoteSequence_IsTimedOnTheClient_RestsAreSilence()
    {
        var beat = new NoteSequenceScheduler();
        var seq = new NoteSequence { Notes = new[] { 0, SounderAlphabet.Rest, 4, 7 }, Instrument = 4, NoteSeconds = 0.5f, X = 1, Y = 2, Z = 3, Source = "rune_stone" };

        Assert.Equal(3, beat.Enqueue(seq, now: 100.0));
        Assert.Equal(2.0, NoteSequenceScheduler.Duration(seq), 6);

        var due = new List<ScheduledNote>();
        Assert.Equal(1, beat.Due(100.0, due));  // the first note at once
        Assert.Equal(0, beat.Due(100.9, due));  // 100.5 is the rest
        Assert.Equal(1, beat.Due(101.0, due));
        Assert.Equal(1, beat.Due(101.6, due));
        Assert.Equal(0, beat.Pending);
        Assert.Equal(new[] { 0, 4, 7 }, due.Select(n => n.Tone));
        Assert.Equal("note_4_7", due[2].ClipId);
        Assert.Equal((1f, 2f, 3f), (due[0].X, due[0].Y, due[0].Z));
        Assert.Equal("rune_stone", due[0].Source);
    }

    [Fact]
    public void TwoSequences_Interleave_InTime_AndTheFifthRidesAlong()
    {
        var beat = new NoteSequenceScheduler();
        beat.Enqueue(new NoteSequence { Notes = new[] { 1, 1 }, NoteSeconds = 1f, Instrument = 5, WithFifth = true }, now: 0.0);
        beat.Enqueue(new NoteSequence { Notes = new[] { 6 }, NoteSeconds = 1f }, now: 0.5);

        var due = new List<ScheduledNote>();
        beat.Due(2.0, due);

        Assert.Equal(new[] { 1, 6, 1 }, due.Select(n => n.Tone));
        Assert.True(due[0].WithFifth);
        Assert.False(due[1].WithFifth);
        Assert.Equal(1.5f, NoteSequenceScheduler.FifthPitch);
    }

    [Fact]
    public void ABrokenSequence_IsClampedOrDropped_NeverStuck()
    {
        var beat = new NoteSequenceScheduler();
        Assert.Equal(0, beat.Enqueue(new NoteSequence { Notes = new[] { 9, -5 } }, 0.0)); // no tone in it
        beat.Enqueue(new NoteSequence { Notes = new[] { 2, 3 }, NoteSeconds = 0f, Instrument = 42 }, 0.0);
        var due = new List<ScheduledNote>();
        beat.Due(NoteSequenceScheduler.MinNoteSeconds, due);
        Assert.Equal(2, due.Count);                // a zero note length is clamped up, not an endless loop
        Assert.Equal("note_5_2", due[0].ClipId);   // an unknown instrument becomes the choir, the last one

        beat.Enqueue(new NoteSequence { Notes = Enumerable.Repeat(1, 1000).ToArray(), NoteSeconds = 0.1f }, 0.0);
        Assert.Equal(NoteSequenceScheduler.MaxPending, beat.Pending);
        beat.Clear();
        Assert.Equal(0, beat.Pending);
    }

    // ---------------- The wire ----------------

    [Fact]
    public void TheClient_RaisesItsMelodyEvents_AndSendsTheBarsTones()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport);
        NoteSequence? seq = null;
        MelodyPuzzleState? state = null;
        client.NoteSequenceReceived += m => seq = m;
        client.MelodyPuzzleStateReceived += m => state = m;
        client.Connect("scripted", 0);

        transport.Deliver(new NoteSequence { Notes = new[] { 3 }, Source = "melody_pillar" });
        transport.Deliver(new MelodyPuzzleState { PuzzleId = "x", Open = true });
        client.Poll();
        Assert.Equal("melody_pillar", seq?.Source);
        Assert.True(state?.Open);

        client.SendMelodyNote(5, "x", 4);
        client.SendUseBlock(7, 8, 9);
        var note = Assert.IsType<MelodyNoteIntent>(transport.Sent[^2]);
        Assert.Equal((5, "x", 4), (note.Tone, note.PuzzleId, note.Instrument));
        var use = Assert.IsType<UseBlockIntent>(transport.Sent[^1]);
        Assert.Equal((7, 8, 9), (use.X, use.Y, use.Z));
    }

    // ---------------- The use key ----------------

    [Fact]
    public void AUsableBlock_IsTheLastPickOfTheAimedTier_AndBeatsEverythingNearby()
    {
        var f = new InteractFacts { UsableBlock = true, NearDoor = true, TalkNear = true };
        Assert.Equal(InteractKind.UseBlock, InteractResolver.Resolve(f));
        Assert.True(InteractResolver.IsAimed(InteractKind.UseBlock));
        Assert.False(InteractResolver.HasWorldLabel(InteractKind.UseBlock)); // the HUD names it ("E: play the rune")

        f.AimedDoor = true; // anything older E does with the aimed cell comes first
        Assert.Equal(InteractKind.AimedDoor, InteractResolver.Resolve(f));
        f.AimedDoor = false;
        f.CrystalDevice = true;
        Assert.Equal(InteractKind.CrystalDevice, InteractResolver.Resolve(f));

        f.Clear();
        Assert.False(f.UsableBlock);
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(f));
    }

    // ---------------- The controls line under the bar (#3431) ----------------

    [Fact]
    public void UnderTheBar_TheControlsLine_DropsOnlyTheHotbarSegment()
    {
        const string line = "{key:Move} move · {key:Jump} jump · {key:Hotbar} hotbar · {key:UiMenu} menu";
        Assert.Equal("{key:Move} move · {key:Jump} jump · {key:UiMenu} menu", MelodyBarInput.WithoutHotbarSegment(line));
        Assert.Equal("{key:Move} move", MelodyBarInput.WithoutHotbarSegment("{key:Hotbar} hotbar · {key:Move} move"));
        Assert.Equal("up·down · {key:Move} move", MelodyBarInput.WithoutHotbarSegment("up·down · {key:Move} move")); // no token: as is
        Assert.Equal(string.Empty, MelodyBarInput.WithoutHotbarSegment(null));
    }

    [Fact]
    public void InEveryLanguage_TheHotbarSegment_IsFoundByItsToken_AndEveryOtherSegmentStays()
    {
        string[] files = Directory.GetFiles(Path.Combine(ClientTestPaths.DataDir(), "locales"), "*.json");
        Assert.True(files.Length >= 2);
        foreach (string file in files.OrderBy(f => f, System.StringComparer.Ordinal))
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(file));
            if (!table.TryGetValue("ui.hud.hint", out string? hint))
            {
                continue; // a community locale without the line reads the English one
            }

            string[] before = hint.Split(PadControlNames.Separator);
            string[] after = MelodyBarInput.WithoutHotbarSegment(hint).Split(PadControlNames.Separator);
            bool IsHotbar(string segment) => segment.Contains(MelodyBarInput.HotbarToken, System.StringComparison.Ordinal);
            Assert.True(before.Count(IsHotbar) == 1, $"{Path.GetFileName(file)}: the hotbar key is not one segment of its own");
            Assert.Equal(before.Where(s => !IsHotbar(s)), after);
        }
    }
}
