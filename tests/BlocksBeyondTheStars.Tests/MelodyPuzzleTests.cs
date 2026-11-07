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
/// punish: they only restart a round or reset the input.
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
    public void Repeat_AWrongNote_RestartsTheRound_AndTakesNothingAway()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Repeat, Target = new[] { 1, 2, 3, 4 }, Rounds = 2 });
        PlayAll(p, 1, 2); // round 1 done

        Assert.Equal(MelodyStep.Correct, p.Play(1));
        Assert.Equal(MelodyStep.Wrong, p.Play(6));
        Assert.Equal(1, p.Round);    // still the second round — no round is taken back
        Assert.Equal(0, p.Entered);  // …which simply starts again
        Assert.Equal(1, p.Mistakes);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 1, 2, 3, 4).Last());
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
    public void Compose_AWrongNote_ResetsTheInput()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 3, 5, 3, 1 } });

        PlayAll(p, 3, 5);
        Assert.Equal(MelodyStep.Wrong, p.Play(7));
        Assert.Equal(0, p.Entered); // reset
        Assert.Equal(1, p.Mistakes);
        Assert.Equal(MelodyStep.Solved, PlayAll(p, 3, 5, 3, 1).Last());
    }

    [Fact]
    public void Compose_AWrongNoteThatIsTheFirstTone_CountsAsTheStartOfTheNextTry()
    {
        var p = new MelodyPuzzle(new MelodyPuzzleSpec { Mode = MelodyPuzzleMode.Compose, Target = new[] { 2, 4, 6 } });

        PlayAll(p, 2, 4);
        Assert.Equal(MelodyStep.Wrong, p.Play(2)); // lost the thread and began again
        Assert.Equal(1, p.Entered);
        Assert.Equal(new[] { MelodyStep.Correct, MelodyStep.Solved }, PlayAll(p, 4, 6));
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
