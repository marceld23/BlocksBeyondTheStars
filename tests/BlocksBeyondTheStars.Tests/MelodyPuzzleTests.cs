// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3346 — the pure rules of the melody puzzles (<see cref="MelodyPuzzle"/>) in all four modes, the counter-voice's
/// transposition and the melody sites' geometry (the shrine's pillar order, the player-built ring). Wrong notes never
/// punish (#3678): in every mode a wrong note simply does not count — the input stays, nothing restarts. The Bridge Song
/// (#3677) is one round per letter, and only it shows the next tone.
/// </summary>
public sealed class MelodyPuzzleTests
{
    private const int R = SounderAlphabet.Rest;

    private static MelodyStep[] PlayAll(MelodyPuzzle p, params int[] tones) => tones.Select(p.Play).ToArray();

    // ---------------- Repeat ----------------

    [Fact]
    public void Repeat_ThePromptGrowsEveryRound_UntilTheWholeMelody()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Id = "t", Mode = MelodyPuzzleMode.Repeat, Target = new[] { 0, 2, R, 4, 7 }, Rounds = 2 });

        Assert.True(p.IsValid);
        Assert.Equal(2, p.RoundCount);
        Assert.Equal(new[] { 0, 2 }, p.Prompt);                 // round 1: the first two tones (rests are prompt timing)
        Assert.Equal(new[] { 0, 2 }, p.Expected);
        Assert.Equal(new[] { MelodyStep.Correct, MelodyStep.RoundDone }, PlayAll(p, 0, 2));

        Assert.Equal(1, p.Round);
        Assert.Equal(new[] { 0, 2, R, 4, 7 }, p.Prompt);        // round 2: the whole melody, its rest included
        Assert.Equal(new[] { 0, 2, 4, 7 }, p.Expected);
        Assert.Equal(new[] { MelodyStep.Correct, MelodyStep.Correct, MelodyStep.Correct, MelodyStep.Solved }, PlayAll(p, 0, 2, 4, 7));
        Assert.True(p.Solved);
        Assert.Equal(MelodyStep.Ignored, p.Play(0)); // done is done
    }

    [Fact]
    public void Repeat_WithoutRounds_IsClassicSimonSays_OneMoreToneEachRound()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Repeat, Target = new[] { 5, 3, 1 } });

        Assert.Equal(3, p.RoundCount);
        Assert.Equal(MelodyStep.RoundDone, p.Play(5));
        Assert.Equal(new[] { 5, 3 }, p.Expected);
        PlayAll(p, 5, 3);
        Assert.Equal(new[] { 5, 3, 1 }, p.Expected);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 5, 3, 1).Last());
    }

    [Fact]
    public void Repeat_AWrongNote_DoesNotCount_TheRoundGoesOnWhereItWas()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Repeat, Target = new[] { 1, 2, 3, 4 }, Rounds = 2 });
        PlayAll(p, 1, 2); // round 1 done

        Assert.Equal(MelodyStep.Correct, p.Play(1));
        Assert.Equal(MelodyStep.Wrong, p.Play(6));
        Assert.Equal(1, p.Round);    // still the second round — no round is taken back
        Assert.Equal(1, p.Entered);  // #3678: …and the right tone stays
        Assert.Equal(1, p.Mistakes);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 2, 3, 4).Last());
    }

    [Fact]
    public void Repeat_MoreRoundsThanTones_AsksEachLengthOnce()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Repeat, Target = new[] { 0, 1 }, Rounds = 9 });

        Assert.Equal(2, p.RoundCount);
    }

    // ---------------- Compose ----------------

    [Fact]
    public void Compose_TheWholeSequence_InOrder_WithoutAPrompt()
    {
        int[] song = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord);
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = song });

        Assert.Equal(1, p.RoundCount);
        Assert.False(p.HasPrompt);
        Assert.Empty(p.Prompt);
        Assert.Equal(SounderAlphabet.TonesOnly(song), p.Expected);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, SounderAlphabet.TonesOnly(song)).Last());
    }

    [Fact]
    public void Compose_AWrongNote_DoesNotCount_TheInputStays()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 3, 5, 3, 1 } });

        PlayAll(p, 3, 5);
        Assert.Equal(MelodyStep.Wrong, p.Play(7));
        Assert.Equal(2, p.Entered); // #3678: no reset — the next right tone carries on
        Assert.Equal(1, p.Mistakes);
        Assert.Equal(new[] { MelodyStep.Correct, MelodyStep.Solved }, PlayAll(p, 3, 1));
    }

    [Fact]
    public void Compose_StartingOverFromTheFirstTone_IsJustAnotherWrongNote_NothingIsLost()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 2, 4, 6 } });

        PlayAll(p, 2, 4);
        Assert.Equal(MelodyStep.Wrong, p.Play(2)); // not the third tone
        Assert.Equal(2, p.Entered);
        Assert.Equal(MelodyStep.Solved, p.Play(6));
    }

    /// <summary>#3678: in every mode a wrong tone only counts as a mistake — the round, the right tones so far and the
    /// expected tone stay, so the very next right tone carries on.</summary>
    [Theory]
    [InlineData(MelodyPuzzleMode.Repeat)]
    [InlineData(MelodyPuzzleMode.Compose)]
    [InlineData(MelodyPuzzleMode.Harmony)]
    [InlineData(MelodyPuzzleMode.CallAndResponse)]
    public void InEveryMode_AWrongNote_NeverResetsTheInput(MelodyPuzzleMode mode)
    {
        // Every spec asks for 5, 3, 7 in one round: the Harmony melody 1, 6, 3 is that a fifth down.
        var p = new MelodyPuzzle(new MelodyPuzzleSpec
        {
            Mode = mode,
            Target = mode == MelodyPuzzleMode.Harmony ? new[] { 1, 6, 3 } : new[] { 5, 3, 7 },
            Rounds = 1,
            Calls = new[] { new[] { 0, 0 } },
            Answers = new[] { new[] { 5, 3, 7 } },
        });
        Assert.Equal(new[] { 5, 3, 7 }, p.Expected);

        Assert.Equal(MelodyStep.Correct, p.Play(5));
        Assert.Equal(MelodyStep.Wrong, p.Play(0));
        Assert.Equal(MelodyStep.Wrong, p.Play(5)); // the first tone again is wrong too — and changes nothing either
        Assert.Equal((0, 1, 2), (p.Round, p.Entered, p.Mistakes));
        Assert.Equal(3, p.NextTone);
        Assert.Equal(new[] { MelodyStep.Correct, MelodyStep.Solved }, PlayAll(p, 3, 7));
    }

    // ---------------- Harmony ----------------

    [Fact]
    public void Harmony_ThePlayerPlaysTheCounterVoice_AFifthAbove()
    {
        int[] melody = { 0, 1, 2, R, 3 };
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Harmony, Target = melody });

        Assert.Equal(melody, p.Prompt);                 // the server plays the melody…
        Assert.Equal(new[] { 4, 5, 6, 7 }, p.Expected); // …the player answers C→G, D→A, E→B, F→C'
        Assert.Equal(MelodyStep.Wrong, p.Play(0));      // singing the melody itself is not the counter-voice
        Assert.Equal(0, p.Entered);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 4, 5, 6, 7).Last());
    }

    /// <summary>#3677: the Bridge Song is seven rounds, one letter of ORNEMIS each — the choir's prompt is that letter's two
    /// melody tones, the answer their counter-voice; the spelling position counts across the rounds.</summary>
    [Fact]
    public void Harmony_ByLetter_TheBridgeSong_IsSevenRounds_TheLettersMelodyThenItsCounterVoice()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec
        {
            Mode = MelodyPuzzleMode.Harmony,
            Target = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord),
            Word = SoundersIds.SongOfReturnWord,
            RoundPerLetter = true,
            ShowNextTone = true,
        });

        Assert.Equal(7, p.RoundCount);
        for (int r = 0; r < 7; r++)
        {
            int[] melody = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord.Substring(r, 1));
            int[] answer = MelodyPuzzle.Counter(melody, 4);
            Assert.Equal(r, p.Round);
            Assert.Equal(2, melody.Length);
            Assert.Equal(melody, p.Prompt);   // the choir sings the letter…
            Assert.Equal(answer, p.Expected); // …the player answers its second voice
            Assert.Equal(2 * r, p.WordEntered);
            Assert.Equal(answer[0], p.NextTone);
            Assert.Equal(MelodyStep.Wrong, p.Play(melody[0] == answer[0] ? (answer[0] + 1) % 8 : melody[0])); // copying the choir
            Assert.Equal(0, p.Entered);
            Assert.Equal(MelodyStep.Correct, p.Play(answer[0]));
            Assert.Equal((2 * r) + 1, p.WordEntered);
            Assert.Equal(answer[1], p.NextTone);
            Assert.Equal(r < 6 ? MelodyStep.RoundDone : MelodyStep.Solved, p.Play(answer[1]));
        }

        Assert.True(p.Solved);
        Assert.Equal(-1, p.NextTone);
        Assert.Equal(7, p.Mistakes);

        // Together the rounds ask exactly what the spelling line shows (#3426): O = C' B · R = D A · …
        var whole = new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Harmony, Target = p.Spec.Target, Word = p.Spec.Word };
        Assert.Equal(MelodySpelling.PlayedTones(whole), new MelodyPuzzle(whole).Expected);
        Assert.Equal(new[] { 7, 6 }, MelodySpelling.PlayedTones(whole).Take(2)); // O = F E, a fifth up: C' B
    }

    [Fact]
    public void Harmony_ByLetter_WithAToneOffTheBar_IsNoPuzzle_AndWithoutRestsIsOneRound()
    {
        Assert.False(new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Harmony, Target = new[] { 3, 2, R, 9, 1 }, RoundPerLetter = true }).IsValid);

        var one = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Harmony, Target = new[] { R, 0, 1, 2, R, R }, RoundPerLetter = true });
        Assert.Equal(1, one.RoundCount);
        Assert.Equal(new[] { 0, 1, 2 }, one.Prompt);
        Assert.Equal(new[] { 4, 5, 6 }, one.Expected);
    }

    [Fact]
    public void TheNextTone_IsTheExpectedTone_AndTheSpecCopiesTheBridgeSongsFlags()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 6, 1 } });
        Assert.Equal(6, p.NextTone);
        p.Play(6);
        Assert.Equal(1, p.NextTone);
        p.Play(1);
        Assert.Equal(-1, p.NextTone); // solved
        Assert.Equal(-1, new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose }).NextTone);

        var copy = new MelodyPuzzleSpec { RoundPerLetter = true, ShowNextTone = true }.Clone();
        Assert.True(copy.RoundPerLetter);
        Assert.True(copy.ShowNextTone);
        Assert.False(new MelodyPuzzleSpec().ShowNextTone); // off unless a puzzle asks for it
    }

    [Theory]
    [InlineData(4, 4, 1)]  // G + a fifth = D (an octave down, so it stays on the bar)
    [InlineData(7, 4, 4)]  // C' + a fifth = G
    [InlineData(3, 4, 7)]  // F + a fifth = C'
    [InlineData(0, -4, 3)] // C − a fifth = F (an octave up)
    [InlineData(5, 2, 7)]  // A + a third = C'
    [InlineData(R, 4, R)]  // a rest stays a rest
    public void Transpose_StaysOnTheEightKeys(int tone, int steps, int expected)
        => Assert.Equal(expected, MelodyPuzzle.Transpose(tone, steps));

    [Fact]
    public void Transpose_EveryToneAndOffset_LandsOnTheBar()
    {
        for (int tone = 0; tone < SounderAlphabet.ToneCount; tone++)
        {
            for (int steps = -14; steps <= 14; steps++)
            {
                int t = MelodyPuzzle.Transpose(tone, steps);
                Assert.InRange(t, 0, SounderAlphabet.ToneCount - 1);
            }
        }
    }

    // ---------------- Call and response ----------------

    [Fact]
    public void CallAndResponse_EachCallHasItsAnswer_AndAnEmptyAnswerSingsTheCallBack()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec
        {
            Mode = MelodyPuzzleMode.CallAndResponse,
            Calls = new[] { new[] { 0, 2, 4 }, new[] { 7, R, 7 } },
            Answers = new[] { new[] { 4, 2, 0 } },
        });

        Assert.Equal(2, p.RoundCount);
        Assert.Equal(new[] { 0, 2, 4 }, p.Prompt);
        Assert.Equal(new[] { 4, 2, 0 }, p.Expected);
        Assert.Equal(MelodyStep.Wrong, p.Play(0)); // repeating the call is not the answer here
        Assert.Equal(MelodyStep.RoundDone, PlayAll(p, 4, 2, 0).Last());

        Assert.Equal(new[] { 7, R, 7 }, p.Prompt);
        Assert.Equal(new[] { 7, 7 }, p.Expected); // no answer given: sing the call back
        Assert.Equal(MelodyStep.Wrong, p.Play(6));
        Assert.Equal(1, p.Round);                  // the same call again, nothing lost
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 7, 7).Last());
    }

    // ---------------- Validity ----------------

    [Fact]
    public void ASpecWithoutATone_OrWithAToneOffTheBar_IsNoPuzzle()
    {
        Assert.False(new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Repeat, Target = new[] { R, R } }).IsValid);
        Assert.False(new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 0, 9 } }).IsValid);
        Assert.False(new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.CallAndResponse }).IsValid);
        var none = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose });
        Assert.Equal(MelodyStep.Ignored, none.Play(0));
    }

    [Fact]
    public void ToneOffTheBar_IsIgnored_NotWrong()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 1, 2 } });

        Assert.Equal(MelodyStep.Ignored, p.Play(8));
        Assert.Equal(MelodyStep.Ignored, p.Play(-1));
        Assert.Equal(0, p.Mistakes);
    }

    [Fact]
    public void TheSpec_IsCopied_SoAReusedSpecCannotChangeARunningPuzzle()
    {
        var spec = new MelodyPuzzleSpec { Id = "x", Mode = MelodyPuzzleMode.Compose, Target = new[] { 1, 2 }, Calls = new[] { new[] { 3 } } };
        var copy = spec.Clone();
        spec.Target[0] = 5;
        spec.Calls[0][0] = 6;

        Assert.Equal(new[] { 1, 2 }, copy.Target);
        Assert.Equal(3, copy.Calls[0][0]);
        Assert.Equal("x", copy.Id);
    }

    // ---------------- Site geometry ----------------

    [Fact]
    public void TheShrinesPillars_StartInTheNorth_AndGoClockwise()
    {
        Vector3i[] pillars = MelodySiteGeometry.ShrinePillars(100, 40, 200);

        Assert.Equal(8, pillars.Length);
        Assert.Equal(new Vector3i(100, 41, 205), pillars[0]); // north (+Z): tone 0, C
        Assert.Equal(new Vector3i(104, 41, 204), pillars[1]); // north-east
        Assert.Equal(new Vector3i(105, 41, 200), pillars[2]); // east (+X)
        Assert.Equal(new Vector3i(100, 41, 195), pillars[4]); // south
        Assert.Equal(new Vector3i(95, 41, 200), pillars[6]);  // west
        Assert.Equal(new Vector3i(96, 41, 204), pillars[7]);  // north-west: tone 7, C'
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(3, 3, 1)]
    [InlineData(5, 0, 2)]
    [InlineData(2, -4, 3)]
    [InlineData(0, -2, 4)]
    [InlineData(-4, -4, 5)]
    [InlineData(-5, 1, 6)]
    [InlineData(-1, 2, 7)]
    public void TheSectors_CountClockwiseFromNorth(int dx, int dz, int sector)
        => Assert.Equal(sector, MelodySiteGeometry.Sector(dx, dz));

    [Fact]
    public void ARingOfEightStones_AroundTheCentre_IsFound_InPillarOrder()
    {
        var centre = new Vector3i(10, 20, 30);
        var stones = RingOffsets(radius: 3).Select(o => (o.X, 1, o.Z)).ToHashSet(); // standing on the centre's level + 1

        Vector3i[]? ring = MelodySiteGeometry.FindRing(centre, (x, y, z) => stones.Contains((x, y, z)));

        Assert.NotNull(ring);
        Assert.Equal(new Vector3i(10, 21, 33), ring![0]); // north
        Assert.Equal(new Vector3i(13, 21, 30), ring[2]);  // east
        Assert.Equal(0, MelodySiteGeometry.PillarIndex(ring, new Vector3i(10, 23, 33))); // a stone three up the column counts
        Assert.Equal(-1, MelodySiteGeometry.PillarIndex(ring, new Vector3i(10, 25, 33)));
    }

    [Fact]
    public void SevenStones_TwoInOneSector_OrALopsidedRing_AreNoRing()
    {
        var centre = new Vector3i(0, 0, 0);
        var ring = RingOffsets(radius: 4).Select(o => (o.X, 0, o.Z)).ToList();

        var seven = ring.Skip(1).ToHashSet();
        Assert.Null(MelodySiteGeometry.FindRing(centre, (x, y, z) => seven.Contains((x, y, z))));

        var crowded = ring.ToHashSet();
        crowded.Add((1, 0, 4)); // a second stone in the north sector
        Assert.Null(MelodySiteGeometry.FindRing(centre, (x, y, z) => crowded.Contains((x, y, z))));

        var lopsided = ring.ToHashSet();
        lopsided.Remove((0, 0, 4));
        lopsided.Add((0, 0, 2)); // the north stone far closer than the others (4 → 2, more than 1.5 apart)
        lopsided.Remove((4, 0, 0));
        lopsided.Add((5, 0, 0));
        Assert.Null(MelodySiteGeometry.FindRing(centre, (x, y, z) => lopsided.Contains((x, y, z))));
    }

    [Fact]
    public void TheShrine_IsARingToo()
    {
        var centre = new Vector3i(0, 0, 0);
        var pillars = MelodySiteGeometry.ShrinePillars(0, 0, 0).Select(p => (p.X, p.Y, p.Z)).ToHashSet();

        Vector3i[]? ring = MelodySiteGeometry.FindRing(centre, (x, y, z) => pillars.Contains((x, y, z)));

        Assert.Equal(MelodySiteGeometry.ShrinePillars(0, 0, 0), ring);
    }

    /// <summary>Eight stone offsets at the compass points of a ring of <paramref name="radius"/> (diagonals rounded down).</summary>
    private static List<(int X, int Z)> RingOffsets(int radius)
    {
        int d = (int)(radius * 0.75);
        return new List<(int X, int Z)>
        {
            (0, radius), (d, d), (radius, 0), (d, -d), (0, -radius), (-d, -d), (-radius, 0), (-d, d),
        };
    }
}
