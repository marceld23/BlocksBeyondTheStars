// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3240: the water around the player's own body — before this, water was only a low-pass: no splash in or out, no
/// strokes. <see cref="SwimSounds"/> splashes by the entry speed, strokes in the swim rhythm and gasps after a long dive,
/// without spamming splashes while the swimmer bobs at the surface.
/// </summary>
public sealed class SwimSoundsTests
{
    private const float Frame = 1f / 60f;

    private static SwimInput Dry(float fall = 0f) => new(Frame, feetWet: false, swimming: false, stroking: false, headUnder: false, fall);

    private static SwimInput Wading() => new(Frame, feetWet: true, swimming: false, stroking: false, headUnder: false, 0f);

    private static SwimInput Swim(bool stroking = false, bool headUnder = false, float fall = 0f)
        => new(Frame, feetWet: true, swimming: true, stroking, headUnder, fall);

    private static List<(float At, BodyCue Cue)> Run(SwimSounds swim, float seconds, SwimInput input)
    {
        var cues = new List<(float, BodyCue)>();
        for (float t = 0f; t < seconds; t += Frame)
        {
            var cue = swim.Tick(input);
            if (!cue.IsNone)
            {
                cues.Add((t, cue));
            }
        }

        return cues;
    }

    [Fact]
    public void TheSplash_GrowsWithTheEntrySpeed_AndAGentleWadeIsSilent()
    {
        Assert.Equal(0f, SwimSounds.SplashInVolume(1f));
        Assert.Equal(0f, SwimSounds.SplashInVolume(SwimSounds.SplashMinSpeed - 0.01f));
        float hop = SwimSounds.SplashInVolume(7f);
        float dive = SwimSounds.SplashInVolume(14f);
        Assert.True(hop > 0.25f && dive > hop, $"hop {hop}, dive {dive}");
        Assert.Equal(0.7f, SwimSounds.SplashInVolume(40f), 4);
    }

    [Fact]
    public void JumpingIn_Splashes_WadingIn_DoesNot()
    {
        var swim = new SwimSounds(new Random(1));
        swim.Tick(Dry());
        var splash = swim.Tick(new SwimInput(Frame, true, false, false, false, 9f));
        Assert.Equal("water_splash_in", splash.Id);
        Assert.Equal(SwimSounds.SplashInVolume(9f), splash.Volume, 4);

        var wade = new SwimSounds(new Random(2));
        wade.Tick(Dry());
        Assert.True(wade.Tick(Wading()).IsNone); // walking in from the beach: the footsteps say it
    }

    [Fact]
    public void LeavingTheWater_SplashesOnlyAfterSwimming()
    {
        var swim = new SwimSounds(new Random(3));
        Run(swim, 2f, Swim());
        Run(swim, 0.5f, Wading()); // climbing out: the feet still wet for a moment
        Assert.Equal("water_splash_out", swim.Tick(Dry()).Id);

        var wader = new SwimSounds(new Random(4));
        Run(wader, 3f, Wading());
        Assert.True(wader.Tick(Dry()).IsNone); // out of a puddle: no splash
    }

    [Fact]
    public void BobbingAtTheSurface_NeverSpamsSplashes()
    {
        var swim = new SwimSounds(new Random(5));
        var cues = new List<BodyCue>();
        for (int i = 0; i < 20; i++)
        {
            // The chest dips in and out of the water while the feet stay wet: no edge for a splash.
            cues.AddRange(Run(swim, 0.2f, Swim()).Select(c => c.Cue));
            cues.AddRange(Run(swim, 0.2f, new SwimInput(Frame, true, false, false, false, 3f)).Select(c => c.Cue));
        }

        Assert.DoesNotContain(cues, c => c.Id != null && c.Id.StartsWith("water_splash", StringComparison.Ordinal));
    }

    [Fact]
    public void FastHops_InAndOut_AreRateLimited()
    {
        var swim = new SwimSounds(new Random(6));
        swim.Tick(Dry());
        Assert.False(swim.Tick(new SwimInput(Frame, true, true, false, false, 8f)).IsNone);
        Assert.True(swim.Tick(Dry()).IsNone); // out again at once: inside the splash gap
        Assert.True(swim.Tick(new SwimInput(Frame, true, true, false, false, 8f)).IsNone);
    }

    [Fact]
    public void Strokes_FollowTheSwimRhythm_AlternatingTakes_OnlyWhilePushing()
    {
        var swim = new SwimSounds(new Random(7));
        Assert.Empty(Run(swim, 3f, Swim()));
        swim.Tick(Wading()); // the feet-wet edge is behind us

        var strokes = Run(swim, 5f, Swim(stroking: true));
        Assert.InRange(strokes.Count, 5, 6);
        Assert.InRange(strokes[0].At, SwimSounds.StrokeLead - (2 * Frame), SwimSounds.StrokeLead + (2 * Frame));
        for (int i = 1; i < strokes.Count; i++)
        {
            Assert.InRange(strokes[i].At - strokes[i - 1].At, (SwimSounds.StrokeInterval * 0.9f) - Frame, (SwimSounds.StrokeInterval * 1.1f) + Frame);
            Assert.NotEqual(strokes[i - 1].Cue.Id, strokes[i].Cue.Id);
            Assert.StartsWith("swim_stroke", strokes[i].Cue.Id);
        }

        Assert.Empty(Run(swim, 3f, Wading() /* stroking keys on land make no strokes */));
    }

    [Fact]
    public void ALongDive_EndsWithAGasp_AShortOneDoesNot()
    {
        var swim = new SwimSounds(new Random(8));
        Run(swim, 0.1f, Swim());
        Run(swim, SwimSounds.GaspAfterSeconds - 1f, Swim(headUnder: true));
        Assert.True(swim.Tick(Swim()).IsNone);

        Run(swim, SwimSounds.GaspAfterSeconds + 1f, Swim(headUnder: true));
        var gasp = swim.Tick(Swim());
        Assert.Equal("surface_gasp", gasp.Id);
        Assert.Equal(SwimSounds.GaspLevel, gasp.Volume, 4);
    }

    [Fact]
    public void AResync_IsNoPlunge_AndNoGasp()
    {
        var swim = new SwimSounds(new Random(9));
        Run(swim, SwimSounds.GaspAfterSeconds + 2f, Swim(headUnder: true));
        swim.Resync(feetWet: false); // a respawn on dry land
        Assert.True(swim.Tick(Dry()).IsNone);

        var fresh = new SwimSounds(new Random(10));
        Assert.True(fresh.Tick(Swim(fall: 12f)).IsNone); // the first frame only learns where the feet are
    }
}
